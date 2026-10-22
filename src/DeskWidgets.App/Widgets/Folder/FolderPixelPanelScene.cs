using DeskWidgets.Motion;
using DeskWidgets.Pixel;
using DeskWidgets.PixelUi;

namespace DeskWidgets.Widgets.Folder;

internal enum PanelHitKind
{
    None,
    Cell,
    Sort,
    Search,
    Title,
}

/// <summary>What the pointer is over in the Pixel panel: a cell (index into the shown list), the sort button, the search field or the title.</summary>
internal readonly record struct PanelHit(PanelHitKind Kind, int Index = 0)
{
    public static readonly PanelHit None = new(PanelHitKind.None);
}

internal enum PanelEditTarget
{
    None,
    Folder,
    Item,
}

/// <summary>A field being edited in place: the folder's name (title row) or an item's name (its cell).</summary>
internal sealed record PanelEdit(PanelEditTarget Target, LineEditor Editor, int ItemIndex);

/// <summary>Everything the Pixel panel shows at one moment.</summary>
internal sealed record FolderPanelView(
    string Name,
    string Path,
    IReadOnlyList<FolderItem> Items,
    string Search,
    FolderSort Sort,
    int Total,
    PanelSelection Selection,
    int Hover,
    int Focus,
    int FirstRow,
    bool SortHover,
    bool SortPressed,
    PanelEdit? Edit,
    PixelFolderIcons Icons,
    int Pressed = -1,
    int Flash = -1,
    ListMotion? Motion = null,
    IReadOnlySet<int>? Dimmed = null);

/// <summary>
/// The Big Folder's panel in the Pixel theme (the Pixel design's HANDOFF, "Panel"): 220 × 196 art pixels with the title row, the
/// search field and the sort button, five columns of cells with two-line names, a scrollbar and the footer. <see cref="Plan"/>
/// works out every position once per state; <see cref="Draw"/> paints it and <see cref="HitTest"/> reads the same numbers.
/// </summary>
internal static class FolderPixelPanelScene
{
    public const int Width = 220, Height = 196;
    public const int Columns = 5;
    public const int CellWidth = 36;
    public const int ColumnPitch = 41;
    public const int Left = 7;

    private static readonly PixelSprite Magnifier = new(
        ".###..",
        "#...#.",
        "#...#.",
        "#...#.",
        ".####.",
        "....##");

    private static readonly PixelSprite Chevron = new(
        "#####",
        ".###.",
        "..#..");

    internal sealed class PanelPlan
    {
        /// <summary>The metric set: 0 pixel fonts, 1 Fusion 8 px, 2 SimSun.</summary>
        public required int Rank { get; init; }
        public required PixelText Title { get; init; }
        public required PixelText Count { get; init; }
        public required PixelText SortText { get; init; }
        public required int FieldY { get; init; }
        public required int FieldHeight { get; init; }
        public required int FieldWidth { get; init; }
        public required int SortX { get; init; }
        public required int SortWidth { get; init; }
        public required int GridY { get; init; }
        public required int GridBottom { get; init; }
        public required int CellHeight { get; init; }
        public required int Pitch { get; init; }
        public required int VisibleRows { get; init; }
        public required int TotalRows { get; init; }
        public required int NameHeight { get; init; }
        public required int NameGap { get; init; }
    }

    /// <summary>
    /// The panel's metric set: that of its highest-ranked visible text (title, search, sort button or a name): 0 when the pixel fonts spell them all, 1 when Fusion 8 px has to, 2 when only SimSun can.
    /// </summary>
    public static int MetricRank(string name, string search, FolderSort sort, IReadOnlyList<FolderItem> items)
    {
        int rank = Math.Max(FolderPixelScene.Layout(name, PixelFonts.Regular).Rank, FolderPixelScene.Layout(search.Length > 0 ? search : Strings.FolderSearch, PixelFonts.Regular).Rank);
        rank = Math.Max(rank, FolderPixelScene.Layout(Strings.FolderSortButton(sort), PixelFonts.Regular).Rank);
        foreach (var item in items)
            rank = Math.Max(rank, RankOf(item.DisplayName, PixelFonts.Small));
        return rank;
    }

    private static int RankOf(string text, PixelFont latin)
    {
        var font = FolderPixelScene.FontFor(text, latin);
        return font == PixelFonts.System ? 2 : font == PixelFonts.Fusion ? 1 : 0;
    }

    public static PanelPlan Plan(FolderPanelView view, int rank)
    {
        var count = FolderPixelScene.Layout(Strings.FolderItemCount(view.Total), PixelFonts.Regular);
        var title = FolderPixelScene.OneLine(view.Name, PixelFonts.Regular, Width - 14 - count.Width - 8);
        var sortText = FolderPixelScene.Layout(Strings.FolderSortButton(view.Sort), PixelFonts.Regular);
        // Metric sets: pixel fonts (cell 30, pitch 33), Fusion (cell 36: 16, 1, 9, 1, 9; pitch 39), SimSun (cell 42, pitch 45).
        int fieldY = 20, fieldHeight = rank == 2 ? 18 : 15;
        int sortWidth = sortText.Width + 8 + 5 + 4 + 8;
        int sortX = Width - 7 - sortWidth;
        int fieldWidth = sortX - 5 - 7;
        int gridY = fieldY + fieldHeight + 6, gridBottom = Height - 24;
        int cellHeight = rank switch { 2 => 42, 1 => 36, _ => 30 }, pitch = rank switch { 2 => 45, 1 => 39, _ => 33 };
        int visibleRows = (gridBottom - gridY + (pitch - cellHeight)) / pitch;
        int totalRows = (view.Items.Count + Columns - 1) / Columns;
        return new PanelPlan
        {
            Rank = rank, Title = title, Count = count, SortText = sortText, FieldY = fieldY, FieldHeight = fieldHeight, FieldWidth = fieldWidth,
            SortX = sortX, SortWidth = sortWidth, GridY = gridY, GridBottom = gridBottom, CellHeight = cellHeight, Pitch = pitch,
            VisibleRows = visibleRows, TotalRows = totalRows, NameHeight = rank switch { 2 => 12, 1 => 9, _ => 5 }, NameGap = rank == 0 ? 2 : 1,
        };
    }

    // ----- geometry -----

    /// <summary>The cell of item <paramref name="index"/> in art pixels, or null when its row is scrolled out of sight.</summary>
    public static (int X, int Y, int W, int H)? CellRect(PanelPlan plan, int index, int firstRow)
    {
        int row = index / Columns - firstRow;
        if (index < 0 || row < 0 || row >= plan.VisibleRows)
            return null;
        int x = Left + index % Columns * ColumnPitch, y = plan.GridY + row * plan.Pitch;
        return (x - 1, y - 2, CellWidth + 2, plan.CellHeight + 3);
    }

    public static (int X, int Y, int W, int H) SortRect(PanelPlan plan) => (plan.SortX, plan.FieldY, plan.SortWidth, plan.FieldHeight);

    public static (int X, int Y, int W, int H) SearchRect(PanelPlan plan) => (7, plan.FieldY, plan.FieldWidth, plan.FieldHeight);

    public static (int X, int Y, int W, int H) TitleRect => (3, 3, Width - 6, 16);

    public static PanelHit HitTest(PanelPlan plan, int itemCount, int firstRow, int x, int y)
    {
        static bool In((int X, int Y, int W, int H) r, int px, int py) => px >= r.X && px < r.X + r.W && py >= r.Y && py < r.Y + r.H;
        if (In(SortRect(plan), x, y))
            return new PanelHit(PanelHitKind.Sort);
        if (In(SearchRect(plan), x, y))
            return new PanelHit(PanelHitKind.Search);
        if (In(TitleRect, x, y))
            return new PanelHit(PanelHitKind.Title);
        for (int row = 0; row < plan.VisibleRows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                int index = (row + firstRow) * Columns + column;
                if (index >= itemCount)
                    return PanelHit.None;
                if (CellRect(plan, index, firstRow) is { } cell && In(cell, x, y))
                    return new PanelHit(PanelHitKind.Cell, index);
            }
        }
        return PanelHit.None;
    }

    // ----- drawing -----

    /// <summary>The name in at most two lines, both in the font chosen from the whole name (Fusion or SimSun for Chinese, even when a line is only digits).</summary>
    public static List<PixelText> NameLines(string displayName)
    {
        string encoded = PixelEmoji.Encode(displayName);
        var font = FolderPixelScene.FontFor(encoded, PixelFonts.Small);
        var (first, second) = NameShortener.TwoLines(encoded, CellWidth, FolderPixelScene.Measure(font));
        return second.Length == 0 ? [PixelText.Layout(first, font)] : [PixelText.Layout(first, font), PixelText.Layout(second, font)];
    }

    public static PixelCanvas Draw(PixelPalette p, FolderPanelView view, PanelPlan plan)
    {
        var canvas = new PixelCanvas(Width, Height);
        PixelFrame.Draw(canvas, 0, 0, Width, Height, PixelFrame.DialogCorner, p.Outline, p.Panel, p.Highlight, p.Shade, 2);

        DrawTitle(canvas, p, view, plan);
        DrawField(canvas, p, view, plan);
        DrawGrid(canvas, p, view, plan);
        DrawFooter(canvas, p, view);
        return canvas;
    }

    private static void DrawTitle(PixelCanvas canvas, PixelPalette p, FolderPanelView view, PanelPlan plan)
    {
        if (view.Edit is { Target: PanelEditTarget.Folder } edit)
        {
            DrawEditField(canvas, p, 6, 4, Width - 12, 14, edit.Editor, PixelFonts.Regular);
            return;
        }
        plan.Title.Draw(canvas, 7, 5 + (12 - plan.Title.Height) / 2, p.Ink);
        plan.Count.Draw(canvas, Width - 7 - plan.Count.Width, 5 + (12 - plan.Count.Height) / 2, p.Soft);
    }

    private static void DrawField(PixelCanvas canvas, PixelPalette p, FolderPanelView view, PanelPlan plan)
    {
        int y = plan.FieldY, h = plan.FieldHeight;
        PixelFrame.Draw(canvas, 7, y, plan.FieldWidth, h, PixelFrame.ButtonCorner, p.Outline, p.Well, p.WellEdge, p.Well, 0);
        canvas.Draw(Magnifier, 12, y + (h - 6) / 2, _ => p.Soft);
        var text = FolderPixelScene.Layout(view.Search.Length > 0 ? view.Search : Strings.FolderSearch, PixelFonts.Regular);
        int room = plan.FieldWidth - 21 + 7 - 6; // the text's room: from x 21 to the field's right edge less a margin
        int shift = text.Width + 1 > room && view.Search.Length > 0 ? text.Width + 1 - room : 0;
        var inner = new PixelCanvas(Math.Max(1, room), h - 2);
        text.Draw(inner, -shift, (h - 2 - text.Height) / 2, view.Search.Length > 0 ? p.Ink : p.Soft);
        if (view.Search.Length > 0)
            inner.Fill(text.Width + 1 - shift, (h - 2 - text.Height) / 2 - 1, 1, text.Height + 2, p.Ink);
        FolderPixelScene.Blit(inner, canvas, 21, y + 1);

        var look = view.SortPressed ? ButtonLook.Pressed : view.SortHover ? ButtonLook.Hover : ButtonLook.Normal;
        DialogPainter.Button(canvas, p, plan.SortX, y, plan.SortWidth, h, PixelText.Layout("", PixelFonts.Regular), false, look, false);
        int dy = view.SortPressed ? 1 : 0;
        plan.SortText.Draw(canvas, plan.SortX + 8, y + (h - plan.SortText.Height) / 2 + dy, p.Ink);
        canvas.Draw(Chevron, plan.SortX + plan.SortWidth - 8 - 5, y + (h - 3) / 2 + dy, _ => p.Ink);
    }

    private static void DrawGrid(PixelCanvas canvas, PixelPalette p, FolderPanelView view, PanelPlan plan)
    {
        var items = view.Items;
        if (items.Count == 0 && view.Search.Length > 0)
        {
            var none = FolderPixelScene.Layout(Strings.FolderNoMatches, PixelFonts.Regular);
            none.Draw(canvas, 7, plan.GridY + 30, p.Soft, PixelAlign.Center, Width - 14);
        }
        // The cells that left stay a moment longer, at half their pixels, then a quarter (LEAVE), under the ones that came.
        if (view.Motion is { } motion && motion.Frame < ListMotion.Frames)
        {
            int minBayer = motion.Frame == 1 ? 8 : 12;
            foreach (var gone in motion.Leaving)
                PixelMotion.BlitMasked(gone.Pixels, canvas, gone.X, gone.Y, minBayer);
        }
        for (int row = 0; row < plan.VisibleRows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                int index = (row + view.FirstRow) * Columns + column;
                if (index >= items.Count)
                    break;
                DrawCell(canvas, p, view, plan, index, row, column);
            }
        }
        if (view.Edit is { Target: PanelEditTarget.Item } edit && CellRect(plan, edit.ItemIndex, view.FirstRow) is { } cell)
        {
            // The field is the cell's width (38, from the cell's left edge), under the icon; the text scrolls to keep the caret in view.
            DrawEditField(canvas, p, cell.X, cell.Y + 2 + 16 + 1, cell.W, plan.Rank == 2 ? 16 : 14, edit.Editor, PixelFonts.Small);
        }

        if (plan.TotalRows > plan.VisibleRows)
        {
            int trackX = Width - 10, trackHeight = plan.GridBottom - plan.GridY;
            canvas.Fill(trackX, plan.GridY, 3, trackHeight, p.Well);
            int thumbHeight = Math.Max(8, trackHeight * plan.VisibleRows / plan.TotalRows);
            int thumbY = plan.GridY + (trackHeight - thumbHeight) * view.FirstRow / Math.Max(1, plan.TotalRows - plan.VisibleRows);
            FolderPixelScene.FillRound(canvas, trackX, thumbY, 3, thumbHeight, p.Soft);
        }
    }

    private static void DrawCell(PixelCanvas canvas, PixelPalette p, FolderPanelView view, PanelPlan plan, int index, int row, int column)
    {
        int x = Left + column * ColumnPitch, y = plan.GridY + row * plan.Pitch;
        int lift = 0;
        if (view.Motion is { } motion)
        {
            var (effect, moveDx) = motion.Of(index);
            if (effect == CellEffect.Move)
            {
                x += motion.MoveOffset(moveDx);
            }
            else if (effect == CellEffect.Pop && motion.Frame == 1)
            {
                // POP, frame 1: the item's mini tile, centred on where its icon will be.
                FolderPixelScene.Blit(PixelFolderIcons.Mini(p, view.Icons.Icon(p, view.Items[index])), canvas, x + 15, y + 5);
                return;
            }
            else if (effect == CellEffect.Pop && motion.Frame == 2)
            {
                lift = -1; // the full cell, 1 px above its place
            }
        }
        if (view.Dimmed is { } dimmed && dimmed.Contains(index))
        {
            // A2d: a cell being dragged out is drawn at half its pixels while the drag lasts.
            var scratch = new PixelCanvas(CellWidth + 2, plan.CellHeight + 3);
            DrawCellAt(scratch, p, view, plan, index, 1, 2, lift);
            PixelMotion.BlitMasked(scratch, canvas, x - 1, y - 2, 8);
            return;
        }
        DrawCellAt(canvas, p, view, plan, index, x, y, lift);
    }

    /// <summary>A3: 1 px up on hover, 1 px down (and the Shade plate) on a press; a completed click flashes the plate once (Select, the name in OnSelect).</summary>
    private static void DrawCellAt(PixelCanvas canvas, PixelPalette p, FolderPanelView view, PanelPlan plan, int index, int x, int y, int lift)
    {
        var item = view.Items[index];
        bool selected = view.Selection.IsSelected(index);
        bool pressed = view.Pressed == index, hover = view.Hover == index, flash = view.Flash == index;
        int dy = (pressed ? 1 : hover ? -1 : 0) + lift;
        if (selected || flash)
            FolderPixelScene.FillRound(canvas, x - 1, y - 2, CellWidth + 2, plan.CellHeight + 3, p.Select);
        else if (pressed)
            FolderPixelScene.FillRound(canvas, x - 1, y - 2, CellWidth + 2, plan.CellHeight + 3, p.Shade);
        else if (hover)
            FolderPixelScene.FillRound(canvas, x - 1, y - 2, CellWidth + 2, plan.CellHeight + 3, p.OpenFill);
        if (view.Selection.KeyboardFocus && view.Focus == index)
            PixelFrame.FocusRing(canvas, x - 1, y - 2, CellWidth + 2, plan.CellHeight + 3, p.Ink);

        var icon = view.Icons.Icon(p, item);
        FolderPixelScene.Blit(item.TargetMissing ? PixelFolderIcons.Dimmed(p, icon) : icon, canvas, x + 10, y + dy);
        if (view.Edit is { Target: PanelEditTarget.Item } edit && edit.ItemIndex == index)
            return;
        int lineY = y + 16 + plan.NameGap + dy;
        uint color = selected || flash ? p.OnSelect : item.TargetMissing ? p.Faint : p.Ink;
        foreach (var line in NameLines(item.DisplayName))
        {
            line.Draw(canvas, x + (CellWidth - line.Width) / 2, lineY, color);
            // Each name keeps its own font's line pitch, even in a panel with other metrics: 5 + 2 for the pixel font, 9 + 1 for Fusion, 12 + 1 for SimSun.
            lineY += line.Rank switch { 2 => 13, 1 => 10, _ => 7 };
        }
    }

    private static void DrawFooter(PixelCanvas canvas, PixelPalette p, FolderPanelView view)
    {
        canvas.DottedLine(7, Height - 21, Width - 14, p.Faint);
        int pathWidth = Width - 14;
        if (view.Selection.Count > 0)
        {
            var selected = FolderPixelScene.Layout(Strings.FolderSelected(view.Selection.Count), PixelFonts.Small);
            selected.Draw(canvas, Width - 7 - selected.Width, Height - 18 + (12 - selected.Height) / 2, p.Ink);
            pathWidth -= selected.Width + 8;
        }
        string encodedPath = PixelEmoji.Encode(view.Path);
        var pathFont = FolderPixelScene.FontFor(encodedPath, PixelFonts.Small);
        var path = PixelText.Layout(NameShortener.Path(encodedPath, pathWidth, FolderPixelScene.Measure(pathFont)), pathFont);
        path.Draw(canvas, 7, Height - 18 + (12 - path.Height) / 2, p.Soft);
    }

    /// <summary>
    /// A text field in a rename: Well with an outline, the text scrolled so that the caret shows, a Select plate behind
    /// everything when it is all selected, and a 1-pixel caret.
    /// </summary>
    internal static void DrawEditField(PixelCanvas canvas, PixelPalette p, int x, int y, int width, int height, LineEditor editor, PixelFont latin)
    {
        PixelFrame.Draw(canvas, x, y, width, height, PixelFrame.ButtonCorner, p.Outline, p.Well, p.WellEdge, p.Well, 0);
        var text = FolderPixelScene.Layout(editor.Text, latin);
        int innerWidth = width - 6, innerHeight = height - 2;
        int caretX = editor.AllSelected || editor.Caret >= editor.Text.Length
            ? text.Width + 1
            : text.Font.Measure(text.Font.Normalize(PixelEmoji.Encode(editor.Text[..editor.Caret]))) + 1;
        int shift = Math.Max(0, caretX - innerWidth + 2);
        var inner = new PixelCanvas(innerWidth, innerHeight);
        int textY = (innerHeight - text.Height) / 2;
        uint color = p.Ink;
        if (editor.AllSelected)
        {
            inner.Fill(-shift, textY - 1, text.Width + 2, text.Height + 2, p.Select);
            color = p.OnSelect;
        }
        text.Draw(inner, -shift + (editor.AllSelected ? 1 : 0), textY, color);
        if (!editor.AllSelected)
            inner.Fill(caretX - shift, textY - 1, 1, text.Height + 2, p.Ink);
        FolderPixelScene.Blit(inner, canvas, x + 3, y + 1);
    }
}
