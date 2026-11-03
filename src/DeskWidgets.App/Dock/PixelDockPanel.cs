using DeskWidgets.Interop;
using DeskWidgets.Motion;
using DeskWidgets.Pixel;
using DeskWidgets.PixelUi;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>
/// A group's panel in the Pixel theme (HANDOFF section 6, D9): a layered popup above the dock that shows <see cref="PixelDockPanelScene"/>
/// 1:1 at the dock's factor. Opened by a click it takes the activation, like the dock's menus, so Esc and the rename field work, and gives it
/// back when Esc closes it; opened for a file drag it never activates. It reports what happens as <see cref="DockPanelEvent"/>s and decides
/// nothing about the group itself. A new window each time it opens; it disposes itself after its SHRINK.
/// </summary>
internal sealed class PixelDockPanel : PixelPopupWindow
{
    private const int VkShift = 0x10, VkControl = 0x11, VkApps = 0x5D, VkF10 = 0x79, VkDelete = 0x2E, VkA = 0x41, VkV = 0x56;
    private static readonly GrowSpec Grow = new(GrowFrom.Bottom, [1.0 / 3, 2.0 / 3]);

    private readonly PixelDockArt _art;
    private readonly Action<DockPanelEvent> _raise;
    private readonly PanelFocusGuard _guard;
    private readonly DropTargetRegistration _drop = new();
    private readonly LineEditor _idle = new(); // what the text service sees when no rename is under way (it only runs during one)
    private (int CenterX, int BarTop, DockRect Work) _anchor;
    private string _title;
    private IReadOnlyList<DockItemView> _members;
    private IReadOnlyList<bool>? _lit;
    private int _litHover = -1;
    private TextService? _text;
    private LineEditor? _edit;
    private int _firstRow;
    private int _hover = -1, _pressed = -1, _focus = -1;
    private bool _buttonHover, _buttonPressed;
    private bool _dragging, _dragOutside;
    private int _dragFrom = -1, _dragTo = -1;
    private (int X, int Y)? _dragAt;
    private Win32.POINT _pressPoint;
    private bool _growing, _closing, _launched, _swallowChar;
    private char _highSurrogate;
    private nint _previousForeground;
    private FrameClock.Ticket? _repaint;
    private readonly StepAnimator _cellSteps = new(); // D7's MOVE of the cells while a member is dragged
    private readonly Dictionary<int, (int X, int Y)> _cellShown = [];
    private readonly PixelDockPanelAccessible _accessible;
    private const uint ActivateMessage = Win32.WM_APP + 31; // assistive technology pressed an element: answered from the message loop

    public PixelDockPanel(PixelDockArt art, Action<DockPanelEvent> raise, int factor, (int CenterX, int BarTop, DockRect Work) anchor,
        string title, IReadOnlyList<DockItemView> members, bool forDrop, IDropTarget? dropTarget)
    {
        _art = art;
        _raise = raise;
        _anchor = anchor;
        _title = title;
        _members = members;
        ForDrop = forDrop;
        Factor = factor;
        _guard = new PanelFocusGuard(() => IsOpen, () => Hwnd, () => MenuOpen || ForDrop || _dragging, CloseFromGuard, TakeForeground);
        _accessible = new PixelDockPanelAccessible(this);
        CreateWindow(noActivate: false);
        if (dropTarget is not null)
            _drop.RegisterOn(Hwnd, dropTarget);
    }

    public bool IsOpen => Hwnd != 0 && !_closing && !Leaving;

    /// <summary>The members as shown, in the order the panel's events count them.</summary>
    public IReadOnlyList<DockItemView> Members => _members;

    /// <summary>Opened for files dragged over the group: it does not activate, and only a hide from the controller closes it.</summary>
    public bool ForDrop { get; private set; }

    /// <summary>One of the dock's menus is up over the panel (a member's app menu): its taking the activation is not the user leaving.</summary>
    public bool MenuOpen { get; set; }

    /// <summary>The frame and its pointer on the screen, in pixels.</summary>
    public DockRect Rect => IsOpen ? new DockRect(Origin.X, Origin.Y, PixelDockPanelLayout.Width(_members.Count) * Factor, (PixelDockPanelLayout.Height(_members.Count) + PixelDockPanelLayout.Tip) * Factor) : default;

    // ----- opening and closing -----

    public void Open()
    {
        var (x, y, _) = Place();
        _growing = true;
        _guard.Touch();
        if (!ForDrop)
            _previousForeground = Native.GetForegroundWindow();
        GrowIn(Grow, Factor, x, y); // D9: up from the dock at full width, 1/3 and 2/3 of the height, then the panel (all at once with motion off)
        ShowWindow(activate: !ForDrop);
        if (!ForDrop)
            TakeForeground();
        _accessible.RaiseOpened(UiaNative.EventWindowOpened);
    }

    /// <summary>
    /// The panel goes: SHRINK (2/3, 1/3), or at once. A rename under way is dropped. With <paramref name="restoreForeground"/> the window that
    /// was in front before it opened gets the activation back, unless something else took it meanwhile or the panel started a program.
    /// </summary>
    public void Close(bool animated, bool restoreForeground)
    {
        if (_closing || Hwnd == 0)
            return;
        _closing = true;
        _guard.Stop();
        _repaint?.Stop();
        _repaint = null;
        EndRename();
        bool hadForeground = Native.GetForegroundWindow() == Hwnd;
        if (Native.GetCapture() == Hwnd)
            Native.ReleaseCapture();
        if (restoreForeground && hadForeground && !_launched && _previousForeground != 0 && Native.IsWindow(_previousForeground))
        {
            Win32.GetWindowThreadProcessId(_previousForeground, out uint process);
            if (process != (uint)Environment.ProcessId)
                Win32.SetForegroundWindow(_previousForeground);
        }
        CancelGrow();
        if (animated)
            ShrinkOut(Grow); // disposes the window after its last frame; at once with motion off
        else
            Dispose();
    }

    /// <summary>Esc, a click elsewhere, the user leaving for another program: the panel closes itself and says so.</summary>
    public void Dismiss(bool restoreForeground)
    {
        if (!IsOpen)
            return;
        Close(animated: true, restoreForeground);
        _raise(new DockPanelEvent(DockPanelEventKind.Dismissed));
    }

    /// <summary>The user went to another program: a rename under way is kept, as the Big Folder's is.</summary>
    private void CloseFromGuard()
    {
        CommitRename();
        Dismiss(restoreForeground: false);
    }

    private void TakeForeground()
    {
        if (!IsOpen || ForDrop)
            return;
        Win32.SetForegroundWindow(Hwnd);
        Native.SetFocus(Hwnd);
        _text?.Focus();
    }

    /// <summary>A menu over the panel closed: the panel takes the activation back, or goes if the user went to another program meanwhile.</summary>
    public void MenuClosed()
    {
        MenuOpen = false;
        if (IsOpen)
            _guard.Reclaim();
    }

    public override void Dispose()
    {
        _closing = true;
        _guard.Stop();
        _repaint?.Stop();
        _repaint = null;
        _cellSteps.Dispose();
        _drop.Revoke();
        _text?.Dispose();
        _text = null;
        if (Hwnd != 0)
            UiaNative.UiaReturnRawElementProvider(Hwnd, 0, 0, null);
        _accessible.DisconnectUia();
        base.Dispose();
    }

    // ----- what it shows -----

    public void Update(string title, IReadOnlyList<DockItemView> members, bool? forDrop = null)
    {
        if (!IsOpen)
            return;
        _title = title;
        _members = members;
        if (forDrop is { } drop && drop != ForDrop)
        {
            ForDrop = drop;
            if (!drop)
            {
                // Kept open as a click-opened panel once the drag is over: it takes the activation now.
                _previousForeground = Native.GetForegroundWindow();
                _guard.Touch();
                TakeForeground();
            }
        }
        _firstRow = Math.Clamp(_firstRow, 0, PixelDockPanelLayout.MaxFirstRow(members.Count));
        if (_hover >= members.Count)
            _hover = -1;
        if (_focus >= members.Count)
            _focus = members.Count - 1;
        if (_dragFrom >= members.Count)
            EndDrag();
        Redraw();
    }

    /// <summary>D12 in the panel: the members that take the files light up; the one under them goes 1 px up and the title row says "Open with {name}".</summary>
    public void SetOpenWith(IReadOnlyList<bool>? lit, int hovered)
    {
        if (!IsOpen)
            return;
        _lit = lit?.ToList();
        _litHover = lit is null ? -1 : hovered;
        if (lit is not null && hovered >= 0)
            EnsureShown(hovered);
        Redraw();
    }

    public void BeginRename()
    {
        if (!IsOpen)
            return;
        if (ForDrop)
            Update(_title, _members, forDrop: false);
        _edit = new LineEditor(_title, selectAll: true);
        // The text service runs only while a rename is under way: with no field, Pinyin must not compose into the panel.
        _text ??= new TextService(Hwnd, () => _edit ?? _idle, FieldOnScreen, Redraw);
        TakeForeground();
        Redraw();
    }

    /// <summary>The member under a screen pixel, or -1 (a file drag over the panel: OLE gives screen points only).</summary>
    public int MemberAt(int screenX, int screenY)
    {
        if (!IsOpen)
            return -1;
        var (x, y) = ToArt(screenX, screenY);
        return PixelDockPanelLayout.CellAt(_members.Count, _firstRow, x, y);
    }

    /// <summary>A picture arrived: one redraw for however many come together, never cutting the GROW short (its last frame draws afresh).</summary>
    public void RepaintSoon()
    {
        if (!IsOpen || _growing || _repaint is not null)
            return;
        _repaint = FrameClock.After(1, () =>
        {
            _repaint = null;
            if (!_growing)
                Redraw();
        });
    }

    public void Redraw()
    {
        if (!IsOpen)
            return;
        if (_growing)
        {
            CancelGrow(); // a new state cuts the GROW short
            _growing = false;
        }
        var (x, y, _) = Place();
        DrawFinal(Factor, x, y);
    }

    private (int X, int Y, int PointerX) Place()
        => PixelDockPanelLayout.Place(_anchor.CenterX, _anchor.BarTop, _anchor.Work, Factor, PixelDockPanelLayout.Width(_members.Count), PixelDockPanelLayout.Height(_members.Count));

    private PixelDockPanelView View(int pointerX) => new(_title, _members, _firstRow, _hover, _pressed, _focus, _buttonHover, _buttonPressed, _edit, _lit, _litHover,
        _dragging ? _dragFrom : -1, _dragTo, _dragging && !_dragOutside ? _dragAt : null, pointerX, _cellShown.Count > 0 ? new Dictionary<int, (int X, int Y)>(_cellShown) : null);

    protected override PixelCanvas? EmptyFrame(GrowFrom from, double share) => PixelDockPanelScene.EmptyFrame(CurrentPalette(), _members.Count, share);

    protected override void DrawFinal(int factor, int x, int y)
    {
        if (Hwnd == 0 || Leaving)
            return;
        _growing = false;
        var (_, _, pointerX) = Place();
        Present(PixelDockPanelScene.Draw(CurrentPalette(), _art, View(pointerX)), factor, x, y);
        _text?.Sync(); // the field may have changed: the IME works from the same text
    }

    // ----- geometry -----

    private (int X, int Y) ToArt(int screenX, int screenY)
    {
        static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
        return (FloorDiv(screenX - Origin.X, Factor), FloorDiv(screenY - Origin.Y, Factor));
    }

    private bool InFrame(int artX, int artY)
        => artX >= 0 && artY >= 0 && artX < PixelDockPanelLayout.Width(_members.Count) && artY < PixelDockPanelLayout.Height(_members.Count);

    private static bool In((int X, int Y, int W, int H) r, int x, int y) => x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H;

    private Win32.RECT FieldOnScreen()
    {
        var (x, y, w, h) = PixelDockPanelScene.FieldRect(_members.Count);
        return new Win32.RECT { Left = Origin.X + x * Factor, Top = Origin.Y + y * Factor, Right = Origin.X + (x + w) * Factor, Bottom = Origin.Y + (y + h) * Factor };
    }

    private void EnsureShown(int index)
    {
        int columns = PixelDockPanelLayout.Columns(_members.Count), rows = PixelDockPanelLayout.VisibleRows(_members.Count);
        int row = index / columns;
        if (row < _firstRow)
            _firstRow = row;
        else if (row >= _firstRow + rows)
            _firstRow = row - rows + 1;
    }

    // ----- renaming -----

    private void CommitRename()
    {
        if (_edit is not { } editor)
            return;
        string text = editor.Text.Trim();
        EndRename();
        _raise(new DockPanelEvent(DockPanelEventKind.Renamed, Text: text));
        Redraw();
    }

    private void CancelRename()
    {
        EndRename();
        Redraw();
    }

    private void EndRename()
    {
        _edit = null;
        _text?.Dispose();
        _text = null;
    }

    private void EditKey(int key, bool ctrl)
    {
        var editor = _edit!;
        switch (key)
        {
            case Native.VK_RETURN:
                CommitRename();
                return;
            case Native.VK_ESCAPE:
                CancelRename();
                return;
            case Native.VK_LEFT:
                editor.Left();
                break;
            case Native.VK_RIGHT:
                editor.Right();
                break;
            case Native.VK_HOME:
                editor.Home();
                break;
            case Native.VK_END:
                editor.End();
                break;
            case Native.VK_BACK:
                editor.Backspace();
                break;
            case VkDelete:
                editor.Delete();
                break;
            case VkA when ctrl:
                editor.SelectAll();
                break;
            case VkV when ctrl:
                editor.Insert(TextService.ReadClipboardText());
                break;
            default:
                return;
        }
        Redraw();
    }

    private void TypeCharacter(char c)
    {
        if (_edit is null)
            return;
        if (char.IsHighSurrogate(c))
        {
            _highSurrogate = c;
            return;
        }
        string typed;
        if (char.IsLowSurrogate(c))
        {
            if (_highSurrogate == 0)
                return;
            typed = new string([_highSurrogate, c]);
            _highSurrogate = '\0';
        }
        else
        {
            _highSurrogate = '\0';
            if (c < 0x20 || c == 0x7F)
                return;
            typed = c.ToString();
        }
        _edit.Insert(typed);
        Redraw();
    }

    // ----- keyboard -----

    private static bool IsDown(int virtualKey) => (Win32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void OnKey(int key)
    {
        _swallowChar = key is Native.VK_RETURN or Native.VK_ESCAPE or Native.VK_BACK or Native.VK_TAB or Native.VK_SPACE;
        if (_edit is not null)
        {
            _swallowChar = key is Native.VK_RETURN or Native.VK_ESCAPE or Native.VK_BACK or Native.VK_TAB;
            EditKey(key, IsDown(VkControl));
            return;
        }
        int count = _members.Count, columns = PixelDockPanelLayout.Columns(count);
        switch (key)
        {
            case Native.VK_ESCAPE:
                Dismiss(restoreForeground: true);
                return;
            case Native.VK_TAB:
                // Tab goes between the members and the Open all button.
                _focus = _focus == -2 ? (count > 0 ? 0 : -2) : -2;
                break;
            case Native.VK_LEFT:
                MoveFocus(-1);
                break;
            case Native.VK_RIGHT:
                MoveFocus(1);
                break;
            case Native.VK_UP:
                MoveFocus(-columns);
                break;
            case Native.VK_DOWN:
                MoveFocus(columns);
                break;
            case Native.VK_HOME:
                MoveFocus(-count);
                break;
            case Native.VK_END:
                MoveFocus(count);
                break;
            case Native.VK_RETURN or Native.VK_SPACE:
                if (_focus == -2)
                    Launch(new DockPanelEvent(DockPanelEventKind.OpenAll));
                else if (_focus >= 0 && _focus < count)
                    Launch(new DockPanelEvent(DockPanelEventKind.Click, _focus));
                return;
            case VkApps:
                MenuAtFocus();
                return;
            default:
                return;
        }
        Redraw();
        AnnounceFocus();
    }

    private void AnnounceFocus()
    {
        int id = PixelDockPanelAccessible.IdOf(_focus);
        if (id <= 0)
            return;
        Native.NotifyWinEvent(Native.EVENT_OBJECT_FOCUS, Hwnd, Native.OBJID_CLIENT, id);
        _accessible.RaiseFocus(id);
    }

    // ----- what the accessibility tree reads -----

    internal string Title => _title;

    /// <summary>The keyboard focus: a member, -2 for Open all, -1 none.</summary>
    internal int KeyboardFocus => _focus;

    internal Win32.RECT ScreenRect((int X, int Y, int W, int H) art) => new()
    {
        Left = Origin.X + art.X * Factor,
        Top = Origin.Y + art.Y * Factor,
        Right = Origin.X + (art.X + art.W) * Factor,
        Bottom = Origin.Y + (art.Y + art.H) * Factor,
    };

    internal Win32.RECT FrameOnScreen() => ScreenRect((0, 0, PixelDockPanelLayout.Width(_members.Count), PixelDockPanelLayout.Height(_members.Count)));

    internal Win32.RECT ButtonOnScreen() => ScreenRect(PixelDockPanelScene.ButtonRect(_members.Count));

    /// <summary>A member's cell on the screen, or empty while its row is scrolled out of sight.</summary>
    internal Win32.RECT MemberOnScreen(int index)
        => PixelDockPanelLayout.CellRect(_members.Count, _firstRow, index) is { } cell ? ScreenRect(cell) : default;

    internal bool IsMemberShown(int index) => PixelDockPanelLayout.CellRect(_members.Count, _firstRow, index) is not null;

    internal void FocusFromAccessibility(int focus)
    {
        if (!IsOpen)
            return;
        _focus = focus;
        if (focus >= 0)
            EnsureShown(focus);
        Redraw();
    }

    /// <summary>Assistive technology pressed an element (on its own call): done from the message loop, not inside the COM call.</summary>
    internal void RequestActivate(int focus) => Win32.PostMessage(Hwnd, ActivateMessage, focus, 0);

    private void MoveFocus(int delta)
    {
        int count = _members.Count;
        if (count == 0)
            return;
        _focus = _focus < 0 ? 0 : Math.Clamp(_focus + delta, 0, count - 1);
        EnsureShown(_focus);
    }

    private void MenuAtFocus()
    {
        if (_focus < 0 || _focus >= _members.Count || PixelDockPanelLayout.CellRect(_members.Count, _firstRow, _focus) is not { } cell)
            return;
        _raise(new DockPanelEvent(DockPanelEventKind.RightClick, _focus, ScreenX: Origin.X + (cell.X + cell.W / 2) * Factor, ScreenY: Origin.Y + (cell.Y + cell.H - 4) * Factor));
    }

    /// <summary>A member or Open all starts programs: the controller closes the panel, which then leaves the activation to them.</summary>
    private void Launch(DockPanelEvent what)
    {
        _launched = true;
        _raise(what);
    }

    // ----- the pointer -----

    private void OnMouseMove()
    {
        Native.TrackLeave(Hwnd);
        Win32.GetCursorPos(out var point);
        var (x, y) = ToArt(point.X, point.Y);
        if (_pressed >= 0 && !_dragging && IsDown(0x01 /* VK_LBUTTON */)
            && (Math.Abs(point.X - _pressPoint.X) >= Win32.GetSystemMetrics(68 /* SM_CXDRAG */) || Math.Abs(point.Y - _pressPoint.Y) >= Win32.GetSystemMetrics(69 /* SM_CYDRAG */)))
        {
            _dragging = true;
            _dragFrom = _pressed;
            _dragTo = _pressed;
            _pressed = -1;
        }
        if (_dragging)
        {
            bool outside = !InFrame(x, y);
            if (_dragOutside && !outside)
            {
                // Back in the panel: a move to nowhere takes away the controller's landing gap and the dock's copy of the picture.
                _raise(new DockPanelEvent(DockPanelEventKind.DragMove, _dragFrom, ScreenX: int.MinValue / 2, ScreenY: int.MinValue / 2));
            }
            _dragOutside = outside;
            _dragAt = (x, y);
            // Outside the panel the member would leave the group: the cells go back to their order until it comes back in.
            int to = outside ? _dragFrom : PixelDockPanelLayout.ReorderTarget(_members.Count, _firstRow, x, y, _dragFrom);
            if (outside)
                _raise(new DockPanelEvent(DockPanelEventKind.DragMove, _dragFrom, ScreenX: point.X, ScreenY: point.Y));
            if (to != _dragTo && to >= 0)
                MoveCells(to);
            Redraw();
            return;
        }
        int hover = PixelDockPanelLayout.CellAt(_members.Count, _firstRow, x, y);
        bool button = In(PixelDockPanelScene.ButtonRect(_members.Count), x, y);
        if (hover == _hover && button == _buttonHover)
            return;
        _hover = hover;
        _buttonHover = button;
        Redraw();
    }

    private void OnButtonDown()
    {
        Win32.GetCursorPos(out var point);
        var (x, y) = ToArt(point.X, point.Y);
        int count = _members.Count;
        if (_edit is not null)
        {
            if (In(PixelDockPanelScene.FieldRect(count), x, y))
                return; // a click in the field keeps the rename going
            CommitRename();
        }
        if (In(PixelDockPanelScene.ButtonRect(count), x, y))
        {
            _buttonPressed = true;
            Native.SetCapture(Hwnd);
            Redraw();
            return;
        }
        int cell = PixelDockPanelLayout.CellAt(count, _firstRow, x, y);
        if (cell < 0)
            return;
        _pressed = cell;
        _focus = -1;
        _pressPoint = point;
        Native.SetCapture(Hwnd); // a drag's moves keep coming outside the panel
        Redraw();
    }

    private void OnButtonUp()
    {
        Win32.GetCursorPos(out var point);
        var (x, y) = ToArt(point.X, point.Y);
        int count = _members.Count;
        int pressed = _pressed;
        bool button = _buttonPressed, dragging = _dragging;
        int from = _dragFrom, to = _dragTo;
        bool outside = _dragOutside;
        _pressed = -1;
        _buttonPressed = false;
        EndDrag();
        // After the flags are cleared: letting go of the capture sends WM_CAPTURECHANGED, which would take it for a cancel.
        if (Native.GetCapture() == Hwnd)
            Native.ReleaseCapture();
        Redraw();
        if (dragging)
        {
            if (outside || !InFrame(x, y))
                _raise(new DockPanelEvent(DockPanelEventKind.DragOut, from, ScreenX: point.X, ScreenY: point.Y));
            else if (to >= 0 && to != from)
                _raise(new DockPanelEvent(DockPanelEventKind.Reorder, from, to));
        }
        else if (button && In(PixelDockPanelScene.ButtonRect(count), x, y))
        {
            Launch(new DockPanelEvent(DockPanelEventKind.OpenAll));
        }
        else if (pressed >= 0 && PixelDockPanelLayout.CellAt(count, _firstRow, x, y) == pressed)
        {
            Launch(new DockPanelEvent(DockPanelEventKind.Click, pressed));
        }
    }

    private void OnRightButtonUp()
    {
        Win32.GetCursorPos(out var point);
        var (x, y) = ToArt(point.X, point.Y);
        int cell = PixelDockPanelLayout.CellAt(_members.Count, _firstRow, x, y);
        if (cell >= 0)
            _raise(new DockPanelEvent(DockPanelEventKind.RightClick, cell, ScreenX: point.X, ScreenY: point.Y));
    }

    private void OnWheel(int delta)
    {
        int rows = Math.Max(1, Math.Abs(delta) / 120);
        int next = Math.Clamp(_firstRow + (delta > 0 ? -rows : rows), 0, PixelDockPanelLayout.MaxFirstRow(_members.Count));
        if (next == _firstRow)
            return;
        _firstRow = next;
        Win32.GetCursorPos(out var point);
        var (x, y) = ToArt(point.X, point.Y);
        _hover = PixelDockPanelLayout.CellAt(_members.Count, _firstRow, x, y);
        Redraw();
    }

    /// <summary>
    /// D7 in the panel: the dragged member's box is in its new cell at once; every other member whose cell changed MOVEs there from where it is
    /// shown now (1/3, 2/3, all of the way, one Pixel frame each). A row scrolled out of sight snaps.
    /// </summary>
    private void MoveCells(int to)
    {
        int count = _members.Count;
        var before = PixelDockPanelLayout.Preview(count, _dragFrom, _dragTo);
        var after = PixelDockPanelLayout.Preview(count, _dragFrom, to);
        _dragTo = to;
        var paths = new List<(int Member, (int X, int Y) From, (int X, int Y) To)>();
        for (int slot = 0; slot < count; slot++)
        {
            int member = after[slot];
            int was = before.IndexOf(member);
            if (member == _dragFrom || was == slot)
                continue;
            if (PixelDockPanelLayout.CellRect(count, _firstRow, slot) is not { } target || PixelDockPanelLayout.CellRect(count, _firstRow, was) is not { } old)
                continue;
            var from = _cellShown.TryGetValue(member, out var shown) ? shown : (old.X, old.Y);
            paths.Add((member, from, (target.X, target.Y)));
        }
        _cellSteps.Cancel();
        _cellShown.Clear();
        if (paths.Count == 0 || !MotionSettings.Enabled)
            return;
        const int frames = 3;
        static int At(int from, int to, int frame) => to + (int)Math.Round((from - to) * (frames - frame) / (double)frames);
        _cellSteps.Start(frames, frame =>
        {
            int k = frame + 1;
            _cellShown.Clear();
            if (k < frames)
            {
                foreach (var (member, from, target) in paths)
                    _cellShown[member] = (At(from.X, target.X, k), At(from.Y, target.Y, k));
            }
            if (frame > 0)
                Redraw(); // frame 0 is drawn by the move that started it
        });
    }

    private void EndDrag()
    {
        _cellSteps.Cancel();
        _cellShown.Clear();
        _dragging = false;
        _dragOutside = false;
        _dragFrom = _dragTo = -1;
        _dragAt = null;
    }

    // ----- messages -----

    protected override nint OnMessage(uint msg, nint wParam, nint lParam)
    {
        if ((_closing || Leaving) && msg is Native.WM_KEYDOWN or Native.WM_CHAR or Native.WM_MOUSEMOVE or Native.WM_LBUTTONDOWN or Native.WM_LBUTTONUP or Native.WM_RBUTTONUP or Native.WM_MOUSEWHEEL)
            return 0;
        switch (msg)
        {
            case Native.WM_ERASEBKGND:
                return 1;
            case Native.WM_GETOBJECT:
            {
                int objectId = unchecked((int)lParam.ToInt64());
                if (objectId == UiaNative.UiaRootObjectId)
                    return _accessible.GetUiaObject(wParam, lParam);
                if (objectId == Native.OBJID_CLIENT)
                    return _accessible.GetObject(wParam);
                break;
            }
            case ActivateMessage:
                if (IsOpen && (int)wParam == -2)
                    Launch(new DockPanelEvent(DockPanelEventKind.OpenAll));
                else if (IsOpen && (int)wParam >= 0 && (int)wParam < _members.Count)
                    Launch(new DockPanelEvent(DockPanelEventKind.Click, (int)wParam));
                return 0;
            case Native.WM_MOUSEACTIVATE when ForDrop:
                return Native.MA_NOACTIVATE;
            case Native.WM_ACTIVATE when Native.LowWord(wParam) == Native.WA_INACTIVE:
                if (IsOpen && !ForDrop)
                    _guard.OnDeactivated();
                return 0;
            case Native.WM_KEYDOWN:
                OnKey((int)wParam);
                return 0;
            case Native.WM_SYSKEYDOWN when (int)wParam == VkF10 && IsDown(VkShift):
                MenuAtFocus();
                return 0;
            case Native.WM_CHAR:
                if (_swallowChar)
                    _swallowChar = false;
                else
                    TypeCharacter((char)wParam);
                return 0;
            case Native.WM_MOUSEMOVE:
                OnMouseMove();
                return 0;
            case Native.WM_MOUSELEAVE:
                if (!_dragging && (_hover != -1 || _buttonHover))
                {
                    _hover = -1;
                    _buttonHover = false;
                    Redraw();
                }
                return 0;
            case Native.WM_LBUTTONDOWN:
                OnButtonDown();
                return 0;
            case Native.WM_LBUTTONUP:
                OnButtonUp();
                return 0;
            case Native.WM_RBUTTONUP:
                OnRightButtonUp();
                return 0;
            case Native.WM_MOUSEWHEEL:
                OnWheel(Native.SignedHigh(wParam));
                return 0;
            case Native.WM_CAPTURECHANGED when lParam != Hwnd && (_pressed >= 0 || _buttonPressed || _dragging):
                // The capture went without a release: the press or the drag is cancelled, nothing moves. A drag held over the bar has the
                // controller showing where it would land: a move to nowhere (far off every screen) takes that gap away.
                if (_dragging && _dragOutside)
                    _raise(new DockPanelEvent(DockPanelEventKind.DragMove, _dragFrom, ScreenX: int.MinValue / 2, ScreenY: int.MinValue / 2));
                _pressed = -1;
                _buttonPressed = false;
                EndDrag();
                Redraw();
                return 0;
            case 0x0010 /* WM_CLOSE */:
                Dismiss(restoreForeground: true);
                return 0;
        }
        return base.OnMessage(msg, wParam, lParam);
    }
}

/// <summary>
/// The group panel's accessibility tree, as the Pixel menus and the Big Folder panel have theirs: the window named by the group, 1 = the Open
/// all button, 2.. = the members as list items with their names (pressing one starts it).
/// </summary>
internal sealed class PixelDockPanelAccessible(PixelDockPanel panel) : PopupAccessible(() => panel.Hwnd)
{
    private const int OpenAllId = 1, FirstMember = 2;

    /// <summary>The element id of a keyboard focus (-2 Open all, a member, -1 none: 0, the window).</summary>
    public static int IdOf(int focus) => focus == -2 ? OpenAllId : focus >= 0 ? focus + FirstMember : 0;

    protected override int ChildCount => FirstMember - 1 + panel.Members.Count;

    protected override int SelfRole => RoleWindow;

    protected override string? Name(int id) => id switch
    {
        0 => panel.Title,
        OpenAllId => Strings.DockOpenAll,
        _ => panel.Members[id - FirstMember].Name,
    };

    protected override int Role(int id) => id == OpenAllId ? RolePushButton : RoleListItem;

    protected override int State(int id)
    {
        if (id == 0)
            return 0;
        int state = StateFocusable;
        if (IdOf(panel.KeyboardFocus) == id)
            state |= StateFocused;
        if (id >= FirstMember)
        {
            state |= StateSelectable;
            if (!panel.IsMemberShown(id - FirstMember))
                state |= StateOffscreen;
            if (panel.Members[id - FirstMember].Missing)
                state |= StateUnavailable;
        }
        return state;
    }

    protected override Win32.RECT Location(int id) => id switch
    {
        0 => panel.FrameOnScreen(),
        OpenAllId => panel.ButtonOnScreen(),
        _ => panel.MemberOnScreen(id - FirstMember),
    };

    protected override int FocusedId => IdOf(panel.KeyboardFocus);

    protected override void Select(int id)
    {
        if (id == OpenAllId)
            panel.FocusFromAccessibility(-2);
        else if (id >= FirstMember)
            panel.FocusFromAccessibility(id - FirstMember);
    }

    protected override void PatternSelect(int id) => Select(id);

    protected override void DoDefault(int id)
    {
        if (id == OpenAllId)
            panel.RequestActivate(-2);
        else if (id >= FirstMember)
            panel.RequestActivate(id - FirstMember);
    }

    protected override string? DefaultActionText(int id) => id switch
    {
        0 => null,
        OpenAllId => "Press",
        _ => "Open",
    };
}
