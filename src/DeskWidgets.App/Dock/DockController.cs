using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Interop;
using DeskWidgets.Motion;
using DeskWidgets.PixelUi;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI.Dispatching;

namespace DeskWidgets.Dock;

/// <summary>What a <see cref="DockController"/> needs from the app around it (the <see cref="DockManager"/>).</summary>
internal interface IDockHost
{
    WidgetTheme Theme { get; }

    /// <summary>Saves the settings soon.</summary>
    void RequestSave();

    void ShowWarning(string text);

    /// <summary>Asks "remove this dock?" in the theme's own dialog; runs <paramref name="yes"/> on a yes.</summary>
    void AskRemoveDock(nint owner, Action yes);

    /// <summary>Asks "this program can't be found: remove it?" (Pixel's missing-app dialog).</summary>
    void AskRemoveMissing(nint owner, string name, Action yes);

    void RemoveDock(DockController dock);

    /// <summary>Asks "remove this group?" in the theme's own dialog; runs <paramref name="yes"/> on a yes.</summary>
    void AskRemoveGroup(nint owner, string name, Action yes);

    /// <summary>Whether another dock than <paramref name="self"/> is on this screen (one dock per screen).</summary>
    bool OtherDockOn(DockController self, string? device);

    /// <summary>What the window tracker says for a screen.</summary>
    WindowTracker.Verdict VerdictFor(nint monitor);

    /// <summary>A new window in the current theme's look.</summary>
    IDockSurface CreateSurface();
}

/// <summary>
/// One dock: its programs, where it sits, when it shows. It decides everything; its <see cref="IDockSurface"/> only draws and reports the
/// pointer. Moves the window itself (the slide, SetWindowPos each frame: SPIKE section 1), watches the tracker's verdict, owns the edge
/// strip, and does what a click, a menu row or a drop asks.
/// </summary>
internal sealed partial class DockController : IDisposable
{
    private const double DragThresholdDip = 4;
    private const int LeaveHideMs = 600;
    private const int ExitMs = 150; // Pixel's slide away
    private const int GlassHideMs = 280; // Glass hides on the Move token (HANDOFF section 5, D6)
    private const int EnterMs = 240;

    private readonly IDockHost _host;
    private readonly DockEdgeStrip _edge = new();
    private IDockSurface? _surface;
    private DockLayout _layout;
    private Win32.MonitorArea _monitor;
    private double _scale = 1;

    private bool _hidden;
    private bool _revealed; // up because of the pointer or a drag although the rule says hide
    private bool _ruleHides;
    private bool _menuOpen;
    private bool _disposed;
    private FrameClock.Ticket? _slide;
    private FrameClock.Ticket? _leave;
    private DockRect _strip; // the edge strip's rectangle, screen pixels
    private readonly HashSet<string> _missing = [];
    private readonly Dictionary<string, string?> _customKeys = []; // DockItem.Key -> custom tile key (null: none), found by CheckMissing's worker
    private readonly DockRunStates _runStates = new();
    private IReadOnlyList<RunningWindow> _windows = [];
    private FrameClock.Ticket? _expiry;
    private readonly StaWorker _worker = new("Dock checks");

    // Pointer state.
    private int _hover = -1;
    private int _pressed = -1;
    private DockPointer _down;
    private bool _dragging;
    private DragKind _dragKind;
    private int _dragFrom = -1;
    private List<DockItem>? _preview; // the order while an icon is being dragged
    private int _barOffsetStart;

    private enum DragKind
    {
        None,
        Reorder,
        Bar,
    }

    public DockController(IDockHost host, DockSettings settings)
    {
        _host = host;
        Settings = settings;
    }

    public DockSettings Settings { get; }

    public IReadOnlyList<DockItem> Items => Settings.Items;

    public nint Handle => _surface?.Handle ?? 0;

    public bool IsHidden => _hidden;

    /// <summary>The window's rectangle when shown, in screen pixels (the bar plus the gap to the edge: what the desktop icons keep clear of).</summary>
    public DockRect BarRectWithGap => new(_layout.Bar.X, _layout.Bar.Y, _layout.Bar.Width, _layout.Bar.Height + (int)Math.Round(DockGeometry.EdgeGapDip * _scale));

    // ---------------------------------------------------------------- start and layout

    public void Start()
    {
        _surface = _host.CreateSurface();
        _surface.Pointer += OnPointer;
        _surface.Panel += OnPanelEvent;
        _edge.Reached += () => Reveal("pointer at the edge");
        _edge.DragReached += () => Reveal("drag at the edge");
        FindMonitor();
        // Over a maximized window the dock starts hidden: it must not show for a frame first.
        if (_monitor.Device is not null && _host.VerdictFor(MonitorHandle()).ShouldHide)
        {
            _hidden = true;
            _ruleHides = true;
        }
        Relayout(new DockChange(DockChangeKind.Reset));
        if (_hidden)
            _edge.SetVisible(true);
        else
            _surface.Show();
        EvaluateRule();
        CheckMissing();
        _surface.AttachDropTarget(new DockDropTarget(this));
        _surface.AttachPanelDropTarget(new DockDropTarget(this, panel: true));
    }

    /// <summary>Swaps the window for the other theme's (the host does this on a theme switch).</summary>
    public void ReplaceSurface()
    {
        if (_surface is null)
            return;
        _surface.Pointer -= OnPointer;
        _surface.Panel -= OnPanelEvent;
        _surface.Dispose();
        _openGroup = null; // the old surface's panel went with it
        ClearHold();
        _surface = _host.CreateSurface();
        _surface.Pointer += OnPointer;
        _surface.Panel += OnPanelEvent;
        _hover = -1; // the new surface knows no hover
        Relayout(new DockChange(DockChangeKind.Reset)); // a hidden dock is placed below the edge by this
        if (!_hidden)
            _surface.Show();
        _surface.AttachDropTarget(new DockDropTarget(this));
        _surface.AttachPanelDropTarget(new DockDropTarget(this, panel: true));
    }

    private void FindMonitor()
    {
        var monitors = Win32.GetMonitors();
        _monitor = monitors.FirstOrDefault(m => string.Equals(m.Device, Settings.Monitor, StringComparison.OrdinalIgnoreCase));
        if (_monitor.Device is null)
            _monitor = monitors.FirstOrDefault(m => m.Primary);
        if (_monitor.Device is null && monitors.Count > 0)
            _monitor = monitors[0];
    }

    /// <summary>A display change (or the first layout): find the screen again, place the bar, clamp its offset.</summary>
    public void OnDisplayChanged()
    {
        FindMonitor();
        Relayout(new DockChange(DockChangeKind.None));
        CheckMissing(); // a drive may have come or gone with the screens
    }

    private DockRect Work => new(_monitor.Work.Left, _monitor.Work.Top, _monitor.Work.Width, _monitor.Work.Height);

    private int PxOf(double dip) => (int)Math.Round(dip * _scale);

    private void UpdateScale() => _scale = Win32.GetScaleAt(new Win32.POINT { X = _monitor.Work.Left + 1, Y = _monitor.Work.Top + 1 });

    private nint MonitorHandle() => Win32.MonitorFromPoint(new Win32.POINT { X = _monitor.Work.Left + 1, Y = _monitor.Work.Top + 1 }, Win32.MONITOR_DEFAULTTONEAREST);

    /// <summary>Places the window, the strip and the bar and draws it all again. Also a bar that only moved: the picture must follow its hit boxes (B2-2).</summary>
    private void Relayout(DockChange change)
    {
        if (_surface is null || _monitor.Device is null)
            return;
        UpdateScale();
        bool pixel = _host.Theme == WidgetTheme.Pixel;
        int count = Settings.Items.Count;
        // A pixel bar is whole art pixels, a whole factor of screen pixels each (art pixel = 2 DIPs), as every Pixel card is.
        int factor = Pixel.PixelCard.Factor(_scale);
        int length = pixel ? DockGeometry.PixelBarLengthArt(Settings.IconSize, count) * factor : PxOf(DockGeometry.BarLengthDip(Settings.IconSize, count));
        int height = pixel ? DockGeometry.PixelBarHeightArt(Settings.IconSize) * factor : PxOf(DockGeometry.BarHeightDip(Settings.IconSize));
        int headroom = pixel ? DockGeometry.PixelHeadroomArt * factor : PxOf(DockGeometry.HeadroomDip);
        int edgeGap = pixel ? DockGeometry.PixelEdgeGapArt * factor : PxOf(DockGeometry.EdgeGapDip);
        int overhang = PxOf(DockGeometry.LabelMaxWidthDip / 2);
        int offset = PxOf(Settings.OffsetDip);
        _layout = DockPlacement.Compute(Work, _monitor.Bounds.Bottom, length, height, headroom, edgeGap, overhang, offset);
        // Only the stretch under the dock, plus about one icon slot each side (B2-16): the edge anywhere else brings nothing up.
        int margin = pixel ? (DockGeometry.PixelSlotArt(Settings.IconSize) + DockGeometry.PixelSlotGapArt) * factor : PxOf(DockGeometry.IconDip(Settings.IconSize) + DockGeometry.GapDip);
        _strip = DockVisibility.EdgeStrip(Work, _layout.Bar, Math.Max(2, PxOf(DockGeometry.EdgeStripDip)), margin);
        _edge.Place(_strip);

        _surface.PlaceWindow(_layout.Window, _layout.Bar, _scale);
        _surface.SetItems(Views(Settings.Items), Settings.IconSize, change);
        if (_hidden)
            _surface.MoveWindowTo(_layout.Window.X, _layout.HiddenTop);
        RefreshLabelFor(_hover);
    }

    // A key the worker has not resolved yet falls back to the match that reads no file (AUMID, exe name, Steam id): the exe's version info
    // is read only by CheckMissing's worker (a network exe would stall the UI), and the redraw that follows brings the full key.
    private IReadOnlyList<DockItemView> Views(IEnumerable<DockItem> items) => [.. items.Select(ViewOf)];

    private DockItemView ViewOf(DockItem item)
    {
        if (item.IsGroup)
        {
            var members = item.Children!.Select(ViewOf).ToList();
            return new DockItemView(item, false, DockGroups.GroupState(members.Select(m => m.State)), null, members);
        }
        return new DockItemView(item, _missing.Contains(item.Key), _runStates.StateOf(item.Key),
            _customKeys.TryGetValue(item.Key, out var key) ? key : DockCustomKeys.Quick(item));
    }

    // ---------------------------------------------------------------- hiding and showing

    /// <summary>The tracker said something changed: ask it again for this screen.</summary>
    public void EvaluateRule()
    {
        if (_surface is null || _disposed)
            return;
        var verdict = _host.VerdictFor(MonitorHandle());
        _ruleHides = verdict.ShouldHide;
        if (!_ruleHides)
        {
            _revealed = false;
            Slide(hide: false);
        }
        else if (!_revealed || verdict.FullScreen)
        {
            // A full-screen window in front also takes back a dock that a drag or the pointer had brought up.
            if (verdict.FullScreen)
                _revealed = false;
            else if (InUse && !_hidden)
            {
                // Never away mid-use (a menu, a drag): treat it as brought up, so its close or end hides it (B2-7).
                _revealed = true;
                return;
            }
            Slide(hide: true);
        }
    }

    private bool InUse => _menuOpen || _dragging || _preview is not null || _dropping || _surface is { PanelOpen: true };

    private bool _dropping;

    private void Reveal(string how)
    {
        if (!_hidden || _surface is null)
            return;
        if (_host.VerdictFor(MonitorHandle()).FullScreen)
            return; // the edge brings nothing back over a full-screen window
        _revealed = true;
        Slide(hide: false);
        ScheduleLeaveHide(); // the pointer may never visit the bar: it hides again once it is off the edge (B2-1)
    }

    private void ScheduleLeaveHide()
    {
        _leave?.Stop();
        if (!_revealed || !_ruleHides)
            return;
        _leave = FrameClock.After(LeaveHideMs, () =>
        {
            if (_disposed)
                return;
            // In use: the menu's close or the drop's end asks again. The pointer still on the edge or the bar: look again later.
            if (InUse)
                return;
            Win32.GetCursorPos(out var cursor);
            if (DockVisibility.StaysRevealed(false, _layout.Bar, _strip, cursor.X, cursor.Y))
            {
                ScheduleLeaveHide();
                return;
            }
            _revealed = false;
            Slide(hide: true);
        });
    }

    private void CancelLeaveHide() => _leave?.Stop();

    /// <summary>Slides the window off the bottom edge or back (D6: Glass hides on Move 280 ms, Pixel on Exit 150 ms; both reveal on Enter 240 ms). A request in the direction already running is ignored.</summary>
    private void Slide(bool hide)
    {
        if (_surface is null || _hidden == hide && _slide is null)
            return;
        if (_slide is not null && hide == _hidden)
            return; // already sliding that way
        _slide?.Stop();
        _slide = null;
        _hidden = hide;
        _edge.SetVisible(false);
        int from = _surface.WindowRect.Y, to = hide ? _layout.HiddenTop : _layout.Window.Y;
        int x = _layout.Window.X;
        if (hide)
        {
            _surface.SetHover(-1, null);
            _hover = -1; // or the next hover of the same icon would be ignored as "no change" (B2-10)
        }
        else
        {
            _surface.Show(); // a hidden dock's window is hidden, not parked below the screen (B2-13)
        }
        if (from == to)
        {
            FinishSlide(hide);
            return;
        }
        if (!MotionSettings.Enabled)
        {
            _surface.MoveWindowTo(x, to);
            FinishSlide(hide);
            return;
        }
        bool glass = _host.Theme != WidgetTheme.Pixel;
        int duration = hide ? (glass ? GlassHideMs : ExitMs) : EnterMs;
        double start = FrameClock.Now;
        _slide = FrameClock.Every(8, now =>
        {
            double t = Math.Min(1, (now - start) / duration);
            double eased = hide ? (glass ? Bezier.Move.At(t) : t * t * t) : 1 - Math.Pow(1 - t, 3);
            _surface?.MoveWindowTo(x, (int)Math.Round(from + (to - from) * eased));
            if (t >= 1)
            {
                _slide?.Stop();
                _slide = null;
                FinishSlide(hide);
            }
        });
    }

    private void FinishSlide(bool hide)
    {
        if (hide)
        {
            _surface?.Hide(); // closes a panel that was still up, without telling us
            _openGroup = null;
        }
        _edge.SetVisible(hide);
    }

    // ---------------------------------------------------------------- pointer

    private int IconAtPointer(DockPointer p)
    {
        if (p.Y < 0 || p.Y > DockGeometry.BarHeightDip(Settings.IconSize))
            return -1;
        return DockGeometry.IconAt(Settings.IconSize, Settings.Items.Count, p.X);
    }

    private bool OverBar(DockPointer p) => p.Y >= 0 && p.Y <= DockGeometry.BarHeightDip(Settings.IconSize) && p.X >= 0 && p.X <= DockGeometry.BarLengthDip(Settings.IconSize, Settings.Items.Count);

    private void OnPointer(DockPointer p)
    {
        if (_disposed)
            return;
        switch (p.Kind)
        {
            case DockPointerKind.Move:
                CancelLeaveHide();
                if (_pressed >= 0 || _dragKind != DragKind.None || _down.Kind == DockPointerKind.Down && p.Left)
                {
                    Dragged(p);
                    return;
                }
                SetHover(IconAtPointer(p));
                break;
            case DockPointerKind.Leave:
                if (_dragKind == DragKind.None)
                {
                    SetHover(-1);
                    ScheduleLeaveHide();
                }
                break;
            case DockPointerKind.Down:
                PointerDown(p);
                break;
            case DockPointerKind.Up:
                PointerUp(p);
                break;
            case DockPointerKind.Cancel:
                CancelPress();
                break;
        }
    }

    private void SetHover(int index)
    {
        if (index == _hover)
            return;
        _hover = index;
        RefreshLabelFor(index);
    }

    private void RefreshLabelFor(int index)
    {
        if (_surface is null)
            return;
        if (index < 0 || index >= Settings.Items.Count)
        {
            _surface.SetHover(-1, null);
            return;
        }
        var item = Settings.Items[index];
        string name = ViewOf(item).Name;
        _surface.SetHover(index, _missing.Contains(item.Key) ? Strings.DockCantFind(name) : name);
    }

    private void PointerDown(DockPointer p)
    {
        if (p.Right)
        {
            OpenMenu(p);
            return;
        }
        _down = default;
        _dragging = false;
        _dragKind = DragKind.None;
        if (!p.Left && !p.Middle)
            return;
        _pressed = IconAtPointer(p);
        if (_pressed < 0 && !OverBar(p))
            return; // on the label or the headroom: not a press on anything (B2-8)
        _down = p;
        if (_pressed >= 0)
            _surface?.SetPressed(_pressed);
        else
            _barOffsetStart = Settings.OffsetDip == 0 ? 0 : PxOf(Settings.OffsetDip);
    }

    private void Dragged(DockPointer p)
    {
        if (_surface is null)
            return;
        double dx = Math.Abs(p.ScreenX - _down.ScreenX) / _scale, dy = Math.Abs(p.ScreenY - _down.ScreenY) / _scale;
        if (!_dragging && dx + dy < DragThresholdDip)
            return;
        if (!_dragging)
        {
            _dragging = true;
            _dragKind = _pressed >= 0 ? DragKind.Reorder : DragKind.Bar;
            if (_dragKind == DragKind.Reorder)
            {
                _dragFrom = _pressed;
                _preview = [.. Settings.Items];
                _surface.SetPressed(-1);
                SetHover(-1);
            }
        }
        if (_dragKind == DragKind.Reorder)
            ReorderTo(p);
        else if (_dragKind == DragKind.Bar)
            MoveBar(p);
    }

    private void ReorderTo(DockPointer p)
    {
        if (_surface is null || _preview is null)
            return;
        UpdateHold(p);
        int slot = DockGeometry.SlotAt(Settings.IconSize, _preview.Count, p.X);
        // slot counts the icons whose middle is left of the pointer; the dragged icon is one of them while it is left of itself.
        int target = Math.Clamp(slot > _dragFrom ? slot - 1 : slot, 0, _preview.Count - 1);
        // Over another icon's middle zone the pointer is holding for a group: the icons do not reorder there.
        if (target != _dragFrom && _hold.Target < 0)
        {
            var item = _preview[_dragFrom];
            _preview.RemoveAt(_dragFrom);
            _preview.Insert(target, item);
            var change = new DockChange(DockChangeKind.Moved, _dragFrom, target);
            _dragFrom = target;
            _surface.SetItems(Views(_preview), Settings.IconSize, change);
        }
        _surface.SetDrag(_dragFrom, p.X);
    }

    private void MoveBar(DockPointer p)
    {
        // The bar follows the pointer along the bottom edge, or onto another screen's (D3, PROPOSAL 3.1). No motion of its own.
        var at = Win32.MonitorAt(new Win32.POINT { X = p.ScreenX, Y = p.ScreenY });
        if (at is { } screen && !string.Equals(screen.Device, _monitor.Device, StringComparison.OrdinalIgnoreCase) && !_host.OtherDockOn(this, screen.Device))
        {
            // On another screen the bar's centre goes under the pointer. A screen that has a dock of its own is not for this one (one dock per screen).
            _monitor = screen;
            UpdateScale(); // this screen's scale, not the last one's (B2-9)
            Settings.Monitor = screen.Device;
            _down = p;
            _barOffsetStart = p.ScreenX - (screen.Work.Left + screen.Work.Width / 2);
        }
        int offsetPx = _barOffsetStart + (p.ScreenX - _down.ScreenX);
        // Clamped by the layout, which gives back what fits.
        var clamped = DockPlacement.ClampOffset(offsetPx, _monitor.Work.Width, _layout.Bar.Width, PxOf(DockGeometry.EdgeGapDip));
        Settings.OffsetDip = clamped / _scale;
        Relayout(new DockChange(DockChangeKind.None));
    }

    private void PointerUp(DockPointer p)
    {
        if (_surface is null)
            return;
        int pressed = _pressed;
        var kind = _dragKind;
        bool dragged = _dragging;
        CancelPress();
        if (kind == DragKind.Reorder)
        {
            CommitReorder();
            return;
        }
        if (kind == DragKind.Bar)
        {
            _host.RequestSave();
            return;
        }
        if (pressed >= 0 && pressed == IconAtPointer(p) && !dragged && (p.Left || p.Middle))
            Click(pressed, p);
    }

    private void CancelPress()
    {
        _pressed = -1;
        _dragging = false;
        _dragKind = DragKind.None;
        _down = default;
        _surface?.SetPressed(-1);
        if (_preview is not null)
            CommitReorder();
    }

    private void CommitReorder()
    {
        var preview = _preview;
        _preview = null;
        var grouped = preview is null ? null : TryGroup(preview);
        ClearHold();
        _dragFrom = -1;
        _surface?.SetDrag(-1, 0);
        if (preview is null)
            return;
        if (grouped is not null || !preview.SequenceEqual(Settings.Items))
        {
            Settings.Items.Clear();
            Settings.Items.AddRange(preview);
            _host.RequestSave();
        }
        _surface?.SetItems(Views(Settings.Items), Settings.IconSize, grouped ?? new DockChange(DockChangeKind.None));
    }

    // ---------------------------------------------------------------- click and menus

    private void Click(int index, DockPointer p)
    {
        var item = Settings.Items[index];
        if (item.IsGroup)
        {
            TogglePanel(index);
            return;
        }
        ClickProgram(item, p.Middle || p.Shift, p.ScreenX, p.ScreenY);
    }

    private void ClickProgram(DockItem item, bool another, int screenX, int screenY)
    {
        // A click on a running program switches to its window; middle-click or Shift+click starts another copy (PROPOSAL 3.4, 3.8).
        if (!another && _runStates.StateOf(item.Key) == DockRunState.Running && DockRunning.WindowsOf(item, _windows) is { Count: > 0 } windows)
        {
            DockSwitcher.Do(DockRunning.PlanSwitch([.. windows.Select(w => w.Handle)], Win32.ForegroundWindow()));
            return;
        }
        if (!another && _runStates.StateOf(item.Key) == DockRunState.Opening)
            return; // it is opening: another click would only start a second copy
        Start(item, screenX, screenY);
    }

    /// <summary>
    /// Starts the program on the worker (a Store app's activation took 460 ms: the dock's own release must not wait for it) after looking again
    /// whether it is there (a drive may have come back, or a program gone). If the dock can tell when it is running, its icon goes into the
    /// opening state at once until the first window comes (D3); a start that fails, or a program that is gone, takes it back.
    /// </summary>
    private void Start(DockItem item, int screenX, int screenY, bool asAdministrator = false)
    {
        bool tracks = item.SupportsRunning && _runStates.Started(item.Key, Now); // a program that was missing and came back is tracked too: the check below takes it back if it is not there
        if (tracks)
        {
            ApplyRunState(item.Key, DockRunState.Opening, DockRunTransition.Opening);
            ScheduleExpiry();
        }
        var copy = item.Copy();
        nint owner = Handle;
        var ui = DispatcherQueue.GetForCurrentThread();
        _worker.Post(() =>
        {
            bool exists = DockLauncher.Exists(copy);
            string? error = exists ? DockLauncher.Start(copy, owner, asAdministrator) : null;
            ui.TryEnqueue(() =>
            {
                if (_disposed || _surface is null)
                    return;
                if (tracks && (!exists || error is not null) && _runStates.Cancel(item.Key))
                    ApplyRunState(item.Key, DockRunState.Idle, DockRunTransition.TimedOut);
                if (SetMissing(item, !exists))
                {
                    if (!exists)
                        OpenMissing(item, screenX, screenY);
                    return;
                }
                Report(error);
            });
        });
    }

    /// <summary>Notes that a program is, or is no longer, missing and redraws if that changed it. Returns whether it is missing.</summary>
    private bool SetMissing(DockItem item, bool missing)
    {
        if (missing != _missing.Contains(item.Key))
        {
            if (missing)
                _missing.Add(item.Key);
            else
                _missing.Remove(item.Key);
            _surface?.SetItems(Views(Settings.Items), Settings.IconSize, new DockChange(DockChangeKind.None));
        }
        return missing;
    }

    private static TimeSpan Now => TimeSpan.FromMilliseconds(FrameClock.Now);

    private void Report(string? error)
    {
        if (error is not null)
            _host.ShowWarning(error);
    }

    private void OpenMissing(DockItem item, int screenX, int screenY)
    {
        if (_host.Theme == WidgetTheme.Pixel)
        {
            _host.AskRemoveMissing(Handle, item.Name, () => RemoveProgram(item));
            return;
        }
        ShowMenu(
        [
            new MenuEntry { Text = Strings.DockMissingHeader(item.Name), Kind = MenuKind.Item, Enabled = false, Glyph = "" },
            MenuEntry.Separator(),
            MenuEntry.Item(Strings.DockRemoveFromDock, () => RemoveProgram(item), glyph: "", icon: PixelIcons.Remove),
        ], screenX, screenY);
    }

    private void OpenMenu(DockPointer p)
    {
        int index = IconAtPointer(p);
        if (index >= 0)
        {
            var item = Settings.Items[index];
            ShowMenu(item.IsGroup ? GroupMenu(item) : AppMenu(item), p.ScreenX, p.ScreenY);
            return;
        }
        if (OverBar(p) || p.Y < 0)
            ShowMenu(BarMenu(), p.ScreenX, p.ScreenY);
    }

    private void ShowMenu(IReadOnlyList<MenuEntry> entries, int x, int y)
    {
        if (_surface is null)
            return;
        _menuOpen = true;
        CancelLeaveHide();
        SetHover(-1); // the label goes while a menu is open
        _surface.ShowMenu(entries, x, y, () =>
        {
            _menuOpen = false;
            ScheduleLeaveHide();
        });
    }

    internal IReadOnlyList<MenuEntry> AppMenu(DockItem item)
    {
        var rows = new List<MenuEntry> { MenuEntry.Item(Strings.DockOpen, () => StartFromMenu(item), glyph: "") };
        if (!item.IsStoreApp)
            rows.Add(MenuEntry.Item(Strings.FolderRunAsAdministrator, () => StartFromMenu(item, asAdministrator: true), glyph: "", icon: PixelIcons.Shield));
        rows.Add(new MenuEntry
        {
            Text = Strings.FolderOpenFileLocation, Kind = MenuKind.Item, Enabled = !string.IsNullOrEmpty(item.ExePath) && File.Exists(item.ExePath),
            Click = () => DockLauncher.OpenFileLocation(item), Glyph = "", Icon = PixelIcons.Folder,
        });
        rows.Add(MenuEntry.Separator());
        rows.Add(MenuEntry.Item(Strings.DockRemoveFromDock, () => RemoveProgram(item), glyph: "", icon: PixelIcons.Remove));
        if (!string.IsNullOrEmpty(item.ExePath) && File.Exists(item.ExePath))
        {
            rows.Add(MenuEntry.Separator());
            rows.Add(MenuEntry.Item(Strings.FolderMoreOptions, () => ShowMoreOptions(item), glyph: ""));
        }
        return rows;
    }

    private void StartFromMenu(DockItem item, bool asAdministrator = false)
    {
        Win32.GetCursorPos(out var cursor);
        Start(item, cursor.X, cursor.Y, asAdministrator);
    }

    private void ShowMoreOptions(DockItem item)
    {
        Win32.GetCursorPos(out var cursor);
        Report(ShellContextMenu.Show([item.ExePath!], Handle, cursor.X, cursor.Y, onOpen: () => StartFromMenu(item), onRename: null, onDelete: null));
    }

    internal IReadOnlyList<MenuEntry> BarMenu()
    {
        var sizes = Enum.GetValues<DockIconSize>().Select(size => MenuEntry.Radio(SizeName(size), size == Settings.IconSize, () => SetIconSize(size), group: "dock-size")).ToList();
        return
        [
            MenuEntry.Item(Strings.FolderAddApps, () => Report(FolderActions.AddApps()), glyph: "", icon: PixelIcons.Add),
            MenuEntry.Item(Strings.DockAddTaskbarApps, AddTaskbarApps, glyph: "", icon: PixelIcons.TaskbarApps),
            MenuEntry.Separator(),
            MenuEntry.Sub(Strings.DockIconSize, sizes, glyph: "", icon: PixelIcons.Size),
            MenuEntry.Separator(),
            MenuEntry.Item(Strings.DockRemoveDock, () => FrameClock.After(1, () => _host.AskRemoveDock(Handle, () => _host.RemoveDock(this))), glyph: "", icon: PixelIcons.Remove),
        ];
    }

    private static string SizeName(DockIconSize size) => size switch { DockIconSize.Small => Strings.SizeSmall, DockIconSize.Medium => Strings.SizeMedium, _ => Strings.SizeLarge };

    // ---------------------------------------------------------------- changes

    public void SetIconSize(DockIconSize size)
    {
        if (size == Settings.IconSize)
            return;
        Settings.IconSize = size;
        _host.RequestSave();
        Relayout(new DockChange(DockChangeKind.Resized));
    }

    /// <summary>Adds programs at <paramref name="slot"/> (-1: the end), skipping ones already on the dock. Returns how many went in.</summary>
    public int Add(IEnumerable<DockItem> items, int slot = -1)
    {
        int added = 0;
        int at = slot < 0 ? Settings.Items.Count : Math.Min(slot, Settings.Items.Count);
        int first = at;
        foreach (var item in items)
        {
            if (DockGroups.Contains(Settings.Items, item.Key))
                continue;
            Settings.Items.Insert(at++, item);
            added++;
        }
        if (added == 0)
            return 0;
        _host.RequestSave();
        Relayout(new DockChange(DockChangeKind.Added, first));
        CheckMissing();
        return added;
    }

    public void Remove(DockItem item)
    {
        int index = Settings.Items.IndexOf(item);
        if (index < 0)
            return;
        Settings.Items.RemoveAt(index);
        foreach (var program in DockGroups.Programs([item]))
        {
            _missing.Remove(program.Key);
            _runStates.Forget(program.Key);
        }
        if (item.IsGroup && _openGroup == item)
            HidePanel(animated: false);
        _hover = -1;
        _host.RequestSave();
        // D11: the icon goes (the surface draws it leaving), then the bar shortens.
        Relayout(new DockChange(DockChangeKind.Removed, index));
    }

    /// <summary>"Add taskbar apps": the taskbar's pins that are not on the dock yet, in the taskbar's order. Read off the UI thread.</summary>
    public void AddTaskbarApps()
    {
        var ui = DispatcherQueue.GetForCurrentThread();
        _worker.Post(() =>
        {
            var pins = TaskbandPins.Read();
            ui.TryEnqueue(() =>
            {
                if (_disposed)
                    return;
                int added = Add(pins);
                if (added == 0)
                    _host.ShowWarning(pins.Count == 0 ? Strings.DockNoTaskbarApps : Strings.DockNoNewTaskbarApps);
            });
        });
    }

    /// <summary>Checks, off the UI thread, which programs can still be found; a missing one is dimmed.</summary>
    public void CheckMissing()
    {
        var items = DockGroups.Programs(Settings.Items).Select(i => i.Copy()).ToList();
        var unkeyed = items.Where(i => !_customKeys.ContainsKey(i.Key)).ToList();
        var ui = DispatcherQueue.GetForCurrentThread();
        _worker.Post(() =>
        {
            var missing = items.Where(item => !DockLauncher.Exists(item)).Select(item => item.Key).ToHashSet();
            // The custom tiles' keys, once per program: this may read an exe's version info, which is why it is here and not in a draw.
            var keys = unkeyed.Select(item => (item.Key, Value: DockCustomKeys.Resolve(item))).ToList();
            ui.TryEnqueue(() =>
            {
                if (_disposed)
                    return;
                // A redraw is due when a key differs from what the bar shows now (the no-read match Views falls back to).
                bool newKey = false;
                foreach (var (key, value) in keys)
                {
                    string? shown = _customKeys.TryGetValue(key, out var known) ? known
                        : DockGroups.Programs(Settings.Items).FirstOrDefault(i => i.Key == key) is { } item ? DockCustomKeys.Quick(item) : value;
                    newKey |= shown != value;
                    _customKeys[key] = value;
                }
                if (_surface is null || _missing.SetEquals(missing) && !newKey)
                    return;
                _missing.Clear();
                _missing.UnionWith(missing);
                _surface.SetItems(Views(Settings.Items), Settings.IconSize, new DockChange(DockChangeKind.None));
                RefreshPanel();
            });
        });
    }

    // ---------------------------------------------------------------- running programs (Build B)

    /// <summary>The taskbar's windows changed (the manager read them): see which of this dock's programs have one.</summary>
    public void OnWindows(IReadOnlyList<RunningWindow> windows)
    {
        _windows = windows;
        if (_surface is null || _disposed)
            return;
        var programs = DockGroups.Programs(Settings.Items).ToList();
        var running = programs.Where(i => i.SupportsRunning && DockRunning.WindowsOf(i, windows).Count > 0).Select(i => i.Key).ToHashSet();
        // What is already running when the dock starts is shown as it is, with no wipe.
        bool first = !_haveWindows;
        _haveWindows = true;
        foreach (var (key, state, how) in _runStates.WindowsNow(running, programs.Select(i => i.Key)))
            ApplyRunState(key, state, first ? DockRunTransition.None : how);
        ScheduleExpiry();
    }

    private bool _haveWindows;

    private void ApplyRunState(string key, DockRunState state, DockRunTransition how)
    {
        // While an icon is dragged the surface shows the preview order, which is not the saved one.
        int index = (_preview ?? Settings.Items).FindIndex(i => i.Key == key);
        if (index >= 0)
        {
            _surface?.SetRunState(index, state, how);
            return;
        }
        // A member of a group: the group's icon (its minis, its halo) and the panel follow, with no motion of their own.
        if (Settings.Items.Any(i => i.IsGroup && i.Children!.Any(c => c.Key == key)))
        {
            _surface?.SetItems(Views(_preview ?? Settings.Items), Settings.IconSize, new DockChange(DockChangeKind.None));
            RefreshPanel();
        }
    }

    /// <summary>One timer, only while some icon is opening: when the earliest one runs out, it goes back to idle (D3: not an error).</summary>
    private void ScheduleExpiry()
    {
        _expiry?.Stop();
        _expiry = null;
        if (_runStates.NextExpiry() is not { } due)
            return;
        double wait = Math.Max(1, (due - Now).TotalMilliseconds);
        _expiry = FrameClock.After((int)Math.Min(wait, int.MaxValue), () =>
        {
            foreach (string key in _runStates.Expire(Now))
                ApplyRunState(key, DockRunState.Idle, DockRunTransition.TimedOut);
            ScheduleExpiry();
        });
    }

    // ---------------------------------------------------------------- drops (called by DockDropTarget)

    /// <summary>The slot a drop at this screen x would land in.</summary>
    internal int SlotAtScreenX(int screenX)
        => DockGeometry.SlotAt(Settings.IconSize, Settings.Items.Count, (screenX - _layout.Bar.X) / PxPerGeometryDip);

    /// <summary>Screen pixels in one DIP of <see cref="DockGeometry"/>: the screen's scale for Glass; for Pixel, half a factor (an art pixel is two geometry DIPs).</summary>
    private double PxPerGeometryDip => _host.Theme == WidgetTheme.Pixel ? Pixel.PixelCard.Factor(_scale) / 2.0 : _scale;

    internal bool IsOverBar(int screenX, int screenY)
        => _layout.Bar.Contains(screenX, screenY) || _layout.Window.Contains(screenX, screenY);

    private int _gap = -1;

    internal void ShowGap(int slot)
    {
        _dropping = slot >= 0;
        CancelLeaveHide();
        if (slot == _gap)
            return;
        _gap = slot;
        _surface?.SetGap(slot);
    }

    internal void DropEnded()
    {
        EndProgramDrop();
        _dropping = false;
        _gap = -1;
        _surface?.SetGap(-1);
        ScheduleLeaveHide();
    }

    internal void Warn(string text) => _host.ShowWarning(text);

    // ---------------------------------------------------------------- end

    public void ApplyLanguage()
    {
        _surface?.ApplyLanguage();
        _surface?.SetItems(Views(Settings.Items), Settings.IconSize, new DockChange(DockChangeKind.Reset));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _slide?.Stop();
        _leave?.Stop();
        _expiry?.Stop();
        _edge.Dispose();
        if (_surface is not null)
        {
            _surface.Pointer -= OnPointer;
            _surface.Panel -= OnPanelEvent;
            _surface.Dispose();
        }
        StopGroupTimers();
        _worker.Dispose();
    }
}
