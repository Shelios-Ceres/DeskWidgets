using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskWidgets.Dock;

/// <summary>
/// The event-driven answer to "should the dock hide on this monitor" (PROPOSAL 3.1): is a window maximized there, or is the window in front a
/// full-screen one. WinEvent hooks only, no polling: a scan runs once, a moment after something relevant happened (SPIKE section 2).
/// Pure Win32, on the UI thread that made it.
/// </summary>
internal sealed class WindowTracker : IDisposable
{
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MOVESIZEEND = 0x000B, EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    internal const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003;
    internal const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B, EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;

    /// <summary>What a scan found on one monitor.</summary>
    public readonly record struct Verdict(bool Maximized, string? MaximizedWindow, bool ForegroundCovers, int NotificationState, string? ForegroundWindow)
    {
        /// <summary>The dock hides for a maximized window, or for a full-screen one in front (leaning towards hiding).</summary>
        public bool ShouldHide => DockVisibility.ShouldHide(Maximized, ForegroundCovers, NotificationState);

        /// <summary>A pointer or a drag at the edge may not bring a hidden dock back while this is true.</summary>
        public bool FullScreen => !DockVisibility.MayReveal(ForegroundCovers, NotificationState);
    }

    private readonly NativeMethods.WinEventProc _callback; // must outlive the hooks
    private readonly List<nint> _hooks = [];
    private readonly Dictionary<nint, bool> _zoomed = [];
    private readonly Action<Action>? _post;
    private bool _scanQueued;
    private bool _setChanged;
    private nint _foreground;
    private bool _foregroundCovers;

    /// <summary>Raised on the thread that made the tracker, after the events settled. The argument says which event started it.</summary>
    public event Action<string>? Changed;

    /// <summary>
    /// Raised after <see cref="Changed"/> when a taskbar window appeared, went, hid, showed, was cloaked or uncloaked or was minimized: the set of
    /// windows the dock matches its programs against may have changed. Never for a window that only moved (the pointer's and a drag's flood of
    /// <c>LOCATIONCHANGE</c> stays out of this path).
    /// </summary>
    public event Action? WindowSetChanged;

    /// <summary>The windows the dock currently knows as its programs' windows: their destruction is relevant (a destroyed window can no longer be asked).</summary>
    public HashSet<nint> TrackedWindows { get; set; } = [];

    public long Callbacks { get; private set; }

    public long Relevant { get; private set; }

    public long Scans { get; private set; }

    public double ScanMsTotal { get; private set; }

    public double ScanMsMax { get; private set; }

    public double CallbackMsMax { get; private set; }

    /// <param name="postDelayed">Runs an action about 60 ms later on the creating thread (the host's timer); null scans at once.</param>
    public WindowTracker(Action<Action>? postDelayed = null, bool includeLocationChange = true)
    {
        _post = postDelayed;
        _callback = OnEvent;
        foreach (var (min, max) in new (uint, uint)[]
        {
            (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND), (EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND),
            (EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE), (EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED),
        })
            _hooks.Add(NativeMethods.SetWinEventHook(min, max, 0, _callback, 0, 0, 0));
        if (includeLocationChange)
            _hooks.Add(NativeMethods.SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, 0, _callback, 0, 0, 0));
        ScanAll(); // at once: a dock started over a maximized window must not show for a frame before the first scan
    }

    private void OnEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        long begin = Stopwatch.GetTimestamp();
        OnEventCore(ev, hwnd, idObject, idChild);
        CallbackMsMax = Math.Max(CallbackMsMax, Stopwatch.GetElapsedTime(begin).TotalMilliseconds);
    }

    private void OnEventCore(uint ev, nint hwnd, int idObject, int idChild)
    {
        Callbacks++;
        if (idObject != 0 || idChild != 0 || hwnd == 0)
            return; // a caret, a cursor, a child control: not a window
        bool relevant;
        switch (ev)
        {
            case EVENT_OBJECT_LOCATIONCHANGE:
                // The cheap test: only a window whose maximized state differs from what the last scan saw, or the one in front changing between
                // "covers its monitor" and not, can change the answer. A window being dragged around never does.
                bool zoomed = NativeMethods.IsZoomed(hwnd);
                relevant = _zoomed.TryGetValue(hwnd, out bool known) ? known != zoomed : zoomed;
                if (!relevant && hwnd == _foreground && NativeMethods.CoversMonitor(hwnd) != _foregroundCovers)
                    relevant = true;
                break;
            case EVENT_OBJECT_CREATE or EVENT_OBJECT_DESTROY or EVENT_OBJECT_SHOW or EVENT_OBJECT_HIDE:
                // Only a top-level window the taskbar would list can change the answer (a Chrome video fires a burst of tooltip and overlay
                // windows: 10 scans a second before this filter). A destroyed window can no longer be asked: it counts if the last scan saw it maximized.
                relevant = ev == EVENT_OBJECT_DESTROY
                    ? _zoomed.ContainsKey(hwnd) || hwnd == _foreground || TrackedWindows.Contains(hwnd)
                    : NativeMethods.GetAncestor(hwnd, 2 /* GA_ROOT */) == hwnd && NativeMethods.IsTaskbarWindow(hwnd);
                break;
            default:
                relevant = true;
                break;
        }
        if (!relevant)
            return;
        if (ev != EVENT_SYSTEM_FOREGROUND && ev != EVENT_OBJECT_LOCATIONCHANGE)
            _setChanged = true;
        Relevant++;
        Rescan(NativeMethods.EventName(ev));
    }

    private void Rescan(string reason)
    {
        if (_scanQueued)
            return;
        _scanQueued = true;
        void Run()
        {
            _scanQueued = false;
            var sw = Stopwatch.StartNew();
            ScanAll();
            double ms = sw.Elapsed.TotalMilliseconds;
            Scans++;
            ScanMsTotal += ms;
            ScanMsMax = Math.Max(ScanMsMax, ms);
            Changed?.Invoke(reason);
            if (_setChanged)
            {
                _setChanged = false;
                WindowSetChanged?.Invoke();
            }
        }
        if (_post is null)
            Run();
        else
            _post(Run);
    }

    private readonly Dictionary<nint, (string Name, bool Maximized)> _maximizedByMonitor = [];

    private void ScanAll()
    {
        _zoomed.Clear();
        _maximizedByMonitor.Clear();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
                return true;
            bool zoomed = NativeMethods.IsZoomed(hwnd);
            if (!zoomed)
                return true;
            _zoomed[hwnd] = true;
            if (NativeMethods.IsTaskbarWindow(hwnd))
            {
                nint monitor = NativeMethods.MonitorFromWindow(hwnd, 2);
                _maximizedByMonitor[monitor] = (NativeMethods.Describe(hwnd), true);
            }
            return true;
        }, 0);
        _foreground = NativeMethods.GetForegroundWindow();
        _foregroundCovers = _foreground != 0 && !NativeMethods.IsShellSurface(_foreground) && NativeMethods.CoversMonitor(_foreground);
    }

    /// <summary>The answer for <paramref name="monitor"/> as of the last scan.</summary>
    public Verdict Evaluate(nint monitor)
    {
        NativeMethods.SHQueryUserNotificationState(out int state);
        bool covers = _foregroundCovers && NativeMethods.MonitorFromWindow(_foreground, 2) == monitor;
        return new Verdict(
            _maximizedByMonitor.TryGetValue(monitor, out var max), max.Name,
            covers, state, _foreground == 0 ? null : NativeMethods.Describe(_foreground));
    }

    public void Dispose()
    {
        foreach (nint hook in _hooks)
            NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();
    }

    internal static class NativeMethods
    {
        public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

        public delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
        [DllImport("user32.dll")] public static extern bool IsIconic(nint h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(nint h);
        [DllImport("user32.dll")] public static extern nint GetWindow(nint h, uint cmd);
        [DllImport("user32.dll")] public static extern nint GetAncestor(nint h, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint h, int index);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(nint h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint h, StringBuilder s, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(nint h, StringBuilder s, int max);
        [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] public static extern bool GetMonitorInfo(nint m, ref MONITORINFO info);
        [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint h, int attr, out int value, int size);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h, out uint pid);

        public static string ClassOf(nint h)
        {
            var s = new StringBuilder(128);
            GetClassName(h, s, s.Capacity);
            return s.ToString();
        }

        public static bool IsShellSurface(nint h) => ClassOf(h) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

        /// <summary>The taskbar's own idea of a window: visible, unowned, not a tool window (unless it asks for the taskbar), not cloaked, not a shell surface, not ours.</summary>
        public static bool IsTaskbarWindow(nint h)
        {
            bool owned = GetWindow(h, 4 /* GW_OWNER */) != 0;
            bool cloaked = DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int cloakedValue, 4) == 0 && cloakedValue != 0;
            GetWindowThreadProcessId(h, out uint pid);
            return CountsOnTheTaskbar(owned, IsShellSurface(h), (long)GetWindowLongPtr(h, -20), cloaked, pid == (uint)Environment.ProcessId);
        }

        /// <summary>
        /// The taskbar's own idea of a window, as a rule on facts (so it can be tested without windows): not owned by another window, not a shell
        /// surface, not a tool window unless it asks for the taskbar (<c>WS_EX_APPWINDOW</c>), not <c>WS_EX_NOACTIVATE</c> (a dock or an overlay),
        /// not cloaked (another virtual desktop), and not ours.
        /// </summary>
        public static bool CountsOnTheTaskbar(bool owned, bool shellSurface, long exStyle, bool cloaked, bool ours)
        {
            if (owned || shellSurface || cloaked || ours)
                return false;
            if ((exStyle & 0x80) != 0 && (exStyle & 0x40000) == 0) // TOOLWINDOW without APPWINDOW
                return false;
            return (exStyle & 0x08000000) == 0;
        }

        public static bool CoversMonitor(nint h)
        {
            if (!GetWindowRect(h, out var r))
                return false;
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref info))
                return false;
            return r.Left <= info.rcMonitor.Left && r.Top <= info.rcMonitor.Top && r.Right >= info.rcMonitor.Right && r.Bottom >= info.rcMonitor.Bottom;
        }

        public static string Describe(nint h)
        {
            var title = new StringBuilder(64);
            GetWindowText(h, title, title.Capacity);
            return $"{ClassOf(h)} \"{title}\"";
        }

        public static string EventName(uint e) => e switch
        {
            EVENT_SYSTEM_FOREGROUND => "FOREGROUND", EVENT_SYSTEM_MINIMIZESTART => "MINIMIZESTART", EVENT_SYSTEM_MINIMIZEEND => "MINIMIZEEND",
            EVENT_OBJECT_CREATE => "CREATE", EVENT_OBJECT_DESTROY => "DESTROY", EVENT_OBJECT_SHOW => "SHOW", EVENT_OBJECT_HIDE => "HIDE",
            EVENT_OBJECT_LOCATIONCHANGE => "LOCATIONCHANGE", EVENT_OBJECT_CLOAKED => "CLOAKED", EVENT_OBJECT_UNCLOAKED => "UNCLOAKED", _ => "0x" + e.ToString("X"),
        };
    }
}
