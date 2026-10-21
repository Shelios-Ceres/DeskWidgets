using System.Runtime.InteropServices;
using DeskWidgets.Interop;
using Microsoft.UI.Dispatching;

namespace DeskWidgets.Widgets.Folder;

/// <summary>Whose window is in the foreground: ours (a menu, a dialog, a Shell window we started), another program's, or none (mid-switch).</summary>
internal enum ForegroundKind { None, Own, Other }

/// <summary>What brought the panel to ask whether it should stay: it was deactivated, or a second look after a short delay.</summary>
internal enum PanelFocusEvent { Deactivated, Rechecked }

internal enum PanelFocusDecision { Stay, Close, Recheck }

internal enum ReclaimAction { Keep, Take, Close }

/// <summary>
/// When the Big Folder panel (either theme) leaves the screen because it lost the activation. The panel stays through its own
/// work (menus, dialogs, file operations) and goes when the user leaves it for another program. Plain logic, unit-tested; the
/// panels only gather the facts and obey.
/// </summary>
internal static class PanelFocusPolicy
{
    /// <summary>The panel ignores a deactivation this soon after opening (or after a menu closed): the click that opened it may still be settling.</summary>
    public const int GuardMilliseconds = 400;

    /// <summary>The foreground window can be empty while Windows switches between windows; one more look after this long settles it.</summary>
    public const int RecheckMilliseconds = 150;

    public static ForegroundKind Classify(nint foregroundWindow, uint processId, uint ownProcessId)
        => foregroundWindow == 0 ? ForegroundKind.None : processId == ownProcessId ? ForegroundKind.Own : ForegroundKind.Other;

    /// <param name="busy">A file operation, one of our menus or dialogs, a drag out or a design capture is under way.</param>
    /// <param name="openMilliseconds">How long the panel has been open (or since its last menu closed).</param>
    public static PanelFocusDecision Decide(PanelFocusEvent what, ForegroundKind foreground, bool busy, double openMilliseconds)
    {
        if (busy || foreground == ForegroundKind.Own)
            return PanelFocusDecision.Stay;
        // In the first moments the foreground is still settling (the click that opened the panel, a menu that just closed): look again later, don't judge now.
        if (openMilliseconds < GuardMilliseconds)
            return PanelFocusDecision.Recheck;
        return foreground == ForegroundKind.Other ? PanelFocusDecision.Close
            : what == PanelFocusEvent.Deactivated ? PanelFocusDecision.Recheck : PanelFocusDecision.Close;
    }

    /// <summary>
    /// Our own work (an operation, a menu, a dialog) ended: take the focus back unless one of our other windows has it. When another
    /// program has it, the panel goes, except right after an operation with no input from the user since: Windows hands the
    /// activation to the next window when an operation's window closes, and that is not the user leaving.
    /// </summary>
    public static ReclaimAction Reclaim(ForegroundKind foreground, bool panelHasForeground, bool afterOperation, bool userActedSince)
        => panelHasForeground || foreground == ForegroundKind.None ? ReclaimAction.Take
            : foreground == ForegroundKind.Own ? ReclaimAction.Keep
            : afterOperation && !userActedSince ? ReclaimAction.Take
            : ReclaimAction.Close;
}

/// <summary>
/// Counts the Shell operations and dialogs that take the activation from the panel on purpose: a file operation with Explorer's
/// own progress and conflict windows, the UAC prompt of a Public Desktop move, a folder picker. While any is under way the
/// panel does not close.
/// </summary>
internal static class PanelBusy
{
    private static int s_count;

    public static bool IsBusy => Volatile.Read(ref s_count) > 0;

    public static IDisposable Enter()
    {
        Interlocked.Increment(ref s_count);
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
                Interlocked.Decrement(ref s_count);
        }
    }
}

/// <summary>
/// What the panel should select once the list shows the result of its own work: the renamed item under its new name, or, after a
/// removal, the item that now stands where the removed ones were (the one after the last removed, as in Explorer). The model
/// rescans after the work is done, so the answer comes with the next list; a result that never shows up is dropped after a while.
/// </summary>
internal sealed class PanelAftermath
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    private string? _renamedTo;
    private IReadOnlyList<string> _before = [];
    private HashSet<string>? _removed;
    private DateTime _since;

    public void Renamed(string newFileName, DateTime now)
    {
        _renamedTo = newFileName;
        _removed = null;
        _since = now;
    }

    /// <param name="viewBefore">The file names the panel showed, in order, before the removal.</param>
    public void Removing(IReadOnlyList<string> viewBefore, IEnumerable<string> removed, DateTime now)
    {
        _renamedTo = null;
        _before = viewBefore;
        _removed = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
        _since = now;
    }

    /// <summary>The file name to select in <paramref name="viewNow"/>, or null (nothing pending, not shown yet, or the list is empty).</summary>
    public string? Resolve(IReadOnlyList<string> viewNow, DateTime now)
    {
        if (_renamedTo is null && _removed is null)
            return null;
        if (now - _since > Lifetime)
        {
            Clear();
            return null;
        }
        if (_renamedTo is { } renamed)
        {
            string? shown = viewNow.FirstOrDefault(n => n.Equals(renamed, StringComparison.OrdinalIgnoreCase));
            if (shown is null)
                return null;
            Clear();
            return shown;
        }
        if (viewNow.Any(_removed!.Contains))
            return null; // not rescanned yet, or the removal was cancelled
        int last = -1, count = 0;
        for (int i = 0; i < _before.Count; i++)
        {
            if (_removed.Contains(_before[i]))
            {
                last = i;
                count++;
            }
        }
        Clear();
        if (viewNow.Count == 0 || last < 0)
            return null;
        return viewNow[Math.Clamp(last - count + 1, 0, viewNow.Count - 1)];
    }

    private void Clear()
    {
        _renamedTo = null;
        _removed = null;
        _before = [];
    }
}

/// <summary>
/// The panels that are open, so a press on any other card of the desktop can close them (cards never take the activation, so
/// such a click does not deactivate the panel by itself).
/// </summary>
internal static class FolderPanels
{
    private static readonly List<FolderWidget> s_open = [];

    public static void Opened(FolderWidget widget)
    {
        if (!s_open.Contains(widget))
            s_open.Add(widget);
    }

    public static void Closed(FolderWidget widget) => s_open.Remove(widget);

    /// <summary>A press on a card: every open panel but the one of <paramref name="pressed"/> (the card's own widget) goes.</summary>
    public static void CardPressed(object? pressed)
    {
        foreach (var widget in s_open.Where(w => !ReferenceEquals(w, pressed)).ToList())
            widget.ClosePanelFromOutside();
    }
}

/// <summary>
/// The glue both panels share around <see cref="PanelFocusPolicy"/>: reads the foreground window, schedules the second look and
/// the focus hand-back, and calls the panel back to close or to take the foreground.
/// </summary>
internal sealed class PanelFocusGuard
{
    private readonly Func<bool> _isOpen;
    private readonly Func<nint> _panelWindow;
    private readonly Func<bool> _panelBusy;
    private readonly Action _close;
    private readonly Action _take;
    private readonly DispatcherQueueTimer _recheck;
    private readonly DispatcherQueueTimer _reclaim;
    private readonly DispatcherQueueTimer _watch;
    private const int WatchMilliseconds = 500;
    private nint _ownForeground; // the window of ours that had the foreground when the panel last stayed for it
    private DateTime _touched = DateTime.UtcNow;
    private bool _afterOperation;
    private uint _workEndedTick;

    /// <param name="panelBusy">The panel's own reasons to stay: one of its menus is up, items are being dragged out, a design capture pinned it.</param>
    /// <param name="close">Closes the panel (committing a rename in progress, if that is what the theme does).</param>
    /// <param name="take">Brings the panel to the foreground and puts the keyboard focus back inside it.</param>
    public PanelFocusGuard(Func<bool> isOpen, Func<nint> panelWindow, Func<bool> panelBusy, Action close, Action take)
    {
        _isOpen = isOpen;
        _panelWindow = panelWindow;
        _panelBusy = panelBusy;
        _close = close;
        _take = take;
        var queue = DispatcherQueue.GetForCurrentThread();
        _recheck = queue.CreateTimer();
        _recheck.IsRepeating = false;
        _recheck.Interval = TimeSpan.FromMilliseconds(PanelFocusPolicy.RecheckMilliseconds);
        _recheck.Tick += (_, _) =>
        {
            _recheck.Stop();
            Evaluate(PanelFocusEvent.Rechecked);
        };
        _watch = queue.CreateTimer();
        _watch.IsRepeating = true;
        _watch.Interval = TimeSpan.FromMilliseconds(WatchMilliseconds);
        _watch.Tick += (_, _) => WatchTick();
        _reclaim = queue.CreateTimer();
        _reclaim.IsRepeating = false;
        _reclaim.Interval = TimeSpan.FromMilliseconds(PanelFocusPolicy.RecheckMilliseconds);
        _reclaim.Tick += (_, _) =>
        {
            _reclaim.Stop();
            ReclaimNow();
        };
    }

    /// <summary>The panel opened, or one of its menus just closed: the click that did it must not close the panel as well.</summary>
    public void Touch() => _touched = DateTime.UtcNow;

    public void OnDeactivated() => Evaluate(PanelFocusEvent.Deactivated);

    /// <summary>Our own work (an operation, a menu, a dialog) is over: after a short moment the panel takes the focus back, or closes if the user went to another program.</summary>
    /// <param name="afterOperation">The work was a file operation (not a menu): a foreground that fell to another program with no input since is not the user leaving.</param>
    public void Reclaim(bool afterOperation = false)
    {
        if (!_isOpen())
            return;
        _afterOperation |= afterOperation; // a menu closing right after an operation does not make it less of one
        _workEndedTick = (uint)Environment.TickCount;
        Touch();
        _reclaim.Stop();
        _reclaim.Start();
    }

    private bool Busy => PanelBusy.IsBusy || _panelBusy();


    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInput info);

    /// <summary>Whether the user pressed a key or moved or clicked the mouse after <paramref name="tick"/> (an <see cref="Environment.TickCount"/> value).</summary>
    private static bool UserActedSince(uint tick)
    {
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref info) && unchecked((int)(info.Time - tick)) > 0;
    }

    private static ForegroundKind Foreground(out nint window)
    {
        window = Win32.ForegroundWindow();
        uint process = 0;
        // A hidden window holding the activation (a Shell operation's helper window, say) is nobody the user is working in: as good as none.
        if (window != 0 && !Win32.IsVisible(window))
            window = 0;
        if (window != 0)
            Win32.GetWindowThreadProcessId(window, out process);
        return PanelFocusPolicy.Classify(window, process, (uint)Environment.ProcessId);
    }

    private void Evaluate(PanelFocusEvent what)
    {
        if (!_isOpen())
            return;
        var foreground = Foreground(out nint fgw);
        // Windows still names the panel as the foreground window while it is being deactivated; the switch is not done yet.
        if (what == PanelFocusEvent.Deactivated && fgw == _panelWindow())
            foreground = ForegroundKind.None;
        double open = (DateTime.UtcNow - _touched).TotalMilliseconds;
        switch (PanelFocusPolicy.Decide(what, foreground, Busy, open))
        {
            case PanelFocusDecision.Close:
                _close();
                break;
            case PanelFocusDecision.Recheck:
                _recheck.Stop();
                _recheck.Start();
                break;
            default:
                // Staying because a window of ours (a Properties window, a menu) has the foreground: no event tells us when it hands
                // the foreground to another program, so look now and then until it does.
                if (foreground == ForegroundKind.Own && fgw != _panelWindow() && !Busy)
                {
                    _ownForeground = fgw;
                    _watch.Start();
                }
                break;
        }
    }

    private void WatchTick()
    {
        if (!_isOpen())
        {
            _watch.Stop();
            return;
        }
        var foreground = Foreground(out nint window);
        if (window == _panelWindow() || foreground == ForegroundKind.Own)
        {
            if (window == _panelWindow())
                _watch.Stop();
            else
                _ownForeground = window;
            return;
        }
        _watch.Stop();
        // The window of ours that had the foreground is gone (a Properties window was closed): Windows hands the activation to the next
        // ordinary window, never to a tool window like this panel, so that is not the user leaving. The panel takes it back.
        if (_ownForeground != 0 && !Win32.IsVisible(_ownForeground))
        {
            _ownForeground = 0;
            _take();
            return;
        }
        Evaluate(PanelFocusEvent.Rechecked);
    }

    private void ReclaimNow()
    {
        if (!_isOpen())
            return;
        if (Busy)
        {
            Reclaim(); // another operation started meanwhile: look again when it is over
            return;
        }
        var foreground = Foreground(out nint window);
        bool afterOperation = _afterOperation;
        _afterOperation = false;
        switch (PanelFocusPolicy.Reclaim(foreground, window == _panelWindow(), afterOperation, UserActedSince(_workEndedTick)))
        {
            case ReclaimAction.Take:
                _take();
                break;
            case ReclaimAction.Close:
                _close();
                break;
        }
    }
}
