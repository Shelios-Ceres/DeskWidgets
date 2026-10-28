using System.Runtime.InteropServices;
using DeskWidgets.Interop;
using DeskWidgets.Motion;
using DeskWidgets.Pixel;
using Windows.UI.ViewManagement;

namespace DeskWidgets.PixelUi;

/// <summary>
/// A borderless top-level window that shows a <see cref="PixelCanvas"/> 1:1:
/// every art pixel is an exact factor × factor block of screen pixels, pushed
/// to the screen with UpdateLayeredWindow (per-pixel alpha, no XAML, no DWM
/// frame, shadow or rounding). Menus and dialogs derive from it.
/// </summary>
internal abstract class PixelPopupWindow : IDisposable
{
    private const string ClassName = "DeskWidgets.PixelPopup";

    private static readonly Dictionary<nint, PixelPopupWindow> s_windows = [];
    private static readonly Win32.WindowProcedure s_procedure = StaticProc; // must outlive the class
    private static readonly UISettings s_ui = new();
    private static bool s_registered;

    private nint _dc;
    private nint _bitmap;
    private nint _oldBitmap;
    private nint _bits;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private byte[] _buffer = [];

    public nint Hwnd { get; private set; }

    /// <summary>Window size in screen pixels, as last presented.</summary>
    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    /// <summary>Screen position of the window's top-left corner, as last presented or moved (a borderless popup's client area is the whole window).</summary>
    public Win32.POINT Origin { get; private set; }

    public int Factor { get; protected set; } = 3;

    /// <summary>Design QA (<c>--force-dark</c>, <c>--force-light</c>): a look to use whatever the system's mode is; null follows the system.</summary>
    public static bool? Forced { get; set; }

    /// <summary>Whether the system's apps are in dark mode right now (read every time, so a switch shows at the next open).</summary>
    public static bool IsDark()
    {
        if (Forced is { } forced)
            return forced;
        var background = s_ui.GetColorValue(UIColorType.Background);
        return background.R + background.G + background.B < 3 * 128;
    }

    protected void CreateWindow(bool noActivate, nint owner = 0)
    {
        if (!s_registered)
        {
            var windowClass = new Win32.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<Win32.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_procedure),
                hInstance = Win32.GetModuleHandle(null),
                hCursor = Native.LoadCursor(0, 32512 /* IDC_ARROW */),
                lpszClassName = ClassName,
            };
            Win32.RegisterClassEx(ref windowClass);
            s_registered = true;
        }
        int exStyle = Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED | (noActivate ? Native.WS_EX_NOACTIVATE : 0);
        Hwnd = Win32.CreateWindowEx(exStyle, ClassName, "", Native.WS_POPUP, 0, 0, 0, 0, owner, 0, Win32.GetModuleHandle(null), 0);
        if (Hwnd == 0)
            throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        s_windows[Hwnd] = this;
    }

    /// <summary>Paints the canvas into the window and puts the window at (x, y); the window is exactly canvas × factor.</summary>
    protected void Present(PixelCanvas canvas, int factor, int x, int y)
    {
        Factor = factor;
        int width = canvas.Width * factor, height = canvas.Height * factor;
        if (!EnsureBitmap(width, height))
            return; // no bitmap (GDI handles exhausted): the window keeps its last picture rather than writing to address 0
        canvas.CopyScaled(_buffer, factor);
        Marshal.Copy(_buffer, 0, _bits, width * height * 4);

        var destination = new Win32.POINT { X = x, Y = y };
        var size = new Native.SIZE { cx = width, cy = height };
        var source = new Win32.POINT();
        var blend = new Native.BLENDFUNCTION { BlendOp = 0 /* AC_SRC_OVER */, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        Native.UpdateLayeredWindow(Hwnd, 0, ref destination, ref size, _dc, ref source, 0, ref blend, 2 /* ULW_ALPHA */);
        Origin = destination;
        PixelWidth = width;
        PixelHeight = height;
    }

    private bool EnsureBitmap(int width, int height)
    {
        if (_bitmap != 0 && _bitmapWidth == width && _bitmapHeight == height)
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
            Core.Diagnostics.Log.Warn($"A pixel popup could not get a {width} x {height} bitmap (GDI handles exhausted?)");
            return false;
        }
        _oldBitmap = Native.SelectObject(_dc, _bitmap);
        _bitmapWidth = width;
        _bitmapHeight = height;
        _buffer = new byte[width * height * 4];
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

    public void ShowWindow(bool activate) => Native.ShowWindow(Hwnd, activate ? Native.SW_SHOW : Native.SW_SHOWNOACTIVATE);

    public void HideWindow() => Native.ShowWindow(Hwnd, Native.SW_HIDE);

    protected virtual nint OnMessage(uint msg, nint wParam, nint lParam) => Win32.DefWindowProc(Hwnd, msg, wParam, lParam);

    private static nint StaticProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (s_windows.TryGetValue(hwnd, out var window))
        {
            if (msg == Native.WM_NCDESTROY)
            {
                s_windows.Remove(hwnd);
            }
            else
            {
                // Dragged by its title strip (or moved by anyone else): the hit tests go by Origin, so it follows the window.
                if (msg == Native.WM_MOVE)
                    window.Origin = new Win32.POINT { X = Native.SignedLow(lParam), Y = Native.SignedHigh(lParam) };
                return window.OnMessage(msg, wParam, lParam);
            }
        }
        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>Palette for the system's current light or dark mode.</summary>
    protected static PixelPalette CurrentPalette() => PixelPalette.For(IsDark());

    // ----- B5: GROW and SHRINK (menus, submenus, dialogs opt in) -----

    private StepAnimator? _grow;

    /// <summary>Set when the popup is shrinking away: it takes no input and nothing redraws it.</summary>
    public bool Leaving { get; private set; }

    /// <summary>The popup's empty frame (its own panel at a share of its size, with its shadow) for GROW and SHRINK; null where a popup has none.</summary>
    protected virtual PixelCanvas? EmptyFrame(GrowFrom from, double share) => null;

    /// <summary>Draws the whole popup, content included, at a screen position (the last GROW frame).</summary>
    protected virtual void DrawFinal(int factor, int x, int y)
    {
    }

    /// <summary>
    /// GROW. The window is at its final place and size from the first frame, with a transparent canvas outside the growing panel: the empty
    /// frames in turn (each 40 ms), then the whole popup. With motion off the whole popup comes at once.
    /// </summary>
    public void GrowIn(GrowSpec spec, int factor, int x, int y)
    {
        _grow ??= new StepAnimator();
        _grow.Start(spec.Shares.Length + 1, frame =>
        {
            if (frame < spec.Shares.Length && EmptyFrame(spec.From, spec.Shares[frame]) is { } empty)
                Present(empty, factor, x, y);
            else
                DrawFinal(factor, x, y);
        });
    }

    /// <summary>A new state (input) cuts a running GROW short: the whole popup is drawn by the caller next.</summary>
    public void CancelGrow() => _grow?.Cancel();

    /// <summary>
    /// SHRINK, then the window is disposed (after the last frame, so the popup is not cut off mid-shrink). The window takes no clicks
    /// meanwhile. With motion off it is disposed at once.
    /// </summary>
    public void ShrinkOut(GrowSpec spec)
    {
        if (Leaving || Hwnd == 0)
            return;
        Leaving = true;
        if (!MotionSettings.Enabled || EmptyFrame(spec.From, 0.5) is null)
        {
            Dispose();
            return;
        }
        SetWindowLong(Hwnd, -20, GetWindowLong(Hwnd, -20) | 0x20 /* WS_EX_TRANSPARENT */ | Native.WS_EX_NOACTIVATE);
        _grow ??= new StepAnimator();
        var origin = Origin;
        int factor = Factor;
        _grow.Start(spec.Shares.Length + 1, frame =>
        {
            if (Hwnd == 0)
                return;
            if (frame < spec.Shares.Length && EmptyFrame(spec.From, spec.Shares[spec.Shares.Length - 1 - frame]) is { } empty)
                Present(empty, factor, origin.X, origin.Y);
            else
                Dispose();
        });
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint hwnd, int index, int value);

    public virtual void Dispose()
    {
        _grow?.Dispose();
        _grow = null;
        if (Hwnd != 0)
        {
            Win32.DestroyWindow(Hwnd);
            Hwnd = 0;
        }
        ReleaseBitmap();
        if (_dc != 0)
        {
            Native.DeleteDC(_dc);
            _dc = 0;
        }
    }
}
