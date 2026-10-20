using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Hosting;
using DeskWidgets.Interop;

namespace DeskWidgets.Desktop;

internal enum IconArrangerState
{
    Off,
    Waiting,
    Arranging,
}

/// <param name="Problem">Why the arranger is waiting, in the UI language; null when all is well.</param>
internal sealed record IconArrangerStatus(IconArrangerState State, int IconCount, string? Problem);

/// <summary>
/// Keeps the desktop icons arranged from the top-right corner: the standalone
/// DesktopRightAlign tool, merged into Desk Widgets. It works on its own STA
/// thread, so a busy or restarting Explorer never stalls the widgets. Every two
/// seconds, or soon after the desktop folder or a widget changes, it reads the
/// icons, plans the layout around the widgets' cards, moves only misplaced icons
/// and reads them back to confirm Explorer kept them. Nothing moves while the
/// user drags, has a menu open or renames an icon, nor while the old tool still
/// runs (the two would fight over the same icons). An icon the user drops
/// somewhere stays there: the first pass after a drag takes the new place as
/// the icon's place in the order, and the others shift to make room. Turning
/// arranging back on takes the icons' order as it is then.
/// </summary>
internal sealed class IconArranger : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>How often the mouse is sampled, so that even a quick drag is seen.</summary>
    private static readonly TimeSpan MouseInterval = TimeSpan.FromMilliseconds(40);

    /// <summary>After a drag ends, Explorer needs a moment to record where the icon landed.</summary>
    private static readonly TimeSpan DropSettle = TimeSpan.FromMilliseconds(300);

    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly IconLayoutSettings _layout; // used on the arranger's thread only
    private volatile IReadOnlyDictionary<string, Insets> _margins;
    private volatile Win32.RECT[] _blocked = [];
    private volatile bool _enabled;
    private volatile bool _changed = true;
    private volatile bool _disposed;
    private volatile bool _adoptCurrentOrder; // set by the user's switching arranging back on
    private readonly DesktopDragWatcher _drag = new(Math.Max(Win32.GetSystemMetrics(Win32.SM_CXDRAG), Win32.GetSystemMetrics(Win32.SM_CYDRAG)));
    private bool _userDragged; // arranger's thread only: a drag ended on the desktop and the next pass should learn from it
    private bool _layoutDirty; // arranger's thread only: the layout changed in memory and is not saved yet
    private DateTime _settleUntil = DateTime.MinValue;
    private DateTime _nextPass = DateTime.MinValue;
    private int _failures;
    private int _iconCount;
    private IconArrangerStatus? _published;

    public IconArranger(bool enabled)
    {
        _enabled = enabled;
        _layout = IconStore.LoadLayout();
        _margins = new Dictionary<string, Insets>(_layout.Margins);
        WatchDesktopFolders();
        _thread = new Thread(Run) { IsBackground = true, Name = "Desktop icons" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Raised on the arranger's thread whenever the status changes.</summary>
    public event Action<IconArrangerStatus>? StatusChanged;

    /// <summary>Raised on the arranger's thread when a monitor's grid margins become known (widgets should re-snap).</summary>
    public event Action? MarginsChanged;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            // Only the user's switching on adopts the icons' order (startup never does:
            // Explorer may have reflowed the icons while the app was not running).
            _adoptCurrentOrder = value && !_enabled;
            _enabled = value;
            Invalidate();
        }
    }

    /// <summary>Checks the layout again soon (desktop folder, display or widget changes).</summary>
    public void Invalidate()
    {
        _changed = true;
        _wake.Set();
    }

    /// <summary>The widgets' cards, in screen pixels: icons keep clear of them.</summary>
    public void SetBlocked(IEnumerable<Win32.RECT> cards)
    {
        var next = cards.ToArray();
        if (next.SequenceEqual(_blocked))
            return;
        _blocked = next;
        Invalidate();
    }

    /// <summary>
    /// The right-aligned grid's margins on the monitor at <paramref name="point"/>,
    /// so widgets can line up with the icon columns; null while arranging is off.
    /// </summary>
    public GridAnchor? AnchorAt(Win32.POINT point)
    {
        if (!_enabled || Win32.GetDesktopIconSpacing() is not (int width, int height))
            return null;
        var insets = Win32.MonitorAt(point) is { } monitor && _margins.TryGetValue(monitor.Device, out var known) ? known : IconLayout.DefaultInsets;
        var (right, top) = IconLayout.MarginPixels(width, height, insets);
        return new GridAnchor(right, top);
    }

    /// <summary>
    /// Read-only check (<c>DeskWidgets.exe --icons-inspect &lt;file&gt;</c>): the
    /// desktop as it is and what a pass would move with <paramref name="cards"/>
    /// in the way, as JSON. Moves nothing and writes no other file.
    /// </summary>
    public static void Inspect(string path, IReadOnlyList<Win32.RECT> cards)
    {
        using var desktop = new ShellDesktop();
        var current = desktop.Capture();
        var layout = IconStore.LoadLayout(save: false);
        IconLayout.Learn(current, layout);
        var blocked = cards.Select(desktop.ToClient).ToList();
        var planned = IconLayout.Plan(current, layout, blocked);
        var positions = current.Items.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
        var report = new
        {
            LegacyToolRunning = LegacyToolRunning(),
            Snapshot = current,
            BlockedInView = blocked.Select(b => new { b.Left, b.Top, b.Right, b.Bottom }),
            Moves = planned
                .Where(p => !IconLayout.SamePosition(p, positions[p.Key]))
                .Select(p => new { p.Key, FromX = positions[p.Key].X, FromY = positions[p.Key].Y, ToX = p.X, ToY = p.Y }),
        };
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(path, JsonSerializer.Serialize(report, options));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _wake.Set();
        _thread.Join(TimeSpan.FromSeconds(3));
        foreach (var watcher in _watchers)
            watcher.Dispose();
    }

    private void Run()
    {
        while (!_disposed)
        {
            _wake.WaitOne(_enabled ? MouseInterval : TimeSpan.FromMilliseconds(500));
            if (_disposed)
                break;
            try
            {
                WatchMouse();
                Pass();
            }
            catch (Exception ex)
            {
                Log.Error("desktop icons: unexpected failure", ex);
            }
        }
    }

    private void Pass()
    {
        if (!_enabled)
        {
            Publish(new IconArrangerStatus(IconArrangerState.Off, _iconCount, null));
            return;
        }
        if (DateTime.UtcNow < _settleUntil)
            return;
        if (DateTime.UtcNow < _nextPass && !_changed)
            return;
        if (UserIsInteracting())
        {
            _nextPass = DateTime.UtcNow.AddMilliseconds(750);
            return;
        }
        _changed = false;
        try
        {
            Reconcile();
            _failures = 0;
            _nextPass = DateTime.UtcNow + Interval;
        }
        catch (Exception ex)
        {
            _failures++;
            _nextPass = DateTime.UtcNow.AddSeconds(Math.Min(30, 2 * _failures));
            if (_failures == 1 || _failures % 30 == 0)
                Log.Warn($"desktop icons: pass failed ({_failures}x)", ex);
            Publish(new IconArrangerStatus(IconArrangerState.Waiting, _iconCount, ex is InvalidOperationException ? ex.Message : Strings.IconsDesktopNotReady));
        }
    }

    private void Reconcile()
    {
        if (LegacyToolRunning())
            throw new InvalidOperationException(Strings.IconsLegacyToolRunning);
        using var desktop = new ShellDesktop();
        var current = desktop.Capture();
        if (current.Items.Count == 0)
        {
            Publish(new IconArrangerStatus(IconArrangerState.Waiting, 0, Strings.IconsWaitingForIcons));
            return;
        }
        if (IconLayout.Learn(current, _layout))
            _layoutDirty = true;
        var blocked = _blocked.Select(desktop.ToClient).ToList();
        if (_adoptCurrentOrder)
        {
            IconLayout.Adopt(current, _layout);
            _adoptCurrentOrder = false;
            _userDragged = false;
            _layoutDirty = true;
            Log.Info($"desktop icons: kept the current order of {current.Items.Count} icons");
        }
        else if (_userDragged)
        {
            int placed = IconLayout.LearnDrops(current, _layout, blocked);
            _userDragged = false;
            if (placed > 0)
            {
                _layoutDirty = true;
                Log.Info($"desktop icons: {placed} icon(s) placed by hand");
            }
        }
        var planned = IconLayout.Plan(current, _layout, blocked); // validates everything before anything moves
        if (_layoutDirty)
        {
            IconStore.SaveLayout(_layout);
            _layoutDirty = false;
            if (_layout.Margins.Count != _margins.Count)
            {
                _margins = new Dictionary<string, Insets>(_layout.Margins);
                MarginsChanged?.Invoke();
            }
        }

        var positions = current.Items.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
        var moves = planned.Where(p => !IconLayout.SamePosition(p, positions[p.Key])).ToList();
        if ((current.Flags & ShellDesktop.LayoutFlags) != 0)
            desktop.SetLayoutFlags(0);
        if (moves.Count > 0)
        {
            desktop.Position(moves);
            // A successful call alone does not prove that Explorer kept the positions.
            var after = desktop.Capture().Items.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
            if (moves.Any(p => !after.TryGetValue(p.Key, out var seen) || !IconLayout.SamePosition(p, seen)))
                throw new InvalidOperationException(Strings.IconsNotKept);
            Log.Info($"desktop icons: moved {moves.Count} of {current.Items.Count}");
        }
        _iconCount = current.Items.Count;
        Publish(new IconArrangerStatus(IconArrangerState.Arranging, _iconCount, null));
    }

    /// <summary>
    /// Samples the left button. A drag that ends after passing over the desktop
    /// makes the next pass learn where the user put the icons, instead of
    /// moving them back.
    /// </summary>
    private void WatchMouse()
    {
        if (!_enabled)
        {
            _drag.Reset();
            _userDragged = false;
            return;
        }
        bool down = (Win32.GetAsyncKeyState(0x01 /* VK_LBUTTON */) & 0x8000) != 0;
        if (!down && !_drag.Pressed)
            return;
        int x = 0, y = 0;
        bool over = false;
        if (down)
        {
            if (!Win32.GetCursorPos(out var cursor))
                return;
            (x, y) = (cursor.X, cursor.Y);
            over = (!_drag.Pressed || _drag.NeedsDesktopCheck) && Win32.IsDesktopAt(cursor);
        }
        if (_drag.Update(down, x, y, over))
        {
            _userDragged = true;
            _settleUntil = DateTime.UtcNow + DropSettle;
            _changed = true;
        }
    }

    /// <summary>Never move an icon while the user drags something, has a menu open or is renaming an icon.</summary>
    private static bool UserIsInteracting()
    {
        if ((Win32.GetAsyncKeyState(0x01 /* VK_LBUTTON */) & 0x8000) != 0 || (Win32.GetAsyncKeyState(0x02 /* VK_RBUTTON */) & 0x8000) != 0)
            return true;
        var info = new Win32.GUITHREADINFO { cbSize = Marshal.SizeOf<Win32.GUITHREADINFO>() };
        if (!Win32.GetGUIThreadInfo(0, ref info))
            return false;
        if ((info.flags & (0x4 | 0x8 | 0x10)) != 0) // GUI_INMENUMODE | GUI_SYSTEMMENUMODE | GUI_POPUPMENUMODE
            return true;
        var className = new StringBuilder(64);
        Win32.GetClassName(info.hwndFocus, className, className.Capacity);
        if (!className.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase))
            return false;
        Win32.GetWindowThreadProcessId(info.hwndFocus, out uint processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>Whether the standalone DesktopRightAlign tool is running (its single-instance mutex exists).</summary>
    private static bool LegacyToolRunning()
    {
        if (!Mutex.TryOpenExisting(@"Local\DesktopRightAlign-v1-" + WindowsIdentity.GetCurrent().User!.Value, out var mutex))
            return false;
        mutex.Dispose();
        return true;
    }

    private void WatchDesktopFolders()
    {
        var folders = new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory }
            .Select(folder => Environment.GetFolderPath(folder))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            try
            {
                var watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                watcher.Created += (_, _) => Invalidate();
                watcher.Deleted += (_, _) => Invalidate();
                watcher.Renamed += (_, _) => Invalidate();
                watcher.Error += (_, _) => Invalidate();
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // The periodic pass still notices new icons, only a little later.
            }
        }
    }

    private void Publish(IconArrangerStatus status)
    {
        if (status == _published)
            return;
        _published = status;
        StatusChanged?.Invoke(status);
    }
}
