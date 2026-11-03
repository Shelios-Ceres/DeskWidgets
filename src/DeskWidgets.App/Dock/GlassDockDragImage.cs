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
        Place(x, y);
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
    public void FlyTo(int x, int y, Action landed)
    {
        if (!_visible || _disposed)
        {
            landed();
            return;
        }
        Claimed = true;
        StopFlight(land: false);
        _landed = landed;
        if (!MotionSettings.Enabled)
        {
            Place(x, y);
            Land();
            return;
        }
        var from = _at;
        double begin = FrameClock.Now, ms = GlassMotion.Move.TotalMilliseconds;
        _flight = FrameClock.Every(16, now =>
        {
            double t = Math.Clamp((now - begin) / ms, 0, 1), k = Bezier.Move.At(t);
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
        if (_hwnd != 0 && _visible)
            Native.ShowWindow(_hwnd, Native.SW_HIDE);
        _visible = false;
    }

    /// <summary>Copies the pixels into the window's bitmap and pushes them (per-pixel alpha); the window is made on the first picture.</summary>
    private bool Paint(byte[] premultipliedBgra, int width, int height)
    {
        if (_hwnd == 0 && !CreateWindow())
            return false;
        if (!EnsureBitmap(width, height))
            return false;
        Marshal.Copy(premultipliedBgra, 0, _bits, Math.Min(premultipliedBgra.Length, width * height * 4));
        var destination = _visible ? _at : new Win32.POINT { X = -32000, Y = -32000 }; // off screen until MoveTo places it
        var size = new Native.SIZE { cx = width, cy = height };
        var source = new Win32.POINT();
        var blend = new Native.BLENDFUNCTION { BlendOp = 0 /* AC_SRC_OVER */, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        Native.UpdateLayeredWindow(_hwnd, 0, ref destination, ref size, _dc, ref source, 0, ref blend, 2 /* ULW_ALPHA */);
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
