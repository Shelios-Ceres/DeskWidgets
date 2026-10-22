using DeskWidgets.Interop;
using DeskWidgets.Motion;
using DeskWidgets.Pixel;

namespace DeskWidgets.PixelUi;

/// <summary>
/// One panel of a pixel menu: the root menu or a cascaded submenu. A layered window showing the panel the
/// <see cref="MenuPainter"/> draws, with its hard shadow in the window's transparent margin.
/// </summary>
internal sealed class MenuLevel : PixelPopupWindow
{
    private readonly PixelMenu _menu;
    private readonly MenuAccessible _accessible;
    private MenuDrawing? _drawing;
    private int _panelHeight;

    public MenuLevel(PixelMenu menu, IReadOnlyList<MenuEntry> items, MenuLevel? parent, int parentIndex, bool noActivate)
    {
        _menu = menu;
        Items = items;
        Parent = parent;
        ParentIndex = parentIndex;
        Layout = MenuLayout.Create(items);
        _panelHeight = Layout.ContentHeight;
        _accessible = new MenuAccessible(this);
        CreateWindow(noActivate);
    }

    public IReadOnlyList<MenuEntry> Items { get; }

    public MenuLayout Layout { get; }

    public MenuLevel? Parent { get; }

    /// <summary>The row of the parent level that opened this one.</summary>
    public int ParentIndex { get; }

    private int _selected = -1;
    private StepAnimator? _wipe;
    private int _wipeRow = -1; // while the first frame of a WIPE shows: the row whose bar is half drawn

    /// <summary>
    /// The current row: the one under the pointer or reached by the keyboard (one highlight per menu), or -1.
    /// A new current row's bar WIPEs in from the left (B5): half its width, then all of it; a newer row cuts the last wipe short.
    /// </summary>
    public int Selected
    {
        get => _selected;
        set
        {
            if (value == _selected)
                return;
            _selected = value;
            BeginWipe(value);
        }
    }

    /// <summary>How this panel grows and shrinks (B5): set by the menu when it opens it.</summary>
    public GrowSpec Spec { get; set; } = GrowSpec.Menu(above: false);

    /// <summary>The row the mouse button is held on, or -1.</summary>
    public int Pressed { get; set; } = -1;

    /// <summary>Whether a submenu opened from <see cref="Selected"/> has a current row of its own (the parent row then shows "open").</summary>
    public bool ChildFocused { get; set; }

    public bool Scrollable { get; private set; }

    /// <summary>The first row shown; a long list scrolls a row at a time.</summary>
    public int FirstRow { get; private set; }

    public int PanelWidth => Layout.Width;

    public int PanelHeight => _panelHeight;

    /// <summary>Window size in art pixels: the panel and the shadow's margin.</summary>
    public (int Width, int Height) ArtSize => (Layout.Width + MenuLayout.ShadowOffset, _panelHeight + MenuLayout.ShadowOffset);

    /// <summary>Fits the panel into <paramref name="maxHeight"/> art pixels and, if it then scrolls, shows the checked row.</summary>
    public void Plan(int maxHeight)
    {
        (_panelHeight, bool scrollable) = Layout.Plan(maxHeight);
        Scrollable = scrollable;
        FirstRow = 0;
        if (!scrollable)
            return;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Checked)
            {
                FirstRow = Math.Min(Layout.FirstRowFor(i, 0, _panelHeight), Layout.MaxFirstRow(_panelHeight));
                break;
            }
        }
    }

    private MenuDrawing Paint(PixelPalette palette)
    {
        var paint = new MenuPaint(_panelHeight, Scrollable, FirstRow, Selected, Pressed, ChildFocused);
        var drawing = MenuPainter.Draw(palette, Layout, paint);
        if (_wipeRow >= 0)
            MenuPainter.WipeRow(drawing, MenuPainter.Draw(palette, Layout, paint with { Selected = -1 }), _wipeRow, Layout.Width);
        return drawing;
    }

    /// <summary>Works out the rows' places without showing anything yet (the pointer can hit them from the first GROW frame).</summary>
    public void Prepare() => _drawing = Paint(CurrentPalette());

    public void Draw(int factor, int screenX, int screenY)
    {
        var palette = CurrentPalette();
        _drawing = Paint(palette);
        Present(PixelFrame.WithShadow(_drawing.Panel, palette.Shadow, MenuLayout.ShadowOffset), factor, screenX, screenY);
    }

    /// <summary>Draws the panel as it is now. A change in a panel that is still growing shows it whole at once: input wins.</summary>
    public void Redraw()
    {
        if (Leaving || Hwnd == 0)
            return;
        CancelGrow();
        Draw(Factor, Origin.X, Origin.Y);
    }

    protected override PixelCanvas? EmptyFrame(GrowFrom from, double share)
        => PixelMotion.EmptyPanel(CurrentPalette(), Layout.Width, _panelHeight, PixelFrame.MenuCorner, MenuLayout.ShadowOffset, from, share);

    protected override void DrawFinal(int factor, int x, int y) => Draw(factor, x, y);

    private void BeginWipe(int row)
    {
        if (row < 0 || _drawing is null || Leaving || !MotionSettings.Enabled || !Items[row].Focusable)
        {
            EndWipe();
            return;
        }
        _wipeRow = row;
        _wipe ??= new StepAnimator();
        _wipe.Start(2, frame =>
        {
            if (frame > 0)
                _wipeRow = -1;
            Redraw();
        });
    }

    private void EndWipe()
    {
        _wipe?.Cancel();
        _wipeRow = -1;
    }

    /// <summary>Scrolls just enough to show row <paramref name="index"/>.</summary>
    public void EnsureVisible(int index)
    {
        if (Scrollable && index >= 0 && index < Items.Count)
            FirstRow = Layout.FirstRowFor(index, FirstRow, _panelHeight);
    }

    /// <summary>Scrolls by whole rows (positive: down); returns whether anything moved.</summary>
    public bool Scroll(int rows)
    {
        if (!Scrollable)
            return false;
        int before = FirstRow;
        FirstRow = Math.Clamp(FirstRow + rows, 0, Layout.MaxFirstRow(_panelHeight));
        return FirstRow != before;
    }

    private (int X, int Y) ToArt(int screenX, int screenY) => (FloorDiv(screenX - Origin.X, Factor), FloorDiv(screenY - Origin.Y, Factor));

    private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

    public bool Contains(int screenX, int screenY)
    {
        if (Hwnd == 0)
            return false;
        var (x, y) = ToArt(screenX, screenY);
        return x >= 0 && y >= 0 && x < Layout.Width && y < _panelHeight;
    }

    /// <summary>The row under a screen point, or -1 (outside the panel, in the frame or on a separator).</summary>
    public int RowAt(int screenX, int screenY)
    {
        if (_drawing is null || !Contains(screenX, screenY))
            return -1;
        var (_, y) = ToArt(screenX, screenY);
        foreach (var row in _drawing.Rows)
        {
            if (y >= row.Y && y < row.Y + row.Height)
                return row.Index;
        }
        return -1;
    }

    /// <summary>-1 when the point is on no scroll row, 0 on the top one, 1 on the bottom one; only a scroll row with more behind it counts.</summary>
    public int ScrollRowAt(int screenX, int screenY)
    {
        if (_drawing is null || !Scrollable || !Contains(screenX, screenY))
            return -1;
        var (_, y) = ToArt(screenX, screenY);
        var (first, last) = Layout.Visible(FirstRow, _panelHeight, true);
        if (first > 0 && y >= _drawing.UpY && y < _drawing.UpY + MenuLayout.ScrollHeight)
            return 0;
        if (last < Items.Count - 1 && y >= _drawing.DownY && y < _drawing.DownY + MenuLayout.ScrollHeight)
            return 1;
        return -1;
    }

    /// <summary>Screen rectangle of a visible row, or empty.</summary>
    public Win32.RECT RowRect(int index)
    {
        foreach (var row in _drawing?.Rows ?? [])
        {
            if (row.Index != index)
                continue;
            return new Win32.RECT
            {
                Left = Origin.X + MenuLayout.BarInset * Factor,
                Top = Origin.Y + row.Y * Factor,
                Right = Origin.X + (Layout.Width - MenuLayout.BarInset) * Factor,
                Bottom = Origin.Y + (row.Y + row.Height) * Factor,
            };
        }
        return default;
    }

    public Win32.RECT PanelRect() => new()
    {
        Left = Origin.X,
        Top = Origin.Y,
        Right = Origin.X + Layout.Width * Factor,
        Bottom = Origin.Y + _panelHeight * Factor,
    };

    /// <summary>Whether row <paramref name="index"/> is inside the part of the list now showing (before the next redraw too).</summary>
    public bool IsRowShown(int index)
    {
        if (!Scrollable)
            return true;
        var (first, last) = Layout.Visible(FirstRow, _panelHeight, true);
        return index >= first && index <= last;
    }

    public bool IsVisibleRow(int index) => _drawing?.Rows.Any(r => r.Index == index) == true;

    /// <summary>Assistive technology asked to press a row (accDoDefaultAction): same as Enter on it.</summary>
    public void RequestActivate(int index) => _menu.ActivateFromAccessibility(this, index);

    /// <summary>Tells assistive technology that the focus is on a row (index) or on the panel (-1).</summary>
    public void AnnounceFocus(int index)
    {
        if (Hwnd == 0)
            return;
        Native.NotifyWinEvent(Native.EVENT_OBJECT_FOCUS, Hwnd, Native.OBJID_CLIENT, index + 1);
        _accessible.RaiseFocus(index + 1);
    }

    public void AnnounceOpened(bool submenu)
    {
        Native.NotifyWinEvent(submenu ? Native.EVENT_SYSTEM_MENUPOPUPSTART : Native.EVENT_SYSTEM_MENUSTART, Hwnd, Native.OBJID_CLIENT, 0);
        _accessible.RaiseOpened(UiaNative.EventMenuOpened);
    }

    protected override nint OnMessage(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                return _menu.IsRoot(this) ? Win32.DefWindowProc(Hwnd, msg, wParam, lParam) : Native.MA_NOACTIVATE;
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
                _menu.OnDeactivated(this);
                return 0;
            case Native.WM_CAPTURECHANGED:
                _menu.OnCaptureChanged(this, lParam);
                return 0;
            case Native.WM_KEYDOWN:
                _menu.OnKey((int)wParam);
                return 0;
            case Native.WM_SYSKEYDOWN when wParam is 0x12 or 0x79: // Alt on its own, F10
                _menu.OnSysKey((int)wParam);
                return 0;
            case Native.WM_CHAR:
                return 0;
            case Native.WM_MOUSEMOVE:
                _menu.OnMouseMove();
                return 0;
            case Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN:
                _menu.OnButtonDown();
                return 0;
            case Native.WM_LBUTTONUP or Native.WM_RBUTTONUP:
                _menu.OnButtonUp();
                return 0;
            case Native.WM_MOUSEWHEEL:
                _menu.OnWheel(Native.SignedHigh(wParam));
                return 0;
            case Native.WM_TIMER:
                _menu.OnTimer((int)wParam);
                return 0;
            case 0x0010 /* WM_CLOSE */:
                _menu.CloseAll(restoreForeground: true);
                return 0;
        }
        if (_menu.HandleActivateMessage(msg, wParam, lParam))
            return 0;
        return base.OnMessage(msg, wParam, lParam);
    }

    public override void Dispose()
    {
        _wipe?.Dispose();
        _wipe = null;
        if (Hwnd != 0)
        {
            _accessible.RaiseOpened(UiaNative.EventMenuClosed);
            UiaNative.UiaReturnRawElementProvider(Hwnd, 0, 0, null);
        }
        _accessible.DisconnectUia();
        base.Dispose();
    }
}

/// <summary>The accessibility tree of one menu panel: rows are menu items, separators are separators, status lines are text.</summary>
internal sealed class MenuAccessible(MenuLevel level) : PopupAccessible(() => level.Hwnd)
{
    protected override int ChildCount => level.Items.Count;

    protected override int SelfRole => RoleMenuPopup;

    protected override string? Name(int id) => id == 0 ? null : level.Items[id - 1].Text;

    protected override int Role(int id) => level.Items[id - 1].Kind switch
    {
        MenuKind.Separator => RoleSeparator,
        MenuKind.Status => RoleStaticText,
        _ => RoleMenuItem,
    };

    protected override int State(int id)
    {
        if (id == 0)
            return 0;
        var entry = level.Items[id - 1];
        int state = 0;
        if (entry.Kind is MenuKind.Separator or MenuKind.Status)
            return state;
        if (!entry.Enabled)
            state |= StateUnavailable;
        else
            state |= StateFocusable | StateSelectable;
        if (level.Selected == id - 1)
            state |= StateSelected | StateFocused;
        if (entry.Checked)
            state |= StateChecked;
        if (entry.Kind == MenuKind.Submenu)
            state |= StateHasPopup;
        if (!level.IsVisibleRow(id - 1))
            state |= StateOffscreen;
        return state;
    }

    protected override Win32.RECT Location(int id) => id == 0 ? level.PanelRect() : level.RowRect(id - 1);

    protected override bool IsCheckable(int id) => level.Items[id - 1].Kind == MenuKind.Check;

    protected override bool IsRadio(int id) => level.Items[id - 1].Kind == MenuKind.Radio;

    protected override int FocusedId => level.Selected >= 0 ? level.Selected + 1 : 0;

    protected override void Select(int id) => level.Selected = id - 1;

    protected override void DoDefault(int id) => level.RequestActivate(id - 1);

    protected override string? DefaultActionText(int id)
        => id == 0 ? null : level.Items[id - 1].Kind == MenuKind.Submenu ? "Open" : "Execute";
}
