using DeskWidgets.Search;

namespace DeskWidgets.Tests;

/// <summary>Phase 13.2: the pinyin table (built from Unihan) and the one match both Big Folder panels use.</summary>
public class NameSearchTests
{
    private static bool Matches(string name, string query) => NameSearch.Matches(name, new NameQuery(query));

    // ----- the table -----

    [Fact]
    public void TheTable_KnowsCommonCharacters_Toneless()
    {
        var table = PinyinTable.Shared;
        Assert.True(table.Count > 40000);
        Assert.Equal(["bi"], table.Readings('壁')!);
        Assert.Equal(["zhi"], table.Readings('纸')!);
        Assert.Contains("lv", table.Readings('绿')!); // ü is written v
        Assert.Contains("nv", table.Readings('女')!);
        Assert.Null(table.Readings('a'));
        Assert.Null(table.Readings('!'));
    }

    [Fact]
    public void ACharacterWithSeveralReadings_HasThemAll()
    {
        var table = PinyinTable.Shared;
        Assert.Contains("le", table.Readings('乐')!);
        Assert.Contains("yue", table.Readings('乐')!);
        Assert.Contains("xing", table.Readings('行')!);
        Assert.Contains("hang", table.Readings('行')!);
        Assert.Contains("zhang", table.Readings('长')!);
        Assert.Contains("chang", table.Readings('长')!);
    }

    [Fact]
    public void ACharacterBeyondTheBasicPlane_IsInTheTable()
    {
        Assert.NotNull(PinyinTable.Shared.Readings(0x20000)); // CJK Extension B
    }

    // ----- the plain match stays -----

    [Theory]
    [InlineData("Budget 2026.xlsx", "budget", true)]
    [InlineData("Budget 2026.xlsx", "T 20", true)]
    [InlineData("Budget 2026.xlsx", "xyz", false)]
    [InlineData("壁纸", "壁", true)]
    [InlineData("Photo 01", "pho", true)]
    [InlineData("anything", "", true)]
    [InlineData("anything", "   ", true)]
    public void TheSubstringMatch_IsAsItWas(string name, string query, bool expected)
        => Assert.Equal(expected, Matches(name, query));

    // ----- initials -----

    [Theory]
    [InlineData("壁纸", "bz", true)]
    [InlineData("壁纸", "BZ", true)]
    [InlineData("我的壁纸", "bz", true)] // a run in the middle
    [InlineData("我的壁纸", "dbz", true)]
    [InlineData("壁纸", "bq", false)]
    [InlineData("表情包&头像", "bqb", true)]
    [InlineData("表情包&头像", "bqbtx", true)] // across the "&"
    [InlineData("表情包&头像", "bqt", false)] // not a run (the third character starts with b)
    [InlineData("QQ音乐", "qqyy", true)] // mixed
    [InlineData("QQ音乐", "qqyl", true)] // 乐 reads le and yue
    [InlineData("QQ音乐", "yy", true)]
    [InlineData("工作资料", "gzzl", true)]
    [InlineData("2026年度总结", "2026ndzj", true)]
    [InlineData("银行", "yh", true)] // 行 reads hang
    [InlineData("银行", "yx", true)] // and xing
    public void Initials_FindChineseNames(string name, string query, bool expected)
        => Assert.Equal(expected, Matches(name, query));

    // ----- pinyin -----

    [Theory]
    [InlineData("壁纸", "bizhi", true)]
    [InlineData("壁纸", "biz", true)] // the last syllable may be cut short
    [InlineData("壁纸", "bizh", true)]
    [InlineData("壁纸", "bi", true)]
    [InlineData("壁纸", "zhi", true)] // a run from the second character
    [InlineData("壁纸", "bizhix", false)]
    [InlineData("壁纸", "bzhi", false)] // initials and syllables are not mixed
    [InlineData("QQ音乐", "qqyinyue", true)]
    [InlineData("QQ音乐", "qqyinle", true)]
    [InlineData("绿色", "lvse", true)]
    [InlineData("绿色", "luse", true)] // lü is often typed as lu
    [InlineData("西安", "xian", true)] // whole syllables xi + an read as "xian"
    [InlineData("西安", "xi", true)]
    public void Pinyin_FindsChineseNames(string name, string query, bool expected)
        => Assert.Equal(expected, Matches(name, query));

    [Fact]
    public void ANameWithNoChinese_NeedsThePlainMatch()
    {
        Assert.False(Matches("Photo 01", "p0")); // only a Chinese name is searched by initials
        Assert.False(Matches("Report", "rp"));
        Assert.True(Matches("Report", "port"));
    }

    [Theory]
    [InlineData("壁纸", "b z")]
    [InlineData("壁纸", "b-z")]
    [InlineData("壁纸", "壁z")]
    public void AQueryThatIsNotLettersAndDigits_IsNotPinyin(string name, string query)
        => Assert.False(Matches(name, query) && query != "壁z" && !name.Contains(query));

    [Fact]
    public void ASearchOfThousandsOfNames_IsQuick()
    {
        var names = Enumerable.Range(0, 20000).Select(i => i % 2 == 0 ? $"文件夹资料 {i}.txt" : $"Report {i}.pdf").ToList();
        var query = new NameQuery("wjjzl");
        Assert.True(names.Count(n => NameSearch.Matches(n, query)) > 9000); // builds every key once
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int hits = names.Count(n => NameSearch.Matches(n, new NameQuery("wjjzl")));
        watch.Stop();
        Assert.Equal(10000, hits);
        Assert.True(watch.ElapsedMilliseconds < 200, $"took {watch.ElapsedMilliseconds} ms");
    }
}
