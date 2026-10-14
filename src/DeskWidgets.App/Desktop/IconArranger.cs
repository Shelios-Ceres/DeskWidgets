using System.Collections.Concurrent;
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
/// runs (the two would fight over the same icons).
/// </summary>
internal sealed class IconArranger : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly ConcurrentQueue<Action> _jobs = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly IconLayoutSettings _layout; // used on the arranger's thread only
    private volatile IReadOnlyDictionary<string, Insets> _margins;
    private volatile Win32.RECT[] _blocked = [];
    private volatile bool _enabled;
    private volatile bool _changed = true;
    private volatile bool _disposed;
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
    /// Puts the icons back where the restore point has them and gives Explorer
    /// its own arranging flags back. Turn <see cref="Enabled"/> off first, or the
    /// next pass would arrange them again.
    /// </summary>
    public Task<int> RestoreOriginalAsync()
    {
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _jobs.Enqueue(() =>
        {
            try
            {
                done.SetResult(RestoreOriginal());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        _wake.Set();
        return done.Task;
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
            _wake.WaitOne(TimeSpan.FromMilliseconds(500));
            if (_disposed)
                break;
            while (_jobs.TryDequeue(out var job))
                job();
            try
            {
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
        bool learned = IconLayout.Learn(current, _layout);
        var blocked = _blocked.Select(desktop.ToClient).ToList();
        var planned = IconLayout.Plan(current, _layout, blocked); // validates everything before anything moves
        IconStore.EnsureOriginal(current); // never arrange without a restore point
        if (learned)
        {
            IconStore.SaveLayout(_layout);
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

    private int RestoreOriginal()
    {
        if (LegacyToolRunning())
            throw new InvalidOperationException(Strings.IconsLegacyToolRunning);
        var backup = IconStore.LoadOriginal() ?? throw new InvalidOperationException(Strings.IconsNoBackup);
        using var desktop = new ShellDesktop();
        var current = desktop.Capture();
        if (!backup.Displays.SequenceEqual(current.Displays) || backup.SpacingX != current.SpacingX || backup.SpacingY != current.SpacingY)
            throw new InvalidOperationException(Strings.IconsRestoreMismatch);
        var present = new HashSet<string>(current.Items.Select(i => i.Key), StringComparer.OrdinalIgnoreCase);
        var targets = backup.Items.Where(i => present.Contains(i.Key)).ToList();
        desktop.SetLayoutFlags(0);
        desktop.Position(targets);
        var after = desktop.Capture().Items.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);
        if (targets.Any(p => !after.TryGetValue(p.Key, out var seen) || !IconLayout.SamePosition(p, seen)))
            throw new InvalidOperationException(Strings.IconsNotKept);
        desktop.SetLayoutFlags(backup.Flags);
        Log.Info($"desktop icons: restored {targets.Count} icons from the original layout");
        return targets.Count;
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
