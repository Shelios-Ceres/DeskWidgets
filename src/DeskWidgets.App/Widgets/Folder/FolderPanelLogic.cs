namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// The state of a one-line text field drawn by hand (the Pixel panel's search and rename fields): the text, the caret and
/// whether the whole text is selected. Typing replaces a selection; the caret never lands inside a surrogate pair.
/// </summary>
internal sealed class LineEditor
{
    public LineEditor(string text = "", bool selectAll = false)
    {
        Text = text;
        Caret = text.Length;
        AllSelected = selectAll && text.Length > 0;
    }

    public string Text { get; private set; }

    /// <summary>UTF-16 index of the caret, 0 to Text.Length.</summary>
    public int Caret { get; private set; }

    public bool AllSelected { get; private set; }

    public void Set(string text, bool selectAll)
    {
        Text = text;
        Caret = text.Length;
        AllSelected = selectAll && text.Length > 0;
    }

    public void SelectAll()
    {
        AllSelected = Text.Length > 0;
        Caret = Text.Length;
    }

    /// <summary>Types text at the caret (replacing the selection). Returns whether the text changed.</summary>
    public bool Insert(string typed)
    {
        if (typed.Length == 0)
            return false;
        if (AllSelected)
        {
            Text = typed;
            Caret = typed.Length;
            AllSelected = false;
            return true;
        }
        Text = Text.Insert(Caret, typed);
        Caret += typed.Length;
        return true;
    }

    /// <summary>The selection as UTF-16 indexes: the whole text when it is all selected, else an empty range at the caret.</summary>
    public (int Start, int End) Selection => AllSelected ? (0, Text.Length) : (Caret, Caret);

    /// <summary>Replaces a range of the text (what a text service asks for), leaving the caret after the new text.</summary>
    public void Replace(int start, int end, string text)
    {
        start = Math.Clamp(start, 0, Text.Length);
        end = Math.Clamp(end, start, Text.Length);
        Text = Text[..start] + text + Text[end..];
        Caret = start + text.Length;
        AllSelected = false;
    }

    /// <summary>Selects a range the way a text service asks for it. Only the whole text or an empty range is drawn, so another range puts the caret at its end.</summary>
    public void SelectRange(int start, int end)
    {
        start = Math.Clamp(start, 0, Text.Length);
        end = Math.Clamp(end, start, Text.Length);
        AllSelected = start == 0 && end == Text.Length && end > 0;
        Caret = end;
    }

    public bool Backspace()
    {
        if (AllSelected)
            return Clear();
        if (Caret == 0)
            return false;
        int start = StepLeft(Caret);
        Text = Text.Remove(start, Caret - start);
        Caret = start;
        return true;
    }

    public bool Delete()
    {
        if (AllSelected)
            return Clear();
        if (Caret >= Text.Length)
            return false;
        int end = StepRight(Caret);
        Text = Text.Remove(Caret, end - Caret);
        return true;
    }

    public bool Clear()
    {
        bool changed = Text.Length > 0;
        Text = "";
        Caret = 0;
        AllSelected = false;
        return changed;
    }

    public void Left()
    {
        Caret = AllSelected ? 0 : StepLeft(Caret);
        AllSelected = false;
    }

    public void Right()
    {
        Caret = AllSelected ? Text.Length : StepRight(Caret);
        AllSelected = false;
    }

    public void Home()
    {
        Caret = 0;
        AllSelected = false;
    }

    public void End()
    {
        Caret = Text.Length;
        AllSelected = false;
    }

    private int StepLeft(int index)
    {
        if (index <= 0)
            return 0;
        index--;
        return index > 0 && char.IsLowSurrogate(Text[index]) && char.IsHighSurrogate(Text[index - 1]) ? index - 1 : index;
    }

    private int StepRight(int index)
    {
        if (index >= Text.Length)
            return Text.Length;
        index++;
        return index < Text.Length && char.IsLowSurrogate(Text[index]) && char.IsHighSurrogate(Text[index - 1]) ? index + 1 : index;
    }
}

/// <summary>
/// Which items of the panel's current list are selected, which has the keyboard focus, and where a range starts (Finder-style:
/// a click selects one, Ctrl toggles, Shift extends, arrows move the focus and the selection with it). Selection is kept by file
/// name, so it survives a refresh of the list. Plain logic, shared by whatever draws the panel.
/// </summary>
internal sealed class PanelSelection
{
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FolderItem> View { get; private set; } = [];

    /// <summary>The cell with the keyboard focus, or -1.</summary>
    public int Focus { get; set; } = -1;

    /// <summary>Where a Shift-range starts, or -1.</summary>
    public int Anchor { get; set; } = -1;

    /// <summary>Whether the focus ring shows (only after the keyboard moved it).</summary>
    public bool KeyboardFocus { get; set; }

    public int Count => _selected.Count;

    public bool IsSelected(FolderItem item) => _selected.Contains(item.FileName);

    public bool IsSelected(int index) => index >= 0 && index < View.Count && _selected.Contains(View[index].FileName);

    public List<FolderItem> Items() => View.Where(IsSelected).ToList();

    /// <summary>Shows a new list: items that are gone lose their selection, and the focus stays inside the list.</summary>
    public void SetView(IReadOnlyList<FolderItem> view)
    {
        View = view;
        _selected.IntersectWith(view.Select(i => i.FileName));
        if (Focus >= view.Count)
            Focus = view.Count - 1;
        if (Anchor >= view.Count)
            Anchor = -1;
    }

    public void Clear() => _selected.Clear();

    /// <summary>Selects exactly this item and makes it the focus and the anchor.</summary>
    public void SelectOnly(int index)
    {
        if (index < 0 || index >= View.Count)
            return;
        _selected.Clear();
        _selected.Add(View[index].FileName);
        Focus = Anchor = index;
    }

    public void SelectOnly(string fileName)
    {
        int index = View.ToList().FindIndex(i => i.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            SelectOnly(index);
    }

    public void SelectAll()
    {
        foreach (var item in View)
            _selected.Add(item.FileName);
    }

    public void Toggle(int index)
    {
        if (index < 0 || index >= View.Count)
            return;
        string name = View[index].FileName;
        if (!_selected.Remove(name))
            _selected.Add(name);
    }

    /// <summary>
    /// A press on a cell. Returns whether the selection should collapse to this cell on release: pressing an already selected
    /// cell of several keeps them all (it may start dragging the lot) until the button comes up without a drag.
    /// </summary>
    public bool Press(int index, bool ctrl, bool shift)
    {
        if (index < 0 || index >= View.Count)
            return false;
        KeyboardFocus = false;
        bool collapse = false;
        if (shift && Anchor >= 0)
        {
            SelectRange(Anchor, index, add: ctrl);
        }
        else if (ctrl)
        {
            Toggle(index);
            Anchor = index;
        }
        else
        {
            if (!IsSelected(index))
            {
                _selected.Clear();
                _selected.Add(View[index].FileName);
            }
            else if (_selected.Count > 1)
            {
                collapse = true;
            }
            Anchor = index;
        }
        Focus = index;
        return collapse;
    }

    public void SelectRange(int from, int to, bool add)
    {
        if (!add)
            _selected.Clear();
        for (int i = Math.Max(0, Math.Min(from, to)); i <= Math.Max(from, to) && i < View.Count; i++)
            _selected.Add(View[i].FileName);
    }

    /// <summary>
    /// Moves the keyboard focus by <paramref name="delta"/> cells (clamped); the selection follows unless Ctrl holds it, and Shift
    /// extends it from the anchor. <paramref name="first"/> lands on the first cell. Returns the new focus, or -1 for an empty list.
    /// </summary>
    public int MoveFocus(int delta, bool extend, bool keepSelection, bool first = false)
    {
        if (View.Count == 0)
            return -1;
        int next = first ? 0 : Focus < 0 ? (delta >= 0 ? 0 : View.Count - 1) : Math.Clamp(Focus + delta, 0, View.Count - 1);
        Focus = next;
        KeyboardFocus = true;
        if (extend)
        {
            if (Anchor < 0)
                Anchor = next;
            SelectRange(Anchor, next, add: false);
        }
        else if (!keepSelection)
        {
            SelectOnly(next);
        }
        return next;
    }

    /// <summary>The first visible row after scrolling just enough to show the row of <paramref name="index"/>.</summary>
    public static int FirstRowFor(int index, int columns, int firstRow, int visibleRows)
    {
        int row = index / columns;
        if (row < firstRow)
            return row;
        if (row >= firstRow + visibleRows)
            return row - visibleRows + 1;
        return firstRow;
    }
}

/// <summary>Where the Pixel panel opens, as pure arithmetic in screen pixels.</summary>
internal static class FolderPanelPlacement
{
    /// <summary>
    /// Beside the card, <paramref name="gap"/> away, tops aligned: on the right if it fits, else on the left; moved up if it would
    /// run off the bottom; centred over the card if neither side has room. Always kept inside the work area.
    /// </summary>
    public static (int X, int Y) Place(Interop.Win32.RECT card, Interop.Win32.RECT work, int width, int height, int gap)
    {
        int x;
        if (card.Right + gap + width <= work.Right)
            x = card.Right + gap;
        else if (card.Left - gap - width >= work.Left)
            x = card.Left - gap - width;
        else
            x = Math.Clamp((card.Left + card.Right) / 2 - width / 2, work.Left, Math.Max(work.Left, work.Right - width));
        int y = card.Top;
        if (y + height > work.Bottom)
            y = work.Bottom - height;
        return (x, Math.Max(work.Top, y));
    }
}
