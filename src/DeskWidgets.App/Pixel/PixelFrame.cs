namespace DeskWidgets.Pixel;

/// <summary>
/// The pixel panel (<see cref="PixelCard"/>'s outline, highlight and shaded lip) on any
/// rectangle and corner stair, plus the pieces menus and dialogs share.
/// </summary>
internal static class PixelFrame
{
    /// <summary>Corner stair of a menu panel.</summary>
    public static readonly int[] MenuCorner = [2, 1];

    /// <summary>Corner stair of a dialog panel: the card's own.</summary>
    public static readonly int[] DialogCorner = [4, 2, 1, 1];

    /// <summary>Corner stair of a button.</summary>
    public static readonly int[] ButtonCorner = [1];

    /// <summary>
    /// Draws a panel at (x0, y0): a 1-pixel outline, the fill, a 1-pixel highlight below the top outline
    /// (0 for none) and <paramref name="shadeRows"/> rows of shade above the bottom outline.
    /// </summary>
    public static void Draw(PixelCanvas canvas, int x0, int y0, int width, int height, int[] corner, uint outline, uint fill, uint highlight, uint shade, int shadeRows)
    {
        bool Inside(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
                return false;
            int fromEdge = Math.Min(y, height - 1 - y);
            int inset = fromEdge < corner.Length ? corner[fromEdge] : 0;
            return x >= inset && x < width - inset;
        }
        bool Edge(int x, int y) => Inside(x, y) && (!Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!Inside(x, y))
                    continue;
                uint color = Edge(x, y) ? outline
                    : highlight != 0 && Edge(x, y - 1) ? highlight
                    : (shadeRows >= 1 && Edge(x, y + 1)) || (shadeRows >= 2 && Edge(x, y + 2)) ? shade
                    : fill;
                canvas.Set(x0 + x, y0 + y, color);
            }
        }
    }

    /// <summary>A dotted 1-pixel ring around a rectangle, one pixel outside it, square corners skipped.</summary>
    public static void FocusRing(PixelCanvas canvas, int x, int y, int width, int height, uint color)
    {
        int left = x - 2, top = y - 2, right = x + width + 1, bottom = y + height + 1;
        for (int i = left + 1; i < right; i++)
        {
            if ((i - left) % 2 == 1)
            {
                canvas.Set(i, top, color);
                canvas.Set(i, bottom, color);
            }
        }
        for (int j = top + 1; j < bottom; j++)
        {
            if ((j - top) % 2 == 1)
            {
                canvas.Set(left, j, color);
                canvas.Set(right, j, color);
            }
        }
    }

    /// <summary>
    /// Wraps a drawn panel in a transparent margin and puts a hard shadow under it: the panel's own shape,
    /// <paramref name="offset"/> pixels down and to the right, in <paramref name="shadow"/> (an alpha colour).
    /// </summary>
    public static PixelCanvas WithShadow(PixelCanvas panel, uint shadow, int offset = 2)
    {
        var result = new PixelCanvas(panel.Width + offset, panel.Height + offset);
        for (int y = 0; y < panel.Height; y++)
        {
            for (int x = 0; x < panel.Width; x++)
            {
                if (panel.Get(x, y) != 0)
                    result.Set(x + offset, y + offset, shadow);
            }
        }
        for (int y = 0; y < panel.Height; y++)
        {
            for (int x = 0; x < panel.Width; x++)
            {
                uint color = panel.Get(x, y);
                if (color != 0)
                    result.Set(x, y, color);
            }
        }
        return result;
    }
}
