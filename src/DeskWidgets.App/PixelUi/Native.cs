using System.Runtime.InteropServices;
using DeskWidgets.Interop;

namespace DeskWidgets.PixelUi;

/// <summary>Win32 and MSAA entry points for the Pixel popups (menus and dialogs).</summary>
internal static class Native
{
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_GETOBJECT = 0x003D;
    public const uint WM_NCDESTROY = 0x0082;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_MOUSELEAVE = 0x02A3;
    public const int HTCLIENT = 1;
    public const int HTCAPTION = 2;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_CAPTURECHANGED = 0x0215;

    public const int MA_NOACTIVATE = 3;
    public const int WA_INACTIVE = 0;

    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_SHOW = 5;

    public const int VK_BACK = 0x08;
    public const int VK_TAB = 0x09;
    public const int VK_RETURN = 0x0D;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_SPACE = 0x20;
    public const int VK_PRIOR = 0x21;
    public const int VK_NEXT = 0x22;
    public const int VK_END = 0x23;
    public const int VK_HOME = 0x24;
    public const int VK_LEFT = 0x25;
    public const int VK_UP = 0x26;
    public const int VK_RIGHT = 0x27;
    public const int VK_DOWN = 0x28;

    public const int OBJID_WINDOW = 0;
    public const int OBJID_CLIENT = -4;
    public const uint EVENT_SYSTEM_MENUSTART = 0x0004;
    public const uint EVENT_SYSTEM_MENUEND = 0x0005;
    public const uint EVENT_SYSTEM_MENUPOPUPSTART = 0x0006;
    public const uint EVENT_SYSTEM_MENUPOPUPEND = 0x0007;
    public const uint EVENT_SYSTEM_DIALOGSTART = 0x0010;
    public const uint EVENT_SYSTEM_DIALOGEND = 0x0011;
    public const uint EVENT_OBJECT_FOCUS = 0x8005;

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref Win32.POINT pptDst, ref SIZE psize, nint hdcSrc, ref Win32.POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint flags);

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    public static extern nint CreateDIBSection(nint hdc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public int cbSize;
        public uint dwFlags;
        public nint hwndTrack;
        public uint dwHoverTime;
    }

    [DllImport("user32.dll")]
    public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT track);

    /// <summary>Asks for one WM_MOUSELEAVE when the pointer leaves the window.</summary>
    public static void TrackLeave(nint hwnd)
    {
        var track = new TRACKMOUSEEVENT { cbSize = Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = 0x2 /* TME_LEAVE */, hwndTrack = hwnd };
        TrackMouseEvent(ref track);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [DllImport("user32.dll")]
    public static extern nint CreateIconIndirect(ref ICONINFO info);

    [DllImport("gdi32.dll")]
    public static extern nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, nint bits);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    public static extern nint SetCapture(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern nint GetCapture();

    [DllImport("user32.dll")]
    public static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint GetFocus();

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool EnableWindow(nint hwnd, bool enable);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hwnd, out Win32.RECT rect);

    [DllImport("user32.dll")]
    public static extern nuint SetTimer(nint hwnd, nuint id, uint elapse, nint proc);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(nint hwnd, nuint id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint LoadCursor(nint instance, nint name);

    [DllImport("user32.dll")]
    public static extern void NotifyWinEvent(uint eventId, nint hwnd, int idObject, int idChild);

    [DllImport("user32.dll")]
    public static extern uint GetGuiResources(nint process, uint flags);

    [DllImport("kernel32.dll")]
    public static extern nint GetCurrentProcess();

    [DllImport("oleacc.dll")]
    public static extern nint LresultFromObject(ref Guid riid, nint wParam, nint unknown);

    [DllImport("oleacc.dll")]
    public static extern int CreateStdAccessibleObject(nint hwnd, int objectId, ref Guid riid, out nint accessible);

    public static Guid IidIAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    public static int LowWord(nint value) => (int)(value.ToInt64() & 0xFFFF);

    public static int SignedLow(nint value) => (short)(value.ToInt64() & 0xFFFF);

    public static int SignedHigh(nint value) => (short)((value.ToInt64() >> 16) & 0xFFFF);
}
