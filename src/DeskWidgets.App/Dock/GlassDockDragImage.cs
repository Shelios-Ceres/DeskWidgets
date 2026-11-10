using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Interop;
using DeskWidgets.Motion;
using DeskWidgets.PixelUi;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeskWidgets.Dock;

/// <summary>
/// The picture of a member dragged out of the Glass group panel (D7): a small topmost, never-activated, click-through layered window with
/// the member's tile at 100 %, so the picture stays whole and under the pointer over the panel's edge and over the bar (the panel window
/// would cut it, the dock window would need its region widened). The tile is rendered once from the panel's own element
/// (<see cref="RenderTargetBitmap"/>, premultiplied BGRA, as UpdateLayeredWindow wants), then only the window moves. The window is made
/// once and hidden between drags; it ticks the shared clock only while it flies (the landing, Move).
/// </summary>
internal sealed class GlassDockDragImage : IDisposable
{
    private const string ClassName = "DeskWidgets.DockDragImage";
    private const int WsExTransparent = 0x20;
    private static readonly nint HwndTopmost = -1;
    private static readonly Win32.WindowProcedure s_procedure = (hwnd, msg, wParam, lParam) => Win32.DefWindowProc(hwnd, msg, wParam, lParam); // must outlive the class
    private static bool s_registered;

    private nint _hwnd, _dc, _bitmap, _oldBitmap, _bits;
    private int _width, _height;
    private int _generation;
    private bool _visible, _disposed;
    private Win32.POINT _at;
    private FrameClock.Ticket? _flight;
    private Action? _landed;

    // Phase 16.3: the picture is rendered once at the panel's tile size, then drawn at a scale of it (1 = as rendered). It eases to the bar's icon
    // size while the drag is over the bar and back when it leaves, about the pointer, so the point the user holds stays under the pointer.
    private byte[]? _source;
    private int _sourceWidth, _sourceHeight;
    private double _scale = 1;
    private Win32.POINT _natural; // where the top-left would be at scale 1 (what the panel asked for)
    private FrameClock.Ticket? _ease;

    /// <summary>The picture is up (rendered and shown).</summary>
    public bool Visible => _visible;

    /// <summary>A flight was started (<see cref="FlyTo"/>): the picture is no longer the panel's to hide.</summary>
    public bool Claimed { get; private set; }

    /// <summary>The picture's size in pixels, once rendered.</summary>
    public (int Width, int Height) Size => (_width, _height);

    /// <summary>
    /// Renders <paramref name="tile"/> (in the tree, drawn) into the window, off screen until <see cref="MoveTo"/> shows it; <paramref name="ready"/>
    /// runs on the UI thread when the picture is in (not after a <see cref="Hide"/> or another capture meanwhile, and never on failure: the
    /// panel's own cell then stays the picture).
    /// </summary>
    public async void Capture(FrameworkElement tile, Action ready)
    {
        if (_disposed)
            return;
        int generation = ++_generation;
        try
        {
            var target = new RenderTargetBitmap();
            await target.RenderAsync(tile);
            var buffer = await target.GetPixelsAsync();
            if (generation != _generation || _disposed || target.PixelWidth <= 0 || target.PixelHeight <= 0)
                return;
            if (!Paint(buffer.ToArray(), target.PixelWidth, target.PixelHeight))
                return;
            ready();
        }
        catch (Exception ex)
        {
            Log.Warn("Rendering the dragged dock member failed", ex);
        }
    }

    /// <summary>Puts the picture's top-left at a screen pixel and shows it (above the dock and the panel) if it is not up yet.</summary>
    public void MoveTo(int x, int y)
    {
        if (_hwnd == 0 || _disposed)
            return;
        StopFlight(land: false);
        _natural = new Win32.POINT { X = x, Y = y };
        var (ax, ay) = Anchored(_scale);
        Place(ax, ay);
    }

    /// <summary>The top-left for a scale: scaled about the pointer, so the grab point stays put (at scale 1 it is the natural place).</summary>
    private (int X, int Y) Anchored(double scale)
    {
        if (Math.Abs(scale - 1) < 0.0005 || !Win32.GetCursorPos(out var cursor))
            return (_natural.X, _natural.Y);
        return (ScaleAbout(_natural.X, cursor.X, scale), ScaleAbout(_natural.Y, cursor.Y, scale));
    }

    /// <summary>Where a coordinate lands when everything is scaled by <paramref name="scale"/> about <paramref name="about"/>. Pure.</summary>
    internal static int ScaleAbout(int from, int about, double scale) => (int)Math.Round(about - (about - from) * scale);

    /// <summary>The drawn edge for a scale: the rendered edge times the scale, at least one pixel. Pure.</summary>
    internal static int ScaledEdge(int edge, double scale) => Math.Max(1, (int)Math.Round(edge * scale));

    /// <summary>The scale that makes the picture <paramref name="edgePx"/> pixels across, or 1 (as rendered) for null. Pure.</summary>
    internal static double ScaleFor(int renderedEdge, int? edgePx) => edgePx is { } edge && renderedEdge > 0 ? Math.Max(0.1, edge / (double)renderedEdge) : 1;

    /// <summary>
    /// Phase 16.3: eases the picture to <paramref name="edgePx"/> pixels across (the bar's icon), or back to its rendered size for null. It follows
    /// the "Release" scale vocabulary (200 ms, ease out); with "Animation effects" off it is one step.
    /// </summary>
    public void EaseTo(int? edgePx)
    {
        if (!_visible || _disposed || _source is null || Claimed)
            return;
        double target = ScaleFor(_sourceWidth, edgePx);
        if (Math.Abs(target - _scale) < 0.0005 && _ease is null)
            return;
        _ease?.Stop();
        _ease = null;
        double from = _scale;
        if (!MotionSettings.Enabled)
        {
            SetScale(target);
            return;
        }
        double begin = FrameClock.Now, ms = GlassMotion.Release.TotalMilliseconds;
        _ease = FrameClock.Every(16, now =>
        {
            double t = Math.Clamp((now - begin) / ms, 0, 1);
            SetScale(from + (target - from) * Bezier.Out.At(t));
            if (t >= 1)
            {
                _ease?.Stop();
                _ease = null;
            }
        });
    }

    /// <summary>Draws the picture at another scale, about the pointer.</summary>
    private void SetScale(double scale)
    {
        _scale = scale;
        if (!_visible || _source is null)
            return;
        var (x, y) = Anchored(scale);
        _at = new Win32.POINT { X = x, Y = y };
        Present();
    }

    /// <summary>Resamples the rendered picture to the current scale, puts it in the window's bitmap and pushes it with its place.</summary>
    private void Present()
    {
        if (_source is null || _hwnd == 0)
            return;
        int width = ScaledEdge(_sourceWidth, _scale), height = ScaledEdge(_sourceHeight, _scale);
        if (!EnsureBitmap(width, height))
            return;
        if (width == _sourceWidth && height == _sourceHeight)
            Marshal.Copy(_source, 0, _bits, Math.Min(_source.Length, width * height * 4));
        else
            Marshal.Copy(Resample(_source, _sourceWidth, _sourceHeight, width, height), 0, _bits, width * height * 4);
        var destination = _visible ? _at : new Win32.POINT { X = -32000, Y = -32000 }; // off screen until MoveTo places it
        var size = new Native.SIZE { cx = width, cy = height };
        var origin = new Win32.POINT();
        var blend = new Native.BLENDFUNCTION { BlendOp = 0 /* AC_SRC_OVER */, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        Native.UpdateLayeredWindow(_hwnd, 0, ref destination, ref size, _dc, ref origin, 0, ref blend, 2 /* ULW_ALPHA */);
    }

    /// <summary>
    /// Bilinear resampling of premultiplied BGRA (the window wants it premultiplied), pixel centres aligned so a picture shrunk and grown about its
    /// centre does not drift. Pure. The picture is a few hundred pixels across at most and is resampled only while it eases.
    /// </summary>
    internal static byte[] Resample(byte[] source, int sourceWidth, int sourceHeight, int width, int height)
    {
        var result = new byte[width * height * 4];
        double sx = sourceWidth / (double)width, sy = sourceHeight / (double)height;
        for (int y = 0; y < height; y++)
        {
            double fy = Math.Clamp((y + 0.5) * sy - 0.5, 0, sourceHeight - 1);
            int y0 = (int)fy, y1 = Math.Min(y0 + 1, sourceHeight - 1);
            double wy = fy - y0;
            for (int x = 0; x < width; x++)
            {
                double fx = Math.Clamp((x + 0.5) * sx - 0.5, 0, sourceWidth - 1);
                int x0 = (int)fx, x1 = Math.Min(x0 + 1, sourceWidth - 1);
                double wx = fx - x0;
                int target = (y * width + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    double top = source[(y0 * sourceWidth + x0) * 4 + c] * (1 - wx) + source[(y0 * sourceWidth + x1) * 4 + c] * wx;
                    double bottom = source[(y1 * sourceWidth + x0) * 4 + c] * (1 - wx) + source[(y1 * sourceWidth + x1) * 4 + c] * wx;
                    result[target + c] = (byte)Math.Round(top * (1 - wy) + bottom * wy);
                }
            }
        }
        return result;
    }

    private void Place(int x, int y)
    {
        _at = new Win32.POINT { X = x, Y = y };
        uint flags = Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | (_visible ? Win32.SWP_NOZORDER : 0);
        Win32.SetWindowPos(_hwnd, _visible ? 0 : HwndTopmost, x, y, 0, 0, flags);
        if (!_visible)
        {
            Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
            _visible = true;
        }
    }

    /// <summary>D7's landing: from where it is to a screen pixel (Move), then <paramref name="landed"/>; a new flight starts from where this one is.</summary>
    public void FlyTo(int x, int y, Action landed, int? edgePx = null)
    {
        if (!_visible || _disposed)
        {
            landed();
            return;
        }
        Claimed = true;
        _ease?.Stop();
        _ease = null;
        StopFlight(land: false);
        _landed = landed;
        // The landing is at the bar's icon size: a picture that has not eased yet (a quick let-go) grows on the way, with the flight's own curve.
        double scaleFrom = _scale, scaleTo = edgePx is null ? _scale : ScaleFor(_sourceWidth, edgePx);
        if (!MotionSettings.Enabled)
        {
            _scale = scaleTo;
            Present();
            Place(x, y);
            Land();
            return;
        }
        var from = _at;
        double begin = FrameClock.Now, ms = GlassMotion.Move.TotalMilliseconds;
        _flight = FrameClock.Every(16, now =>
        {
            double t = Math.Clamp((now - begin) / ms, 0, 1), k = Bezier.Move.At(t);
            if (Math.Abs(scaleTo - scaleFrom) > 0.0005)
            {
                _scale = scaleFrom + (scaleTo - scaleFrom) * k;
                Present();
            }
            Place((int)Math.Round(from.X + (x - from.X) * k), (int)Math.Round(from.Y + (y - from.Y) * k));
            if (t >= 1)
                Land();
        });
    }

    private void Land()
    {
        _flight?.Stop();
        _flight = null;
        var landed = _landed;
        _landed = null;
        landed?.Invoke();
    }

    private void StopFlight(bool land)
    {
        if (_flight is null && _landed is null)
            return;
        if (land)
        {
            Land();
            return;
        }
        _flight?.Stop();
        _flight = null;
        _landed = null;
    }

    /// <summary>The drag is over: the picture goes at once (a pending capture is dropped, a flight is ended without its landing).</summary>
    public void Hide()
    {
        _generation++;
        Claimed = false;
        StopFlight(land: false);
        _ease?.Stop();
        _ease = null;
        _scale = 1;
        if (_hwnd != 0 && _visible)
            Native.ShowWindow(_hwnd, Native.SW_HIDE);
        _visible = false;
    }

    /// <summary>Copies the pixels into the window's bitmap and pushes them (per-pixel alpha); the window is made on the first picture.</summary>
    private bool Paint(byte[] premultipliedBgra, int width, int height)
    {
        if (_hwnd == 0 && !CreateWindow())
            return false;
        _source = premultipliedBgra;
        _sourceWidth = width;
        _sourceHeight = height;
        _scale = 1; // a new picture is drawn as rendered
        _ease?.Stop();
        _ease = null;
        if (!EnsureBitmap(width, height))
            return false;
        Present();
        return true;
    }

    private bool CreateWindow()
    {
        if (!s_registered)
        {
            var windowClass = new Win32.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<Win32.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_procedure),
                hInstance = Win32.GetModuleHandle(null),
                lpszClassName = ClassName,
            };
            Win32.RegisterClassEx(ref windowClass);
            s_registered = true;
        }
        // Click-through and never activated: the panel keeps the pointer capture and the user's program keeps the foreground.
        int exStyle = Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | WsExTransparent;
        _hwnd = Win32.CreateWindowEx(exStyle, ClassName, "", Native.WS_POPUP, 0, 0, 0, 0, 0, 0, Win32.GetModuleHandle(null), 0);
        if (_hwnd == 0)
            Log.Warn("Creating the dock's drag picture window failed: " + Marshal.GetLastWin32Error());
        return _hwnd != 0;
    }

    private bool EnsureBitmap(int width, int height)
    {
        if (_bitmap != 0 && _width == width && _height == height)
            return true;
        ReleaseBitmap();
        if (_dc == 0)
        {
            nint screen = Native.GetDC(0);
            _dc = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(0, screen);
        }
        var header = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        _bitmap = Native.CreateDIBSection(_dc, ref header, 0, out _bits, 0, 0);
        if (_bitmap == 0 || _bits == 0)
        {
            if (_bitmap != 0)
                Native.DeleteObject(_bitmap);
            _bitmap = 0;
            Log.Warn($"The dock's drag picture could not get a {width} x {height} bitmap");
            return false;
        }
        _oldBitmap = Native.SelectObject(_dc, _bitmap);
        _width = width;
        _height = height;
        return true;
    }

    private void ReleaseBitmap()
    {
        if (_bitmap == 0)
            return;
        Native.SelectObject(_dc, _oldBitmap);
        Native.DeleteObject(_bitmap);
        _bitmap = 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Hide();
        _disposed = true;
        if (_hwnd != 0)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
        ReleaseBitmap();
        if (_dc != 0)
        {
            Native.DeleteDC(_dc);
            _dc = 0;
        }
    }
}
