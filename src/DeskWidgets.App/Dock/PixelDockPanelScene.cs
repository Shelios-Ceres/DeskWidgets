using DeskWidgets.Motion;
using DeskWidgets.Pixel;
using DeskWidgets.PixelUi;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>
/// A group's panel in the Pixel dock (the Pixel dock design's HANDOFF section 6), as plain arithmetic in art pixels with no window, so it can be
/// tested: columns clamp(n, 3, 4) of 40 × 46 cells with 10 of padding each side, a 14-pixel title row at y 8, at most three rows (then a
/// scrollbar), the frame's bottom 6 above the bar, centred on the group's icon and kept 4 inside the screen.
/// </summary>
internal static class PixelDockPanelLayout
{
    public const int CellWidth = 40, CellHeight = 46, Side = 10, TitleY = 8, TitleHeight = 14, GridY = 26, MaxRows = 3;

    /// <summary>The stepped pointer under the frame, pointing at the group's icon (decoration): this many rows.</summary>
    public const int Tip = 4;

    /// <summary>The frame's bottom is this far above the bar's top; the panel is kept this far inside the screen.</summary>
    public const int GapAboveBar = 6, ScreenMargin = 4;

    public const int IconSize = 24;

    public static int Columns(int count) => Math.Clamp(count, 3, 4);

    public static int TotalRows(int count) => Math.Max(1, (count + Columns(count) - 1) / Columns(count));

    public static int VisibleRows(int count) => Math.Min(MaxRows, TotalRows(count));

    public static int MaxFirstRow(int count) => TotalRows(count) - VisibleRows(count);

    /// <summary>The frame's width: 140 for up to three apps, else 180.</summary>
    public static int Width(int count) => 2 * Side + Columns(count) * CellWidth;

    /// <summary>The frame's height, without the pointer: 8 + 14 + 4 + rows × 46 + 4.</summary>
    public static int Height(int count) => TitleY + TitleHeight + 4 + VisibleRows(count) * CellHeight + 4;

    /// <summary>Member <paramref name="index"/>'s cell, or null while its row is scrolled out of sight.</summary>
    public static (int X, int Y, int W, int H)? CellRect(int count, int firstRow, int index)
    {
        int columns = Columns(count), row = index / columns - firstRow;
        if (index < 0 || index >= count || row < 0 || row >= VisibleRows(count))
            return null;
        return (Side + index % columns * CellWidth, GridY + row * CellHeight, CellWidth, CellHeight);
    }

    /// <summary>The member whose cell holds the art pixel (x, y), or -1.</summary>
    public static int CellAt(int count, int firstRow, int x, int y)
    {
        if (x < Side || y < GridY)
            return -1;
        int column = (x - Side) / CellWidth, row = (y - GridY) / CellHeight;
        if (column >= Columns(count) || row >= VisibleRows(count))
            return -1;
        int index = (row + firstRow) * Columns(count) + column;
        return index < count ? index : -1;
    }

    /// <summary>
    /// Where member <paramref name="from"/>, dragged inside the panel, would go (D7 in a grid): the cell under the art pixel (x, y), the nearest
    /// one outside the cells, taken only once the pointer is past that cell's middle on the way there (to the right going forward, to the left
    /// going back); before that the one next to it towards <paramref name="from"/>.
    /// </summary>
    public static int ReorderTarget(int count, int firstRow, int x, int y, int from)
    {
        if (count <= 0 || from < 0 || from >= count)
            return -1;
        int columns = Columns(count);
        int column = Math.Clamp(FloorDiv(x - Side, CellWidth), 0, columns - 1);
        int row = Math.Clamp(FloorDiv(y - GridY, CellHeight), 0, VisibleRows(count) - 1) + firstRow;
        int under = row * columns + column;
        if (under >= count)
            return count - 1; // past the last member: the end
        if (under == from)
            return from;
        int middle = Side + column * CellWidth + CellWidth / 2;
        if (under > from)
            return x > middle ? under : Math.Max(from, under - 1);
        return x < middle ? under : Math.Min(from, under + 1);
    }

    private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

    /// <summary>The order while a member is held over another cell: <paramref name="from"/> taken out and put back at <paramref name="to"/>.</summary>
    public static List<int> Preview(int count, int from, int to)
    {
        var order = Enumerable.Range(0, count).ToList();
        if (from < 0 || from >= count || to < 0 || to >= count || from == to)
            return order;
        order.RemoveAt(from);
        order.Insert(to, from);
        return order;
    }

    /// <summary>
    /// Where the panel's window goes, in screen pixels: its left so the frame is centred on <paramref name="iconCenterX"/> and kept
    /// <see cref="ScreenMargin"/> art pixels inside <paramref name="work"/>, its top so the frame's bottom is <see cref="GapAboveBar"/> above
    /// <paramref name="barTop"/>; and the pointer's x in the frame, in art pixels, kept clear of the frame's corners.
    /// </summary>
    public static (int X, int Y, int PointerX) Place(int iconCenterX, int barTop, DockRect work, int factor, int widthArt, int heightArt)
    {
        int width = widthArt * factor, margin = ScreenMargin * factor;
        int x = Math.Clamp(iconCenterX - width / 2, work.X + margin, Math.Max(work.X + margin, work.Right - margin - width));
        int y = barTop - GapAboveBar * factor - heightArt * factor;
        int pointer = (int)Math.Floor((iconCenterX - x) / (double)factor);
        return (x, y, Math.Clamp(pointer, Tip + 3, widthArt - Tip - 4));
    }

    /// <summary>
    /// <see cref="DockChangeKind.Grouped"/>: the index the group's target had in the old list, from the group's index in the new one and the
    /// dragged program's old index (the dragged one came out of the list, so a target after it was one further on).
    /// </summary>
    public static int GroupedOldTarget(int groupIndex, int draggedOldIndex) => groupIndex < draggedOldIndex ? groupIndex : groupIndex + 1;
}

/// <summary>What the group's panel shows now.</summary>
internal sealed record PixelDockPanelView(
    string Title,
    IReadOnlyList<DockItemView> Members,
    int FirstRow,
    int Hover,
    int Pressed,
    int Focus, // the keyboard focus: a member, -2 the Open all button, -1 none
    bool ButtonHover,
    bool ButtonPressed,
    LineEditor? Edit,
    IReadOnlyList<bool>? Lit,
    int LitHover,
    int DragFrom,
    int DragTo,
    (int X, int Y)? DragAt,
    int PointerX,
    IReadOnlyDictionary<int, (int X, int Y)>? Shown = null); // D7 in the panel: members part-way through their MOVE, at these cell corners

/// <summary>Draws the group's panel (HANDOFF section 6) from a <see cref="PixelDockPanelView"/>: what is drawn is what <see cref="PixelDockPanelLayout"/> hit-tests.</summary>
internal static class PixelDockPanelScene
{
    public static PixelText ButtonText() => FolderPixelScene.Layout(Strings.DockOpenAll, PixelFonts.Regular);

    /// <summary>The Open all button: the dialogs' secondary button, 13 tall (16 for SimSun), at the title row's right.</summary>
    public static (int X, int Y, int W, int H) ButtonRect(int count)
    {
        var text = ButtonText();
        int height = text.Rank == 2 ? 16 : 13, width = text.Width + 10;
        return (PixelDockPanelLayout.Width(count) - PixelDockPanelLayout.Side - width, PixelDockPanelLayout.TitleY + (PixelDockPanelLayout.TitleHeight - height) / 2, width, height);
    }

    /// <summary>The rename field: the title row from x 6 to 4 before the button.</summary>
    public static (int X, int Y, int W, int H) FieldRect(int count)
    {
        var button = ButtonRect(count);
        return (6, PixelDockPanelLayout.TitleY, button.X - 4 - 6, PixelDockPanelLayout.TitleHeight);
    }

    /// <summary>The whole window's picture: the frame with its pointer and hard shadow, (width + 2) × (height + tip + 2).</summary>
    public static PixelCanvas Draw(PixelPalette p, PixelDockArt art, PixelDockPanelView view)
    {
        int count = view.Members.Count;
        int width = PixelDockPanelLayout.Width(count), height = PixelDockPanelLayout.Height(count);
        var body = new PixelCanvas(width, height + PixelDockPanelLayout.Tip);
        PixelFrame.Draw(body, 0, 0, width, height, PixelFrame.DialogCorner, p.Outline, p.Panel, p.Highlight, p.Shade, 2);
        DrawPointer(body, p, view.PointerX, height);

        DrawTitle(body, p, view);
        var order = view.DragFrom >= 0 && view.DragTo >= 0
            ? PixelDockPanelLayout.Preview(count, view.DragFrom, view.DragTo)
            : Enumerable.Range(0, count).ToList();
        for (int slot = 0; slot < count; slot++)
        {
            if (PixelDockPanelLayout.CellRect(count, view.FirstRow, slot) is not { } cell)
                continue;
            int member = order[slot];
            if (member == view.DragFrom)
            {
                // The dragged member's place (its old cell, then where it would go): a dotted box in Faint, as on the bar (D7).
                PixelDockPainter.DottedBox(body, cell.X + (cell.W - PixelDockPanelLayout.IconSize) / 2, cell.Y + 3, PixelDockPanelLayout.IconSize, PixelDockPanelLayout.IconSize, p.Faint);
                continue;
            }
            if (view.Shown is { } shown && shown.TryGetValue(member, out var moving))
                cell = (moving.X, moving.Y, cell.W, cell.H);
            DrawCell(body, p, art, view, member, cell);
        }
        if (PixelDockPanelLayout.TotalRows(count) > PixelDockPanelLayout.VisibleRows(count))
            DrawScrollbar(body, p, view, width, count);
        if (view.DragFrom >= 0 && view.DragFrom < count && view.DragAt is { } at)
        {
            // The dragged icon rides with the pointer, undimmed, kept inside the frame.
            int size = PixelDockPanelLayout.IconSize;
            int x = Math.Clamp(at.X - size / 2, 2, width - size - 2), y = Math.Clamp(at.Y - size / 2, 2, height - size - 3);
            FolderPixelScene.Blit(art.Icon(p, view.Members[view.DragFrom], size), body, x, y);
        }
        return PixelFrame.WithShadow(body, p.Shadow, DialogLayout.ShadowOffset);
    }

    /// <summary>GROW and SHRINK (D9): the empty frame at a share of its height, standing on the frame's bottom, at full width; no pointer, no content.</summary>
    public static PixelCanvas EmptyFrame(PixelPalette p, int count, double share)
    {
        int width = PixelDockPanelLayout.Width(count), height = PixelDockPanelLayout.Height(count);
        var empty = PixelMotion.EmptyPanel(p, width, height, PixelFrame.DialogCorner, DialogLayout.ShadowOffset, GrowFrom.Bottom, share);
        var full = new PixelCanvas(width + DialogLayout.ShadowOffset, height + PixelDockPanelLayout.Tip + DialogLayout.ShadowOffset);
        FolderPixelScene.Blit(empty, full, 0, 0);
        return full;
    }

    /// <summary>The stepped pointer: four rows from the frame's bottom outline, the outline stepping in by 1 a row, a Shade fill.</summary>
    private static void DrawPointer(PixelCanvas body, PixelPalette p, int x, int height)
    {
        for (int j = 0; j < PixelDockPanelLayout.Tip; j++)
        {
            int half = PixelDockPanelLayout.Tip - j;
            for (int i = -half; i <= half; i++)
                body.Set(x + i, height - 1 + j, Math.Abs(i) == half || j == PixelDockPanelLayout.Tip - 1 ? p.Outline : p.Shade);
        }
    }

    private static void DrawTitle(PixelCanvas canvas, PixelPalette p, PixelDockPanelView view)
    {
        int count = view.Members.Count;
        var button = ButtonRect(count);
        // Files over the panel (HANDOFF 6): Open all is hidden (a drag cannot click it), so the row is the panel's width less 10 each side.
        bool files = view.Lit is not null;
        int room = files ? PixelDockPanelLayout.Width(count) - 2 * PixelDockPanelLayout.Side : button.X - 4 - PixelDockPanelLayout.Side;
        if (view.Edit is { } editor)
        {
            var field = FieldRect(count);
            FolderPixelPanelScene.DrawEditField(canvas, p, field.X, field.Y, field.W, field.H, editor, PixelFonts.Regular);
        }
        else if (view.Lit is { } lit && view.LitHover >= 0 && view.LitHover < lit.Count && lit[view.LitHover] && view.LitHover < count)
        {
            // Over a member that takes them: the bar's Select label plate in place of the group's name, at x 10, middle-shortened only if
            // the whole row is still too narrow.
            var plate = PixelDockPainter.BuildLabelPlate(p, Strings.DockOpenWith(view.Members[view.LitHover].Name), select: true, room);
            int plateHeight = plate.Height - PixelDockPainter.ShadowOffset;
            FolderPixelScene.Blit(plate, canvas, PixelDockPanelLayout.Side, PixelDockPanelLayout.TitleY + (PixelDockPanelLayout.TitleHeight - plateHeight) / 2);
        }
        else
        {
            var title = FolderPixelScene.OneLine(view.Title, PixelFonts.Regular, room);
            title.Draw(canvas, PixelDockPanelLayout.Side, PixelDockPanelLayout.TitleY + (PixelDockPanelLayout.TitleHeight - title.Height) / 2, p.Ink);
        }
        if (files)
            return;
        var look = view.ButtonPressed ? ButtonLook.Pressed : view.ButtonHover ? ButtonLook.Hover : ButtonLook.Normal;
        DialogPainter.Button(canvas, p, button.X, button.Y, button.W, button.H, ButtonText(), false, look, view.Focus == -2);
    }

    /// <summary>
    /// One member: a 24 icon at y + 3 (the halo shows while it runs), the name below on up to two lines by the Big Folder's breaks. Hover: an
    /// OpenFill plate, the icon 1 px up; pressed: Shade, 1 down; lit for files (D12): Select over the cell, the one under the files 1 px up (its "Open with" is in the title row).
    /// </summary>
    private static void DrawCell(PixelCanvas canvas, PixelPalette p, PixelDockArt art, PixelDockPanelView view, int index, (int X, int Y, int W, int H) cell)
    {
        var member = view.Members[index];
        bool lit = view.Lit is { } l && index < l.Count && l[index];
        bool litHover = lit && view.LitHover == index;
        bool pressed = !lit && view.Pressed == index, hover = !lit && !pressed && view.Hover == index && view.DragFrom < 0;
        int dy = pressed ? 1 : hover || litHover ? -1 : 0;
        bool running = member.State == DockRunState.Running && !member.Missing;

        var lines = NameLines(member.Name); // a lit cell keeps its own name: "Open with" is in the title row
        int ix = cell.X + (cell.W - PixelDockPanelLayout.IconSize) / 2, iy = cell.Y + 3 + dy;
        int lineY = iy + PixelDockPanelLayout.IconSize + (lines[0].Rank == 0 ? 2 : 1);
        int textBottom = lineY + lines.Sum(LinePitch) - 1;
        int plateHeight = Math.Clamp(textBottom + 2 - dy - cell.Y, 38, cell.H);
        if (lit || pressed || hover)
            Plate(canvas, cell.X + 1, cell.Y, cell.W - 2, plateHeight, lit ? p.Select : pressed ? p.Shade : p.OpenFill);
        if (view.Focus == index)
            PixelFrame.FocusRing(canvas, cell.X + 1, cell.Y, cell.W - 2, plateHeight, p.Ink);

        if (running && !lit)
            FolderPixelScene.Blit(art.Running(p, member, PixelDockPanelLayout.IconSize), canvas, ix - 2, iy - 2);
        else
            FolderPixelScene.Blit(art.Icon(p, member, PixelDockPanelLayout.IconSize, awake: running), canvas, ix, iy);
        uint color = lit ? p.OnSelect : member.Missing ? p.Faint : p.Ink;
        foreach (var line in lines)
        {
            line.Draw(canvas, cell.X + (cell.W - line.Width) / 2, lineY, color);
            lineY += LinePitch(line);
        }
    }

    /// <summary>The name on up to two lines (SimSun: one), all in the font chosen from the whole name, broken by the Big Folder's rule.</summary>
    public static List<PixelText> NameLines(string name)
    {
        string encoded = PixelEmoji.Encode(name);
        var font = FolderPixelScene.FontFor(encoded, PixelFonts.Small);
        int room = PixelDockPanelLayout.CellWidth - 2;
        if (font == PixelFonts.System)
            return [PixelText.Layout(NameShortener.OneLine(encoded, room, FolderPixelScene.Measure(font)), font)];
        var (first, second) = NameShortener.TwoLines(encoded, room, FolderPixelScene.Measure(font));
        return second.Length == 0 ? [PixelText.Layout(first, font)] : [PixelText.Layout(first, font), PixelText.Layout(second, font)];
    }

    /// <summary>Each font keeps its own line pitch: 5 + 2 for the pixel font, 9 + 1 for Fusion, 12 + 1 for SimSun.</summary>
    private static int LinePitch(PixelText line) => line.Rank switch { 2 => 13, 1 => 10, _ => 7 };

    /// <summary>A cell's plate, corners [2, 1].</summary>
    private static void Plate(PixelCanvas canvas, int x, int y, int width, int height, uint color)
    {
        for (int j = 0; j < height; j++)
        {
            int edge = Math.Min(j, height - 1 - j);
            int inset = edge == 0 ? 2 : edge == 1 ? 1 : 0;
            canvas.Fill(x + inset, y + j, width - 2 * inset, 1, color);
        }
    }

    /// <summary>The Big Folder panel's 3-pixel scrollbar, in the right padding.</summary>
    private static void DrawScrollbar(PixelCanvas canvas, PixelPalette p, PixelDockPanelView view, int width, int count)
    {
        int rows = PixelDockPanelLayout.VisibleRows(count), total = PixelDockPanelLayout.TotalRows(count);
        int trackX = width - 8, trackY = PixelDockPanelLayout.GridY, trackHeight = rows * PixelDockPanelLayout.CellHeight - 2;
        canvas.Fill(trackX, trackY, 3, trackHeight, p.Well);
        int thumbHeight = Math.Max(8, trackHeight * rows / total);
        int thumbY = trackY + (trackHeight - thumbHeight) * view.FirstRow / Math.Max(1, total - rows);
        FolderPixelScene.FillRound(canvas, trackX, thumbY, 3, thumbHeight, p.Soft);
    }
}
