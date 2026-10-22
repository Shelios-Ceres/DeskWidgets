using DeskWidgets.Widgets;

namespace DeskWidgets.Pixel;

/// <summary>
/// The pixel theme's card: its size in art pixels, how art pixels map to
/// screen pixels, and the panel every widget sits on.
/// </summary>
internal static class PixelCard
{
    /// <summary>Insets of the top rows (mirrored at the bottom): the stair that rounds each corner.</summary>
    private static readonly int[] Corner = [4, 2, 1, 1];

    /// <summary>Card size in art pixels: the glass card's DIPs halved, so cards line up across themes.</summary>
    public static (int Width, int Height) ArtSize(WidgetSize size) => size switch
    {
        WidgetSize.Small => (82, 82),
        WidgetSize.Medium => (172, 82),
        _ => (172, 182),
    };

    /// <summary>
    /// Screen pixels per art pixel: two DIPs' worth, rounded to a whole number
    /// so art pixels stay square (2 at 100 %, 3 at 150 %, 4 at 200 %).
    /// </summary>
    public static int Factor(double scale) => Math.Max(1, (int)Math.Floor(scale * 2 + 0.5));

    /// <summary>
    /// The panel: an outline with stepped round corners, a one-pixel highlight
    /// along the top and a two-pixel shaded lip along the bottom.
    /// </summary>
    public static void Draw(PixelCanvas canvas, PixelPalette palette)
    {
        int width = canvas.Width, height = canvas.Height;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!Inside(x, y, width, height))
                    continue;
                uint color = Edge(x, y, width, height) ? palette.Outline
                    : Edge(x, y - 1, width, height) ? palette.Highlight
                    : Edge(x, y + 1, width, height) || Edge(x, y + 2, width, height) ? palette.Shade
                    : palette.Panel;
                canvas.Set(x, y, color);
            }
        }
    }

    private static bool Inside(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return false;
        int fromEdge = Math.Min(y, height - 1 - y);
        int inset = fromEdge < Corner.Length ? Corner[fromEdge] : 0;
        return x >= inset && x < width - inset;
    }

    /// <summary>An inside pixel with a side neighbour outside: the outline.</summary>
    private static bool Edge(int x, int y, int width, int height)
        => Inside(x, y, width, height)
            && (!Inside(x - 1, y, width, height) || !Inside(x + 1, y, width, height) || !Inside(x, y - 1, width, height) || !Inside(x, y + 1, width, height));
}
