namespace DeskWidgets.Pixel;

/// <summary>
/// The small bitmap the pixel theme draws on, one art pixel at a time.
/// <see cref="PixelSurface"/> enlarges it by a whole number of screen pixels
/// per art pixel, so every edge stays hard and square. Colours are
/// 0xAARRGGBB with 0 meaning transparent; nothing is blended.
/// </summary>
internal sealed class PixelCanvas
{
    public PixelCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row-major, top-left first.</summary>
    public uint[] Pixels { get; }

    public void Clear() => Array.Clear(Pixels);

    public uint Get(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? Pixels[y * Width + x] : 0;

    public void Set(int x, int y, uint color)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            Pixels[y * Width + x] = color;
    }

    public void Fill(int x, int y, int width, int height, uint color)
    {
        int left = Math.Max(0, x), top = Math.Max(0, y);
        int right = Math.Min(Width, x + width), bottom = Math.Min(Height, y + height);
        for (int row = top; row < bottom && left < right; row++)
            Pixels.AsSpan(row * Width + left, right - left).Fill(color);
    }

    /// <summary>Bresenham line, both ends included.</summary>
    public void Line(int x0, int y0, int x1, int y1, uint color)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            Set(x0, y0, color);
            if (x0 == x1 && y0 == y1)
                return;
            int doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x0 += sx;
            }
            if (doubled <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>A disc centred on pixel (cx, cy), 2·radius + 1 pixels across.</summary>
    public void FillCircle(int cx, int cy, int radius, uint color)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (InCircle(dx, dy, radius))
                    Set(cx + dx, cy + dy, color);
            }
        }
    }

    /// <summary>The 1-pixel rim of <see cref="FillCircle"/>'s disc.</summary>
    public void Circle(int cx, int cy, int radius, uint color)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (InCircle(dx, dy, radius) && !InCircle(dx, dy, radius - 1))
                    Set(cx + dx, cy + dy, color);
            }
        }
    }

    // r(r+1) rather than r² gives the rounder circles pixel artists draw by hand.
    private static bool InCircle(int dx, int dy, int radius) => dx * dx + dy * dy <= radius * radius + radius;

    /// <summary>Draws the set pixels of <paramref name="mask"/> in one colour, each as scale × scale art pixels.</summary>
    public void Stamp(PixelMask mask, int x, int y, uint color, int scale = 1)
    {
        for (int row = 0; row < mask.Height; row++)
        {
            for (int col = 0; col < mask.Width; col++)
            {
                if (mask[col, row])
                    Fill(x + col * scale, y + row * scale, scale, scale, color);
            }
        }
    }

    /// <summary>
    /// Draws <paramref name="sprite"/>, colouring each of its symbols with
    /// <paramref name="colors"/> (0 skips the pixel), each pixel as scale × scale art pixels.
    /// </summary>
    public void Draw(PixelSprite sprite, int x, int y, Func<char, uint> colors, int scale = 1)
    {
        for (int row = 0; row < sprite.Height; row++)
        {
            for (int col = 0; col < sprite.Width; col++)
            {
                char symbol = sprite[col, row];
                if (symbol == '.')
                    continue;
                uint color = colors(symbol);
                if (color != 0)
                    Fill(x + col * scale, y + row * scale, scale, scale, color);
            }
        }
    }

    /// <summary>Draws a sprite in one colour inside a one-pixel ring of <paramref name="outline"/>, so it reads over anything.</summary>
    public void DrawOutlined(PixelSprite sprite, int x, int y, uint fill, uint outline)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx != 0 || dy != 0)
                    Draw(sprite, x + dx, y + dy, _ => outline);
            }
        }
        Draw(sprite, x, y, _ => fill);
    }

    /// <summary>A horizontal line of every other pixel.</summary>
    public void DottedLine(int x, int y, int width, uint color)
    {
        for (int i = 0; i < width; i += 2)
            Set(x + i, y, color);
    }

    /// <summary>
    /// Writes the canvas into a premultiplied BGRA buffer of
    /// (Width·factor) × (Height·factor) pixels, each art pixel a factor × factor block.
    /// </summary>
    public void CopyScaled(Span<byte> target, int factor)
    {
        int stride = Width * factor * 4;
        for (int y = 0; y < Height; y++)
        {
            var first = target.Slice(y * factor * stride, stride);
            for (int x = 0; x < Width; x++)
            {
                uint argb = Pixels[y * Width + x];
                uint a = argb >> 24;
                byte b = (byte)((argb & 0xFF) * a / 255);
                byte g = (byte)(((argb >> 8) & 0xFF) * a / 255);
                byte r = (byte)(((argb >> 16) & 0xFF) * a / 255);
                for (int i = 0, offset = x * factor * 4; i < factor; i++, offset += 4)
                {
                    first[offset] = b;
                    first[offset + 1] = g;
                    first[offset + 2] = r;
                    first[offset + 3] = (byte)a;
                }
            }
            for (int i = 1; i < factor; i++)
                first.CopyTo(target.Slice((y * factor + i) * stride, stride));
        }
    }
}

/// <summary>A 1-bit image: a font glyph or a single-colour sprite.</summary>
internal sealed class PixelMask
{
    private readonly bool[] _bits;

    public PixelMask(int width, int height, bool[] bits)
    {
        Width = width;
        Height = height;
        _bits = bits;
    }

    public int Width { get; }

    public int Height { get; }

    public bool this[int x, int y] => _bits[y * Width + x];

    /// <summary>Parses rows such as ".#.|#.#" ('#' set, anything else clear).</summary>
    public static PixelMask Parse(string rows)
    {
        string[] lines = rows.Split('|');
        int width = lines.Max(l => l.Length);
        var bits = new bool[width * lines.Length];
        for (int y = 0; y < lines.Length; y++)
        {
            for (int x = 0; x < lines[y].Length; x++)
                bits[y * width + x] = lines[y][x] == '#';
        }
        return new PixelMask(width, lines.Length, bits);
    }
}

/// <summary>A small multi-colour image whose pixels are symbols ('.' = transparent) coloured at draw time.</summary>
internal sealed class PixelSprite
{
    private readonly string[] _rows;

    public PixelSprite(params string[] rows)
    {
        _rows = rows;
        Width = rows.Max(r => r.Length);
        Height = rows.Length;
    }

    public int Width { get; }

    public int Height { get; }

    public char this[int x, int y] => x < _rows[y].Length ? _rows[y][x] : '.';
}
