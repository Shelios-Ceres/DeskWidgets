namespace DeskWidgets.Widgets.Folder;

/// <summary>Area-averaging resize of the Shell's pictures (box filter, fractional coverage counted).</summary>
internal static class PixelScaler
{
    /// <summary>
    /// Scales straight-alpha BGRA to <paramref name="width"/> × <paramref name="height"/>, returning premultiplied BGRA bytes
    /// (what a WriteableBitmap wants). Colours are averaged weighted by alpha, so a transparent edge does not darken the icon.
    /// </summary>
    public static byte[] ToPremultiplied(Pixels source, int width, int height) => ToPremultiplied(source, width, height, 0, 0, source.Width, source.Height);

    /// <summary>A square of side <paramref name="size"/>, cover-cropped: the shorter side fills it and the longer is cut evenly at both ends.</summary>
    public static byte[] CoverToPremultiplied(Pixels source, int size)
    {
        double side = Math.Min(source.Width, source.Height);
        return ToPremultiplied(source, size, size, (source.Width - side) / 2, (source.Height - side) / 2, side, side);
    }

    private static byte[] ToPremultiplied(Pixels source, int width, int height, double cropX, double cropY, double cropWidth, double cropHeight)
    {
        var result = new byte[width * height * 4];
        double scaleX = cropWidth / width, scaleY = cropHeight / height;
        for (int y = 0; y < height; y++)
        {
            double top = cropY + y * scaleY, bottom = top + scaleY;
            for (int x = 0; x < width; x++)
            {
                double left = cropX + x * scaleX, right = left + scaleX;
                var (b, g, r, a) = Average(source, left, top, right, bottom);
                int o = (y * width + x) * 4;
                // Premultiply: the averaged colour is straight, the alpha is the coverage-weighted alpha.
                result[o] = (byte)Math.Round(b * a);
                result[o + 1] = (byte)Math.Round(g * a);
                result[o + 2] = (byte)Math.Round(r * a);
                result[o + 3] = (byte)Math.Round(a * 255);
            }
        }
        return result;
    }

    /// <summary>The average colour (straight, 0..255) and alpha (0..1) of a rectangle of the source, partial pixels weighted by coverage.</summary>
    public static (double B, double G, double R, double A) Average(Pixels source, double left, double top, double right, double bottom)
    {
        int x0 = (int)Math.Floor(left), x1 = Math.Min(source.Width, (int)Math.Ceiling(right));
        int y0 = (int)Math.Floor(top), y1 = Math.Min(source.Height, (int)Math.Ceiling(bottom));
        double weightSum = 0, alphaSum = 0, b = 0, g = 0, r = 0;
        for (int y = Math.Max(0, y0); y < y1; y++)
        {
            double wy = Math.Min(bottom, y + 1) - Math.Max(top, y);
            for (int x = Math.Max(0, x0); x < x1; x++)
            {
                double w = wy * (Math.Min(right, x + 1) - Math.Max(left, x));
                if (w <= 0)
                    continue;
                int i = (y * source.Width + x) * 4;
                double a = source.Bgra[i + 3] / 255.0;
                weightSum += w;
                alphaSum += w * a;
                b += w * a * source.Bgra[i];
                g += w * a * source.Bgra[i + 1];
                r += w * a * source.Bgra[i + 2];
            }
        }
        if (weightSum <= 0 || alphaSum <= 0)
            return (0, 0, 0, 0);
        return (b / alphaSum, g / alphaSum, r / alphaSum, alphaSum / weightSum);
    }
}
