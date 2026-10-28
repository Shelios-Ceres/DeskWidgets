using System.Globalization;
using System.Text;

namespace DeskWidgets.Pixel;

/// <summary>
/// Emoji in Pixel text (the Pixel design's HANDOFF, Phase 8, "Emoji"). Each emoji, a whole grapheme cluster (ZWJ sequences, skin tones and
/// variation selectors count as one), is swapped for one private-use character before the text is laid out; the fonts that can carry
/// an emoji (Fusion 8 px, SimSun) give that character an empty cell, and the cell's tile is drawn over it. The pixel fonts never cover it,
/// so a name with an emoji is set in Fusion (or SimSun). The tile is Segoe UI Emoji rendered at 64 × 64, cropped to its ink and shrunk to
/// 7 × 7 (11 × 11 in a SimSun line) on the Sweetie colours: each tile pixel takes the most common colour of the source pixels it covers.
/// UI thread only (the cache and the registry are not locked).
/// </summary>
internal static class PixelEmoji
{
    public const char First = '';
    private const int Capacity = 0xF8FF - First + 1;

    /// <summary>Renders one emoji to straight-alpha BGRA at <see cref="SourceSize"/> squared; null when it cannot. Replaced in tests.</summary>
    internal static Func<string, byte[]?> Renderer { get; set; } = EmojiRenderer.Render;

    public const int SourceSize = 64;

    private static readonly List<string> s_clusters = [];
    private static readonly Dictionary<string, char> s_codes = [];
    private static readonly Dictionary<(char, int), uint[]?> s_tiles = [];

    /// <summary>The Sweetie 16 (the palette's fixed sprite colours), as ARGB.</summary>
    private static readonly uint[] s_sweetie = BuildSweetie();

    private static uint[] BuildSweetie()
    {
        var palette = PixelPalette.For(dark: false);
        return new[] { '#', 'p', 'r', 'o', 'y', 'L', 'G', 't', 'n', 'b', 'l', 'c', 'w', 'g', 's', 'd' }.Select(c => palette.Sprite(c)).ToArray();
    }

    /// <summary>Whether the character stands in for an emoji.</summary>
    public static bool IsEmoji(char c) => c >= First && c < First + s_clusters.Count;

    /// <summary>The emoji a stand-in character stands for.</summary>
    public static string Cluster(char c) => IsEmoji(c) ? s_clusters[c - First] : "";

    /// <summary>The text with every emoji swapped for its stand-in character; text without one comes back as it is.</summary>
    public static string Encode(string text)
    {
        bool maybe = false;
        foreach (char c in text)
        {
            if (c >= 0x203C)
            {
                maybe = true;
                break;
            }
        }
        if (!maybe)
            return text;
        var builder = new StringBuilder(text.Length);
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = (string)elements.Current;
            if (IsEmojiCluster(element) && Register(element) is { } code)
                builder.Append(code);
            else
                builder.Append(element);
        }
        return builder.ToString();
    }

    private static char? Register(string cluster)
    {
        if (s_codes.TryGetValue(cluster, out char known))
            return known;
        if (s_clusters.Count >= Capacity)
            return null;
        char code = (char)(First + s_clusters.Count);
        s_clusters.Add(cluster);
        s_codes[cluster] = code;
        return code;
    }

    // The BMP characters that are emoji by default (Emoji_Presentation); the others need a variation selector.
    private static readonly (int Low, int High)[] s_bmpEmoji =
    [
        (0x231A, 0x231B), (0x23E9, 0x23EC), (0x23F0, 0x23F0), (0x23F3, 0x23F3), (0x25FD, 0x25FE), (0x2614, 0x2615), (0x2648, 0x2653),
        (0x267F, 0x267F), (0x2693, 0x2693), (0x26A1, 0x26A1), (0x26AA, 0x26AB), (0x26BD, 0x26BE), (0x26C4, 0x26C5), (0x26CE, 0x26CE),
        (0x26D4, 0x26D4), (0x26EA, 0x26EA), (0x26F2, 0x26F3), (0x26F5, 0x26F5), (0x26FA, 0x26FA), (0x26FD, 0x26FD), (0x2705, 0x2705),
        (0x270A, 0x270B), (0x2728, 0x2728), (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797),
        (0x27B0, 0x27B0), (0x27BF, 0x27BF), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55),
    ];

    /// <summary>Whether a grapheme cluster is an emoji: a pictograph or flag (U+1F000 to U+1FAFF), a keycap, a BMP character that asks for emoji presentation (U+FE0F), or one that is an emoji by default.</summary>
    internal static bool IsEmojiCluster(string element)
    {
        if (!Rune.TryGetRuneAt(element, 0, out var rune))
            return false; // a lone surrogate (NTFS names may hold one) is no emoji
        int first = rune.Value;
        if (first is >= 0x1F000 and <= 0x1FAFF)
            return true;
        if (element.Contains('⃣') || (first >= 0x2000 && element.Contains('️')))
            return true;
        foreach (var (low, high) in s_bmpEmoji)
        {
            if (first >= low && first <= high)
                return true;
        }
        return false;
    }

    // ----- tiles -----

    /// <summary>The tile of a stand-in character at <paramref name="size"/> × <paramref name="size"/>: ARGB per pixel, 0 where transparent; null when there is no picture (a placeholder is drawn).</summary>
    internal static uint[]? Tile(char c, int size)
    {
        if (!IsEmoji(c))
            return null;
        if (s_tiles.TryGetValue((c, size), out var cached))
            return cached;
        uint[]? tile = null;
        try
        {
            if (Renderer(s_clusters[c - First]) is { } bgra)
                tile = Shrink(bgra, SourceSize, SourceSize, size);
        }
        catch (Exception ex)
        {
            DeskWidgets.Core.Diagnostics.Log.Warn("Rendering an emoji tile failed", ex);
        }
        return s_tiles[(c, size)] = tile;
    }

    /// <summary>
    /// The HANDOFF's rule: crop to the ink (alpha at least 0.5) as a square round its bounding box, centred; shrink to <paramref name="size"/> × <paramref name="size"/>.
    /// A tile pixel is transparent when the average alpha of the area it covers is under 0.5; otherwise it takes the most common Sweetie colour among the
    /// source pixels of that area (each opaque source pixel counts as its nearest Sweetie colour), weighted by coverage. No dithering, no outline.
    /// Returns null when the picture has no ink.
    /// </summary>
    internal static uint[]? Shrink(byte[] bgra, int width, int height, int size)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (bgra[(y * width + x) * 4 + 3] >= 128)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }
        if (right < left)
            return null;
        double side = Math.Max(right - left + 1, bottom - top + 1);
        double originX = (left + right + 1) / 2.0 - side / 2, originY = (top + bottom + 1) / 2.0 - side / 2;
        double step = side / size;

        // Each opaque source pixel's nearest Sweetie colour, once.
        var nearest = new int[width * height];
        for (int i = 0; i < nearest.Length; i++)
            nearest[i] = bgra[i * 4 + 3] >= 128 ? Nearest(bgra[i * 4 + 2], bgra[i * 4 + 1], bgra[i * 4]) : -1;

        var tile = new uint[size * size];
        var votes = new double[s_sweetie.Length];
        for (int ty = 0; ty < size; ty++)
        {
            for (int tx = 0; tx < size; tx++)
            {
                double x0 = originX + tx * step, x1 = x0 + step, y0 = originY + ty * step, y1 = y0 + step;
                Array.Clear(votes);
                double alpha = 0;
                for (int sy = (int)Math.Floor(y0); sy < (int)Math.Ceiling(y1); sy++)
                {
                    double wy = Math.Min(y1, sy + 1) - Math.Max(y0, sy);
                    for (int sx = (int)Math.Floor(x0); sx < (int)Math.Ceiling(x1); sx++)
                    {
                        if (sx < 0 || sy < 0 || sx >= width || sy >= height)
                            continue; // outside the picture: transparent
                        double w = wy * (Math.Min(x1, sx + 1) - Math.Max(x0, sx));
                        alpha += w * bgra[(sy * width + sx) * 4 + 3] / 255.0;
                        int color = nearest[sy * width + sx];
                        if (color >= 0)
                            votes[color] += w;
                    }
                }
                if (alpha / (step * step) < 0.5)
                    continue;
                int best = 0;
                for (int k = 1; k < votes.Length; k++)
                {
                    if (votes[k] > votes[best])
                        best = k;
                }
                tile[ty * size + tx] = s_sweetie[best];
            }
        }
        return tile;
    }

    /// <summary>The Sweetie colour nearest to a colour by the redmean distance.</summary>
    private static int Nearest(double r, double g, double b)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < s_sweetie.Length; i++)
        {
            uint argb = s_sweetie[i];
            double pr = (argb >> 16) & 0xFF, pg = (argb >> 8) & 0xFF, pb = argb & 0xFF;
            double mean = (r + pr) / 2, dr = r - pr, dg = g - pg, db = b - pb;
            double distance = (2 + mean / 256) * dr * dr + 4 * dg * dg + (2 + (255 - mean) / 256) * db * db;
            if (distance < bestDistance)
                (bestDistance, best) = (distance, i);
        }
        return best;
    }

    // ----- drawing -----

    /// <summary>
    /// Draws the emoji's tile with its top-left at (x, y), each tile pixel <paramref name="scale"/> × <paramref name="scale"/>. With no picture, a
    /// placeholder: a box with 1-step corners in the text's colour, empty inside.
    /// </summary>
    public static void Draw(PixelCanvas canvas, char c, int size, int x, int y, uint textColor, int scale = 1)
    {
        var tile = Tile(c, size);
        if (tile is null)
        {
            DrawPlaceholder(canvas, size, x, y, textColor, scale);
            return;
        }
        for (int ty = 0; ty < size; ty++)
        {
            for (int tx = 0; tx < size; tx++)
            {
                if (tile[ty * size + tx] != 0)
                    canvas.Fill(x + tx * scale, y + ty * scale, scale, scale, tile[ty * size + tx]);
            }
        }
    }

    private static void DrawPlaceholder(PixelCanvas canvas, int size, int x, int y, uint color, int scale)
    {
        for (int i = 1; i < size - 1; i++)
        {
            canvas.Fill(x + i * scale, y, scale, scale, color);
            canvas.Fill(x + i * scale, y + (size - 1) * scale, scale, scale, color);
            canvas.Fill(x, y + i * scale, scale, scale, color);
            canvas.Fill(x + (size - 1) * scale, y + i * scale, scale, scale, color);
        }
    }
}
