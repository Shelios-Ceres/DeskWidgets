namespace DeskWidgets.Pixel;

/// <summary>
/// Fusion Pixel Font 8 px (Simplified Chinese build, proportional; SIL OFL 1.1, bundled with its license) as glyph bitmaps for a
/// <see cref="PixelFont"/>: the Big Folder's Chinese text. Every glyph sits in a 9-row cell, 6 rows above the baseline, the baseline
/// row and 2 below; a hanzi is 7 × 7 ink on an 8-pixel advance. The table is embedded (see tools\Make-FusionFont.ps1); the rendering
/// is whole-pixel, one bitmap pixel per art pixel.
/// </summary>
internal static class FusionGlyphs
{
    public const int Above = 6, Below = 2, Height = Above + 1 + Below;

    /// <summary>An emoji stands in a Fusion line as one empty 8-pixel cell, over which its 7 × 7 tile is drawn.</summary>
    public const int EmojiAdvance = 8;

    private sealed record Glyph(int Advance, int Width, int Rows, int X, int Y, byte[] Bits);

    private static readonly Lazy<Dictionary<char, Glyph>> s_table = new(() =>
    {
        using var stream = typeof(FusionGlyphs).Assembly.GetManifestResourceStream("FusionPixel8-zh_hans.bin")
            ?? throw new InvalidOperationException("The Fusion Pixel table is not embedded.");
        return Load(stream);
    });

    /// <summary>Whether the table has a glyph for the character (not counting emoji stand-ins).</summary>
    public static bool Has(char c) => s_table.Value.ContainsKey(c);

    public static PixelMask? Render(char c)
    {
        if (PixelEmoji.IsEmoji(c))
            return new PixelMask(EmojiAdvance, Height, new bool[EmojiAdvance * Height]);
        if (!s_table.Value.TryGetValue(c, out var glyph))
            return null;
        var bits = new bool[glyph.Advance * Height];
        int rowBytes = (glyph.Width + 7) / 8;
        for (int r = 0; r < glyph.Rows; r++)
        {
            int yUp = glyph.Y + glyph.Rows - 1 - r; // rows above the baseline, the baseline row being 0
            int row = Above - yUp;
            if (row < 0 || row >= Height)
                continue;
            for (int col = 0; col < glyph.Width; col++)
            {
                int x = glyph.X + col;
                if (x < 0 || x >= glyph.Advance)
                    continue;
                if ((glyph.Bits[r * rowBytes + col / 8] & (0x80 >> (col % 8))) != 0)
                    bits[row * glyph.Advance + x] = true;
            }
        }
        return new PixelMask(glyph.Advance, Height, bits);
    }

    private static Dictionary<char, Glyph> Load(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "FUS8")
            throw new InvalidDataException("Not a Fusion Pixel table.");
        int count = reader.ReadInt32();
        var table = new Dictionary<char, Glyph>(count);
        for (int i = 0; i < count; i++)
        {
            char code = (char)reader.ReadUInt16();
            int advance = reader.ReadByte(), width = reader.ReadByte(), rows = reader.ReadByte();
            int x = reader.ReadSByte(), y = reader.ReadSByte();
            table[code] = new Glyph(advance, width, rows, x, y, reader.ReadBytes(rows * ((width + 7) / 8)));
        }
        return table;
    }
}
