using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Interop;
using DeskWidgets.Pixel;
using DeskWidgets.PixelUi;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// The Big Folder's panel in the Pixel theme: a layered window beside the card that shows the scene of
/// <see cref="FolderPixelPanelScene"/> at the card's own pixel size. A click selects (Ctrl and Shift extend), a double-click or
/// Enter opens, Esc clears a rename, then the search, then closes, typing anywhere searches, the selection drags out to Explorer
/// and the desktop, a right-click opens the item menu, and a click outside closes it. Mouse, keyboard and text input are the
/// window's own; nothing here is a control.
/// </summary>
internal sealed class PixelFolderPanel : PixelPopupWindow, IFolderPanel
{
    private const int VkShift = 0x10, VkControl = 0x11, VkAlt = 0x12, VkApps = 0x5D, VkF2 = 0x71, VkF4 = 0x73, VkDelete = 0x2E, VkA = 0x41;
    private const uint OpenItemMessage = Win32.WM_APP + 21, OpenSortMessage = Win32.WM_APP + 22;
    private const int GuardMilliseconds = 400;

    private readonly FolderWidget _widget;
    private readonly PixelFolderIcons _icons;
    private readonly PanelSelection _selection = new();
    private readonly LineEditor _search = new();
    private readonly PanelAccessible _accessible;
    private List<FolderItem> _view = [];
    private PanelEdit? _edit;
    private string? _renamingFile;
    private FolderPixelPanelScene.PanelPlan? _plan;
    private bool _simSun;
    private int _firstRow;
    private int _hover = -1;
    private bool _sortHover, _sortPressed;
    private int _pressedCell = -1;
    private Win32.POINT _pressPoint;
    private bool _collapseOnRelease;
    private int _lastClickCell = -1;
    private uint _lastClickTick;
    private char _highSurrogate;
    private bool _swallowChar;
    private bool _menuOpen, _closing, _dragging;
    private DateTime _openedAt;

    public PixelFolderPanel(FolderWidget widget)
    {
        _widget = widget;
        _icons = new PixelFolderIcons(Redraw);
        _accessible = new PanelAccessible(this);
        CreateWindow(noActivate: false);
    }

    public event Action? Closed;

    public bool IsOpen => Hwnd != 0 && !_closing;

    private FolderModel Model => _widget.Model;

    private IWidgetSite Site => _widget.Site;

    // ----- what the accessibility tree reads -----

    internal IReadOnlyList<FolderItem> ShownItems => _view;

    internal string Title => Model.Name;

    internal string SearchText => _search.Text;

    internal string SortLabel => Strings.FolderSortButton(Model.Sort);

    internal bool IsItemSelected(int index) => _selection.IsSelected(index);

    internal bool IsGridFocused => _selection.KeyboardFocus && _selection.Focus >= 0 && _edit is null;

    internal int FocusedItem => _selection.Focus;

    // ----- opening and closing -----

    public void Open()
    {
        int factor = PixelCard.Factor(Site.Scale);
        var card = Site.CardRect;
        var work = Win32.GetWorkArea(new Win32.POINT { X = (card.Left + card.Right) / 2, Y = (card.Top + card.Bottom) / 2 });
        int width = (FolderPixelPanelScene.Width + DialogLayout.ShadowOffset) * factor, height = (FolderPixelPanelScene.Height + DialogLayout.ShadowOffset) * factor;
        var (x, y) = FolderPanelPlacement.Place(card, work, width, height, 4 * factor);
        Factor = factor;
        RecomputeView();
        Draw(x, y);
        _openedAt = DateTime.UtcNow;
        ShowWindow(activate: true);
        Win32.SetForegroundWindow(Hwnd);
        Native.SetFocus(Hwnd);
        _accessible.RaiseOpened(UiaNative.EventWindowOpened);
    }

    public bool Reusable => false;

    public void Destroy() => Close();

    public void Close()
    {
        if (_closing || Hwnd == 0)
            return;
        _closing = true;
        if (Native.GetCapture() == Hwnd)
            Native.ReleaseCapture();
        Dispose();
        Closed?.Invoke();
    }

    public override void Dispose()
    {
        if (Hwnd != 0)
            UiaNative.UiaReturnRawElementProvider(Hwnd, 0, 0, null);
        _accessible.DisconnectUia();
        base.Dispose();
    }

    // ----- the content: filter, plan, paint -----

    public void Refresh()
    {
        if (Hwnd == 0)
            return;
        RecomputeView();
        Redraw();
    }

    public void ApplyLanguage() => Refresh();

    public void PrepareForCapture(FolderCapture state)
    {
        _search.Set(state.Search, selectAll: false);
        OnSearchChanged();
        if (state.Rename && _view.Count > 0)
        {
            BeginRename(_view[0].FileName);
            return;
        }
        for (int i = 0; i < Math.Min(state.Select, _view.Count); i++)
            _selection.Toggle(i);
        _hover = state.Hover;
        if (state.Focus >= 0)
        {
            _selection.Focus = state.Focus;
            _selection.KeyboardFocus = true;
        }
        Redraw();
    }

    private void RecomputeView()
    {
        string filter = _search.Text.Trim();
        _view = Model.Items.Where(i => filter.Length == 0 || i.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        _selection.SetView(_view);
        if (_hover >= _view.Count)
            _hover = -1;
        if (_edit is { Target: PanelEditTarget.Item } && _renamingFile is not null)
        {
            int index = _view.FindIndex(i => i.FileName.Equals(_renamingFile, StringComparison.OrdinalIgnoreCase));
            _edit = index < 0 ? null : _edit with { ItemIndex = index };
            if (_edit is null)
                _renamingFile = null;
        }
        _simSun = FolderPixelPanelScene.NeedsSimSun(Model.Name, _search.Text, Model.Sort, _view);
    }

    private FolderPanelView Snapshot() => new(Model.Name, Model.Path, _view, _search.Text, Model.Sort, Model.Items.Count, _selection, _hover, _selection.Focus, _firstRow, _sortHover, _sortPressed, _edit, _icons);

    private void Draw(int x, int y)
    {
        var palette = CurrentPalette();
        var view = Snapshot();
        var plan = FolderPixelPanelScene.Plan(view, _simSun);
        _firstRow = Math.Clamp(_firstRow, 0, Math.Max(0, plan.TotalRows - plan.VisibleRows));
        _plan = plan;
        var panel = FolderPixelPanelScene.Draw(palette, view with { FirstRow = _firstRow }, plan);
        Present(PixelFrame.WithShadow(panel, palette.Shadow, DialogLayout.ShadowOffset), Factor, x, y);
    }

    private void Redraw()
    {
        if (Hwnd == 0 || _closing)
            return;
        if (Native.GetWindowRect(Hwnd, out var rect))
            Draw(rect.Left, rect.Top);
    }

    // ----- geometry -----

    private (int X, int Y) ToArt(int screenX, int screenY)
    {
        static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
        return (FloorDiv(screenX - Origin.X, Factor), FloorDiv(screenY - Origin.Y, Factor));
    }

    private Win32.RECT ToScreen((int X, int Y, int W, int H) art) => new()
    {
        Left = Origin.X + art.X * Factor,
        Top = Origin.Y + art.Y * Factor,
        Right = Origin.X + (art.X + art.W) * Factor,
        Bottom = Origin.Y + (art.Y + art.H) * Factor,
    };

    private PanelHit HitAt(int screenX, int screenY)
    {
        if (_plan is null)
            return PanelHit.None;
        var (x, y) = ToArt(screenX, screenY);
        return FolderPixelPanelScene.HitTest(_plan, _view.Count, _firstRow, x, y);
    }

    internal Win32.RECT WindowRect() => new()
    {
        Left = Origin.X,
        Top = Origin.Y,
        Right = Origin.X + FolderPixelPanelScene.Width * Factor,
        Bottom = Origin.Y + FolderPixelPanelScene.Height * Factor,
    };

    internal Win32.RECT SearchRect() => _plan is null ? default : ToScreen(FolderPixelPanelScene.SearchRect(_plan));

    internal Win32.RECT SortRect() => _plan is null ? default : ToScreen(FolderPixelPanelScene.SortRect(_plan));

    /// <summary>The screen rectangle of an item's cell, or empty while its row is scrolled out of sight.</summary>
    internal Win32.RECT ItemRect(int index)
        => _plan is not null && FolderPixelPanelScene.CellRect(_plan, index, _firstRow) is { } cell ? ToScreen(cell) : default;

    internal bool IsItemShown(int index) => _plan is not null && FolderPixelPanelScene.CellRect(_plan, index, _firstRow) is not null;

    private int VisibleRows => _plan?.VisibleRows ?? 1;

    // ----- selecting, opening -----

    private void OpenSelection()
    {
        foreach (var item in _selection.Items())
            _widget.Open(item);
    }

    private void OpenFocusedOrSelected()
    {
        if (_selection.Count == 0)
        {
            if (_selection.Focus >= 0 && _selection.Focus < _view.Count)
                _selection.SelectOnly(_selection.Focus);
            else if (_search.Text.Trim().Length > 0 && _view.Count > 0)
                _selection.SelectOnly(0);
        }
        OpenSelection();
        Redraw();
    }

    private void MoveFocus(int delta, bool extend, bool keepSelection, bool first = false)
    {
        int index = _selection.MoveFocus(delta, extend, keepSelection, first);
        if (index < 0)
            return;
        _firstRow = PanelSelection.FirstRowFor(index, FolderPixelPanelScene.Columns, _firstRow, VisibleRows);
        Redraw();
        AnnounceFocus(index + 3);
    }

    private void AnnounceFocus(int id)
    {
        Native.NotifyWinEvent(Native.EVENT_OBJECT_FOCUS, Hwnd, Native.OBJID_CLIENT, id);
        _accessible.RaiseFocus(id);
    }

    internal void SelectFromAccessibility(int index)
    {
        if (index < 0 || index >= _view.Count)
            return;
        _selection.SelectOnly(index);
        _firstRow = PanelSelection.FirstRowFor(index, FolderPixelPanelScene.Columns, _firstRow, VisibleRows);
        Redraw();
    }

    internal void RequestOpenItem(int index) => Win32.PostMessage(Hwnd, OpenItemMessage, index, 0);

    internal void RequestSortMenu() => Win32.PostMessage(Hwnd, OpenSortMessage, 0, 0);

    // ----- search -----

    private void OnSearchChanged()
    {
        _selection.Focus = -1;
        _selection.Anchor = -1;
        _selection.KeyboardFocus = false;
        _firstRow = 0;
        RecomputeView();
        Redraw();
    }

    private void TypeCharacter(char c)
    {
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
        if (_edit is not null)
        {
            _edit.Editor.Insert(typed);
            Redraw();
            return;
        }
        if (_search.Insert(typed))
            OnSearchChanged();
    }

    private void EscapePressed()
    {
        if (_edit is not null)
        {
            CancelEdit();
            return;
        }
        if (_search.Text.Length > 0)
        {
            _search.Clear();
            OnSearchChanged();
            return;
        }
        Close();
    }

    // ----- renaming -----

    public void BeginRename(string fileName)
    {
        if (Hwnd == 0)
            return;
        _search.Clear();
        RecomputeView();
        int index = _view.FindIndex(i => i.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            Redraw();
            return;
        }
        _renamingFile = fileName;
        _selection.SelectOnly(index);
        _edit = new PanelEdit(PanelEditTarget.Item, new LineEditor(_view[index].DisplayName, selectAll: true), index);
        _firstRow = PanelSelection.FirstRowFor(index, FolderPixelPanelScene.Columns, _firstRow, VisibleRows);
        Redraw();
    }

    public void BeginRenameFolder()
    {
        if (Hwnd == 0)
            return;
        _renamingFile = null;
        _edit = new PanelEdit(PanelEditTarget.Folder, new LineEditor(Model.Name, selectAll: true), -1);
        Redraw();
    }

    private void CancelEdit()
    {
        _edit = null;
        _renamingFile = null;
        Redraw();
    }

    private void CommitEdit()
    {
        var edit = _edit;
        if (edit is null)
            return;
        string? renaming = _renamingFile;
        _edit = null;
        _renamingFile = null;
        string typed = edit.Editor.Text;
        if (edit.Target == PanelEditTarget.Folder)
        {
            string? newPath = FolderActions.RenameFolder(Model.Path, typed, Site.WindowHandle, out string? error);
            if (error is not null)
                Site.ShowWarning(Strings.FolderOperationFailed(error));
            else if (newPath is not null && !string.Equals(newPath, Model.Path, StringComparison.OrdinalIgnoreCase))
                Model.FolderRenamed(newPath);
        }
        else if (renaming is not null)
        {
            var item = Model.Items.FirstOrDefault(i => i.FileName.Equals(renaming, StringComparison.OrdinalIgnoreCase));
            if (item is not null && typed.Trim() != item.DisplayName)
                _widget.Report(FolderActions.Rename(Model, item, typed, Site.WindowHandle));
        }
        Redraw();
    }

    private void EditKey(int key, bool ctrl)
    {
        var editor = _edit!.Editor;
        switch (key)
        {
            case Native.VK_RETURN:
                CommitEdit();
                return;
            case Native.VK_ESCAPE:
                CancelEdit();
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
            default:
                return;
        }
        Redraw();
    }

    // ----- keyboard -----

    private static bool IsDown(int virtualKey) => (Win32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void OnKey(int key)
    {
        bool ctrl = IsDown(VkControl), shift = IsDown(VkShift);
        _swallowChar = key is Native.VK_RETURN or Native.VK_ESCAPE or Native.VK_BACK or Native.VK_TAB;
        if (_edit is not null)
        {
            EditKey(key, ctrl);
            return;
        }
        int page = Math.Max(1, VisibleRows) * FolderPixelPanelScene.Columns;
        const int columns = FolderPixelPanelScene.Columns;
        switch (key)
        {
            case Native.VK_ESCAPE:
                EscapePressed();
                break;
            case Native.VK_LEFT:
                MoveFocus(-1, shift, ctrl);
                break;
            case Native.VK_RIGHT:
                MoveFocus(1, shift, ctrl);
                break;
            case Native.VK_UP:
                MoveFocus(-columns, shift, ctrl);
                break;
            case Native.VK_DOWN:
                MoveFocus(columns, shift, ctrl);
                break;
            case Native.VK_HOME:
                MoveFocus(-_view.Count, shift, ctrl);
                break;
            case Native.VK_END:
                MoveFocus(_view.Count, shift, ctrl);
                break;
            case Native.VK_PRIOR:
                MoveFocus(-page, shift, ctrl);
                break;
            case Native.VK_NEXT:
                MoveFocus(page, shift, ctrl);
                break;
            case Native.VK_RETURN:
                OpenFocusedOrSelected();
                break;
            case Native.VK_SPACE when _search.Text.Length == 0 && _selection.Focus >= 0 && _selection.Focus < _view.Count:
                _selection.Toggle(_selection.Focus);
                _swallowChar = true;
                Redraw();
                break;
            case VkA when ctrl:
                _selection.SelectAll();
                Redraw();
                break;
            case VkF2 when _selection.Focus >= 0 && _selection.Focus < _view.Count:
                BeginRename(_view[_selection.Focus].FileName);
                break;
            case VkDelete when _selection.Count > 0:
                _widget.Report(FolderActions.Recycle(Model, _selection.Items(), Site.WindowHandle));
                break;
            case Native.VK_BACK:
                if (_search.Backspace())
                    OnSearchChanged();
                break;
            case VkF4:
                ShowSortMenu(fromKeyboard: true);
                break;
            case VkApps:
                ShowItemMenuAtFocus();
                break;
        }
    }

    // ----- menus -----

    private void ShowMenu(IReadOnlyList<MenuEntry> entries, int screenX, int screenY, bool fromKeyboard)
    {
        _menuOpen = true;
        PixelMenu.Show(entries, screenX, screenY, fromKeyboard, OnMenuClosed);
    }

    private void OnMenuClosed()
    {
        _menuOpen = false;
        _openedAt = DateTime.UtcNow; // the click that closed the menu must not close the panel as well
        if (Hwnd == 0 || _closing)
            return;
        nint foreground = Native.GetForegroundWindow();
        if (foreground == 0 || foreground == Hwnd)
        {
            Win32.SetForegroundWindow(Hwnd);
            return;
        }
        Win32.GetWindowThreadProcessId(foreground, out uint processId);
        if (processId == (uint)Environment.ProcessId)
            Win32.SetForegroundWindow(Hwnd);
        else
            Close(); // another program took the foreground while the menu was up
    }

    private void ShowSortMenu(bool fromKeyboard = false)
    {
        var entries = Enum.GetValues<FolderSort>()
            .Select(sort => MenuEntry.Radio(Strings.FolderSortRow(sort), sort == Model.Sort, () => Model.SetSort(sort), group: "panel-sort"))
            .ToList();
        var rect = SortRect();
        ShowMenu(entries, rect.Left, rect.Bottom + Factor, fromKeyboard);
    }

    private void ShowItemMenu(int index, int screenX, int screenY, bool fromKeyboard)
    {
        if (index < 0 || index >= _view.Count)
            return;
        var item = _view[index];
        if (!_selection.IsSelected(index))
            _selection.SelectOnly(index);
        Redraw();
        ShowMenu(_widget.ItemMenu(item, _selection.Items()), screenX, screenY, fromKeyboard);
    }

    private void ShowItemMenuAtFocus()
    {
        int index = _selection.Focus;
        if (index < 0 || index >= _view.Count || !IsItemShown(index))
            return;
        var rect = ItemRect(index);
        ShowItemMenu(index, rect.Left + (rect.Right - rect.Left) / 2, rect.Bottom, fromKeyboard: true);
    }

    // ----- the pointer -----

    private void OnMouseMove()
    {
        Native.TrackLeave(Hwnd);
        Win32.GetCursorPos(out var point);
        if (_pressedCell >= 0 && !_dragging && IsDown(0x01 /* VK_LBUTTON */)
            && (Math.Abs(point.X - _pressPoint.X) >= Win32.GetSystemMetrics(68 /* SM_CXDRAG */) || Math.Abs(point.Y - _pressPoint.Y) >= Win32.GetSystemMetrics(69 /* SM_CYDRAG */)))
        {
            StartDrag();
            return;
        }
        UpdateHover(point);
    }

    private void UpdateHover(Win32.POINT point)
    {
        var hit = HitAt(point.X, point.Y);
        int hover = hit.Kind == PanelHitKind.Cell ? hit.Index : -1;
        bool sort = hit.Kind == PanelHitKind.Sort;
        if (hover == _hover && sort == _sortHover)
            return;
        _hover = hover;
        _sortHover = sort;
        Redraw();
    }

    private void OnButtonDown()
    {
        if (_edit is not null)
            CommitEdit();
        Win32.GetCursorPos(out var point);
        var hit = HitAt(point.X, point.Y);
        switch (hit.Kind)
        {
            case PanelHitKind.Cell:
            {
                bool ctrl = IsDown(VkControl), shift = IsDown(VkShift);
                uint now = (uint)Environment.TickCount;
                bool second = hit.Index == _lastClickCell && now - _lastClickTick <= Native.GetDoubleClickTime() && !ctrl && !shift;
                _lastClickCell = second ? -1 : hit.Index;
                _lastClickTick = now;
                _collapseOnRelease = _selection.Press(hit.Index, ctrl, shift);
                _pressedCell = hit.Index;
                _pressPoint = point;
                Native.SetCapture(Hwnd);
                Redraw();
                AnnounceFocus(hit.Index + 3);
                if (second)
                {
                    _collapseOnRelease = false;
                    OpenSelection();
                }
                break;
            }
            case PanelHitKind.Sort:
                _sortPressed = true;
                Native.SetCapture(Hwnd);
                Redraw();
                break;
        }
    }

    private void OnButtonUp()
    {
        Win32.GetCursorPos(out var point);
        var hit = HitAt(point.X, point.Y);
        bool sort = _sortPressed;
        int pressed = _pressedCell;
        bool collapse = _collapseOnRelease;
        _sortPressed = false;
        _pressedCell = -1;
        _collapseOnRelease = false;
        // After the flags are cleared: letting go of the capture sends WM_CAPTURECHANGED, which would clear them first.
        if (Native.GetCapture() == Hwnd)
            Native.ReleaseCapture();
        if (collapse && pressed >= 0 && hit.Kind == PanelHitKind.Cell && hit.Index == pressed)
            _selection.SelectOnly(pressed);
        Redraw();
        if (sort && hit.Kind == PanelHitKind.Sort)
            ShowSortMenu();
    }

    private void OnRightButtonUp()
    {
        Win32.GetCursorPos(out var point);
        var hit = HitAt(point.X, point.Y);
        if (hit.Kind == PanelHitKind.Cell)
            ShowItemMenu(hit.Index, point.X, point.Y, fromKeyboard: false);
    }

    private void OnWheel(int delta)
    {
        int rows = Math.Max(1, Math.Abs(delta) / 120);
        int max = _plan is null ? 0 : Math.Max(0, _plan.TotalRows - _plan.VisibleRows);
        int next = Math.Clamp(_firstRow + (delta > 0 ? -rows : rows), 0, max);
        if (next == _firstRow)
            return;
        _firstRow = next;
        Win32.GetCursorPos(out var point);
        var hit = HitAt(point.X, point.Y);
        _hover = hit.Kind == PanelHitKind.Cell ? hit.Index : -1;
        Redraw();
    }

    // ----- dragging items out -----

    private void StartDrag()
    {
        int pressed = _pressedCell;
        if (pressed < 0 || pressed >= _view.Count)
            return;
        var items = _selection.IsSelected(pressed) ? _selection.Items() : [_view[pressed]];
        _dragging = true;
        _collapseOnRelease = false;
        _pressedCell = -1;
        if (Native.GetCapture() == Hwnd)
            Native.ReleaseCapture();
        try
        {
            var data = Shell.DataObjectOf(items.Select(i => Shell.FromPath(i.Path)));
            ShellNative.DoDragDrop(data, new DropSource(), (uint)(DropEffect.Copy | DropEffect.Move | DropEffect.Link), out _);
        }
        catch (Exception ex)
        {
            Log.Warn("Dragging Big Folder items out failed", ex);
        }
        finally
        {
            _dragging = false;
        }
        if (Hwnd == 0 || _closing)
            return;
        Redraw();
        if (Native.GetForegroundWindow() != Hwnd && !_menuOpen)
            Close(); // dropped into another program, which took the foreground
    }

    // ----- messages -----

    protected override nint OnMessage(uint msg, nint wParam, nint lParam)
    {
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
            case Native.WM_ACTIVATE when Native.LowWord(wParam) == Native.WA_INACTIVE:
                OnDeactivated();
                return 0;
            case Native.WM_KEYDOWN:
                OnKey((int)wParam);
                return 0;
            case Native.WM_SYSKEYDOWN when (int)wParam == Native.VK_DOWN:
                ShowSortMenu(fromKeyboard: true); // Alt+Down opens the sort list, like a drop-down
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
                if (_hover != -1 || _sortHover)
                {
                    _hover = -1;
                    _sortHover = false;
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
            case Native.WM_CAPTURECHANGED when lParam != Hwnd && (_sortPressed || _pressedCell >= 0) && !_dragging:
                _sortPressed = false;
                _pressedCell = -1;
                _collapseOnRelease = false;
                Redraw();
                return 0;
            case OpenItemMessage:
                if ((int)wParam >= 0 && (int)wParam < _view.Count)
                {
                    _selection.SelectOnly((int)wParam);
                    OpenSelection();
                    Redraw();
                }
                return 0;
            case OpenSortMessage:
                ShowSortMenu();
                return 0;
            case 0x0010 /* WM_CLOSE */:
                Close();
                return 0;
        }
        return base.OnMessage(msg, wParam, lParam);
    }

    /// <summary>A click outside deactivates the panel, and closes it. Not while one of its own menus is up, while items are dragged out, nor in its first moments.</summary>
    private void OnDeactivated()
    {
        if (_menuOpen || _dragging || _closing || _widget.PanelPinned || (DateTime.UtcNow - _openedAt).TotalMilliseconds < GuardMilliseconds)
            return;
        CommitEdit();
        Close();
    }
}

/// <summary>The panel's accessibility tree: 1 = the search field, 2 = the sort button, 3.. = the items in view.</summary>
internal sealed class PanelAccessible(PixelFolderPanel panel) : PopupAccessible(() => panel.Hwnd)
{
    private const int FirstItem = 3;

    protected override int ChildCount => FirstItem - 1 + panel.ShownItems.Count;

    protected override int SelfRole => RoleWindow;

    protected override string? Name(int id) => id switch
    {
        0 => panel.Title,
        1 => Strings.FolderSearch,
        2 => Strings.FolderSortBy,
        _ => panel.ShownItems[id - FirstItem].DisplayName,
    };

    protected override string? Value(int id) => id switch
    {
        1 => panel.SearchText,
        2 => panel.SortLabel,
        _ => null,
    };

    protected override int Role(int id) => id switch
    {
        1 => RoleEdit,
        2 => RolePushButton,
        _ => RoleListItem,
    };

    protected override int State(int id)
    {
        switch (id)
        {
            case 0:
                return 0;
            case 1:
                return StateFocusable | (panel.IsGridFocused ? 0 : StateFocused);
            case 2:
                return StateFocusable | StateHasPopup;
        }
        int index = id - FirstItem;
        int state = StateFocusable | StateSelectable;
        if (panel.IsItemSelected(index))
            state |= StateSelected;
        if (panel.IsGridFocused && panel.FocusedItem == index)
            state |= StateFocused;
        if (!panel.IsItemShown(index))
            state |= StateOffscreen;
        return state;
    }

    protected override Win32.RECT Location(int id) => id switch
    {
        0 => panel.WindowRect(),
        1 => panel.SearchRect(),
        2 => panel.SortRect(),
        _ => panel.ItemRect(id - FirstItem),
    };

    protected override int FocusedId => panel.IsGridFocused ? panel.FocusedItem + FirstItem : 1;

    protected override void Select(int id)
    {
        if (id >= FirstItem)
            panel.SelectFromAccessibility(id - FirstItem);
    }

    protected override void PatternSelect(int id) => Select(id);

    protected override void DoDefault(int id)
    {
        if (id == 2)
            panel.RequestSortMenu();
        else if (id >= FirstItem)
            panel.RequestOpenItem(id - FirstItem); // answered from the message loop, not inside the COM call
    }

    protected override string? DefaultActionText(int id) => id switch
    {
        0 or 1 => null,
        2 => "Press",
        _ => "Open",
    };
}
