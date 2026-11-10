using DeskWidgets.Interop;
using DeskWidgets.Motion;
using DeskWidgets.Pixel;
using DeskWidgets.PixelUi;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI.Dispatching;
using Windows.UI.ViewManagement;

namespace DeskWidgets.Dock;

/// <summary>
/// The Pixel theme's dock window: a layered, topmost tool window that is never activated, showing <see cref="PixelDockPainter"/>'s canvas
/// 1:1 (every art pixel a whole factor of screen pixels, like the Pixel menus). Alpha-0 pixels (the headroom) pass clicks and OLE drags
/// through to the window behind (SPIKE section 1). It only draws and reports the pointer; the <see cref="DockController"/> decides.
/// The canvas is aligned to the bar, so the window can be up to one art pixel narrower than placed at each end, and it reaches
/// <see cref="PixelDockPainter.ShadowOffset"/> art pixels below the bar for the bar's hard shadow; <see cref="WindowRect"/> says where it is.
/// </summary>
internal sealed class PixelDockSurface : PixelPopupWindow, IDockSurface
{
    private const uint WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
    private const long MK_LBUTTON = 0x1, MK_RBUTTON = 0x2, MK_SHIFT = 0x4, MK_MBUTTON = 0x10;
    private static readonly nint HwndTopmost = -1;

    private readonly PixelDockArt _art;
    private readonly PixelDockPainter _painter;
    private readonly DropTargetRegistration _drop = new();
    private readonly StepAnimator _motion = new();
    private readonly DispatcherQueue _queue;
    private readonly UISettings _ui = new();
    private PixelCanvas? _canvas;

    private FrameClock.Ticket? _liftTicket;
    private FrameClock.Ticket? _runTicket;
    private FrameClock.Ticket? _moveTicket;
    private FrameClock.Ticket? _gapClose;
    private FrameClock.Ticket? _flashTicket;
    private FrameClock.Ticket? _floatTicket; // Phase 16.3: the dragged member's picture stepping to the bar's icon size
    private FrameClock.Ticket? _repaint;
    private (int X, int Y)? _pendingMove;

    private bool _placed;
    private bool _painted;
    private bool _placedSinceItems;
    private bool _resetPositions = true;
    private int _pendingShift;
    private int _factor = 3;
    private int _x, _y;             // the window's real top-left, screen pixels
    private int _windowDx;          // the real left minus the placed left (the canvas is aligned to the bar)
    private int _placedTop;         // the top the controller last placed the window at
    private int _barOffsetPx;       // the bar's left inside the window, screen pixels
    private int _canvasLeft;        // the canvas's left edge on the screen as last placed (the window's x before any slide)

    private bool _tracking;
    private bool _holding;
    private bool _dragSeen;
    private bool _upPending;
    private int _upIcon = -1;
    private string? _hoverKey;
    private bool _parked; // hidden by Hide (the slide-out ended), until Show
    private bool _disposed;

    private PixelDockPanel? _panel;
    private int _panelGroup = -1;
    private IDropTarget? _panelDrop;
    private bool _swallowUp; // a press on the dock that closed the group's panel: its release is not a click either

    public PixelDockSurface()
    {
        _queue = DispatcherQueue.GetForCurrentThread();
        _art = new PixelDockArt(RepaintSoon);
        _painter = new PixelDockPainter(_art);
        CreateWindow(noActivate: true);
        _ui.ColorValuesChanged += OnColorsChanged;
    }

    public nint Handle => Hwnd;

    public double Scale { get; private set; } = 1;

    public DockRect WindowRect => new(_x, _y, _painter.CanvasWidth * _factor, _painter.CanvasHeight * _factor);

    public event Action<DockPointer>? Pointer;

    public void Show()
    {
        if (Hwnd == 0 || _disposed)
            return;
        if (_parked)
        {
            // Nothing was drawn while hidden: what changed since shows with the window.
            _parked = false;
            Render();
        }
        else if (Win32.IsVisible(Hwnd))
        {
            return;
        }
        ShowWindow(activate: false);
        Win32.SetWindowPos(Hwnd, HwndTopmost, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    public void Hide()
    {
        if (Hwnd == 0 || _disposed || _parked)
            return;
        _parked = true;
        // No Cancel or Leave to the controller: it is the one hiding the dock, and its own state goes with it.
        _holding = false; // first, so losing the capture is not a Cancel
        _tracking = false;
        _upPending = false;
        _dragSeen = false;
        if (Native.GetCapture() == Hwnd)
            Native.ReleaseCapture();

        // Every look ends where it was heading, at once, and every ticket stops: a hidden dock draws nothing and wakes nothing.
        _hoverKey = null;
        _painter.Label = null;
        _painter.LabelIndex = -1;
        _painter.LabelShown = false;
        _painter.StepLifts(null, snap: true);
        _painter.Pressed = -1;
        _painter.Flash = -1;
        _painter.DragIndex = -1;
        _painter.Forming = -1;
        _painter.Floating = null;
        _painter.DropPreview = null;
        _painter.OpenLit = null;
        _painter.OpenHover = -1;
        _swallowUp = false;
        HidePanel(animated: false); // a panel over a dock that has gone would point at nothing
        _motion.Cancel();
        _gapClose?.Stop();
        _gapClose = null;
        _painter.ClearGap();
        _painter.Snap();
        _painter.SnapRuns();
        foreach (var ticket in new[] { _liftTicket, _runTicket, _flashTicket, _repaint, _floatTicket })
            ticket?.Stop();
        _liftTicket = _runTicket = _flashTicket = _repaint = _floatTicket = null;
        _painter.FloatSize = -1;
        // The slide's last step may still be waiting for its frame: the window goes where the controller left it.
        _moveTicket?.Stop();
        _moveTicket = null;
        if (_pendingMove is { } pending)
        {
            _pendingMove = null;
            ApplyMove(pending.X, pending.Y);
        }
        HideWindow();
    }

    // ---------------------------------------------------------------- placing and moving

    public void PlaceWindow(DockRect window, DockRect bar, double scale)
    {
        int factor = PixelCard.Factor(scale);
        Scale = scale;
        // The canvas starts a whole number of art pixels left of the bar, so the bar's art pixels are the screen's.
        int barArt = Math.Max(0, (bar.X - window.X) / factor);
        int originX = bar.X - barArt * factor;
        int widthArt = Math.Max(barArt + bar.Width / factor, (window.Right - originX) / factor);
        int originY = bar.Y - PixelDockPainter.Head * factor;

        // What is shown keeps its place on the screen when the bar moves inside the window (an add or a removal re-centres the bar):
        // a pixel at canvas x was at screen _canvasLeft + x * factor, which is canvas x + (_canvasLeft - originX) / factor now.
        bool sameFactor = _placed && factor == _factor;
        _pendingShift = sameFactor ? _pendingShift + (int)Math.Round((_canvasLeft - originX) / (double)factor) : 0;
        _resetPositions |= !sameFactor;

        // A window parked below the screen's edge (or half-way through the slide) keeps its offset until the controller moves it.
        int dy = _placed ? _y - _placedTop : 0;
        _factor = factor;
        _windowDx = originX - window.X;
        _placedTop = originY;
        _x = originX;
        _y = originY + dy;
        _barOffsetPx = barArt * factor;
        _painter.CanvasWidth = widthArt;
        _painter.BarLeft = barArt;
        var work = Win32.GetWorkArea(new Win32.POINT { X = bar.X + bar.Width / 2, Y = bar.Y + 1 });
        _painter.ScreenLeft = (int)Math.Ceiling((work.Left - originX) / (double)factor);
        _painter.ScreenRight = (work.Right - originX) / factor;
        _placed = true;
        _placedSinceItems = true;
        // A placement is where the window is now: a slide step still waiting would put it back.
        _moveTicket?.Stop();
        _moveTicket = null;
        _pendingMove = null;
        _canvasLeft = originX;
    }

    public void MoveWindowTo(int x, int y)
    {
        int realX = x + _windowDx;
        if (_parked)
        {
            ApplyMove(realX, y); // hidden: no steps to see, no ticket to leave running
            return;
        }
        // D6 is stepped: the window moves at most once a Pixel frame (40 ms), to where the controller's slide is by then.
        if (!MotionSettings.Enabled || _moveTicket is null)
        {
            ApplyMove(realX, y);
            if (MotionSettings.Enabled)
            {
                _moveTicket = FrameClock.Every(StepAnimator.FrameMs, _ =>
                {
                    if (_pendingMove is { } pending)
                    {
                        _pendingMove = null;
                        ApplyMove(pending.X, pending.Y);
                    }
                    else
                    {
                        _moveTicket?.Stop();
                        _moveTicket = null;
                    }
                });
            }
            return;
        }
        _pendingMove = (realX, y);
    }

    private void ApplyMove(int x, int y)
    {
        if (Hwnd == 0)
            return;
        _x = x;
        _y = y;
        // Moving never repaints (SPIKE: 0.2 ms a step).
        Win32.SetWindowPos(Hwnd, 0, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    // ---------------------------------------------------------------- what it shows

    public void SetItems(IReadOnlyList<DockItemView> items, DockIconSize size, DockChange change)
    {
        if (_disposed)
            return;
        bool placed = _placedSinceItems;
        _placedSinceItems = false;
        int shift = _pendingShift;
        _pendingShift = 0;
        bool animate = MotionSettings.Enabled && _painted && !_resetPositions && !_parked;
        _resetPositions = false;

        if (_gapClose is not null)
        {
            // The drop closed the gap and added the program in the same breath: the gap's slot is where the new icon POPs.
            _gapClose.Stop();
            _gapClose = null;
            _painter.ClearGap();
            if (change.Kind is not (DockChangeKind.Added or DockChangeKind.Grouped))
                animate = false; // a program dropped on a group still POPs it
        }
        if (change.Kind is DockChangeKind.Added or DockChangeKind.Removed or DockChangeKind.Grouped or DockChangeKind.Ungrouped)
            _painter.Shift(shift);
        if (change.Kind == DockChangeKind.Grouped)
            _painter.DragIndex = -1; // the dragged icon went into the group: no dotted slot may stay on an index that now means another icon

        var frames = _painter.Change(items, size, change, Palette(), animate, keepMotion: !placed);
        if (frames is null)
        {
            Render();
            return;
        }
        _motion.Cancel();
        if (_painter.Pressed >= items.Count)
            _painter.Pressed = -1;
        if (_painter.LabelIndex >= items.Count)
        {
            _painter.LabelIndex = -1;
            _painter.LabelShown = false;
        }
        Play(frames);
    }

    /// <summary>Plays a change's frames, one each Pixel frame (frame 0 at once); with none, or motion off, the end at once.</summary>
    private void Play(List<Action> frames)
    {
        if (frames.Count == 0)
        {
            Render();
            return;
        }
        _motion.Start(frames.Count, frame =>
        {
            if (_disposed)
                return;
            frames[frame]();
            Render();
        });
    }

    public void SetHover(int index, string? label)
    {
        if (_disposed || _parked)
            return; // nothing can be hovered on a hidden dock (Hide cleared it)
        string? key = index >= 0 && index < _painter.Items.Count ? _painter.Items[index].Item.Key : null;
        if (key is not null && key == _hoverKey)
        {
            if (_painter.Label != label)
            {
                _painter.Label = label;
                Render();
            }
            return;
        }
        bool fromAnother = _hoverKey is not null && key is not null;
        _hoverKey = key;
        _painter.LabelIndex = key is null ? -1 : index;
        _painter.Label = label;
        // D1: the plate shows with the lift's second frame; moving on to the next icon redraws it over that one at once; leaving takes it at once.
        _painter.LabelShown = fromAnother;
        StepLifts();
    }

    private void StepLifts()
    {
        bool motion = MotionSettings.Enabled;
        bool more = _painter.StepLifts(_hoverKey, snap: !motion);
        Render();
        if (more && _liftTicket is null)
        {
            _liftTicket = FrameClock.Every(StepAnimator.FrameMs, _ =>
            {
                bool again = _painter.StepLifts(_hoverKey, snap: !MotionSettings.Enabled);
                Render();
                if (!again)
                {
                    _liftTicket?.Stop();
                    _liftTicket = null;
                }
            });
        }
        else if (!more)
        {
            _liftTicket?.Stop();
            _liftTicket = null;
        }
    }

    public void SetPressed(int index)
    {
        if (_disposed)
            return;
        int was = _painter.Pressed;
        if (index == was)
            return;
        _painter.Pressed = index;
        // D2: a press let go over the icon it began on, with no drag in between, is a click: one FLASH frame on Select.
        if (index < 0 && was >= 0 && _upPending && _upIcon == was && !_dragSeen && MotionSettings.Enabled && !_parked)
        {
            _flashTicket?.Stop();
            _painter.Flash = was;
            _flashTicket = FrameClock.After(StepAnimator.FrameMs, () =>
            {
                _flashTicket = null;
                _painter.Flash = -1;
                Render();
            });
        }
        Render();
    }

    public void SetGap(int index)
    {
        if (_disposed)
            return;
        if (index < 0)
        {
            if (_painter.Gap < 0 || _gapClose is not null)
                return;
            // A drop closes the gap and adds the program right after: wait for that before closing it, or the icons would twitch back.
            _gapClose = FrameClock.After(1, () =>
            {
                _gapClose = null;
                _motion.Cancel();
                Play(_painter.SetGap(-1, MotionSettings.Enabled));
            });
            return;
        }
        if (_gapClose is not null)
        {
            _gapClose.Stop();
            _gapClose = null;
        }
        if (index == _painter.Gap)
            return;
        _motion.Cancel();
        Play(_painter.SetGap(index, MotionSettings.Enabled && _painted && !_parked));
    }

    public void SetDrag(int index, double x)
    {
        if (_disposed)
            return;
        if (index >= 0)
            _dragSeen = true;
        int center = (int)Math.Round(x * Scale / _factor); // DIPs from the bar's left to art pixels
        if (index == _painter.DragIndex && center == _painter.DragCenter)
            return;
        _painter.DragIndex = index;
        _painter.DragCenter = center;
        Render();
    }

    public void SetRunState(int index, DockRunState state, DockRunTransition how)
    {
        if (_disposed || index < 0 || index >= _painter.Items.Count)
            return;
        // D3 to D5. Several icons can be in their moments at once: one ticket steps them all, and it stops when every look is still
        // (D3's half-way hold included), so nothing wakes the app while the dock is at rest.
        bool more = _painter.SetRun(_painter.Items[index].Item.Key, state, how, MotionSettings.Enabled && _painted && !_parked);
        Render();
        if (more && _runTicket is null)
        {
            _runTicket = FrameClock.Every(StepAnimator.FrameMs, _ =>
            {
                bool again = _painter.StepRuns();
                Render();
                if (!again)
                {
                    _runTicket?.Stop();
                    _runTicket = null;
                }
            });
        }
    }

    public void ShowMenu(IReadOnlyList<MenuEntry> entries, int screenX, int screenY, Action closed)
    {
        // Where a menu opens (Pixel HANDOFF section 8): above the bar, its bottom edge 4 art pixels above the bar's top; an icon's menu is centred
        // on the icon, the bar's on the pointer.
        int barLeft = _x + _barOffsetPx;
        double geometryX = (screenX - barLeft) * 2.0 / _factor;
        int icon = DockGeometry.IconAt(_painter.Size, _painter.Items.Count, geometryX);
        int centerX = icon >= 0
            ? barLeft + (int)Math.Round((DockGeometry.IconLeftDip(_painter.Size, icon) + DockGeometry.IconDip(_painter.Size) / 2) * _factor / 2.0)
            : screenX;
        int barTop = _y + PixelDockPainter.Head * _factor;
        if (_panel is { IsOpen: true } panel && panel.Rect.Contains(screenX, screenY))
        {
            // A member's menu, asked from the panel: it opens at the pointer, and its taking the activation does not close the panel.
            panel.MenuOpen = true;
            PixelMenu.Show(entries, screenX, screenY, closed: () =>
            {
                panel.MenuClosed();
                closed();
            });
            return;
        }
        PixelMenu.Show(entries, screenX, screenY, closed: closed, openAbove: (centerX, barTop - 4 * _factor));
    }

    public void AttachDropTarget(IDropTarget target)
    {
        if (Hwnd != 0)
            _drop.RegisterOn(Hwnd, target);
    }

    // ---------------------------------------------------------------- groups and opening files (Build C)

    public void SetGroupForming(int index)
    {
        if (_disposed || index == _painter.Forming)
            return;
        // D8: the target turns into a group of one at once (a still state, nothing ticks); -1 puts it back.
        _painter.Forming = index;
        Render();
    }

    public void SetGroupDropPreview(int groupIndex, DockItemView? dragged)
    {
        if (_disposed)
            return;
        (int, DockItemView)? preview = dragged is not null && groupIndex >= 0 && groupIndex < _painter.Items.Count && _painter.Items[groupIndex].IsGroup
            ? (groupIndex, dragged)
            : null;
        if (preview == _painter.DropPreview)
            return;
        // A still state, at once (the drop's POP comes with the Grouped list).
        _painter.DropPreview = preview;
        Render();
    }

    public void SetOpenWith(IReadOnlyList<bool>? lit, int hovered)
    {
        if (_disposed)
            return;
        // D12: the lit plates, the target's lift and its "Open with" plate show at once, and a drop snaps them off: no frames.
        _painter.OpenLit = lit?.ToList();
        _painter.OpenHover = lit is null ? -1 : hovered;
        Render();
    }

    public event Action<DockPanelEvent>? Panel;

    public bool PanelOpen => _panel?.IsOpen == true;

    public DockRect PanelRect => _panel?.Rect ?? default;

    public void ShowPanel(int groupIndex, string title, IReadOnlyList<DockItemView> members, bool forDrop)
    {
        if (_disposed || Hwnd == 0 || _parked || groupIndex < 0 || groupIndex >= _painter.Items.Count)
            return;
        if (_panel is { IsOpen: true } open)
        {
            if (groupIndex == _panelGroup)
            {
                open.Update(title, members, forDrop);
                return;
            }
            open.Close(animated: false, restoreForeground: false);
        }
        _panelGroup = groupIndex;
        // Above the group's icon as it rests on the bar (HANDOFF 6): the frame's bottom 6 art pixels above the bar's top.
        int barLeft = _x + _barOffsetPx;
        int centerX = barLeft + (DockGeometry.PixelIconLeftArt(_painter.Size, groupIndex) * 2 + _painter.IconArt) * _factor / 2;
        int barTop = _y + PixelDockPainter.Head * _factor;
        var work = Win32.GetWorkArea(new Win32.POINT { X = centerX, Y = barTop + 1 });
        var anchor = (centerX, barTop, new DockRect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top));
        PixelDockPanel? panel = null;
        panel = new PixelDockPanel(_art, e => OnPanelEvent(panel!, e), _factor, anchor, title, members, forDrop, _panelDrop);
        _panel = panel;
        panel.Open();
    }

    private void OnPanelEvent(PixelDockPanel from, DockPanelEvent e)
    {
        if (from != _panel)
            return; // a panel already replaced
        if (e.Kind == DockPanelEventKind.Dismissed)
            _panel = null;
        FollowDragOut(from, e);
        Panel?.Invoke(e);
    }

    /// <summary>
    /// A member dragged out of the panel: the panel draws it only inside its own frame, so from there on the dock draws it under the pointer,
    /// over the bar and its headroom (the panel's bottom lies in that headroom, so the picture never drops out between the two windows).
    /// </summary>
    private void FollowDragOut(PixelDockPanel panel, DockPanelEvent e)
    {
        (DockItemView, int, int)? floating = null;
        if (e.Kind == DockPanelEventKind.DragMove && e.Index >= 0 && e.Index < panel.Members.Count && WindowRect.Contains(e.ScreenX, e.ScreenY))
            floating = (panel.Members[e.Index], (e.ScreenX - _x) / _factor, (e.ScreenY - _y) / _factor);
        if (floating is null && _painter.Floating is null)
            return;
        bool arriving = floating is not null && _painter.Floating is null;
        _painter.Floating = floating;
        if (floating is null)
            EndFloatSteps();
        else if (arriving)
            BeginFloatSteps();
        Render();
    }

    /// <summary>
    /// Phase 16.3: the picture crossed from the panel onto the bar (it is the panel's 24-pixel icon until now): it steps to the bar's icon size, one
    /// frame per step (24, then halfway, then the bar's). No steps with "Animation effects" off, or when the bar's icons are 24 too.
    /// </summary>
    private void BeginFloatSteps()
    {
        EndFloatSteps();
        var sizes = MotionSettings.Enabled ? PixelDockPainter.FloatSizes(PixelDockPanelLayout.IconSize, _painter.IconArt) : [];
        if (sizes.Length == 0)
            return;
        _painter.FloatSize = PixelDockPanelLayout.IconSize;
        int step = 0;
        _floatTicket = FrameClock.Every(StepAnimator.FrameMs, _ =>
        {
            _painter.FloatSize = step < sizes.Length - 1 ? sizes[step] : -1;
            step++;
            if (step >= sizes.Length)
                EndFloatSteps();
            Render();
        });
    }

    private void EndFloatSteps()
    {
        _floatTicket?.Stop();
        _floatTicket = null;
        _painter.FloatSize = -1;
    }

    public void SetPanelMembers(string title, IReadOnlyList<DockItemView> members) => _panel?.Update(title, members);

    public void SetPanelOpenWith(IReadOnlyList<bool>? lit, int hovered) => _panel?.SetOpenWith(lit, hovered);

    public void BeginPanelRename() => _panel?.BeginRename();

    public void HidePanel(bool animated)
    {
        var panel = _panel;
        _panel = null;
        if (_painter.Floating is not null)
        {
            _painter.Floating = null; // a drag out that ended with the panel
            Render();
        }
        panel?.Close(animated && MotionSettings.Enabled && !_parked, restoreForeground: true);
    }

    /// <summary>The member under a screen pixel in the open panel, or -1: the controller's drop target hit-tests a file drag with it.</summary>
    public int PanelMemberAt(int screenX, int screenY) => _panel?.MemberAt(screenX, screenY) ?? -1;

    /// <summary>The controller's drop target, registered on every panel window from now on (each open makes a new window).</summary>
    public void AttachPanelDropTarget(IDropTarget target) => _panelDrop = target;

    public void ApplyLanguage()
    {
        _painter.ForgetText();
        _panel?.Redraw();
    }

    private static PixelPalette Palette() => CurrentPalette();

    private void Render()
    {
        if (_disposed || _parked || Hwnd == 0 || !_placed || _painter.CanvasWidth <= 0)
            return; // a hidden dock is drawn by Show
        int width = _painter.CanvasWidth, height = _painter.CanvasHeight;
        if (_canvas is null || _canvas.Width != width || _canvas.Height != height)
            _canvas = new PixelCanvas(width, height);
        _painter.Paint(_canvas, Palette());
        Present(_canvas, _factor, _x, _y);
        _painted = true;
    }

    /// <summary>A real icon came: one repaint for however many come together.</summary>
    private void RepaintSoon()
    {
        _panel?.RepaintSoon(); // the panel draws from the same pictures
        if (_disposed || _parked || _repaint is not null)
            return; // hidden: Show draws
        _repaint = FrameClock.After(1, () =>
        {
            _repaint = null;
            Render();
        });
    }

    private void OnColorsChanged(UISettings sender, object args)
        => _queue.TryEnqueue(() =>
        {
            if (_disposed)
                return;
            _painter.ForgetText();
            Render();
            _panel?.Redraw();
        });

    // ---------------------------------------------------------------- pointer

    protected override nint OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg is Native.WM_LBUTTONDOWN or WM_MBUTTONDOWN or Native.WM_RBUTTONDOWN)
        {
            _swallowUp = false;
            if (_panel is { IsOpen: true, ForDrop: false } panel)
            {
                // A click anywhere else closes the group's panel, the dock included (the dock never activates, so the panel is not deactivated by
                // it). Like a menu's click-away, the press is used up: a click on the group's own icon closes its panel rather than opening it again.
                _swallowUp = true;
                panel.Dismiss(restoreForeground: true);
                return 0;
            }
        }
        else if (_swallowUp && msg is Native.WM_LBUTTONUP or WM_MBUTTONUP or Native.WM_RBUTTONUP)
        {
            _swallowUp = false;
            return 0;
        }
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                return Native.MA_NOACTIVATE;
            case Native.WM_ERASEBKGND:
                return 1;
            case Native.WM_MOUSEMOVE:
                if (!_tracking)
                {
                    Native.TrackLeave(Hwnd);
                    _tracking = true;
                }
                Raise(DockPointerKind.Move, lParam, Held(wParam));
                return 0;
            case Native.WM_MOUSELEAVE:
                _tracking = false;
                if (!_holding)
                    Raise(DockPointerKind.Leave, CursorInWindow(), (false, false, false, false));
                return 0;
            case Native.WM_LBUTTONDOWN or WM_MBUTTONDOWN:
                _holding = true;
                _dragSeen = false;
                Native.SetCapture(Hwnd); // the Moves of a drag keep coming outside the window
                Raise(DockPointerKind.Down, lParam, (msg == Native.WM_LBUTTONDOWN, false, msg == WM_MBUTTONDOWN, Shift(wParam)));
                return 0;
            case Native.WM_RBUTTONDOWN:
                Raise(DockPointerKind.Down, lParam, (false, true, false, Shift(wParam)));
                return 0;
            case Native.WM_LBUTTONUP or WM_MBUTTONUP:
                ButtonUp(lParam, (msg == Native.WM_LBUTTONUP, false, msg == WM_MBUTTONUP, Shift(wParam)));
                return 0;
            case Native.WM_RBUTTONUP:
                Raise(DockPointerKind.Up, lParam, (false, true, false, Shift(wParam)));
                return 0;
            case Native.WM_CAPTURECHANGED:
                if (_holding && lParam != Hwnd)
                {
                    _holding = false;
                    Raise(DockPointerKind.Cancel, CursorInWindow(), (false, false, false, false));
                }
                return 0;
        }
        return base.OnMessage(msg, wParam, lParam);
    }

    private void ButtonUp(nint lParam, (bool Left, bool Right, bool Middle, bool Shift) buttons)
    {
        // The FLASH needs to know where the press was let go: SetPressed(-1) comes while the controller handles this Up.
        var (x, _) = BarDips(lParam);
        _upIcon = DockGeometry.IconAt(_painter.Size, _painter.Items.Count, x);
        _upPending = true;
        _holding = false; // before the capture goes, so losing it is not a Cancel
        try
        {
            Raise(DockPointerKind.Up, lParam, buttons);
        }
        finally
        {
            _upPending = false;
        }
        if (Hwnd != 0 && Native.GetCapture() == Hwnd)
            Native.ReleaseCapture();
        // Let go outside the window: ask again, so a Leave follows at once.
        if (Hwnd != 0)
        {
            Native.TrackLeave(Hwnd);
            _tracking = true;
        }
    }

    private (double X, double Y) BarDips(nint lParam)
    {
        int clientX = Native.SignedLow(lParam), clientY = Native.SignedHigh(lParam);
        // Geometry DIPs, not screen DIPs: one art pixel is two of them at every scale (a Pixel bar is whole art pixels, a whole factor of screen
        // pixels each, so at 125 % one art pixel is 2.4 DIPs of the screen). The controller hit-tests with DockGeometry, which is in these units.
        return ((clientX - _barOffsetPx) * 2.0 / _factor, (clientY - PixelDockPainter.Head * _factor) * 2.0 / _factor);
    }

    private void Raise(DockPointerKind kind, nint lParam, (bool Left, bool Right, bool Middle, bool Shift) buttons)
    {
        if (_disposed || _parked)
            return;
        var (x, y) = BarDips(lParam);
        int screenX = _x + Native.SignedLow(lParam), screenY = _y + Native.SignedHigh(lParam);
        Pointer?.Invoke(new DockPointer(kind, x, y, screenX, screenY, buttons.Left, buttons.Right, buttons.Middle, buttons.Shift));
    }

    /// <summary>The cursor as a client-coordinates lParam (for messages that carry no position).</summary>
    private nint CursorInWindow()
    {
        Win32.GetCursorPos(out var cursor);
        int x = cursor.X - _x, y = cursor.Y - _y;
        return (nint)((y & 0xFFFF) << 16 | (x & 0xFFFF));
    }

    private static (bool Left, bool Right, bool Middle, bool Shift) Held(nint wParam)
    {
        long flags = wParam.ToInt64();
        return ((flags & MK_LBUTTON) != 0, (flags & MK_RBUTTON) != 0, (flags & MK_MBUTTON) != 0, (flags & MK_SHIFT) != 0);
    }

    private static bool Shift(nint wParam) => (wParam.ToInt64() & MK_SHIFT) != 0;

    // ---------------------------------------------------------------- end

    public override void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _ui.ColorValuesChanged -= OnColorsChanged;
        _motion.Dispose();
        _liftTicket?.Stop();
        _moveTicket?.Stop();
        _gapClose?.Stop();
        _flashTicket?.Stop();
        _repaint?.Stop();
        _runTicket?.Stop();
        _floatTicket?.Stop();
        _panel?.Dispose();
        _panel = null;
        _drop.Revoke();
        if (Hwnd != 0 && Native.GetCapture() == Hwnd)
            Native.ReleaseCapture();
        base.Dispose(); // the window, the DIB and its DC
        _canvas = null;
    }
}
