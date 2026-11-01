using System.Runtime.InteropServices;
using System.Text;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Widgets.Folder;
using Microsoft.Win32.SafeHandles;

namespace DeskWidgets.Dock;

/// <summary>What the dock shows an icon as (PROPOSAL 3.2): idle, opening (a program was started and its first window has not come), or running.</summary>
internal enum DockRunState
{
    Idle,
    Opening,
    Running,
}

/// <summary>How an icon got to its new <see cref="DockRunState"/>, so the surface can animate it (Glass D3 to D5, Pixel's hop and dissolves).</summary>
internal enum DockRunTransition
{
    /// <summary>No motion (a first draw, a redraw).</summary>
    None,

    /// <summary>The user clicked it: the opening state starts (D3).</summary>
    Opening,

    /// <summary>Its first window came after the user started it from the dock (D4: the pop).</summary>
    OpenedHere,

    /// <summary>It is running although it was not started here: it wipes in without the pop (D4).</summary>
    OpenedElsewhere,

    /// <summary>Its last window went (D5).</summary>
    Closed,

    /// <summary>No window came within the time: back to idle, not an error (D3's end).</summary>
    TimedOut,
}

/// <summary>What a taskbar window says about its program: the window's own AppUserModelID, its process's, and its exe (SPIKE section 3).</summary>
internal sealed record WindowIdentity(string? WindowAumid, string? ProcessAumid, string? ExePath);

/// <summary>A taskbar window of some program.</summary>
internal readonly record struct RunningWindow(nint Handle, WindowIdentity Identity);

/// <summary>Which dock programs are running, by matching their taskbar windows (PROPOSAL 3.8). The matching is pure; reading a window is Win32.</summary>
internal static class DockRunning
{
    /// <summary>
    /// Whether <paramref name="window"/> is a window of <paramref name="item"/>. In order: the window's AppUserModelID, then its process's (Electron
    /// Store apps such as Claude have none on the window), then the exe path (a known-folder-relative "AUMID" such as WeChat's is expanded to the
    /// exe); last, the launcher table the icons already use: two different exes of one custom-icon app (DingTalk's launcher and its window) are
    /// the same program. Never by title and never by process id. A program with only a URL (a Steam link) has no running state.
    /// </summary>
    public static bool Matches(DockItem item, WindowIdentity window)
    {
        if (!item.SupportsRunning)
            return false;
        if (!string.IsNullOrEmpty(item.Aumid))
        {
            if (Same(item.Aumid, window.WindowAumid) || Same(item.Aumid, window.ProcessAumid))
                return true;
        }
        foreach (string? candidate in ExePaths(item))
        {
            if (candidate is not null && Same(candidate, window.ExePath))
                return true;
        }
        // The launcher table: both sides name the same custom-icon app.
        string? itemKey = AppIconPack.Match(item.Identity());
        if (itemKey is not null)
        {
            string? windowKey = AppIconPack.Match(new AppIdentity(window.WindowAumid ?? window.ProcessAumid, window.ExePath));
            if (windowKey == itemKey)
                return true;
        }
        return false;
    }

    private static bool Same(string a, string? b) => b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string?> ExePaths(DockItem item)
    {
        yield return item.ExePath;
        if (item.Aumid is { Length: > 0 } aumid && DockIdentity.SplitKnownFolderName(aumid) is { } known && Shell.KnownFolder(known.Folder) is { } folder)
            yield return Path.Combine(folder, known.Remainder);
    }

    /// <summary>The windows of one program, in the order they were given (z-order, front first).</summary>
    public static IReadOnlyList<RunningWindow> WindowsOf(DockItem item, IReadOnlyList<RunningWindow> all)
        => [.. all.Where(w => Matches(item, w.Identity))];

    // ---- click: bring forward, minimize, cycle (PROPOSAL 3.8) ----

    internal enum SwitchKind
    {
        Activate,
        Minimize,
    }

    internal readonly record struct SwitchPlan(SwitchKind Kind, nint Window);

    /// <summary>
    /// What a click on a running program does. Not in front: bring its frontmost window forward. In front with one window: minimize it. In front
    /// with several: bring the next one forward. The cycle goes through the windows in a fixed order (by handle), so every click reaches a new one
    /// and the front pair does not just swap places.
    /// </summary>
    public static SwitchPlan PlanSwitch(IReadOnlyList<nint> windowsFrontFirst, nint foreground)
    {
        if (windowsFrontFirst.Count == 0)
            throw new ArgumentException("A running program has at least one window.", nameof(windowsFrontFirst));
        if (!windowsFrontFirst.Contains(foreground))
            return new SwitchPlan(SwitchKind.Activate, windowsFrontFirst[0]);
        if (windowsFrontFirst.Count == 1)
            return new SwitchPlan(SwitchKind.Minimize, foreground);
        var stable = windowsFrontFirst.OrderBy(h => h).ToList();
        int index = stable.IndexOf(foreground);
        return new SwitchPlan(SwitchKind.Activate, stable[(index + 1) % stable.Count]);
    }
}

/// <summary>
/// The opening/running/closing states of a dock's icons, and when they change (D3 to D5). Pure: the controller feeds it the set of programs that
/// have a window, and the time. An opening state holds until the program's first window comes and gives up after <see cref="OpeningLimit"/>.
/// </summary>
internal sealed class DockRunStates
{
    /// <summary>How long a started program may take to show its first window (measured: Chrome 0.21 s, a cold Word 5.1 s).</summary>
    public static readonly TimeSpan OpeningLimit = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, DockRunState> _state = [];
    private readonly Dictionary<string, TimeSpan> _startedAt = [];

    public DockRunState StateOf(string key) => _state.GetValueOrDefault(key);

    /// <summary>The user started the program: it is opening, unless it is already running (then nothing changes). Returns whether the state changed.</summary>
    public bool Started(string key, TimeSpan now)
    {
        if (_state.GetValueOrDefault(key) != DockRunState.Idle)
            return false;
        _state[key] = DockRunState.Opening;
        _startedAt[key] = now;
        return true;
    }

    /// <summary>
    /// The programs that have a taskbar window now. Returns every change: Idle or Opening to Running (<see cref="DockRunTransition.OpenedHere"/> if it
    /// was opening, else <see cref="DockRunTransition.OpenedElsewhere"/>), and Running to Idle (<see cref="DockRunTransition.Closed"/>).
    /// </summary>
    public List<(string Key, DockRunState State, DockRunTransition How)> WindowsNow(IReadOnlySet<string> runningKeys, IEnumerable<string> knownKeys)
    {
        var changes = new List<(string, DockRunState, DockRunTransition)>();
        foreach (string key in knownKeys)
        {
            var before = _state.GetValueOrDefault(key);
            bool has = runningKeys.Contains(key);
            if (has && before != DockRunState.Running)
            {
                _state[key] = DockRunState.Running;
                _startedAt.Remove(key);
                changes.Add((key, DockRunState.Running, before == DockRunState.Opening ? DockRunTransition.OpenedHere : DockRunTransition.OpenedElsewhere));
            }
            else if (!has && before == DockRunState.Running)
            {
                _state[key] = DockRunState.Idle;
                changes.Add((key, DockRunState.Idle, DockRunTransition.Closed));
            }
        }
        return changes;
    }

    /// <summary>The start failed (or the program was not there): an opening state goes back to idle at once. Returns whether it was opening.</summary>
    public bool Cancel(string key)
    {
        if (_state.GetValueOrDefault(key) != DockRunState.Opening)
            return false;
        _state[key] = DockRunState.Idle;
        _startedAt.Remove(key);
        return true;
    }

    /// <summary>Opening states that have waited too long go back to idle.</summary>
    public List<string> Expire(TimeSpan now)
    {
        var expired = _startedAt.Where(p => _state.GetValueOrDefault(p.Key) == DockRunState.Opening && now - p.Value >= OpeningLimit).Select(p => p.Key).ToList();
        foreach (string key in expired)
        {
            _state[key] = DockRunState.Idle;
            _startedAt.Remove(key);
        }
        return expired;
    }

    /// <summary>The earliest time an opening state runs out, or null when none is opening (so a timer can sleep until then, and not at all otherwise).</summary>
    public TimeSpan? NextExpiry()
    {
        TimeSpan? next = null;
        foreach (var (key, start) in _startedAt)
        {
            if (_state.GetValueOrDefault(key) != DockRunState.Opening)
                continue;
            var due = start + OpeningLimit;
            if (next is null || due < next)
                next = due;
        }
        return next;
    }

    /// <summary>A program left the dock: forget it.</summary>
    public void Forget(string key)
    {
        _state.Remove(key);
        _startedAt.Remove(key);
    }
}

/// <summary>Reads the taskbar's windows and what each says about its program (Win32; cached per window).</summary>
internal sealed class RunningScanner
{
    private readonly Dictionary<nint, (uint Pid, WindowIdentity Identity)> _cache = [];

    /// <summary>The taskbar windows now, front first.</summary>
    public List<RunningWindow> Scan()
    {
        var found = new List<RunningWindow>();
        var seen = new HashSet<nint>();
        WindowTracker.NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!WindowTracker.NativeMethods.IsWindowVisible(hwnd) || !WindowTracker.NativeMethods.IsTaskbarWindow(hwnd) || !HasTitle(hwnd))
                return true;
            seen.Add(hwnd);
            found.Add(new RunningWindow(hwnd, IdentityOf(hwnd)));
            return true;
        }, 0);
        foreach (nint gone in _cache.Keys.Where(h => !seen.Contains(h)).ToList())
            _cache.Remove(gone);
        return found;
    }

    private static bool HasTitle(nint hwnd)
    {
        var title = new StringBuilder(2);
        return WindowTracker.NativeMethods.GetWindowText(hwnd, title, 2) > 0;
    }

    private WindowIdentity IdentityOf(nint hwnd)
    {
        WindowTracker.NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (_cache.TryGetValue(hwnd, out var known) && known.Pid == pid)
            return known.Identity;
        var identity = new WindowIdentity(WindowAumid(hwnd), ProcessAumid(pid), ExePath(pid));
        _cache[hwnd] = (pid, identity);
        return identity;
    }

    // ---- the three reads (limited rights, so elevated apps work) ----

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    private static string? WindowAumid(nint hwnd)
    {
        try
        {
            var iid = IID_IPropertyStore;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out nint store) != 0 || store == 0)
                return null;
            try
            {
                var key = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
                // IPropertyStore::GetValue is the 6th slot of the vtable (IUnknown 3, then GetCount, GetAt, GetValue).
                nint vtable = Marshal.ReadIntPtr(store);
                var getValue = Marshal.GetDelegateForFunctionPointer<GetValueFn>(Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size));
                var value = new PropVariant();
                if (getValue(store, ref key, ref value) != 0)
                    return null;
                try
                {
                    if (value.vt != 31 /* VT_LPWSTR */ || value.ptr == 0)
                        return null;
                    string? text = Marshal.PtrToStringUni(value.ptr);
                    return string.IsNullOrEmpty(text) ? null : text;
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.Release(store);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Reading a window's AppUserModelID failed", ex);
            return null;
        }
    }

    private static string? ProcessAumid(uint pid)
    {
        using var process = OpenProcess(pid);
        if (process.IsInvalid)
            return null;
        uint length = 130;
        var id = new StringBuilder((int)length);
        return GetApplicationUserModelId(process.DangerousGetHandle(), ref length, id) == 0 ? id.ToString() : null;
    }

    private static string? ExePath(uint pid)
    {
        using var process = OpenProcess(pid);
        if (process.IsInvalid)
            return null;
        var name = new StringBuilder(1024);
        int size = name.Capacity;
        return QueryFullProcessImageName(process.DangerousGetHandle(), 0, name, ref size) ? name.ToString() : null;
    }

    private static SafeProcessHandle OpenProcess(uint pid) => new(OpenProcessNative(PROCESS_QUERY_LIMITED_INFORMATION, false, pid), ownsHandle: true);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey(Guid format, uint id)
    {
        public Guid Format = format;
        public uint Id = id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public nint ptr;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetValueFn(nint self, ref PropertyKey key, ref PropVariant value);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid iid, out nint store);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess")]
    private static extern nint OpenProcessNative(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder id);
}
