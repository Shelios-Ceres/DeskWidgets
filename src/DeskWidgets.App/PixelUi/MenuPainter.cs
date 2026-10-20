using DeskWidgets.Pixel;

namespace DeskWidgets.PixelUi;

/// <summary>The geometry of a pixel menu panel in art pixels (1 art pixel = 2 DIP): sizes from its rows, no drawing.</summary>
internal sealed class MenuLayout
{
    /// <summary>Row height of a panel whose labels are all in the pixel font.</summary>
    public const int RowLatin = 15;

    /// <summary>Row height of a panel in which some label needs the SimSun fallback.</summary>
    public const int RowFallback = 18;

    public const int SeparatorHeight = 7;
    public const int ScrollHeight = 9;
    public const int Top = 3;      // outline, highlight, gap
    public const int Bottom = 4;   // gap, two shade rows, outline
    public const int BarInset = 2; // a row's fill runs from x = 2 to W - 3
    public const int LeadX = 4;    // the mark or icon column, 7 wide
    public const int LabelX = 14;
    public const int ArrowWidth = 3;
    public const int ArrowPad = 5;
    public const int PlainPad = 8;
    public const int MinWidth = 64;

    /// <summary>The hard shadow's offset (down and right), in art pixels.</summary>
    public const int ShadowOffset = 2;

    private MenuLayout(IReadOnlyList<MenuEntry> items, PixelText?[] labels, int rowHeight, int width)
    {
        Items = items;
        Labels = labels;
        RowHeight = rowHeight;
        Width = width;
        Heights = items.Select(i => i.Kind == MenuKind.Separator ? SeparatorHeight : rowHeight).ToArray();
    }

    public IReadOnlyList<MenuEntry> Items { get; }

    public PixelText?[] Labels { get; }

    public int RowHeight { get; }

    /// <summary>Panel width, outline to outline.</summary>
    public int Width { get; }

    public int[] Heights { get; }

    /// <summary>Panel height with every row shown.</summary>
    public int ContentHeight => Top + Heights.Sum() + Bottom;

    public static MenuLayout Create(IReadOnlyList<MenuEntry> items)
    {
        var labels = items.Select(i => i.Kind == MenuKind.Separator ? null : PixelText.Layout(i.Text, PixelFonts.Regular)).ToArray();
        bool fallback = labels.Any(t => t?.IsFallback == true);
        bool anySubmenu = items.Any(i => i.Kind == MenuKind.Submenu);
        int widest = labels.Max(t => t?.Width ?? 0);
        int width = Math.Max(MinWidth, LabelX + widest + (anySubmenu ? 8 + ArrowWidth + ArrowPad : PlainPad));
        return new MenuLayout(items, labels, fallback ? RowFallback : RowLatin, width);
    }

    /// <summary>
    /// The panel height to use on a screen that has <paramref name="maxHeight"/> art pixels to give it, and whether the
    /// rows then scroll. A panel that is too tall is cut to whole rows and gets a scroll row at each end.
    /// </summary>
    public (int Height, bool Scrollable) Plan(int maxHeight)
    {
        if (ContentHeight <= maxHeight)
            return (ContentHeight, false);
        int available = maxHeight - Top - Bottom - 2 * ScrollHeight;
        int used = 0;
        for (int i = 0; i < Items.Count && used + Heights[i] <= available; i++)
            used += Heights[i];
        return (Top + ScrollHeight + used + ScrollHeight + Bottom, true);
    }

    /// <summary>The rows shown from <paramref name="firstRow"/> in a panel of <paramref name="panelHeight"/>: (first, last), whole rows only.</summary>
    public (int First, int Last) Visible(int firstRow, int panelHeight, bool scrollable)
    {
        if (!scrollable)
            return (0, Items.Count - 1);
        int available = panelHeight - Top - Bottom - 2 * ScrollHeight;
        int used = 0;
        int last = firstRow - 1;
        for (int i = firstRow; i < Items.Count && used + Heights[i] <= available; i++)
        {
            used += Heights[i];
            last = i;
        }
        return (firstRow, last);
    }

    /// <summary>The largest first row that still fills the panel (no empty space below the last row).</summary>
    public int MaxFirstRow(int panelHeight)
    {
        int available = panelHeight - Top - Bottom - 2 * ScrollHeight;
        int used = 0;
        int first = Items.Count;
        while (first > 0 && used + Heights[first - 1] <= available)
            used += Heights[--first];
        return first;
    }

    /// <summary>The first row to show so that <paramref name="index"/> is visible, moving <paramref name="firstRow"/> as little as possible.</summary>
    public int FirstRowFor(int index, int firstRow, int panelHeight)
    {
        if (index < firstRow)
            return index;
        while (firstRow < index && Visible(firstRow, panelHeight, true).Last < index)
            firstRow++;
        return firstRow;
    }
}

/// <summary>How a row is drawn: no fill, the current row, a pressed row, or the parent of a submenu that has the focus.</summary>
internal enum RowLook
{
    Normal,
    Hover,
    Pressed,
    Open,
}

/// <summary>
/// What a panel is showing: scroll position and which row is current, pressed, or has a submenu with the focus.
/// <paramref name="Override"/> (previews only) sets any row's look directly.
/// </summary>
internal readonly record struct MenuPaint(int PanelHeight, bool Scrollable, int FirstRow, int Selected, int Pressed, bool ChildFocused, Func<int, RowLook?>? Override = null);

/// <summary>Where a drawn row ended up (art pixels inside the panel).</summary>
internal readonly record struct MenuRowRect(int Index, int Y, int Height);

/// <summary>A drawn panel, its visible rows, and the y of its scroll rows (-1 when it does not scroll).</summary>
internal sealed record MenuDrawing(PixelCanvas Panel, List<MenuRowRect> Rows, int UpY, int DownY);

internal static class MenuPainter
{
    public static MenuDrawing Draw(PixelPalette p, MenuLayout layout, MenuPaint state)
    {
        int width = layout.Width, height = state.PanelHeight;
        var canvas = new PixelCanvas(width, height);
        PixelFrame.Draw(canvas, 0, 0, width, height, PixelFrame.MenuCorner, p.Outline, p.Panel, p.Highlight, p.Shade, 2);

        var (first, last) = layout.Visible(state.FirstRow, height, state.Scrollable);
        var rows = new List<MenuRowRect>();
        int y = MenuLayout.Top;
        int upY = -1, downY = -1;
        if (state.Scrollable)
        {
            upY = y;
            if (first > 0)
            {
                canvas.Draw(PixelIcons.Up, (width - 5) / 2, y + 3, _ => p.Soft);
                canvas.DottedLine(MenuLayout.LeadX, y + MenuLayout.ScrollHeight - 1, width - 2 * MenuLayout.LeadX, p.Faint);
            }
            y += MenuLayout.ScrollHeight;
        }
        for (int i = first; i <= last; i++)
        {
            int h = layout.Heights[i];
            rows.Add(new MenuRowRect(i, y, h));
            if (layout.Items[i].Kind == MenuKind.Separator)
                canvas.DottedLine(MenuLayout.LeadX, y + 3, width - 2 * MenuLayout.LeadX, p.Faint);
            else
                DrawRow(canvas, p, layout, i, y, h, state);
            y += h;
        }
        if (state.Scrollable)
        {
            downY = height - MenuLayout.Bottom - MenuLayout.ScrollHeight;
            if (last < layout.Items.Count - 1)
            {
                canvas.DottedLine(MenuLayout.LeadX, downY, width - 2 * MenuLayout.LeadX, p.Faint);
                canvas.Draw(PixelIcons.Down, (width - 5) / 2, downY + 3, _ => p.Soft);
            }
        }
        return new MenuDrawing(canvas, rows, upY, downY);
    }

    private static void DrawRow(PixelCanvas canvas, PixelPalette p, MenuLayout layout, int index, int y, int h, MenuPaint state)
    {
        var entry = layout.Items[index];
        var label = layout.Labels[index]!;
        RowLook look = state.Override?.Invoke(index)
            ?? (!entry.Focusable ? RowLook.Normal
                : index == state.Pressed ? RowLook.Pressed
                : index == state.Selected ? (state.ChildFocused && entry.Kind == MenuKind.Submenu ? RowLook.Open : RowLook.Hover)
                : RowLook.Normal);
        uint fill = look switch { RowLook.Hover => p.Select, RowLook.Pressed => p.SelectPressed, RowLook.Open => p.OpenFill, _ => 0u };
        bool onSelect = fill == p.Select || fill == p.SelectPressed;
        if (fill != 0)
            canvas.Fill(MenuLayout.BarInset, y, layout.Width - 2 * MenuLayout.BarInset, h, fill);

        uint text = onSelect ? p.OnSelect : entry.Kind == MenuKind.Status ? p.Soft : !entry.Enabled ? p.Faint : p.Ink;
        uint icon = onSelect ? p.OnSelect : !entry.Enabled ? p.Faint : p.Soft;

        if (entry.Kind == MenuKind.Check && entry.Checked)
            canvas.Draw(PixelIcons.Check, MenuLayout.LeadX, y + (h - 6) / 2, _ => text);
        else if (entry.Kind == MenuKind.Radio && entry.Checked)
            canvas.Draw(PixelIcons.Radio, MenuLayout.LeadX + 1, y + (h - 5) / 2, _ => text);
        else if (entry.Icon is { } sprite)
            canvas.Draw(sprite, MenuLayout.LeadX, y + (h - 7) / 2, _ => icon);

        label.Draw(canvas, MenuLayout.LabelX, y + (h - label.Height) / 2, text);
        if (entry.Kind == MenuKind.Submenu)
            canvas.Draw(PixelIcons.Arrow, layout.Width - MenuLayout.ArrowPad - MenuLayout.ArrowWidth, y + (h - 5) / 2, _ => fill == 0 ? p.Soft : text);
    }
}
