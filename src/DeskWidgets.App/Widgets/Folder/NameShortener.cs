using System.Globalization;
using System.Text;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// The one shortening function every theme shares (THEMES 2.9): how a file name, a folder name or a path is cut to fit.
/// The caller supplies <c>measure</c>, which must measure in the font the text is drawn in (DirectWrite for Glass, the
/// pixel fonts and SimSun for Pixel); nothing here counts characters as widths. Text is cut only between text elements,
/// so a surrogate pair or a combining sequence is never split.
/// </summary>
internal static class NameShortener
{
    public const string Ellipsis = "…";

    private const string OpeningMarks = "“（「『《";
    private const string ClosingMarks = "，。、：；！？）」』》”";
    private const string HeadTrim = " _-.";

    /// <summary>
    /// One line (card, header, drop label):
    /// whole if it fits; else the head, "…" and a tail, the tail being the last word (at most 12 characters, with its
    /// extension, if "…" + tail fits in half the width) or the last 4 characters (the last 2 when the name ends in a CJK
    /// character). If fewer than 2 characters (3 for Latin) would be left of the head, the tail is dropped and the name is cut at the end,
    /// except that a visible extension is always kept: with it, a head of 1 character is enough (only no head at all cuts at the end).
    /// </summary>
    public static string OneLine(string name, double maxWidth, Func<string, double> measure)
    {
        if (name.Length == 0 || measure(name) <= maxWidth)
            return name;
        string[] elements = Elements(name);
        int count = elements.Length;

        int tailCount = TailLength(elements, maxWidth, measure);
        int headRoom = count - tailCount;
        string tail = string.Concat(elements.Skip(headRoom));
        int headCount = LongestFit(headRoom, h => HeadText(elements, h) + Ellipsis + tail, maxWidth, measure);
        string head = HeadText(elements, headCount);
        // A visible extension wins over the minimum head: cutting at the end would drop it ("度…报告.docx", not "度财务报告.d…").
        int dot = ExtensionDot(elements);
        bool keepsExtension = dot >= 1 && tailCount >= count - dot;
        int minimum = keepsExtension ? 1 : ContainsCjk(head) ? 2 : 3;
        if (tailCount > 0 && head.Length > 0 && TextElementCount(head) >= minimum)
            return head + Ellipsis + tail;

        // No usable head next to the tail: cut at the end instead.
        int cut = LongestFit(count, h => HeadText(elements, h) + Ellipsis, maxWidth, measure);
        string text = HeadText(elements, cut);
        return text + Ellipsis;
    }

    private static int TailLength(string[] elements, double maxWidth, Func<string, double> measure)
    {
        int count = elements.Length;
        int separator = -1;
        for (int i = count - 1; i >= 0; i--)
        {
            if (elements[i] is " " or "_" or "-")
            {
                separator = i;
                break;
            }
        }
        int lastWord = count - 1 - separator;
        if (lastWord is > 0 and <= 12 && separator >= 0 && measure(Ellipsis + string.Concat(elements.Skip(separator + 1))) <= maxWidth / 2)
            return lastWord;
        // A name that is one long word has no separator; its last word is the whole name, too long to be a tail.
        // With a visible extension the tail is the extension and the last 2 characters of the name before it: "cross-d…st.txt", never "cross-dri….txt".
        int dot = ExtensionDot(elements);
        if (dot >= 2)
            return count - dot + 2;
        return IsCjk(elements[^1]) ? Math.Min(2, count) : Math.Min(4, count);
    }

    /// <summary>The index of the "." that starts a visible extension (1 to 5 letters or digits at the end, something before the dot), or -1.</summary>
    private static int ExtensionDot(string[] elements)
    {
        for (int i = elements.Length - 2; i >= 1 && i >= elements.Length - 6; i--)
        {
            if (elements[i] == ".")
                return elements.Skip(i + 1).All(e => e.Length == 1 && char.IsLetterOrDigit(e[0])) ? i : -1;
        }
        return -1;
    }

    private static string HeadText(string[] elements, int count)
        => string.Concat(elements.Take(count)).TrimEnd(' ', '_', '-', '.');

    /// <summary>The largest number of leading elements (up to <paramref name="max"/>) whose candidate text still fits; 0 if none.</summary>
    private static int LongestFit(int max, Func<int, string> candidate, double maxWidth, Func<string, double> measure)
    {
        int low = 0, high = max;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (measure(candidate(middle)) <= maxWidth)
                low = middle;
            else
                high = middle - 1;
        }
        return low;
    }

    /// <summary>
    /// Two lines (the panel): line 1 holds as many characters as fit, broken at the last space that fits or between any two
    /// CJK characters (a word that is too long breaks inside); line 2 is the rest, shortened by <see cref="OneLine"/>.
    /// A line never ends with an opening mark or starts with a closing one. Line 2 is empty when everything fits on one line.
    /// </summary>
    public static (string First, string Second) TwoLines(string name, double maxWidth, Func<string, double> measure)
    {
        if (name.Length == 0 || measure(name) <= maxWidth)
            return (name, "");
        string[] elements = Elements(name);
        int fit = Math.Max(1, LongestFit(elements.Length, n => string.Concat(elements.Take(n)).TrimEnd(), maxWidth, measure));
        int cut = fit;
        bool cjkEdge = IsCjk(elements[fit - 1]) || (fit < elements.Length && IsCjk(elements[fit]));
        if (!cjkEdge && fit < elements.Length && elements[fit] != " ")
        {
            // The latest natural break that fits: a space (dropped), or after "-" or "_" (kept on line 1).
            // Failing those, before the "." of a visible extension. Only then does a word break inside.
            int natural = -1;
            for (int i = fit; i > 0; i--)
            {
                if ((i < fit && elements[i] == " ") || (elements[i - 1] is "-" or "_" && i < elements.Length))
                {
                    natural = i;
                    break;
                }
            }
            if (natural < 0 && ExtensionDot(elements) is var dot and > 0 && dot <= fit)
                natural = dot;
            if (natural > 0)
                cut = natural;
        }
        while (cut > 1 && cut < elements.Length && (OpeningMarks.Contains(elements[cut - 1]) || ClosingMarks.Contains(elements[cut])))
            cut--;
        string first = string.Concat(elements.Take(cut)).TrimEnd();
        string rest = string.Concat(elements.Skip(cut)).TrimStart();
        return (first, OneLine(rest, maxWidth, measure));
    }

    /// <summary>
    /// A path (footer, missing-folder message): the last folder is kept whole ("C:\Users\asus\F…\工作资料"). If even that does not
    /// fit, the one-line rule applies.
    /// </summary>
    public static string Path(string path, double maxWidth, Func<string, double> measure)
    {
        if (path.Length == 0 || measure(path) <= maxWidth)
            return path;
        string trimmed = path.TrimEnd('\\');
        int slash = trimmed.LastIndexOf('\\');
        if (slash > 0)
        {
            string tail = trimmed[slash..]; // "\last folder"
            string[] head = Elements(trimmed[..slash]);
            if (measure(Ellipsis + tail) <= maxWidth)
            {
                int count = LongestFit(head.Length, h => string.Concat(head.Take(h)) + Ellipsis + tail, maxWidth, measure);
                return string.Concat(head.Take(count)) + Ellipsis + tail;
            }
        }
        return OneLine(path, maxWidth, measure);
    }

    private static string[] Elements(string text)
    {
        var list = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            list.Add((string)enumerator.Current);
        return [.. list];
    }

    private static int TextElementCount(string text) => new StringInfo(text).LengthInTextElements;

    public static bool ContainsCjk(string text) => Elements(text).Any(IsCjk);

    /// <summary>Han, kana, hangul and the full-width forms: text that breaks between any two characters.</summary>
    public static bool IsCjk(string element)
    {
        if (!Rune.TryGetRuneAt(element, 0, out var rune))
            return false; // a lone surrogate (NTFS names may hold one)
        int codePoint = rune.Value;
        return codePoint is >= 0x2E80 and <= 0x9FFF
            or >= 0xAC00 and <= 0xD7AF
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFF00 and <= 0xFFEF
            or >= 0x20000 and <= 0x2FA1F;
    }
}
