using DeskWidgets.Pixel;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// The icons of the Big Folder in the Pixel theme, as 16 × 16 canvases: a designed sprite (a file type, or a custom app tile),
/// or tier 2, a real app icon or photo shrunk onto the art grid and the Sweetie 16 palette (the Pixel design's HANDOFF, "Tier 2").
/// Real pictures come from <see cref="IconService"/> at 64 × 64; until one arrives the sprite of the item's type stands in.
/// </summary>
internal sealed class PixelFolderIcons
{
    private static readonly int[,] Bayer4 = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };

    /// <summary>As many as the icon service keeps: every picture it still holds can have its pixel version here.</summary>
    private const int MaxCached = 400;

    private readonly Action _repaint;
    private readonly Dictionary<(string Key, IconImage? Image), PixelCanvas> _cache = [];

    public PixelFolderIcons(Action repaint) => _repaint = repaint;

    /// <summary>The 16 × 16 icon for an item, drawn with the palette's fixed sprite colours.</summary>
    public PixelCanvas Icon(PixelPalette palette, FolderItem item)
    {
        var kind = item.Kind;
        if (item.CustomIcon is { } key && PixelFolderSprites.ForApp(key) is { } custom)
            return Sprite(palette, custom, "app:" + key);

        if (kind == FolderItemKind.App || (kind is FolderItemKind.Image or FolderItemKind.Video && !item.IsShortcut))
        {
            bool app = kind == FolderItemKind.App;
            var request = new IconRequest(item.IconPath, 64, app ? IconFlavor.AppIcon : IconFlavor.Thumbnail, Stamp(item), Fallback(item));
            var icons = IconService.Shared;
            if (icons.TryGet(request, out var image, _repaint))
            {
                if (image is not null)
                {
                    if (!_cache.TryGetValue((item.Path, image), out var tier2))
                        tier2 = BoundedCache.Put(_cache, (item.Path, image), app ? AppOnGrid(image.Pixels) : Photo(image.Pixels, kind == FolderItemKind.Video), MaxCached);
                    return tier2;
                }
            }
            else
            {
                icons.Request(request, _repaint);
            }
        }
        if (kind == FolderItemKind.Other && FolderKinds.OtherSymbol(item.LabelName).Length == 0)
            return Sprite(palette, PixelFolderSprites.Page, "page"); // no extension: the plain page, no tag
        var sprite = PixelFolderSprites.For(kind);
        var canvas = Sprite(palette, sprite, "kind:" + kind);
        if (kind == FolderItemKind.Other)
        {
            canvas = Copy(canvas);
            DrawExtension(canvas, FolderKinds.OtherSymbol(item.LabelName));
        }
        return canvas;
    }

    private readonly Dictionary<string, PixelCanvas> _sprites = [];

    private PixelCanvas Sprite(PixelPalette palette, PixelSprite sprite, string key)
    {
        if (_sprites.TryGetValue(key, out var cached))
            return cached;
        var canvas = new PixelCanvas(16, 16);
        canvas.Draw(sprite, 0, 0, palette.Sprite);
        return _sprites[key] = canvas;
    }

    private static PixelCanvas Copy(PixelCanvas source)
    {
        var copy = new PixelCanvas(source.Width, source.Height);
        Array.Copy(source.Pixels, copy.Pixels, source.Pixels.Length);
        return copy;
    }

    /// <summary>An Other file: up to 3 letters of the extension in the 3 × 5 font, white, centred on the dusk band.</summary>
    private static void DrawExtension(PixelCanvas icon, string symbol)
    {
        string text = symbol.Replace("…", "");
        if (text.Length == 0)
            return;
        if (text.Length > 3)
            text = text[..3];
        var layout = PixelText.Layout(text, PixelFonts.Small);
        layout.Draw(icon, 1 + (14 - layout.Width + 1) / 2, 8, 0xFFF4F4F4);
    }

    private static long Stamp(FolderItem item)
    {
        try
        {
            return File.GetLastWriteTimeUtc(item.Path).Ticks;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string? Fallback(FolderItem item)
        => item.Identity?.Aumid is { } aumid && item.IsShortcut && item.Identity.ExePath is null ? @"shell:AppsFolder\" + aumid : null;

    // ----- the copies the card needs -----

    /// <summary>The icon of a missing target: ink becomes Faint, every other colour Track.</summary>
    public static PixelCanvas Dimmed(PixelPalette palette, PixelCanvas icon)
    {
        var canvas = new PixelCanvas(16, 16);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                uint value = icon.Get(x, y);
                if (value != 0)
                    canvas.Set(x, y, value == palette.SpriteInk ? palette.Faint : palette.Track);
            }
        }
        return canvas;
    }

    /// <summary>A 7 × 7 tile for the stack: a 1-pixel ink outline round the icon's signature colour (its most common colour other than ink, white and silver; silver if none).</summary>
    public static PixelCanvas Mini(PixelPalette palette, PixelCanvas icon)
    {
        var counts = new Dictionary<uint, int>();
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                uint value = icon.Get(x, y);
                if (value is 0 || value == palette.SpriteInk || value == palette.SpriteWhite || value == palette.SpriteSilver)
                    continue;
                counts[value] = counts.GetValueOrDefault(value) + 1;
            }
        }
        uint key = counts.Count == 0 ? palette.SpriteSilver : counts.OrderByDescending(kv => kv.Value).First().Key;
        var tile = new PixelCanvas(7, 7);
        PixelFrame.Draw(tile, 0, 0, 7, 7, [1], palette.SpriteInk, key, 0, key, 0);
        return tile;
    }

    // ----- tier 2 -----

    /// <summary>
    /// An app's real icon shrunk to 14 × 14 with no dithering, with a 1-pixel ink outline round the shape (no tile behind it).
    /// </summary>
    private static PixelCanvas AppOnGrid(Pixels source)
    {
        var palette = PixelPalette.For(dark: false);
        var canvas = new PixelCanvas(16, 16);
        var icon = Shrink(source, 14, 0, palette);
        for (int y = -1; y <= 14; y++)
        {
            for (int x = -1; x <= 14; x++)
            {
                if (icon.Get(x, y) == 0 && (icon.Get(x - 1, y) != 0 || icon.Get(x + 1, y) != 0 || icon.Get(x, y - 1) != 0 || icon.Get(x, y + 1) != 0))
                    canvas.Set(x + 1, y + 1, palette.SpriteInk);
            }
        }
        Blit(icon, canvas, 1, 1);
        return canvas;
    }

    /// <summary>
    /// A photo: its centre square shrunk to 12 × 12 with 4 × 4 Bayer dithering, in a white print border inside an ink outline with the
    /// four corner pixels cut. A video adds a play mark.
    /// </summary>
    private static PixelCanvas Photo(Pixels source, bool video)
    {
        var palette = PixelPalette.For(dark: false);
        var canvas = new PixelCanvas(16, 16);
        canvas.Fill(0, 0, 16, 16, palette.SpriteInk);
        canvas.Fill(1, 1, 14, 14, palette.SpriteWhite);
        var picture = Shrink(CenterSquare(source), 12, 32, palette);
        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 12; x++)
                canvas.Set(x + 2, y + 2, picture.Get(x, y) == 0 ? palette.SpriteSilver : picture.Get(x, y));
        }
        foreach (var (x, y) in new[] { (0, 0), (15, 0), (0, 15), (15, 15) })
            canvas.Set(x, y, 0);
        if (video)
        {
            var play = new PixelSprite("##....", "#w#...", "#ww#..", "#www#.", "#ww#..", "#w#...", "##....");
            var mark = new PixelCanvas(16, 16);
            mark.Draw(play, 5, 4, palette.Sprite);
            Blit(mark, canvas, 0, 0);
        }
        return canvas;
    }

    private static Pixels CenterSquare(Pixels source)
    {
        int side = Math.Min(source.Width, source.Height);
        if (side == source.Width && side == source.Height)
            return source;
        int left = (source.Width - side) / 2, top = (source.Height - side) / 2;
        var bytes = new byte[side * side * 4];
        for (int y = 0; y < side; y++)
            Array.Copy(source.Bgra, ((top + y) * source.Width + left) * 4, bytes, y * side * 4, side * 4);
        return new Pixels(side, side, bytes);
    }

    /// <summary>
    /// Box-filters the source to <paramref name="size"/> × <paramref name="size"/> (each target pixel averages the source area it
    /// covers, fractions counted, colours weighted by alpha), keeps a pixel when its average alpha is at least one half, and maps it
    /// to the nearest Sweetie colour (redmean distance), after a 4 × 4 Bayer offset of <paramref name="dither"/>.
    /// </summary>
    internal static PixelCanvas Shrink(Pixels source, int size, double dither, PixelPalette palette)
    {
        var canvas = new PixelCanvas(size, size);
        double scaleX = (double)source.Width / size, scaleY = (double)source.Height / size;
        for (int ty = 0; ty < size; ty++)
        {
            for (int tx = 0; tx < size; tx++)
            {
                var (b, g, r, a) = PixelScaler.Average(source, tx * scaleX, ty * scaleY, (tx + 1) * scaleX, (ty + 1) * scaleY);
                if (a < 0.5)
                    continue;
                double offset = dither * (Bayer4[ty % 4, tx % 4] / 16.0 - 0.47);
                canvas.Set(tx, ty, Nearest(r + offset, g + offset, b + offset, palette));
            }
        }
        return canvas;
    }

    private static readonly char[] Symbols = ['#', 'p', 'r', 'o', 'y', 'L', 'G', 't', 'n', 'b', 'l', 'c', 'w', 'g', 's', 'd'];

    internal static uint Nearest(double r, double g, double b, PixelPalette palette)
    {
        uint best = 0;
        double bestDistance = double.MaxValue;
        foreach (char symbol in Symbols)
        {
            uint argb = palette.Sprite(symbol);
            double pr = (argb >> 16) & 0xFF, pg = (argb >> 8) & 0xFF, pb = argb & 0xFF;
            double mean = (r + pr) / 2, dr = r - pr, dg = g - pg, db = b - pb;
            double distance = (2 + mean / 256) * dr * dr + 4 * dg * dg + (2 + (255 - mean) / 256) * db * db;
            if (distance < bestDistance)
                (bestDistance, best) = (distance, argb);
        }
        return best;
    }

    private static void Blit(PixelCanvas source, PixelCanvas target, int x, int y)
    {
        for (int sy = 0; sy < source.Height; sy++)
        {
            for (int sx = 0; sx < source.Width; sx++)
            {
                uint value = source.Get(sx, sy);
                if (value != 0)
                    target.Set(x + sx, y + sy, value);
            }
        }
    }
}
