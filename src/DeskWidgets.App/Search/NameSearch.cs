using System.Runtime.CompilerServices;
using System.Text;

namespace DeskWidgets.Search;

/// <summary>
/// What a search box typed: the text, and, when it is only Latin letters and digits, the same in lower case for the pinyin matches.
/// Made once per change of the text, then asked of every name.
/// </summary>
internal sealed class NameQuery
{
    public NameQuery(string text)
    {
        Text = text.Trim();
        bool letters = Text.Length > 0;
        foreach (char c in Text)
            letters &= c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
        Letters = letters ? Text.ToLowerInvariant() : null;
    }

    public string Text { get; }

    /// <summary>The query in lower case if it is Latin letters and digits only (nothing else can be pinyin), else null.</summary>
    public string? Letters { get; }

    public bool IsEmpty => Text.Length == 0;
}

/// <summary>
/// One shared match for both Big Folder panels (Phase 13.2). A name matches a query if it contains the text (any case), or, for a query of
/// Latin letters and digits and a name with Chinese in it, if the query spells a run of the name's characters by
/// <b>initials</b> ("bz" finds 壁纸, "bqb" finds 表情包&amp;头像, "qqyy" finds QQ音乐) or by <b>pinyin</b>, whole syllables with a part of the last
/// one allowed ("bizhi", "biz"). Latin letters and digits in the name stand for themselves; other characters are skipped over, so a
/// run can cross a space or an "&amp;". A character with several readings (行, 乐) matches any of them.
/// </summary>
internal static class NameSearch
{
    private sealed class Key
    {
        public required string[][] Syllables { get; init; }
        public required string[] Initials { get; init; }
    }

    private static readonly ConditionalWeakTable<string, Key?> Keys = new();

    /// <summary>Whether <paramref name="name"/> matches <paramref name="query"/>; an empty query matches everything.</summary>
    public static bool Matches(string name, NameQuery query)
    {
        if (query.IsEmpty)
            return true;
        if (name.Contains(query.Text, StringComparison.OrdinalIgnoreCase))
            return true;
        if (query.Letters is null)
            return false;
        var key = Keys.GetValue(name, Build);
        return key is not null && (MatchesInitials(key, query.Letters) || MatchesPinyin(key, query.Letters));
    }

    /// <summary>The name's characters as matching tokens, or null when it has no Han character (then only the plain match applies).</summary>
    private static Key? Build(string name)
    {
        var table = PinyinTable.Shared;
        var syllables = new List<string[]>();
        var initials = new List<string>();
        bool han = false;
        foreach (var rune in name.EnumerateRunes())
        {
            if (rune.Value < 0x80)
            {
                char c = char.ToLowerInvariant((char)rune.Value);
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                {
                    syllables.Add([c.ToString()]);
                    initials.Add(c.ToString());
                }
                continue; // a space, a dot, an "&": the run goes on across it
            }
            var readings = table.Readings(rune.Value);
            if (readings is null)
                continue;
            han = true;
            // ü is written "v"; people also type "u" (nü, lü), so a syllable with a v matches both spellings.
            var alts = new List<string>(readings.Length + 1);
            foreach (string reading in readings)
            {
                alts.Add(reading);
                if (reading.Contains('v'))
                    alts.Add(reading.Replace('v', 'u'));
            }
            syllables.Add([.. alts]);
            var letters = new StringBuilder();
            foreach (string reading in readings)
                if (letters.ToString().IndexOf(reading[0]) < 0)
                    letters.Append(reading[0]);
            initials.Add(letters.ToString());
        }
        return han ? new Key { Syllables = [.. syllables], Initials = [.. initials] } : null;
    }

    /// <summary>The query's letters are the initials of a run of consecutive characters.</summary>
    private static bool MatchesInitials(Key key, string letters)
    {
        for (int start = 0; start + letters.Length <= key.Initials.Length; start++)
        {
            int k = 0;
            while (k < letters.Length && key.Initials[start + k].Contains(letters[k]))
                k++;
            if (k == letters.Length)
                return true;
        }
        return false;
    }

    /// <summary>The query is the pinyin of a run of consecutive characters: whole syllables, the last one possibly cut short.</summary>
    private static bool MatchesPinyin(Key key, string letters)
    {
        for (int start = 0; start < key.Syllables.Length; start++)
            if (Spell(key, start, letters, 0))
                return true;
        return false;
    }

    private static bool Spell(Key key, int token, string letters, int at)
    {
        if (at == letters.Length)
            return true;
        if (token >= key.Syllables.Length)
            return false;
        var rest = letters.AsSpan(at);
        foreach (string syllable in key.Syllables[token])
        {
            if (syllable.Length >= rest.Length)
            {
                if (syllable.AsSpan().StartsWith(rest))
                    return true; // the last syllable, perhaps not all of it
            }
            else if (rest.StartsWith(syllable) && Spell(key, token + 1, letters, at + syllable.Length))
            {
                return true;
            }
        }
        return false;
    }

    // ----- for the tests -----

    internal static bool MatchesInitialsOnly(string name, string letters) => Build(name) is { } key && MatchesInitials(key, letters.ToLowerInvariant());

    internal static bool MatchesPinyinOnly(string name, string letters) => Build(name) is { } key && MatchesPinyin(key, letters.ToLowerInvariant());
}
