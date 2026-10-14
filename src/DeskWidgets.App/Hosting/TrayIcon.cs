using System.Runtime.InteropServices;
using DeskWidgets.Interop;

namespace DeskWidgets.Hosting;

/// <summary>
/// Notification-area icon with a native popup menu. Callbacks go to a hidden
/// top-level window (not message-only, so it also gets the TaskbarCreated
/// broadcast and re-adds the icon after Explorer restarts).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = Win32.WM_APP + 1;
    private const string WindowClass = "DeskWidgets.TrayWindow";

    private readonly Win32.WindowProcedure _windowProcedure; // must outlive the window class
    private readonly nint _hwnd;
    private readonly nint _icon;
    private readonly uint _taskbarCreatedMessage;
    private string _tooltip;

    public TrayIcon(string tooltip, string iconPath)
    {
        _tooltip = tooltip;
        _windowProcedure = WindowProc;
        nint instance = Win32.GetModuleHandle(null);
        var windowClass = new Win32.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            hInstance = instance,
            lpszClassName = WindowClass,
        };
        Win32.RegisterClassEx(ref windowClass);
        _hwnd = Win32.CreateWindowEx(0, WindowClass, tooltip, 0, 0, 0, 0, 0, 0, 0, instance, 0);
        _taskbarCreatedMessage = Win32.RegisterWindowMessage("TaskbarCreated");
        _icon = Win32.LoadImage(0, iconPath, 1 /* IMAGE_ICON */,
            Win32.GetSystemMetrics(Win32.SM_CXSMICON), Win32.GetSystemMetrics(Win32.SM_CYSMICON), 0x10 /* LR_LOADFROMFILE */);
        Add();
    }

    /// <summary>Raised with the cursor's screen position when the icon is clicked.</summary>
    public event Action<int, int>? MenuRequested;

    /// <summary>Shows a native popup menu and returns the chosen command id (0 when dismissed).</summary>
    public uint ShowMenu(nint menu, int x, int y)
    {
        // Foreground first, or the menu does not close when clicking elsewhere.
        Win32.SetForegroundWindow(_hwnd);
        uint command = Win32.TrackPopupMenuEx(menu,
            Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON | Win32.TPM_NONOTIFY | Win32.TPM_BOTTOMALIGN, x, y, _hwnd, 0);
        Win32.PostMessage(_hwnd, Win32.WM_NULL, 0, 0);
        return command;
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        var data = CreateData();
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
    }

    private void Add()
    {
        var data = CreateData();
        Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data);
    }

    private Win32.NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == CallbackMessage)
        {
            uint mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMessage is Win32.WM_LBUTTONUP or Win32.WM_RBUTTONUP && Win32.GetCursorPos(out var point))
                MenuRequested?.Invoke(point.X, point.Y);
            return 0;
        }
        if (msg == _taskbarCreatedMessage && msg != 0)
        {
            Add();
            return 0;
        }
        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        var data = CreateData();
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
        if (_icon != 0)
            Win32.DestroyIcon(_icon);
        Win32.DestroyWindow(_hwnd);
    }
}
