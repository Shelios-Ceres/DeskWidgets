using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Interop;
using DeskWidgets.Motion;
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
    private const int VkShift = 0x10, VkControl = 0x11, VkAlt = 0x12, VkApps = 0x5D, VkF2 = 0x71, VkF4 = 0x73, VkDelete = 0x2E, VkA = 0x41, VkV = 0x56;
    private const uint OpenItemMessage = Win32.WM_APP + 21, OpenSortMessage = Win32.WM_APP + 22;

    private readonly FolderWidget _widget;
    private readonly PixelFolderIcons _icons;
    private readonly PanelSelection _selection = new();
    private readonly LineEditor _search = new();
    private readonly PanelAccessible _accessible;
    private List<FolderItem> _view = [];
    private PanelEdit? _edit;
    private string? _renamingFile;
    private FolderPixelPanelScene.PanelPlan? _plan;
    private int _rank; // the metric set: 0 pixel fonts, 1 Fusion 8 px, 2 SimSun
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
    private readonly PanelFocusGuard _guard;
    private TextService? _text;
    private readonly StepAnimator _steps = new(); // A1: GROW and SHRINK
    private readonly StepAnimator _listSteps = new(); // A2, A4: MOVE, POP, LEAVE
    private readonly StepAnimator _flashSteps = new(); // A3: FLASH
    private double _revealShare = 1; // how much of the panel's width shows while it grows or shrinks
    private bool _anchorRight; // the card is on the panel's right: the box grows from, and shrinks to, its right edge
    private ListMotion? _motion;
    private PixelCanvas? _lastPanel; // the panel as last drawn (without its shadow): what a leaving cell looked like
    private int _flashCell = -1;
    private IReadOnlySet<int>? _dimmed;

    public PixelFolderPanel(FolderWidget widget)
    {
        _widget = widget;
        _guard = new PanelFocusGuard(() => IsOpen, () => Hwnd, () => _menuOpen || _dragging || _widget.PanelPinned, CloseFromGuard, TakeForeground);
        // A picture that arrives does not cut a grow or a shrink short: the last frame of a grow draws the scene afresh.
        _icons = new PixelFolderIcons(() =>
        {
            if (!_steps.Running)
                Redraw();
        });
        _accessible = new PanelAccessible(this);
        _ = "warm".Contains("RM", StringComparison.CurrentCultureIgnoreCase); // the first filter of a process pays ~60 ms for culture-aware search coming up
        CreateWindow(noActivate: false);
        _text = new TextService(Hwnd, () => _edit?.Editor ?? _search, ActiveFieldRect, OnTextServiceChanged);
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
        _anchorRight = x + width <= card.Left;
        RecomputeView();
        EnsurePlan();
        // A1, GROW: the window is at its final size from the first frame; the box grows from the card's side at full height, 1/3, 2/3, then the panel.
        double[] shares = [1.0 / 3, 2.0 / 3, 1.0];
        _steps.Start(shares.Length, frame =>
        {
            _revealShare = shares[frame];
            if (_revealShare >= 1)
                Draw(x, y);
            else
                DrawBox(x, y);
        });
        _guard.Touch();
        ShowWindow(activate: true);
        Win32.SetForegroundWindow(Hwnd);
        Native.SetFocus(Hwnd);
        _text?.Focus();
        _accessible.RaiseOpened(UiaNative.EventWindowOpened);
    }

    public bool Reusable => false;

    public nint Handle => IsOpen ? Hwnd : 0;

    public IReadOnlyList<string> ViewFileNames => _view.Select(i => i.FileName).ToList();

    public void Reclaim() => _guard.Reclaim(afterOperation: true);

    /// <summary>The user left the panel for another program: a rename in progress is committed, then the panel goes.</summary>
    private void CloseFromGuard()
    {
        CommitEdit();
        Close();
    }

    /// <summary>Our own work is over (or the card's menu started a rename here): the panel takes the activation and the keyboard focus.</summary>
    private void TakeForeground()
    {
        if (!IsOpen)
            return;
        Win32.SetForegroundWindow(Hwnd);
        Native.SetFocus(Hwnd);
        _text?.Focus();
    }

    /// <summary>The card is going away: the window goes at once, with no shrink.</summary>
    public void Destroy()
    {
        if (Hwnd == 0)
            return;
        bool wasOpen = !_closing;
        _closing = true;
        if (Native.GetCapture() == Hwnd)
            Native.ReleaseCapture();
        Dispose();
        if (wasOpen)
            Closed?.Invoke();
    }

    /// <summary>
    /// A1, SHRINK: the box shrinks toward the card's side, 2/3, 1/3, then the window is gone. The panel counts as closed from the first
    /// moment (the action that closed it is not delayed); a close during a grow starts from the width it had reached.
    /// </summary>
    public void Close()
    {
        if (_closing || Hwnd == 0)
            return;
        double from = _steps.Running ? _revealShare : 1;
        _closing = true;
        if (Native.GetCapture() == Hwnd)
            Native.ReleaseCapture();
        _listSteps.Cancel();
        _flashSteps.Cancel();
        _steps.Cancel();
        Closed?.Invoke();
        if (Hwnd == 0)
            return;
        var shares = new[] { 2.0 / 3, 1.0 / 3 }.Where(share => share < from - 0.001).ToList();
        var (x, y) = (Origin.X, Origin.Y);
        _steps.Start(shares.Count + 1, frame =>
        {
            if (frame < shares.Count)
            {
                _revealShare = shares[frame];
                DrawBox(x, y);
            }
        }, Dispose);
    }

    public override void Dispose()
    {
        _guard.Stop();
        _steps.Dispose();
        _listSteps.Dispose();
        _flashSteps.Dispose();
        if (Hwnd != 0)
            UiaNative.UiaReturnRawElementProvider(Hwnd, 0, 0, null);
        _accessible.DisconnectUia();
        _text?.Dispose();
        _text = null;
        base.Dispose();
    }

    // ----- the content: filter, plan, paint -----

    public void Refresh()
    {
        if (Hwnd == 0)
            return;
        ChangeList(RecomputeView);
        Redraw();
    }

    /// <summary>
    /// A2 and A4: runs a change of the list (the filter, the order, the items) and starts what it does for three frames: the cells that
    /// stayed in their row MOVE, new ones and ones that changed rows POP, the old cells of those that left or changed rows LEAVE. Only
    /// cells in view animate; a grow or shrink in progress, or "Animation effects" off, snaps.
    /// </summary>
    private void ChangeList(Action change)
    {
        var oldKeys = _view.Select(i => i.FileName).ToList();
        int oldFirst = _firstRow;
        var oldPlan = _plan;
        var oldScene = _lastPanel;
        _listSteps.Cancel();
        _motion = null;
        change();
        if (Hwnd == 0 || _closing || _steps.Running || !MotionSettings.Enabled || oldPlan is null || oldScene is null)
            return;
        var newKeys = _view.Select(i => i.FileName).ToList();
        if (oldKeys.SequenceEqual(newKeys, StringComparer.OrdinalIgnoreCase))
            return;
        EnsurePlan();
        var plan = _plan!;
        var palette = CurrentPalette();
        (int, int)? PlaceIn(FolderPixelPanelScene.PanelPlan at, int first, int index) => FolderPixelPanelScene.CellRect(at, index, first) is { } rect ? (rect.X + 1, rect.Y + 2) : null;
        LeavingCell? Picture(int index)
        {
            if (FolderPixelPanelScene.CellRect(oldPlan, index, oldFirst) is not { } rect)
                return null;
            var picture = PixelMotion.Snapshot(oldScene, rect.X, rect.Y, rect.W, rect.H);
            for (int i = 0; i < picture.Pixels.Length; i++)
                if (picture.Pixels[i] == palette.Panel)
                    picture.Pixels[i] = 0; // only the cell's own pixels leave, not the panel behind them
            return new LeavingCell(picture, rect.X, rect.Y);
        }
        var motion = ListMotion.Plan(oldKeys, newKeys, index => PlaceIn(oldPlan, oldFirst, index), index => PlaceIn(plan, _firstRow, index), Picture);
        if (motion is null)
            return;
        _motion = motion;
        _listSteps.Start(ListMotion.Frames, frame =>
        {
            motion.Frame = frame + 1;
            if (frame > 0)
                Redraw();
        }, () => _motion = null);
    }

    /// <summary>A3: the FLASH frame of an item that opens, then the hover look.</summary>
    private void FlashItem(int index)
    {
        _flashSteps.Start(2, frame =>
        {
            _flashCell = frame == 0 ? index : -1;
            Redraw();
        });
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
        // The result of the panel's own work: the renamed item, or the item after a removed one, gets the selection.
        if (_widget.Aftermath.Resolve(_view.Select(i => i.FileName).ToList(), DateTime.UtcNow) is { } picked)
        {
            _selection.SelectOnly(picked);
            if (_selection.Focus >= 0)
                _firstRow = PanelSelection.FirstRowFor(_selection.Focus, FolderPixelPanelScene.Columns, _firstRow, VisibleRows);
        }
        if (_hover >= _view.Count)
            _hover = -1;
        if (_edit is { Target: PanelEditTarget.Item } && _renamingFile is not null)
        {
            int index = _view.FindIndex(i => i.FileName.Equals(_renamingFile, StringComparison.OrdinalIgnoreCase));
            _edit = index < 0 ? null : _edit with { ItemIndex = index };
            if (_edit is null)
                _renamingFile = null;
        }
        _rank = FolderPixelPanelScene.MetricRank(Model.Name, _search.Text, Model.Sort, _view);
    }

    private FolderPanelView Snapshot() => new(Model.Name, Model.Path, _view, _search.Text, Model.Sort, Model.Items.Count, _selection, _hover, _selection.Focus, _firstRow, _sortHover, _sortPressed, _edit, _icons,
        _pressedCell, _flashCell, _motion, _dimmed);

    /// <summary>Works out the layout (and so what the pointer can hit) without drawing it: a grow shows empty frames first.</summary>
    private void EnsurePlan()
    {
        var plan = FolderPixelPanelScene.Plan(Snapshot(), _rank);
        _firstRow = Math.Clamp(_firstRow, 0, Math.Max(0, plan.TotalRows - plan.VisibleRows));
        _plan = plan;
    }

    private void Draw(int x, int y)
    {
        var palette = CurrentPalette();
        var view = Snapshot();
        var plan = FolderPixelPanelScene.Plan(view, _rank);
        _firstRow = Math.Clamp(_firstRow, 0, Math.Max(0, plan.TotalRows - plan.VisibleRows));
        _plan = plan;
        var panel = FolderPixelPanelScene.Draw(palette, view with { FirstRow = _firstRow }, plan);
        _lastPanel = panel;
        Present(PixelFrame.WithShadow(panel, palette.Shadow, DialogLayout.ShadowOffset), Factor, x, y);
    }

    /// <summary>GROW and SHRINK's empty box: the panel's own frame at <see cref="_revealShare"/> of its width, the rest of the window transparent.</summary>
    private void DrawBox(int x, int y) => Present(PixelMotion.EmptyFrame(CurrentPalette(), FolderPixelPanelScene.Width, FolderPixelPanelScene.Height, DialogLayout.ShadowOffset, _revealShare, _anchorRight), Factor, x, y);

    private void Redraw()
    {
        if (Hwnd == 0 || _closing)
            return;
        if (_steps.Running)
        {
            _steps.Cancel(); // input wins: a state that needs drawing cuts a grow short
            _revealShare = 1;
        }
        if (Native.GetWindowRect(Hwnd, out var rect))
            Draw(rect.Left, rect.Top);
        _text?.Sync(); // the field may have changed: the IME works from the same text
    }

    /// <summary>The input method or the emoji panel changed the field: the search follows its text; a rename only redraws.</summary>
    private void OnTextServiceChanged()
    {
        if (_edit is null)
            OnSearchChanged();
        else
            Redraw();
    }

    /// <summary>Where the field that takes the typed text is on the screen, so the IME's candidate window opens beside it.</summary>
    private Win32.RECT ActiveFieldRect()
    {
        if (_edit is { Target: PanelEditTarget.Folder })
            return ToScreen((6, 4, FolderPixelPanelScene.Width - 12, 14));
        if (_edit is { Target: PanelEditTarget.Item } edit && _plan is not null && FolderPixelPanelScene.CellRect(_plan, edit.ItemIndex, _firstRow) is { } cell)
            return ToScreen((cell.X, cell.Y + 2 + 16 + 1, cell.W, _plan.Rank == 2 ? 16 : 14));
        return SearchRect();
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
        ChangeList(RecomputeView);
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
        TakeForeground(); // started from the card's menu while the panel was already open
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
            string? newPath = FolderActions.RenameFolder(Model.Path, typed, _widget.OwnerHandle, out string? error);
            if (error is null && newPath is not null && !string.Equals(newPath, Model.Path, StringComparison.OrdinalIgnoreCase))
                Model.FolderRenamed(newPath);
            _widget.Report(error); // the warning, if any, and the focus back to the panel
        }
        else if (renaming is not null)
        {
            var item = Model.Items.FirstOrDefault(i => i.FileName.Equals(renaming, StringComparison.OrdinalIgnoreCase));
            if (item is not null && typed.Trim() != item.DisplayName)
                _widget.RenameItem(item, typed);
        }
        Redraw();
        _guard.Reclaim();
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
            case VkV when ctrl:
                editor.Insert(TextService.ReadClipboardText());
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
            case VkV when ctrl:
                if (_search.Insert(TextService.ReadClipboardText()))
                    OnSearchChanged();
                break;
            case VkF2 when _selection.Focus >= 0 && _selection.Focus < _view.Count:
                BeginRename(_view[_selection.Focus].FileName);
                break;
            case VkDelete when _selection.Count > 0:
                _widget.RecycleItems(_selection.Items());
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
        _guard.Touch(); // the click that closed the menu must not close the panel as well
        if (Hwnd == 0 || _closing)
            return;
        // The menu's command may have opened a window of ours (Properties, a dialog); the panel takes the focus back only if none has it.
        // Another program in the foreground means the user went there while the menu was up: the panel goes.
        _guard.Reclaim();
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
                    FlashItem(hit.Index);
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
        // A2d: while the drag lasts, each dragged cell is drawn at half its pixels (a still state, not an animation).
        _dimmed = new HashSet<int>(items.Select(item => _view.IndexOf(item)).Where(index => index >= 0));
        Redraw();
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
            _dimmed = null;
        }
        if (Hwnd == 0 || _closing)
            return;
        Redraw();
        _guard.Reclaim(); // dropped into another program, which took the foreground: the panel goes; otherwise it stays
    }

    // ----- messages -----

    protected override nint OnMessage(uint msg, nint wParam, nint lParam)
    {
        // While the box shrinks away (SHRINK) the panel is closed: it takes no more keys or clicks.
        if (_closing && msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN or Native.WM_CHAR or Native.WM_MOUSEMOVE or Native.WM_LBUTTONDOWN or Native.WM_LBUTTONUP or Native.WM_RBUTTONUP or Native.WM_MOUSEWHEEL)
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

    /// <summary>Leaving for another program closes the panel; our own menus, dialogs and Shell windows, a drag out and the panel's first moments do not (see <see cref="PanelFocusPolicy"/>).</summary>
    private void OnDeactivated()
    {
        if (!_closing)
            _guard.OnDeactivated();
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
