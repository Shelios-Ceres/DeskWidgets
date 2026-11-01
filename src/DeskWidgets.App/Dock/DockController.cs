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
internal sealed class DockController : IDisposable
{
    private const double DragThresholdDip = 4;
    private const int LeaveHideMs = 600;
    private const int ExitMs = 150;
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
    private readonly HashSet<string> _missing = [];
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
        _edge.Reached += () => Reveal("pointer at the edge");
        _edge.DragReached += () => Reveal("drag at the edge");
        FindMonitor();
        Relayout(change: new DockChange(DockChangeKind.Reset), animateWindow: false);
        _surface.Show();
        EvaluateRule();
        CheckMissing();
        _surface.AttachDropTarget(new DockDropTarget(this));
    }

    /// <summary>Swaps the window for the other theme's (the host does this on a theme switch).</summary>
    public void ReplaceSurface()
    {
        if (_surface is null)
            return;
        _surface.Pointer -= OnPointer;
        _surface.Dispose();
        _surface = _host.CreateSurface();
        _surface.Pointer += OnPointer;
        Relayout(change: new DockChange(DockChangeKind.Reset), animateWindow: false);
        _surface.Show();
        _surface.AttachDropTarget(new DockDropTarget(this));
        if (_hidden)
            _surface.MoveWindowTo(_layout.Window.X, _layout.HiddenTop);
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
        Relayout(new DockChange(DockChangeKind.None), animateWindow: false);
    }

    private DockRect Work => new(_monitor.Work.Left, _monitor.Work.Top, _monitor.Work.Width, _monitor.Work.Height);

    private int PxOf(double dip) => (int)Math.Round(dip * _scale);

    private void Relayout(DockChange change, bool animateWindow, bool redrawItems = true)
    {
        if (_surface is null || _monitor.Device is null)
            return;
        _scale = Win32.GetScaleAt(new Win32.POINT { X = _monitor.Work.Left + 1, Y = _monitor.Work.Top + 1 });
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
        _edge.Place(new DockRect(_monitor.Work.Left, _monitor.Work.Top, _monitor.Work.Width, _monitor.Work.Height), Math.Max(2, PxOf(DockGeometry.EdgeStripDip)));

        _surface.PlaceWindow(_layout.Window, _layout.Bar, _scale);
        if (redrawItems)
            _surface.SetItems(Views(Settings.Items), Settings.IconSize, change);
        if (_hidden)
            _surface.MoveWindowTo(_layout.Window.X, _layout.HiddenTop);
        RefreshLabelFor(_hover);
    }

    private IReadOnlyList<DockItemView> Views(IEnumerable<DockItem> items) => [.. items.Select(item => new DockItemView(item, _missing.Contains(item.Key)))];

    // ---------------------------------------------------------------- hiding and showing

    /// <summary>The tracker said something changed: ask it again for this screen.</summary>
    public void EvaluateRule()
    {
        if (_surface is null || _disposed)
            return;
        nint monitor = Win32.MonitorFromPoint(new Win32.POINT { X = _monitor.Work.Left + 1, Y = _monitor.Work.Top + 1 }, Win32.MONITOR_DEFAULTTONEAREST);
        var verdict = _host.VerdictFor(monitor);
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
            Slide(hide: true);
        }
    }

    private bool InUse => _menuOpen || _dragging || _preview is not null || _dropping;

    private bool _dropping;

    private void Reveal(string how)
    {
        if (!_hidden || _surface is null)
            return;
        nint monitor = Win32.MonitorFromPoint(new Win32.POINT { X = _monitor.Work.Left + 1, Y = _monitor.Work.Top + 1 }, Win32.MONITOR_DEFAULTTONEAREST);
        if (_host.VerdictFor(monitor).FullScreen)
            return; // the edge brings nothing back over a full-screen window
        _revealed = true;
        Slide(hide: false);
    }

    private void ScheduleLeaveHide()
    {
        _leave?.Stop();
        if (!_revealed || !_ruleHides)
            return;
        _leave = FrameClock.After(LeaveHideMs, () =>
        {
            if (InUse || _disposed)
                return;
            _revealed = false;
            Slide(hide: true);
        });
    }

    private void CancelLeaveHide() => _leave?.Stop();

    /// <summary>Slides the window off the bottom edge or back (D6: Exit 150 ms, Enter 240 ms). A request in the direction already running is ignored.</summary>
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
            _surface.SetHover(-1, null);
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
        int duration = hide ? ExitMs : EnterMs;
        double start = FrameClock.Now;
        _slide = FrameClock.Every(8, now =>
        {
            double t = Math.Min(1, (now - start) / duration);
            double eased = hide ? t * t * t : 1 - Math.Pow(1 - t, 3);
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
        _surface.SetHover(index, _missing.Contains(item.Key) ? Strings.DockCantFind(item.Name) : item.Name);
    }

    private void PointerDown(DockPointer p)
    {
        if (p.Right)
        {
            OpenMenu(p);
            return;
        }
        _down = p;
        _dragging = false;
        _dragKind = DragKind.None;
        if (!p.Left && !p.Middle)
            return;
        _pressed = IconAtPointer(p);
        if (_pressed >= 0)
            _surface?.SetPressed(_pressed);
        else if (OverBar(p))
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
        int slot = DockGeometry.SlotAt(Settings.IconSize, _preview.Count, p.X);
        // slot counts the icons whose middle is left of the pointer; the dragged icon is one of them while it is left of itself.
        int target = Math.Clamp(slot > _dragFrom ? slot - 1 : slot, 0, _preview.Count - 1);
        if (target != _dragFrom)
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
        if (at is { } screen && !string.Equals(screen.Device, _monitor.Device, StringComparison.OrdinalIgnoreCase))
        {
            // On another screen the bar's centre goes under the pointer.
            _monitor = screen;
            Settings.Monitor = screen.Device;
            _down = p;
            _barOffsetStart = p.ScreenX - (screen.Work.Left + screen.Work.Width / 2);
        }
        int offsetPx = _barOffsetStart + (p.ScreenX - _down.ScreenX);
        // Clamped by the layout, which gives back what fits.
        var clamped = DockPlacement.ClampOffset(offsetPx, _monitor.Work.Width, _layout.Bar.Width, PxOf(DockGeometry.EdgeGapDip));
        Settings.OffsetDip = clamped / _scale;
        Relayout(new DockChange(DockChangeKind.None), animateWindow: false, redrawItems: false); // only the window moves; the picture is the same
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
        _dragFrom = -1;
        _surface?.SetDrag(-1, 0);
        if (preview is null)
            return;
        if (!preview.SequenceEqual(Settings.Items))
        {
            Settings.Items.Clear();
            Settings.Items.AddRange(preview);
            _host.RequestSave();
        }
        _surface?.SetItems(Views(Settings.Items), Settings.IconSize, new DockChange(DockChangeKind.None));
    }

    // ---------------------------------------------------------------- click and menus

    private void Click(int index, DockPointer p)
    {
        var item = Settings.Items[index];
        if (_missing.Contains(item.Key))
        {
            OpenMissing(item, p);
            return;
        }
        Report(DockLauncher.Start(item, Handle));
    }

    private void Report(string? error)
    {
        if (error is not null)
            _host.ShowWarning(error);
    }

    private void OpenMissing(DockItem item, DockPointer p)
    {
        if (_host.Theme == WidgetTheme.Pixel)
        {
            _host.AskRemoveMissing(Handle, item.Name, () => Remove(item));
            return;
        }
        ShowMenu(
        [
            new MenuEntry { Text = Strings.DockMissingHeader(item.Name), Kind = MenuKind.Item, Enabled = false, Glyph = "" },
            MenuEntry.Separator(),
            MenuEntry.Item(Strings.DockRemoveFromDock, () => Remove(item), glyph: "", icon: PixelIcons.Remove),
        ], p.ScreenX, p.ScreenY);
    }

    private void OpenMenu(DockPointer p)
    {
        int index = IconAtPointer(p);
        if (index >= 0)
        {
            ShowMenu(AppMenu(Settings.Items[index]), p.ScreenX, p.ScreenY);
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
        var rows = new List<MenuEntry> { MenuEntry.Item(Strings.DockOpen, () => Report(DockLauncher.Start(item, Handle)), glyph: "") };
        if (!item.IsStoreApp)
            rows.Add(MenuEntry.Item(Strings.FolderRunAsAdministrator, () => Report(DockLauncher.Start(item, Handle, asAdministrator: true)), glyph: "", icon: PixelIcons.Shield));
        rows.Add(new MenuEntry
        {
            Text = Strings.FolderOpenFileLocation, Kind = MenuKind.Item, Enabled = !string.IsNullOrEmpty(item.ExePath) && File.Exists(item.ExePath),
            Click = () => DockLauncher.OpenFileLocation(item), Glyph = "", Icon = PixelIcons.Folder,
        });
        rows.Add(MenuEntry.Separator());
        rows.Add(MenuEntry.Item(Strings.DockRemoveFromDock, () => Remove(item), glyph: "", icon: PixelIcons.Remove));
        if (!string.IsNullOrEmpty(item.ExePath) && File.Exists(item.ExePath))
        {
            rows.Add(MenuEntry.Separator());
            rows.Add(MenuEntry.Item(Strings.FolderMoreOptions, () => ShowMoreOptions(item), glyph: ""));
        }
        return rows;
    }

    private void ShowMoreOptions(DockItem item)
    {
        Win32.GetCursorPos(out var cursor);
        Report(ShellContextMenu.Show([item.ExePath!], Handle, cursor.X, cursor.Y, onOpen: () => Report(DockLauncher.Start(item, Handle)), onRename: null, onDelete: null));
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
        Relayout(new DockChange(DockChangeKind.Resized), animateWindow: true);
    }

    /// <summary>Adds programs at <paramref name="slot"/> (-1: the end), skipping ones already on the dock. Returns how many went in.</summary>
    public int Add(IEnumerable<DockItem> items, int slot = -1)
    {
        int added = 0;
        int at = slot < 0 ? Settings.Items.Count : Math.Min(slot, Settings.Items.Count);
        int first = at;
        foreach (var item in items)
        {
            if (Settings.Items.Any(existing => existing.Key == item.Key))
                continue;
            Settings.Items.Insert(at++, item);
            added++;
        }
        if (added == 0)
            return 0;
        _host.RequestSave();
        Relayout(new DockChange(DockChangeKind.Added, first), animateWindow: true);
        CheckMissing();
        return added;
    }

    public void Remove(DockItem item)
    {
        int index = Settings.Items.IndexOf(item);
        if (index < 0)
            return;
        Settings.Items.RemoveAt(index);
        _missing.Remove(item.Key);
        _hover = -1;
        _host.RequestSave();
        // D11: the icon goes (the surface draws it leaving), then the bar shortens.
        Relayout(new DockChange(DockChangeKind.Removed, index), animateWindow: true);
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
        var items = Settings.Items.Select(i => i.Copy()).ToList();
        var ui = DispatcherQueue.GetForCurrentThread();
        _worker.Post(() =>
        {
            var missing = items.Where(item => !DockLauncher.Exists(item)).Select(item => item.Key).ToHashSet();
            ui.TryEnqueue(() =>
            {
                if (_disposed || _surface is null || _missing.SetEquals(missing))
                    return;
                _missing.Clear();
                _missing.UnionWith(missing);
                _surface.SetItems(Views(Settings.Items), Settings.IconSize, new DockChange(DockChangeKind.None));
            });
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
        _edge.Dispose();
        if (_surface is not null)
        {
            _surface.Pointer -= OnPointer;
            _surface.Dispose();
        }
        _worker.Dispose();
    }
}
