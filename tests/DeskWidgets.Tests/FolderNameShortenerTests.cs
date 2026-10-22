using System.Globalization;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

public class FolderNameShortenerTests
{
    /// <summary>A stand-in for a font: 6 per character, 11 per CJK character (the ellipsis counts 6).</summary>
    private static double Width(string text)
    {
        double width = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
            width += NameShortener.IsCjk((string)elements.Current) ? 11 : 6;
        return width;
    }

    private static string One(string name, double width) => NameShortener.OneLine(name, width, Width);

    private static bool HasLoneSurrogate(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])))
                return true;
            if (char.IsLowSurrogate(text[i]) && (i == 0 || !char.IsHighSurrogate(text[i - 1])))
                return true;
        }
        return false;
    }

    [Fact]
    public void ANameThatFits_IsShownWhole()
    {
        Assert.Equal("Notes", One("Notes", 74));
        Assert.Equal("", One("", 74));
    }

    [Fact]
    public void TheLastWord_IsKeptAsTheTail()
    {
        // 74 wide = 12 characters: "Quarter" + "…" + "2026".
        Assert.Equal("Quarter…2026", One("Quarterly report 2026", 74));
    }

    [Fact]
    public void TheTailKeepsAVisibleExtension()
    {
        string shortened = One("Annual summary 2026.pdf", 120);
        Assert.EndsWith("2026.pdf", shortened);
        Assert.Contains("…", shortened);
    }

    [Fact]
    public void AChineseName_KeepsItsLastTwoCharacters_AsTheLastCharacterIsCjk()
    {
        Assert.Equal("2026 年…报告", One("2026 年第三季度财务报告", 74));
    }

    [Fact]
    public void ANameWithoutSeparators_KeepsItsLastFourCharacters()
    {
        string name = "abcdefghijklmnopqrstuvwxyz0123456789";
        string shortened = One(name, 80);
        Assert.EndsWith("…6789", shortened);
        Assert.True(Width(shortened) <= 80);
    }

    [Fact]
    public void WhenTheHeadWouldBeTooShort_TheTailIsDropped_AndTheNameIsCutAtTheEnd()
    {
        // The Pixel chat's rule: "腾讯会议" in a narrow cell becomes "腾讯…", not "腾…议".
        Assert.Equal("腾讯…", One("腾讯会议", 36));
        // Latin needs 3 characters of head.
        string latin = One("Wonderful report", 42);
        Assert.True(Width(latin) <= 42);
        Assert.StartsWith("Wonde", latin);
        Assert.EndsWith("…", latin);
    }

    [Fact]
    public void AVisibleExtension_WinsOverTheMinimumHead()
    {
        // Phase 8: a 1-character head with the extension tail beats cutting at the end, which would drop the extension.
        Assert.Equal("度…报告.docx", One("度财务报告.docx", 72));
        // Without an extension the minimum head still applies.
        Assert.Equal("度财…", One("度财务报告", 36));
        // And when not even 1 character fits beside "…" + extension, the name is cut at the end.
        string cut = One("度财务报告.docx", 50);
        Assert.True(Width(cut) <= 50);
        Assert.EndsWith("…", cut);
    }

    [Fact]
    public void TrailingSeparatorsOfTheHead_AreTrimmed()
    {
        Assert.DoesNotContain("_…", One("Quarterly_report_final_2026", 74));
        Assert.DoesNotContain(" …", One("Quarterly   report 2026 draft", 60));
    }

    [Fact]
    public void ASurrogatePair_IsNeverSplit()
    {
        string astral = string.Concat(Enumerable.Repeat("\U00020000\U00020001\U00020002", 4));
        for (double width = 20; width <= 150; width += 3)
        {
            string shortened = One(astral, width);
            Assert.False(HasLoneSurrogate(shortened), $"width {width}: {shortened}");
        }
    }

    [Fact]
    public void AResult_NeverExceedsTheWidth_WhenTheNameCanBeCut()
    {
        foreach (string name in new[] { "Quarterly report 2026", "2026 年第三季度财务报告", "a-very-long-file-name-without-spaces-at-all.docx", "腾讯会议" })
        {
            for (double width = 30; width <= 160; width += 5)
            {
                string shortened = One(name, width);
                Assert.True(Width(shortened) <= width, $"{name} at {width}: {shortened}");
            }
        }
    }

    [Fact]
    public void TwoLines_BreakAtTheLastSpaceThatFits()
    {
        var (first, second) = NameShortener.TwoLines("Quarterly report 2026 final draft", 72, Width);
        Assert.Equal("Quarterly", first);
        Assert.Equal(NameShortener.OneLine("report 2026 final draft", 72, Width), second);
    }

    [Fact]
    public void TwoLines_BreakBetweenAnyTwoCjkCharacters_AndShortenTheSecond()
    {
        var (first, second) = NameShortener.TwoLines("2026年第三季度财务报告审计结果汇总说明", 66, Width);
        Assert.True(Width(first) <= 66);
        Assert.StartsWith("2026", first);
        Assert.True(Width(second) <= 66);
        Assert.Contains("…", second);
    }

    [Fact]
    public void TwoLines_NeverEndALineWithAnOpeningMark()
    {
        var (first, second) = NameShortener.TwoLines("移动到“工作”", 44, Width);
        Assert.Equal("移动到", first);
        Assert.Equal("“工作”", second);
    }

    [Fact]
    public void TwoLines_ANameThatFitsStaysOnOneLine()
    {
        Assert.Equal(("Notes", ""), NameShortener.TwoLines("Notes", 72, Width));
    }

    [Fact]
    public void APath_KeepsItsLastFolderWhole()
    {
        string shortened = NameShortener.Path(@"C:\Users\asus\Documents\Projects\工作资料", 150, Width);
        Assert.EndsWith(@"…\工作资料", shortened);
        Assert.StartsWith(@"C:\Users", shortened);
        Assert.True(Width(shortened) <= 150);
    }

    [Fact]
    public void APathThatFits_IsShownWhole()
    {
        Assert.Equal(@"C:\Users\asus", NameShortener.Path(@"C:\Users\asus", 200, Width));
    }

    [Fact]
    public void APathWhoseLastFolderAloneIsTooWide_FallsBackToTheOneLineRule()
    {
        string shortened = NameShortener.Path(@"C:\Users\asus\a-very-very-long-folder-name-indeed", 60, Width);
        Assert.True(Width(shortened) <= 60);
        Assert.Contains("…", shortened);
    }

    [Fact]
    public void WithAVisibleExtension_TheTailKeepsTheExtensionAndTwoCharactersBeforeItsDot()
    {
        // The 4-character fallback used to give "cross-dri….txt": an ellipsis running into a dot.
        Assert.Equal("cro…ve.txt", One("cross-drive.txt", 60));
        Assert.Equal("quart…rt.docx", One("quarterlyreport.docx", 78));
        Assert.DoesNotContain("….", One("annual-budget-summary.xlsx", 78));
    }

    [Fact]
    public void ANameWithoutAnExtension_KeepsTheOldFallbackTail()
    {
        Assert.Equal("Quar…2026", One("Quarterlyreport2026", 54));
        Assert.Equal("v1.2.0 notes", One("v1.2.0 notes", 200)); // a dot inside the name is not an extension of letters at the end
    }

    [Fact]
    public void TwoLines_PreferABreakAfterAHyphenOrBeforeTheExtension_ToBreakingInsideAWord()
    {
        Assert.Equal(("cross-", "drive.txt"), NameShortener.TwoLines("cross-drive.txt", 72, Width));
        // Line 2 keeps the visible extension (Phase 8: it wins over the minimum head), not "file_nam…".
        Assert.Equal(("my_long_", "fi…me.pdf"), NameShortener.TwoLines("my_long_file_name.pdf", 54, Width));
        // No natural break fits in line 1: the word breaks inside, as before.
        Assert.Equal(("Quarterlyrep", "ort.docx"), NameShortener.TwoLines("Quarterlyreport.docx", 72, Width));
        // The dot is a break when it fits: "Quarterly" + ".docx".
        Assert.Equal(("Quarterly", ".docx"), NameShortener.TwoLines("Quarterly.docx", 54, Width));
    }
}
