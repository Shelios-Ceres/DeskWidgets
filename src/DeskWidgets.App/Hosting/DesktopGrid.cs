using DeskWidgets.Interop;
using Windows.Graphics;

namespace DeskWidgets.Hosting;

/// <summary>The gaps of the right-aligned icon grid kept by <c>IconArranger</c>, in physical pixels.</summary>
internal readonly record struct GridAnchor(int Right, int Top);

/// <summary>
/// The desktop icon grid: cells the size of Explorer's icon spacing. Normally
/// laid out from the work area's top-left like Explorer's own icons; while the
/// icons are arranged from the right (<see cref="GridAnchor"/>), the cells end
/// at the right margin instead, so widgets share the icons' columns. A widget
/// snaps to the whole cells it covers and sits centred in them, so it lines up
/// with the icon rows and columns around it. All values are physical pixels.
/// </summary>
internal readonly record struct DesktopGrid(Win32.RECT Area, int CellWidth, int CellHeight)
{
    public static DesktopGrid At(Win32.POINT point, GridAnchor? anchor = null)
    {
        var area = Win32.GetWorkArea(point);
        var (width, height) = Win32.GetDesktopIconSpacing() ?? DefaultSpacing(point);
        return Create(area, width, height, anchor);
    }

    /// <summary>The grid over <paramref name="workArea"/>; with an anchor, its cells end at the icons' right margin.</summary>
    public static DesktopGrid Create(Win32.RECT workArea, int cellWidth, int cellHeight, GridAnchor? anchor = null)
    {
        var area = workArea;
        if (anchor is { } margins)
        {
            int right = area.Right - margins.Right;
            area = new Win32.RECT
            {
                Left = area.Left + Math.Max(0, right - area.Left) % cellWidth,
                Top = area.Top + margins.Top,
                Right = right,
                Bottom = area.Bottom,
            };
        }
        return new DesktopGrid(area, cellWidth, cellHeight);
    }

    /// <summary>Snapped top-left for a card of the given size, from the block of cells nearest to it.</summary>
    public PointInt32 Snap(int left, int top, int width, int height)
    {
        var (cols, rows, insetX, insetY) = Block(width, height);
        int col = Math.Clamp((int)Math.Round((left - insetX - Area.Left) / (double)CellWidth), 0, MaxColumn(cols));
        int row = Math.Clamp((int)Math.Round((top - insetY - Area.Top) / (double)CellHeight), 0, MaxRow(rows));
        return Position(col, row, insetX, insetY);
    }

    /// <summary>Every snapped position for a card, column by column from the right (or the left), each top to bottom.</summary>
    public IEnumerable<PointInt32> Slots(int width, int height, bool fromLeft = false)
    {
        var (cols, rows, insetX, insetY) = Block(width, height);
        int last = MaxColumn(cols);
        for (int i = 0; i <= last; i++)
        {
            int col = fromLeft ? i : last - i;
            for (int row = 0; row <= MaxRow(rows); row++)
                yield return Position(col, row, insetX, insetY);
        }
    }

    // No desktop list view (e.g. Explorer restarting): Windows' default medium-icon spacing.
    private static (int Width, int Height) DefaultSpacing(Win32.POINT point)
    {
        double scale = Win32.GetScaleAt(point);
        return ((int)Math.Round(77 * scale), (int)Math.Round(100 * scale));
    }

    private (int Cols, int Rows, int InsetX, int InsetY) Block(int width, int height)
    {
        int cols = Math.Max(1, (int)Math.Ceiling(width / (double)CellWidth));
        int rows = Math.Max(1, (int)Math.Ceiling(height / (double)CellHeight));
        return (cols, rows, (cols * CellWidth - width) / 2, (rows * CellHeight - height) / 2);
    }

    private int MaxColumn(int cols) => Math.Max(0, (Area.Width - cols * CellWidth) / CellWidth);

    private int MaxRow(int rows) => Math.Max(0, (Area.Height - rows * CellHeight) / CellHeight);

    private PointInt32 Position(int col, int row, int insetX, int insetY)
        => new(Area.Left + col * CellWidth + insetX, Area.Top + row * CellHeight + insetY);
}
