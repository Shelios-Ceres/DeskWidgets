using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Interop;
using DeskWidgets.Motion;
using DeskWidgets.PixelUi;
using DeskWidgets.Widgets;

namespace DeskWidgets.Dock;

/// <summary>
/// All the docks of the app (PROPOSAL 3.9): at most one per screen, not a widget type. It owns the window tracker they share, makes each
/// dock's window in the current theme, and passes the host's theme, language, display and exit changes on. The host holds one of these.
/// </summary>
internal sealed class DockManager : IDockHost, IDisposable
{
    private readonly Func<WidgetTheme> _theme;
    private readonly Action _requestSave;
    private readonly Action<string> _showWarning;
    private readonly List<DockSettings> _store;
    private readonly List<DockController> _docks = [];
    private WindowTracker? _tracker;
    private readonly RunningScanner _scanner = new();
    private FrameClock.Ticket? _settle;
    private bool _disposed;

    /// <param name="store">The <c>Docks</c> list of the settings the docks are saved in.</param>
    public DockManager(Func<WidgetTheme> theme, Action requestSave, Action<string> showWarning, List<DockSettings> store)
    {
        _theme = theme;
        _requestSave = requestSave;
        _showWarning = showWarning;
        _store = store;
    }

    public IReadOnlyList<DockController> Docks => _docks;

    public WidgetTheme Theme => _theme();

    /// <summary>Opens the saved docks, one per screen (see <see cref="Reconcile"/>).</summary>
    public void Start()
    {
        Reconcile();
        PublishWindows();
    }

    /// <summary>
    /// Matches the saved docks to the screens there are (B2-3): one dock per screen, and no program is ever dropped from the settings. A dock saved
    /// for the same screen as another merges into it; a dock whose screen is gone and whose fallback (the primary screen) is taken keeps its
    /// settings and waits, closed, for its screen to come back (<see cref="OnDisplayChanged"/> looks again).
    /// </summary>
    private void Reconcile()
    {
        var screens = Win32.GetMonitors().Select(m => m.Device).Where(d => d is not null).Select(d => d!).ToList();
        string primary = PrimaryDevice() ?? screens.FirstOrDefault() ?? "";
        var settings = _store.ToList();
        var plan = DockScreens.Plan([.. settings.Select(s => s.Monitor)], screens, primary);
        for (int i = 0; i < settings.Count; i++)
        {
            var open = _docks.FirstOrDefault(d => d.Settings == settings[i]);
            if (plan[i].Fate == DockFate.Open && open is null)
                Open(settings[i]);
            else if (plan[i].Fate != DockFate.Open && open is not null)
            {
                _docks.Remove(open);
                open.Dispose();
            }
        }
        bool merged = false;
        for (int i = 0; i < settings.Count; i++)
        {
            if (plan[i].Fate != DockFate.Merge)
                continue;
            var into = settings[plan[i].Target];
            var extra = settings[i].Items.Where(item => !into.Items.Any(existing => existing.Key == item.Key)).Select(item => item.Copy()).ToList();
            if (_docks.FirstOrDefault(d => d.Settings == into) is { } dock)
                dock.Add(extra);
            else
                into.Items.AddRange(extra);
            _store.Remove(settings[i]);
            merged = true;
        }
        if (merged)
            RequestSave();
    }

    /// <summary>Reads the taskbar's windows once and tells every dock (which programs have a window now). Runs only when the set of windows changed.</summary>
    private void PublishWindows()
    {
        if (_disposed || _docks.Count == 0)
            return;
        var windows = _scanner.Scan();
        if (_tracker is not null)
            _tracker.TrackedWindows = [.. windows.Select(w => w.Handle)];
        foreach (var dock in _docks.ToList())
            dock.OnWindows(windows);
    }

    private static bool SameScreen(string? a, string? b) => string.Equals(a ?? PrimaryDevice(), b ?? PrimaryDevice(), StringComparison.OrdinalIgnoreCase);

    private static string? PrimaryDevice() => Win32.GetMonitors().FirstOrDefault(m => m.Primary).Device;

    private void Open(DockSettings settings)
    {
        EnsureTracker();
        var dock = new DockController(this, settings);
        _docks.Add(dock);
        try
        {
            dock.Start();
        }
        catch (Exception ex)
        {
            Log.Error("A dock could not be opened", ex);
            _docks.Remove(dock);
            dock.Dispose();
        }
    }

    private void EnsureTracker()
    {
        if (_tracker is not null)
            return;
        _tracker = new WindowTracker(action =>
        {
            _settle?.Stop();
            _settle = FrameClock.After(60, action);
        });
        _tracker.Changed += _ =>
        {
            foreach (var dock in _docks.ToList())
                dock.EvaluateRule();
        };
        _tracker.WindowSetChanged += PublishWindows;
    }

    // ---------------------------------------------------------------- adding and removing a dock

    /// <summary>Whether the screen under the pointer (or the primary one) already has a dock: the menu row is greyed then.</summary>
    public bool ScreenHasDock(string? device) => _docks.Any(d => SameScreen(d.Settings.Monitor, device));

    bool IDockHost.OtherDockOn(DockController self, string? device) => _docks.Any(d => d != self && SameScreen(d.Settings.Monitor, device));

    /// <summary>The screen the pointer is on: where "Dock" in a menu adds one.</summary>
    public static string? ScreenUnderPointer()
        => Win32.GetCursorPos(out var cursor) && Win32.MonitorAt(cursor) is { } screen ? screen.Device : PrimaryDevice();

    public DockController? AddDock(string? device)
    {
        if (_disposed || ScreenHasDock(device))
            return null;
        var settings = new DockSettings { Monitor = device };
        _store.Add(settings);
        Open(settings);
        PublishWindows();
        RequestSave();
        return _docks.FirstOrDefault(d => d.Settings == settings);
    }

    public void RemoveDock(DockController dock)
    {
        _docks.Remove(dock);
        _store.Remove(dock.Settings);
        dock.Dispose();
        RequestSave();
    }

    // ---------------------------------------------------------------- what the host passes on

    public void SetTheme()
    {
        foreach (var dock in _docks)
            dock.ReplaceSurface();
    }

    public void ApplyLanguage()
    {
        foreach (var dock in _docks)
            dock.ApplyLanguage();
    }

    public void OnDisplayChanged()
    {
        Reconcile(); // a screen came or went: a dormant dock may open, an open one may have lost its screen to another
        foreach (var dock in _docks)
            dock.OnDisplayChanged();
        PublishWindows();
    }

    /// <summary>The docks' rectangles the desktop icons keep clear of, also while a dock is hidden (or the icons would jump each time it shows).</summary>
    public IEnumerable<Win32.RECT> BlockedRects()
        => _docks.Select(d => d.BarRectWithGap).Select(r => new Win32.RECT { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom });

    // ---------------------------------------------------------------- IDockHost

    public void RequestSave() => _requestSave();

    public void ShowWarning(string text) => _showWarning(text);

    public WindowTracker.Verdict VerdictFor(nint monitor) => _tracker?.Evaluate(monitor) ?? default;

    public IDockSurface CreateSurface() => Theme == WidgetTheme.Pixel ? new PixelDockSurface() : new GlassDockSurface();

    public void AskRemoveDock(nint owner, Action yes)
    {
        if (Theme == WidgetTheme.Pixel)
        {
            PixelDialog.Show(owner, DialogBadge.Question, null, Strings.DockRemoveDockQuestion, false,
                [new DialogButtonSpec(Strings.Remove, false), new DialogButtonSpec(Strings.Cancel, true)], defaultButton: 1, cancelButton: 1, done: answer =>
                {
                    if (answer == 0)
                        yes();
                });
            return;
        }
        int answer = Win32.MessageBox(owner, Strings.DockRemoveDockQuestion, Strings.AppName,
            Win32.MB_YESNO | Win32.MB_ICONQUESTION | Win32.MB_DEFBUTTON2 | Win32.MB_SETFOREGROUND | Win32.MB_TOPMOST);
        if (answer == Win32.IDYES)
            yes();
    }

    public void AskRemoveMissing(nint owner, string name, Action yes)
    {
        PixelDialog.Show(owner, DialogBadge.Question, null, Strings.DockMissingQuestion(name), false,
            [new DialogButtonSpec(Strings.Remove, true), new DialogButtonSpec(Strings.Cancel, false)], defaultButton: 0, cancelButton: 1, done: answer =>
            {
                if (answer == 0)
                    yes();
            });
    }

    void IDockHost.RemoveDock(DockController dock) => RemoveDock(dock);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _settle?.Stop();
        _tracker?.Dispose();
        foreach (var dock in _docks)
            dock.Dispose();
        _docks.Clear();
    }
}
