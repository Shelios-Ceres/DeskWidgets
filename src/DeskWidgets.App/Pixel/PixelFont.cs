using System.Globalization;
using System.Text;

namespace DeskWidgets.Pixel;

internal enum PixelAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// A bitmap font: one <see cref="PixelMask"/> per character, all of the same
/// height, each advanced by its own width plus <see cref="Spacing"/>.
/// </summary>
internal sealed class PixelFont
{
    private readonly Dictionary<char, PixelMask?> _glyphs = new();
    private readonly Func<char, PixelMask?>? _provider;

    /// <param name="capitalsOnly">Lower case is drawn in capitals and accents are dropped ("São" → "SAO").</param>
    /// <param name="provider">Supplies glyphs on demand, for a font that covers every character (the system fallback).</param>
    /// <param name="emojiSize">The side of an emoji's tile in this font (7 in Fusion, 11 in SimSun), or 0 when the font cannot carry an emoji.</param>
    public PixelFont(int height, int lineGap, int spacing, bool capitalsOnly, Func<char, PixelMask?>? provider = null, int emojiSize = 0)
    {
        Height = height;
        LineGap = lineGap;
        Spacing = spacing;
        CapitalsOnly = capitalsOnly;
        _provider = provider;
        EmojiSize = emojiSize;
    }

    /// <summary>The side of an emoji's tile, drawn over the empty cell the font gives the emoji's stand-in character (see <see cref="PixelEmoji"/>); 0 for a font that cannot.</summary>
    public int EmojiSize { get; }

    /// <summary>Rows in every glyph.</summary>
    public int Height { get; }

    /// <summary>Empty rows between wrapped lines.</summary>
    public int LineGap { get; }

    /// <summary>Empty columns between glyphs.</summary>
    public int Spacing { get; }

    public bool CapitalsOnly { get; }

    /// <summary>Defines a glyph from rows such as ".#.|#.#|###|#.#|#.#".</summary>
    public void Add(char character, string rows)
    {
        var glyph = PixelMask.Parse(rows);
        if (glyph.Height != Height)
            throw new ArgumentException($"Glyph '{character}' has {glyph.Height} rows, the font has {Height}.", nameof(rows));
        _glyphs[character] = glyph;
    }

    /// <summary>The text as this font will spell it.</summary>
    public string Normalize(string text)
    {
        if (!CapitalsOnly)
            return text;
        var builder = new StringBuilder(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(c switch
            {
                '‘' or '’' => '\'',
                '“' or '”' => '"',
                '–' => '-',
                ' ' => ' ',
                _ => char.ToUpperInvariant(c),
            });
        }
        return builder.ToString();
    }

    /// <summary>Whether every character of already-normalized text has a glyph.</summary>
    public bool Covers(string normalized)
    {
        foreach (char c in normalized)
        {
            if (Glyph(c) is null)
                return false;
        }
        return true;
    }

    public PixelMask? Glyph(char c)
    {
        if (_glyphs.TryGetValue(c, out var glyph))
            return glyph;
        if (_provider is null)
            return null;
        return _glyphs[c] = _provider(c);
    }

    /// <summary>Width of one line of normalized text, in font pixels (no trailing spacing).</summary>
    public int Measure(string normalized)
    {
        int width = 0;
        foreach (char c in normalized)
            width += (Glyph(c)?.Width ?? 0) + Spacing;
        return Math.Max(0, width - Spacing);
    }

    /// <summary>Draws one line of normalized text with its top-left at (x, y).</summary>
    public void DrawLine(PixelCanvas canvas, string normalized, int x, int y, uint color, int scale = 1)
    {
        foreach (char c in normalized)
        {
            if (Glyph(c) is not { } glyph)
                continue;
            canvas.Stamp(glyph, x, y, color, scale);
            if (EmojiSize > 0 && PixelEmoji.IsEmoji(c))
                PixelEmoji.Draw(canvas, c, EmojiSize, x, y, color, scale);
            x += (glyph.Width + Spacing) * scale;
        }
    }
}

/// <summary>
/// Text laid out for the canvas: wrapped onto at most a given number of
/// lines, the last one cut short with "…" when it still does not fit.
/// Text a pixel font cannot spell (Chinese, say) is set entirely in the
/// system fallback font, so one string never mixes two pixel sizes.
/// </summary>
internal sealed class PixelText
{
    private const string Ellipsis = "…";

    /// <summary>Punctuation that must not begin a line of Chinese text.</summary>
    private const string NoLineStart = "，。、：；！？）」』》";

    /// <summary>Marks that must not end a line: the text they open comes down with them.</summary>
    private const string NoLineEnd = "“（「『《";

    private PixelText(PixelFont font, IReadOnlyList<string> lines, int scale)
    {
        Font = font;
        Lines = lines;
        Scale = scale;
        Width = lines.Count == 0 ? 0 : lines.Max(font.Measure) * scale;
        Height = lines.Count == 0 ? 0 : (lines.Count * font.Height + (lines.Count - 1) * font.LineGap) * scale;
    }

    public PixelFont Font { get; }

    public IReadOnlyList<string> Lines { get; }

    public int Scale { get; }

    /// <summary>Widest line, in art pixels.</summary>
    public int Width { get; }

    public int Height { get; }

    /// <summary>Whether the fallback font had to be used (taller glyphs: lay out accordingly).</summary>
    public bool IsFallback => Font == PixelFonts.System;

    /// <summary>0 when set in a pixel font, 1 in Fusion 8 px, 2 in SimSun: a Big Folder card or panel takes the metric set of its highest rank.</summary>
    public int Rank => Font == PixelFonts.System ? 2 : Font == PixelFonts.Fusion ? 1 : 0;

    public static PixelText Layout(string text, PixelFont font, int maxWidth = int.MaxValue, int maxLines = 1, int scale = 1)
    {
        string normalized = font.Normalize(text);
        if (!font.Covers(normalized))
        {
            font = PixelFonts.System;
            normalized = text;
            scale = 1; // the fallback glyphs are already large
        }
        int limit = maxWidth == int.MaxValue ? int.MaxValue : Math.Max(1, maxWidth / scale);
        return new PixelText(font, Wrap(normalized, font, limit, Math.Max(1, maxLines)), scale);
    }

    /// <summary>
    /// Draws each line inside a box <paramref name="boxWidth"/> wide (default:
    /// the text's own width) whose top-left is (x, y), aligned within it,
    /// optionally over a hard drop shadow offset down and to the right.
    /// </summary>
    public void Draw(PixelCanvas canvas, int x, int y, uint color, PixelAlign align = PixelAlign.Left, int boxWidth = -1, uint shadow = 0)
    {
        if (boxWidth < 0)
            boxWidth = Width;
        if (shadow != 0)
        {
            int offset = Math.Max(1, Scale / 2);
            Draw(canvas, x + offset, y + offset, shadow, align, boxWidth);
        }
        int lineAdvance = (Font.Height + Font.LineGap) * Scale;
        foreach (string line in Lines)
        {
            int width = Font.Measure(line) * Scale;
            int left = align switch
            {
                PixelAlign.Center => x + (boxWidth - width) / 2,
                PixelAlign.Right => x + boxWidth - width,
                _ => x,
            };
            Font.DrawLine(canvas, line, left, y, color, Scale);
            y += lineAdvance;
        }
    }

    /// <summary>
    /// Greedy line breaking at spaces. Unspaced text such as Chinese breaks
    /// between characters; a pixel-font word too wide for the line is cut
    /// short with an ellipsis instead ("HEADPHO…", never "HEADPHONE / S").
    /// </summary>
    private static List<string> Wrap(string text, PixelFont font, int maxWidth, int maxLines)
    {
        var lines = new List<string>();
        int start = 0;
        while (true)
        {
            while (start < text.Length && text[start] == ' ')
                start++;
            if (start >= text.Length)
                return lines;
            if (lines.Count == maxLines - 1)
                break; // the last allowed line takes the rest
            int fit = start, spaceBreak = -1;
            while (fit < text.Length && font.Measure(text[start..(fit + 1)]) <= maxWidth)
            {
                fit++;
                if (fit == text.Length || text[fit] == ' ')
                    spaceBreak = fit;
            }
            if (fit == text.Length)
            {
                lines.Add(text[start..].TrimEnd());
                return lines;
            }
            if (spaceBreak <= start && font.CapitalsOnly)
                break; // an over-long word ends the text
            int end = spaceBreak > start ? spaceBreak : Math.Max(fit, start + 1);
            // Chinese breaks between any two characters, but a line never starts with closing punctuation:
            // the character before it comes down with it.
            while (spaceBreak <= start && end > start + 1 && end < text.Length && (NoLineStart.Contains(text[end]) || NoLineEnd.Contains(text[end - 1])))
                end--;
            lines.Add(text[start..end].TrimEnd());
            start = end;
        }

        string last = text[start..].TrimEnd();
        if (font.Measure(last) <= maxWidth)
        {
            lines.Add(last);
            return lines;
        }
        while (last.Length > 0 && font.Measure(last + Ellipsis) > maxWidth)
            last = last[..^1].TrimEnd();
        lines.Add(last + Ellipsis);
        return lines;
    }
}
