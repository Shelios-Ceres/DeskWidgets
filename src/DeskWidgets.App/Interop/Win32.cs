using System.Runtime.InteropServices;
using System.Text;

namespace DeskWidgets.Interop;

/// <summary>The Win32 surface the host needs that WinUI / AppWindow do not expose.</summary>
internal static class Win32
{
    public const uint WM_NULL = 0x0000;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_WINDOWPOSCHANGING = 0x0046;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_NCCALCSIZE = 0x0083;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_APP = 0x8000;

    public static readonly nint HWND_TOP = 0;
    public static readonly nint HWND_BOTTOM = 1;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_POPUP = 0x80000000L;
    private const long WS_CAPTION = 0x00C00000L; // WS_BORDER | WS_DLGFRAME
    private const long WS_SYSMENU = 0x00080000L;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_MINIMIZEBOX = 0x00020000L;
    private const long WS_MAXIMIZEBOX = 0x00010000L;
    private const long WS_EX_DLGMODALFRAME = 0x00000001L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_WINDOWEDGE = 0x00000100L;
    private const long WS_EX_CLIENTEDGE = 0x00000200L;
    private const long WS_EX_STATICEDGE = 0x00020000L;
    private const long WS_EX_APPWINDOW = 0x00040000L;

    public const uint MONITOR_DEFAULTTONULL = 0;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    public const int SM_CXSMICON = 49;
    public const int SM_CXDRAG = 68;
    public const int SM_CYDRAG = 69;
    public const int SM_CYSMICON = 50;

    public const uint MF_STRING = 0x0000;
    public const uint MF_GRAYED = 0x0001;
    public const uint MF_CHECKED = 0x0008;
    public const uint MF_POPUP = 0x0010;
    public const uint MF_SEPARATOR = 0x0800;

    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_BOTTOMALIGN = 0x0020;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RETURNCMD = 0x0100;

    public const uint NIM_ADD = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;
    public const uint NIF_MESSAGE = 0x1;
    public const uint NIF_ICON = 0x2;
    public const uint NIF_TIP = 0x4;

    public const uint MB_YESNO = 0x0004;
    public const uint MB_ICONQUESTION = 0x0020;
    public const uint MB_ICONWARNING = 0x0030;
    public const uint MB_DEFBUTTON2 = 0x0100;
    public const int IDYES = 6;
    public const uint MB_SETFOREGROUND = 0x00010000;
    public const uint MB_TOPMOST = 0x00040000;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public nint hwnd;
        public nint hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    /// <summary>A monitor: its device name (e.g. \\.\DISPLAY1), whether it is the primary one, and its work area.</summary>
    public readonly record struct MonitorArea(string Device, bool Primary, RECT Work);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    public delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint idSubclass, nuint refData);

    public delegate nint WindowProcedure(nint hWnd, uint msg, nint wParam, nint lParam);

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfoEx(nint monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(nint hWnd, ref POINT point);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    public static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(nint hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    public static extern int MessageBox(nint hWnd, string text, string caption, uint type);

    [DllImport("comctl32.dll")]
    public static extern bool SetWindowSubclass(nint hWnd, SubclassProc subclassProc, nuint idSubclass, nuint refData);

    [DllImport("comctl32.dll")]
    public static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc subclassProc, nuint idSubclass);

    [DllImport("comctl32.dll")]
    public static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowTextW")]
    public static extern bool SetWindowText(nint hWnd, string text);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        public int fEnable;
        public nint hRgnBlur;
        public int fTransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref DWM_BLURBEHIND blurBehind);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int index);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint hdc, ref RECT rect, nint brush);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out RECT rect);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint LoadImage(nint instance, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(nint icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll")]
    public static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenu(nint menu, uint flags, nuint idNewItem, string? newItem);

    [DllImport("user32.dll")]
    public static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hWnd, nint tpmParams);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(nint menu);

    // Undocumented but long-stable uxtheme ordinals that let Win32 popup menus follow dark mode.
    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    /// <summary>
    /// Turns a WinUI window into a frameless, per-pixel transparent tool
    /// window that shows only what XAML draws (the widget's own rounded card).
    /// WinUI renders into a child bridge window without a redirection surface;
    /// the top-level window, however, keeps a dialog frame and an opaque
    /// surface, so both are removed here.
    /// </summary>
    public static void MakeTransparentToolWindow(nint hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_STYLE);
        style = (style & ~(WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)) | WS_POPUP;
        SetWindowLongPtr(hwnd, GWL_STYLE, (nint)style);
        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        exStyle = (exStyle & ~(WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE | WS_EX_APPWINDOW)) | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)exStyle);
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        int doNotRound = 1; // DWMWCP_DONOTROUND
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref doNotRound, sizeof(int));
        int noColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref noColor, sizeof(int));
        // Lets this Win32 window use host-backdrop brushes (the live blur in GlassBackdrop).
        int useHostBackdrop = 1;
        DwmSetWindowAttribute(hwnd, 17 /* DWMWA_USE_HOSTBACKDROPBRUSH */, ref useHostBackdrop, sizeof(int));

        // Blur-behind with an empty region: no blur, but DWM now honours the
        // alpha channel of the window's surface, so cleared pixels are see-through.
        nint emptyRegion = CreateRectRgn(0, 0, -1, -1);
        var blurBehind = new DWM_BLURBEHIND { dwFlags = 0x1 | 0x2 /* DWM_BB_ENABLE | DWM_BB_BLURREGION */, fEnable = 1, hRgnBlur = emptyRegion };
        DwmEnableBlurBehindWindow(hwnd, ref blurBehind);
        DeleteObject(emptyRegion);
    }

    /// <summary>WM_ERASEBKGND handler: black with zero alpha is "fully transparent" to DWM.</summary>
    public static void EraseTransparent(nint hwnd, nint hdc)
    {
        GetClientRect(hwnd, out var rect);
        FillRect(hdc, ref rect, GetStockObject(4 /* BLACK_BRUSH */));
    }

    public static RECT GetWorkArea(POINT point)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST), ref info);
        return info.rcWork;
    }

    /// <summary>Every monitor, in no particular order.</summary>
    public static List<MonitorArea> GetMonitors()
    {
        var monitors = new List<MonitorArea>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            if (DescribeMonitor(monitor) is { } area)
                monitors.Add(area);
            return true;
        }, 0);
        return monitors;
    }

    /// <summary>The monitor nearest to <paramref name="point"/>.</summary>
    public static MonitorArea? MonitorAt(POINT point) => DescribeMonitor(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST));

    private static MonitorArea? DescribeMonitor(nint monitor)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
        return GetMonitorInfoEx(monitor, ref info) ? new MonitorArea(info.szDevice, (info.dwFlags & 1) != 0 /* MONITORINFOF_PRIMARY */, info.rcWork) : null;
    }

    public static double GetScaleAt(POINT point)
    {
        nint monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
    }

    /// <summary>
    /// Icon spacing of the desktop list view (the desktop icon grid cell), in
    /// physical pixels, or null when the desktop is not reachable.
    /// </summary>
    public static (int Width, int Height)? GetDesktopIconSpacing()
    {
        nint list = FindDesktopListView();
        if (list == 0)
            return null;
        const uint LVM_GETITEMSPACING = 0x1033;
        const uint SMTO_ABORTIFHUNG = 0x0002;
        if (SendMessageTimeout(list, LVM_GETITEMSPACING, 0, 0, SMTO_ABORTIFHUNG, 200, out nint spacing) == 0)
            return null;
        int width = (int)(spacing & 0xFFFF), height = (int)((spacing >> 16) & 0xFFFF);
        return width > 0 && height > 0 ? (width, height) : null;
    }

    /// <summary>
    /// Whether the window under <paramref name="point"/> belongs to the desktop
    /// (Progman, or the WorkerW that hosts the icon view): not a widget, not an
    /// application window.
    /// </summary>
    public static bool IsDesktopAt(POINT point)
    {
        nint hit = WindowFromPoint(point);
        if (hit == 0)
            return false;
        nint root = GetAncestor(hit, 2 /* GA_ROOT */);
        var className = new StringBuilder(32);
        GetClassName(root == 0 ? hit : root, className, className.Capacity);
        return className.ToString() is "Progman" or "WorkerW";
    }

    private static nint FindDesktopListView()
    {
        // Progman hosts the icon view; with some wallpaper setups it lives in a WorkerW instead.
        nint defView = FindWindowEx(FindWindow("Progman", null), 0, "SHELLDLL_DefView", null);
        for (nint worker = 0; defView == 0;)
        {
            worker = FindWindowEx(0, worker, "WorkerW", null);
            if (worker == 0)
                break;
            defView = FindWindowEx(worker, 0, "SHELLDLL_DefView", null);
        }
        return defView == 0 ? 0 : FindWindowEx(defView, 0, "SysListView32", null);
    }

    public static void TryEnableDarkMenus()
    {
        try
        {
            SetPreferredAppMode(1); // AllowDark: follow the system app theme
            FlushMenuThemes();
        }
        catch (Exception)
        {
            // Older or future builds without the ordinals keep light menus.
        }
    }
}
