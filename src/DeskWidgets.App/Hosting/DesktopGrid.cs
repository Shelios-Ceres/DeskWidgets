using DeskWidgets.Interop;
using Windows.Graphics;

namespace DeskWidgets.Hosting;

/// <summary>
/// The desktop icon grid: cells the size of Explorer's icon spacing, laid out
/// from the work area's top-left like the icons themselves. A widget snaps to
/// the whole cells it covers and sits centred in them, so it lines up with the
/// icon rows and columns around it. All values are physical pixels.
/// </summary>
internal readonly record struct DesktopGrid(Win32.RECT WorkArea, int CellWidth, int CellHeight)
{
    public static DesktopGrid At(Win32.POINT point)
    {
        var work = Win32.GetWorkArea(point);
        if (Win32.GetDesktopIconSpacing() is (int width, int height))
            return new DesktopGrid(work, width, height);
        // No desktop list view (e.g. Explorer restarting): Windows' default medium-icon spacing.
        double scale = Win32.GetScaleAt(point);
        return new DesktopGrid(work, (int)Math.Round(77 * scale), (int)Math.Round(100 * scale));
    }

    /// <summary>Snapped top-left for a card of the given size, from the block of cells nearest to it.</summary>
    public PointInt32 Snap(int left, int top, int width, int height)
    {
        var (cols, rows, insetX, insetY) = Block(width, height);
        int col = Math.Clamp((int)Math.Round((left - insetX - WorkArea.Left) / (double)CellWidth), 0, MaxColumn(cols));
        int row = Math.Clamp((int)Math.Round((top - insetY - WorkArea.Top) / (double)CellHeight), 0, MaxRow(rows));
        return Position(col, row, insetX, insetY);
    }

    /// <summary>Every snapped position for a card, right-most column first, top to bottom.</summary>
    public IEnumerable<PointInt32> Slots(int width, int height)
    {
        var (cols, rows, insetX, insetY) = Block(width, height);
        for (int col = MaxColumn(cols); col >= 0; col--)
        {
            for (int row = 0; row <= MaxRow(rows); row++)
                yield return Position(col, row, insetX, insetY);
        }
    }

    private (int Cols, int Rows, int InsetX, int InsetY) Block(int width, int height)
    {
        int cols = Math.Max(1, (int)Math.Ceiling(width / (double)CellWidth));
        int rows = Math.Max(1, (int)Math.Ceiling(height / (double)CellHeight));
        return (cols, rows, (cols * CellWidth - width) / 2, (rows * CellHeight - height) / 2);
    }

    private int MaxColumn(int cols) => Math.Max(0, (WorkArea.Width - cols * CellWidth) / CellWidth);

    private int MaxRow(int rows) => Math.Max(0, (WorkArea.Height - rows * CellHeight) / CellHeight);

    private PointInt32 Position(int col, int row, int insetX, int insetY)
        => new(WorkArea.Left + col * CellWidth + insetX, WorkArea.Top + row * CellHeight + insetY);
}
