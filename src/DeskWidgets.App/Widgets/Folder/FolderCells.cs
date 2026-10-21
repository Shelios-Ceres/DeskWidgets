namespace DeskWidgets.Widgets.Folder;

internal enum FolderHitKind
{
    None,
    Header,
    Item,
    Stack,
}

/// <summary>What the pointer is over on a card: the header, item <see cref="Index"/> (into the shown items), or the stack.</summary>
internal readonly record struct FolderHit(FolderHitKind Kind, int Index = 0)
{
    public static readonly FolderHit None = new(FolderHitKind.None);
}

/// <summary>How many items a card of a size shows, and what the stack holds (the same in both themes: the shared facts of THEMES 2.3).</summary>
internal readonly record struct CardCells(int Columns, int Rows, int Shown, bool HasStack, int StackMore, int[] Minis)
{
    public int Cells => Columns * Rows;
}

internal static class FolderCards
{
    public const int MiniCount = 4;

    public static int Columns(WidgetSize size) => size == WidgetSize.Small ? 2 : 4;

    public static int Rows(WidgetSize size) => size == WidgetSize.Large ? 4 : 2;

    public static int CellCount(WidgetSize size) => Columns(size) * Rows(size); // 4, 8, 16

    /// <summary>
    /// Exactly as many cells as the card has (4, 8, 16) hold the items; when there are more, the last cell becomes the stack:
    /// <see cref="CardCells.StackMore"/> is K, what "+K" says, and <see cref="CardCells.Minis"/> are the next up to four items.
    /// </summary>
    public static CardCells Layout(WidgetSize size, int total)
    {
        int cells = CellCount(size);
        if (total <= cells)
            return new CardCells(Columns(size), Rows(size), total, false, 0, []);
        int shown = cells - 1;
        int more = total - shown;
        var minis = Enumerable.Range(shown, Math.Min(MiniCount, more)).ToArray();
        return new CardCells(Columns(size), Rows(size), shown, true, more, minis);
    }
}
