using System.Text;

namespace DeskWidgets.Search;

/// <summary>
/// The readings of Han characters, as toneless pinyin syllables ("bi", "zhi", "lv"), from the table built out of the Unicode Han Database by
/// <c>tools\Make-PinyinTable.ps1</c> (Phase 13.2). A character with several readings (行: xing, hang) has them all.
/// </summary>
internal sealed class PinyinTable
{
    private static PinyinTable? s_shared;

    private readonly string[] _syllables;
    private readonly int[] _codes;
    private readonly int[] _starts; // _starts[i] .. _starts[i + 1] are character i's readings in _flat
    private readonly ushort[] _flat;

    private PinyinTable(string[] syllables, int[] codes, int[] starts, ushort[] flat)
    {
        _syllables = syllables;
        _codes = codes;
        _starts = starts;
        _flat = flat;
    }

    /// <summary>The table that ships inside the app.</summary>
    public static PinyinTable Shared => s_shared ??= LoadEmbedded();

    private static PinyinTable LoadEmbedded()
    {
        using var stream = typeof(PinyinTable).Assembly.GetManifestResourceStream("pinyin.bin")
            ?? throw new InvalidOperationException("The pinyin table is missing from the app.");
        return Parse(stream);
    }

    /// <summary>Reads a table in the format <c>Make-PinyinTable.ps1</c> writes.</summary>
    internal static PinyinTable Parse(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "PYN1")
            throw new InvalidDataException("Not a pinyin table.");
        var syllables = new string[reader.ReadInt32()];
        for (int i = 0; i < syllables.Length; i++)
            syllables[i] = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadByte()));
        int count = reader.ReadInt32();
        var codes = new int[count];
        var starts = new int[count + 1];
        var flat = new List<ushort>(count + count / 8);
        int code = 0;
        for (int i = 0; i < count; i++)
        {
            int delta = 0, shift = 0;
            byte next;
            do
            {
                next = reader.ReadByte();
                delta |= (next & 0x7F) << shift;
                shift += 7;
            }
            while ((next & 0x80) != 0);
            code += delta;
            codes[i] = code;
            starts[i] = flat.Count;
            int readings = reader.ReadByte();
            for (int r = 0; r < readings; r++)
                flat.Add(reader.ReadUInt16());
        }
        starts[count] = flat.Count;
        return new PinyinTable(syllables, codes, starts, [.. flat]);
    }

    public int Count => _codes.Length;

    /// <summary>The distinct toneless syllables of a character, or null when it has no reading (not a Han character, or one Unihan gives none).</summary>
    public string[]? Readings(int codePoint)
    {
        int at = Array.BinarySearch(_codes, codePoint);
        if (at < 0)
            return null;
        int from = _starts[at], to = _starts[at + 1];
        var result = new string[to - from];
        for (int i = from; i < to; i++)
            result[i - from] = _syllables[_flat[i]];
        return result;
    }
}
