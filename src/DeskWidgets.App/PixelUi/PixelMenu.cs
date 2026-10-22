using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Interop;
using DeskWidgets.Motion;
using DeskWidgets.Pixel;

namespace DeskWidgets.PixelUi;

/// <summary>Where a popup of a given size goes relative to an anchor point, inside a work area (pure arithmetic).</summary>
internal static class MenuPlacement
{
    /// <summary>
    /// The top-left of a <paramref name="width"/> × <paramref name="height"/> popup that opens at the anchor:
    /// to the right and below by default, flipped left or above when it would leave the work area;
    /// <paramref name="preferAbove"/> tries above first.
    /// </summary>
    public static (int X, int Y) Place(int anchorX, int anchorY, int width, int height, Win32.RECT work, bool preferAbove)
    {
        int x = anchorX + width <= work.Right ? anchorX : Math.Max(work.Left, anchorX - width);
        int y;
        if (preferAbove && anchorY - height >= work.Top)
            y = anchorY - height;
        else if (anchorY + height <= work.Bottom)
            y = anchorY;
        else if (anchorY - height >= work.Top)
            y = anchorY - height;
        else
            y = Math.Max(work.Top, work.Bottom - height);
        return (x, y);
    }

    /// <summary>
    /// Where a submenu goes beside its parent: its left edge one art pixel inside the parent's right edge (so the two outlines
    /// share a column), or, with no room on the right, to the left of the parent the same way; its top three art pixels above
    /// the parent row so the first rows line up; then kept inside the work area. All in screen pixels.
    /// </summary>
    public static (int X, int Y) PlaceSubmenu(int parentLeft, int parentWidth, int parentRowTop, int width, int height, Win32.RECT work, int factor)
    {
        int x = parentLeft + parentWidth - factor;
        if (x + width > work.Right)
            x = Math.Max(work.Left, parentLeft - width + factor);
        int y = parentRowTop - MenuLayout.Top * factor;
        y = Math.Max(work.Top, Math.Min(y, work.Bottom - height));
        return (x, y);
    }
}

/// <summary>
/// A cascading pixel menu: a root panel that holds the keyboard focus and the mouse capture, plus any open
/// submenus (non-activating panels). Dismissal, like a native menu's: Esc, a click outside, losing the
/// foreground or Alt.
/// </summary>
internal sealed class PixelMenu
{
    private const nuint SubmenuTimer = 1;
    private const nuint ScrollTimer = 2;
    private const uint SubmenuDelayMs = 250;
    private const uint ScrollRepeatMs = 100;
    private const uint ActivateMessage = Win32.WM_APP + 10; // posted when assistive technology presses a row

    private readonly List<MenuLevel> _levels = [];
    private readonly Action? _closed;
    private nint _previousForeground;
    private bool _closing;
    private int _pendingLevel = -1;
    private int _pendingRow = -1;
    private int _scrollLevel = -1;
    private int _scrollDirection;

    private PixelMenu(Action? closed) => _closed = closed;

    public static PixelMenu? Active { get; private set; }

    public IReadOnlyList<MenuLevel> Levels => _levels;

    public MenuLevel Root => _levels[0];

    public MenuLevel Top => _levels[^1];

    public bool IsRoot(MenuLevel level) => _levels.Count > 0 && _levels[0] == level;

    /// <summary>
    /// Opens a menu with its top-left corner at a screen point (physical pixels), flipped to stay on the monitor.
    /// Opened from the pointer no row is current; <paramref name="fromKeyboard"/> makes the first row current.
    /// </summary>
    public static PixelMenu Show(IReadOnlyList<MenuEntry> items, int anchorX, int anchorY, bool fromKeyboard = false, Action? closed = null)
    {
        Active?.CloseAll(restoreForeground: false, animate: false);
        var anchor = new Win32.POINT { X = anchorX, Y = anchorY };
        var menu = new PixelMenu(closed) { _previousForeground = Native.GetForegroundWindow() };

        int factor = PixelCard.Factor(Win32.GetScaleAt(anchor));
        var work = Win32.GetWorkArea(anchor);
        var root = new MenuLevel(menu, items, null, -1, noActivate: false);
        root.Plan(MaxArtHeight(work, factor));
        var (artWidth, artHeight) = root.ArtSize;
        var (x, y) = MenuPlacement.Place(anchorX, anchorY, artWidth * factor, artHeight * factor, work, preferAbove: false);
        menu._levels.Add(root);
        Active = menu;
        if (fromKeyboard)
            root.Selected = MenuNav.First(items);
        root.Spec = GrowSpec.Menu(above: y < anchorY);
        root.Prepare();
        root.GrowIn(root.Spec, factor, x, y); // B5: GROW from the pointer's corner (the whole menu at once with motion off)
        root.ShowWindow(activate: true);
        Win32.SetForegroundWindow(root.Hwnd);
        Native.SetFocus(root.Hwnd);
        Native.SetCapture(root.Hwnd);
        root.AnnounceOpened(submenu: false);
        if (root.Selected >= 0)
            root.AnnounceFocus(root.Selected);
        return menu;
    }

    private static int MaxArtHeight(Win32.RECT work, int factor) => work.Height / factor - MenuLayout.ShadowOffset;

    // --- pointer ---------------------------------------------------------------------------------------------

    private bool TryHit(out MenuLevel level)
    {
        Win32.GetCursorPos(out var point);
        for (int i = _levels.Count - 1; i >= 0; i--)
        {
            if (_levels[i].Contains(point.X, point.Y))
            {
                level = _levels[i];
                return true;
            }
        }
        level = null!;
        return false;
    }

    public void OnMouseMove()
    {
        if (_closing || !TryHit(out var level))
            return;
        Win32.GetCursorPos(out var point);

        int scrollRow = level.ScrollRowAt(point.X, point.Y);
        if (scrollRow >= 0)
        {
            if (_scrollLevel != _levels.IndexOf(level) || _scrollDirection != (scrollRow == 0 ? -1 : 1))
            {
                _scrollLevel = _levels.IndexOf(level);
                _scrollDirection = scrollRow == 0 ? -1 : 1;
                Native.SetTimer(Root.Hwnd, ScrollTimer, ScrollRepeatMs, 0);
            }
            return;
        }
        StopScrolling();

        int row = level.RowAt(point.X, point.Y);
        if (row < 0 || !level.Items[row].Focusable || row == level.Selected)
            return;
        CloseAbove(level);
        SetSelected(level, row);
        Native.KillTimer(Root.Hwnd, SubmenuTimer);
        if (level.Items[row].Kind == MenuKind.Submenu)
        {
            _pendingLevel = _levels.IndexOf(level);
            _pendingRow = row;
            Native.SetTimer(Root.Hwnd, SubmenuTimer, SubmenuDelayMs, 0);
        }
    }

    public void OnTimer(int id)
    {
        if (_closing)
            return;
        if (id == (int)SubmenuTimer)
        {
            Native.KillTimer(Root.Hwnd, SubmenuTimer);
            if (_pendingLevel >= 0 && _pendingLevel < _levels.Count && _levels[_pendingLevel].Selected == _pendingRow)
                OpenSub(_levels[_pendingLevel], _pendingRow, selectEntry: false);
        }
        else if (id == (int)ScrollTimer)
        {
            if (_scrollLevel < 0 || _scrollLevel >= _levels.Count || !_levels[_scrollLevel].Scroll(_scrollDirection))
            {
                StopScrolling();
                return;
            }
            RedrawAfterScroll(_levels[_scrollLevel]);
        }
    }

    private void StopScrolling()
    {
        if (_scrollLevel >= 0 && _levels.Count > 0)
            Native.KillTimer(Root.Hwnd, ScrollTimer);
        _scrollLevel = -1;
    }

    private static void RedrawAfterScroll(MenuLevel level)
    {
        // The row under the pointer changed with the scroll: let the next move pick it up, but never keep a hidden row current.
        if (level.Selected >= 0 && !level.IsRowShown(level.Selected))
            level.Selected = -1;
        level.Redraw();
    }

    public void OnButtonDown()
    {
        if (_closing)
            return;
        if (!TryHit(out var level))
        {
            CloseAll(restoreForeground: true); // a click outside dismisses the menu and is swallowed, like a native one
            return;
        }
        Win32.GetCursorPos(out var point);
        int scrollRow = level.ScrollRowAt(point.X, point.Y);
        if (scrollRow >= 0)
        {
            if (level.Scroll(scrollRow == 0 ? -1 : 1))
                RedrawAfterScroll(level);
            return;
        }
        int row = level.RowAt(point.X, point.Y);
        if (row < 0 || !level.Items[row].Focusable)
            return;
        level.Pressed = row;
        if (level.Items[row].Kind == MenuKind.Submenu)
        {
            SetSelected(level, row);
            OpenSub(level, row, selectEntry: false);
            return;
        }
        level.Redraw();
    }

    public void OnButtonUp()
    {
        if (_closing || !TryHit(out var level))
            return;
        Win32.GetCursorPos(out var point);
        int row = level.RowAt(point.X, point.Y);
        int pressed = level.Pressed;
        level.Pressed = -1;
        if (row < 0 || (pressed >= 0 && pressed != row))
        {
            level.Redraw();
            return;
        }
        ActivateRow(level, row);
    }

    public void OnWheel(int delta)
    {
        if (_closing)
            return;
        var level = TryHit(out var hit) ? hit : Top;
        int rows = Math.Max(1, Math.Abs(delta) / 120);
        if (level.Scroll(delta > 0 ? -rows : rows))
            RedrawAfterScroll(level);
    }

    // --- keyboard --------------------------------------------------------------------------------------------

    public void OnKey(int virtualKey)
    {
        if (_closing)
            return;
        var level = Top;
        var items = level.Items;
        int selected = level.Selected;
        switch (virtualKey)
        {
            case Native.VK_DOWN:
                SetSelected(level, MenuNav.Step(items, selected, +1));
                break;
            case Native.VK_UP:
                SetSelected(level, MenuNav.Step(items, selected, -1));
                break;
            case Native.VK_TAB:
                SetSelected(level, MenuNav.Step(items, selected, (Win32.GetAsyncKeyState(0x10) & 0x8000) != 0 ? -1 : +1));
                break;
            case Native.VK_HOME:
                SetSelected(level, MenuNav.First(items));
                break;
            case Native.VK_END:
                SetSelected(level, MenuNav.Last(items));
                break;
            case Native.VK_RIGHT:
                if (selected >= 0 && items[selected].Kind == MenuKind.Submenu)
                    OpenSub(level, selected, selectEntry: true);
                break;
            case Native.VK_LEFT:
                if (_levels.Count > 1)
                    CloseTop();
                break;
            case Native.VK_ESCAPE:
                if (_levels.Count > 1)
                    CloseTop();
                else
                    CloseAll(restoreForeground: true);
                break;
            case Native.VK_RETURN or Native.VK_SPACE:
                if (selected >= 0)
                    ActivateRow(level, selected, selectEntryOfSubmenu: true);
                break;
        }
    }

    public void OnSysKey(int virtualKey)
    {
        if (virtualKey == 0x12 /* VK_MENU: Alt on its own closes a menu */ || virtualKey == 0x79 /* F10 */)
            CloseAll(restoreForeground: true);
    }

    // --- dismissal -------------------------------------------------------------------------------------------

    public void OnDeactivated(MenuLevel level)
    {
        if (!_closing && IsRoot(level))
            CloseAll(restoreForeground: false);
    }

    public void OnCaptureChanged(MenuLevel level, nint newOwner)
    {
        if (!_closing && IsRoot(level) && newOwner != level.Hwnd)
            CloseAll(restoreForeground: false);
    }

    public void CloseAll(bool restoreForeground, bool animate = true)
    {
        if (_closing)
            return;
        _closing = true;
        if (Active == this)
            Active = null;
        nint rootHwnd = _levels.Count > 0 ? Root.Hwnd : 0;
        if (rootHwnd != 0)
        {
            Native.KillTimer(rootHwnd, SubmenuTimer);
            Native.KillTimer(rootHwnd, ScrollTimer);
            if (Native.GetCapture() == rootHwnd)
                Native.ReleaseCapture();
            Native.NotifyWinEvent(Native.EVENT_SYSTEM_MENUEND, rootHwnd, Native.OBJID_CLIENT, 0);
        }
        for (int i = _levels.Count - 1; i >= 0; i--)
        {
            if (animate)
                _levels[i].ShrinkOut(_levels[i].Spec); // B5: SHRINK, then the window goes
            else
                _levels[i].Dispose();
        }
        _levels.Clear();
        if (restoreForeground && _previousForeground != 0 && Native.IsWindow(_previousForeground))
        {
            Win32.GetWindowThreadProcessId(_previousForeground, out uint processId);
            if (processId != (uint)Environment.ProcessId)
                Win32.SetForegroundWindow(_previousForeground);
        }
        _closed?.Invoke();
    }

    private void CloseTop()
    {
        var level = Top;
        Native.NotifyWinEvent(Native.EVENT_SYSTEM_MENUPOPUPEND, level.Hwnd, Native.OBJID_CLIENT, 0);
        _levels.Remove(level);
        level.ShrinkOut(level.Spec);
        if (_levels.Count > 0)
        {
            Top.ChildFocused = false;
            Top.Redraw();
            Top.AnnounceFocus(Top.Selected);
        }
        if (_scrollLevel >= _levels.Count)
            StopScrolling();
    }

    private void CloseAbove(MenuLevel level)
    {
        while (_levels.Count > 0 && Top != level)
            CloseTop();
    }

    // --- selection and activation ----------------------------------------------------------------------------

    private void SetSelected(MenuLevel level, int index)
    {
        if (index < 0)
            return;
        level.Selected = index;
        level.ChildFocused = false;
        level.EnsureVisible(index);
        level.Redraw();
        level.AnnounceFocus(index);
        // The parent of the level that now has a current row shows its row as "open"; the one above it, too.
        int at = _levels.IndexOf(level);
        if (at > 0 && !_levels[at - 1].ChildFocused)
        {
            _levels[at - 1].ChildFocused = true;
            _levels[at - 1].Redraw();
        }
    }

    private void ActivateRow(MenuLevel level, int row, bool selectEntryOfSubmenu = false)
    {
        var entry = level.Items[row];
        if (!entry.Focusable)
            return;
        if (entry.Kind == MenuKind.Submenu)
        {
            SetSelected(level, row);
            OpenSub(level, row, selectEntryOfSubmenu);
            return;
        }
        var click = entry.Click;
        CloseAll(restoreForeground: true);
        try
        {
            click?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warn($"Menu item '{entry.Text}' failed", ex);
        }
    }

    /// <summary>A screen reader asked to press a row (on its own thread's call): do it from the message loop.</summary>
    public void ActivateFromAccessibility(MenuLevel level, int row)
    {
        int levelIndex = _levels.IndexOf(level);
        if (levelIndex >= 0 && _levels.Count > 0)
            Win32.PostMessage(Root.Hwnd, ActivateMessage, levelIndex, row);
    }

    public bool HandleActivateMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg != ActivateMessage)
            return false;
        int levelIndex = (int)wParam, row = (int)lParam;
        if (!_closing && levelIndex < _levels.Count && row >= 0 && row < _levels[levelIndex].Items.Count)
            ActivateRow(_levels[levelIndex], row, selectEntryOfSubmenu: true);
        return true;
    }

    private void OpenSub(MenuLevel parent, int row, bool selectEntry)
    {
        CloseAbove(parent);
        var entry = parent.Items[row];
        if (entry.Kind != MenuKind.Submenu || entry.Children.Count == 0)
            return;
        int factor = Root.Factor;
        var work = Win32.GetWorkArea(new Win32.POINT { X = parent.Origin.X, Y = parent.Origin.Y });
        var child = new MenuLevel(this, entry.Children, parent, row, noActivate: true);
        child.Plan(MaxArtHeight(work, factor));
        var (artWidth, artHeight) = child.ArtSize;
        var (x, y) = MenuPlacement.PlaceSubmenu(parent.Origin.X, parent.PanelWidth * factor, parent.RowRect(row).Top, artWidth * factor, artHeight * factor, work, factor);

        if (selectEntry)
        {
            child.Selected = MenuNav.Entry(entry.Children);
            child.EnsureVisible(child.Selected);
        }
        _levels.Add(child);
        child.Spec = GrowSpec.Submenu(toTheLeft: x < parent.Origin.X);
        child.Prepare();
        child.GrowIn(child.Spec, factor, x, y); // B5: GROW sideways from the parent's edge
        child.ShowWindow(activate: false);
        child.AnnounceOpened(submenu: true);
        if (child.Selected >= 0)
        {
            parent.ChildFocused = true;
            parent.Redraw();
            child.AnnounceFocus(child.Selected);
        }
    }
}
