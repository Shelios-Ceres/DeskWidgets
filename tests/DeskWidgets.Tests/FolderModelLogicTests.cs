using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

public class FolderModelLogicTests
{
    private static readonly DateTime Day = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private static FolderItem Item(string fileName, FolderItemKind kind = FolderItemKind.Other, int daysAgo = 0, string? display = null) => new()
    {
        Path = @"D:\Cards\Projects\" + fileName,
        DisplayName = display ?? fileName,
        Kind = kind,
        AddedUtc = Day.AddDays(-daysAgo),
    };

    private static string[] Names(IEnumerable<FolderItem> items) => items.Select(i => i.FileName).ToArray();

    // ----- identity -----

    [Theory]
    [InlineData("Microsoft.Office.WINWORD.EXE.15", null, "word")]
    [InlineData("Chrome", null, "chrome")]
    [InlineData("MSEdge", null, "edge")]
    [InlineData("Claude_pzs8sxrjxfjjc!Claude", null, "claude")]
    [InlineData("OpenAI.Codex_2p2nqsd0c76g0!App", null, "chatgpt")]
    [InlineData("Anysphere.Cursor", null, "cursor")]
    [InlineData("com.moonshot.kimichat", null, "kimi")]
    [InlineData("QQ", null, "qq")]
    [InlineData(null, @"C:\Program Files\Tencent\Weixin\Weixin.exe", "wechat")]
    [InlineData(null, @"C:\Program Files\Tencent\WeMeet\WeMeetApp.exe", "tencentmeeting")]
    [InlineData(null, @"D:\DingDing\DingtalkLauncher.exe", "dingtalk")]
    [InlineData(null, @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE", "excel")]
    [InlineData(null, @"C:\Program Files\Microsoft Office\root\Office16\POWERPNT.EXE", "powerpoint")]
    [InlineData(null, @"c:\program files\google\chrome\application\CHROME.EXE", "chrome")]
    public void TheFirstBatchOf13_IsRecognised_ByAumidOrByExeName(string? aumid, string? exe, string expected)
        => Assert.Equal(expected, AppIconPack.Match(new AppIdentity(Aumid: aumid, ExePath: exe)));

    [Fact]
    public void TheTableHasThirteenApps_WithUniqueKeys()
    {
        Assert.Equal(13, AppIconPack.Rows.Count);
        Assert.Equal(13, AppIconPack.Rows.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    public void TheAumid_WinsOverTheExeName()
        => Assert.Equal("edge", AppIconPack.Match(new AppIdentity(Aumid: "MSEdge", ExePath: @"C:\x\chrome.exe")));

    [Fact]
    public void TheProduct_IsTheLastResort_AndNeedsBothHalves()
    {
        Assert.Equal("word", AppIconPack.Match(new AppIdentity(ExePath: @"C:\x\launcher.exe", ProductName: "Microsoft Office", FileDescription: "Microsoft Word")));
        // Office's ProductName alone says nothing: all three share it.
        Assert.Null(AppIconPack.Match(new AppIdentity(ExePath: @"C:\x\launcher.exe", ProductName: "Microsoft Office")));
        Assert.Null(AppIconPack.Match(new AppIdentity(ExePath: @"C:\x\launcher.exe", ProductName: "Microsoft Office", FileDescription: "Microsoft Access")));
    }

    [Fact]
    public void ADisplayName_IsNeverUsed()
    {
        // The WeChat input method and its uninstaller are not WeChat, and QQ Music is not QQ.
        Assert.Null(AppIconPack.Match(new AppIdentity(ExePath: @"C:\Program Files\Tencent\WeType\3.0.0.34\wetype_update.exe", ProductName: "微信输入法", FileDescription: "微信输入法")));
        Assert.Null(AppIconPack.Match(new AppIdentity(ExePath: @"D:\QQMusic\QQMusic.exe", ProductName: "QQ音乐", FileDescription: "QQ音乐 听我想听")));
        Assert.Null(AppIconPack.Match(new AppIdentity(Aumid: "com.kimi.code.desktop", ExePath: @"C:\Users\asus\AppData\Local\Programs\Kimi Code\Kimi Code.exe")));
        Assert.Null(AppIconPack.Match(new AppIdentity()));
    }

    [Fact]
    public void ARowCanBeAddedWithNoCode_ThroughTheTable()
    {
        var rows = new List<AppIconRow> { new("steamgame", [], [], [], ["431960"]) };
        Assert.Equal("steamgame", AppIconPack.Match(new AppIdentity(SteamId: "431960"), rows));
        Assert.Null(AppIconPack.Match(new AppIdentity(SteamId: "2622000"), rows));
    }

    [Theory]
    [InlineData("[InternetShortcut]\r\nURL=steam://rungameid/431960\r\nIconFile=C:\\x.ico\r\n", "431960")]
    [InlineData("[InternetShortcut]\nURL=steam://run/570/\n", "570")]
    [InlineData("[InternetShortcut]\r\nURL=https://example.com/\r\n", null)]
    [InlineData("[InternetShortcut]\r\nURL=steam://rungameid/\r\n", null)]
    [InlineData("", null)]
    public void ASteamGameId_IsReadFromAnUrlFile(string text, string? expected) => Assert.Equal(expected, AppIconPack.SteamId(text));

    // ----- kinds -----

    [Theory]
    [InlineData("a.docx", "Document")]
    [InlineData("a.PAGES", "Document")]
    [InlineData("a.csv", "Spreadsheet")]
    [InlineData("a.key", "Presentation")]
    [InlineData("a.pdf", "Pdf")]
    [InlineData("a.md", "Text")]
    [InlineData("a.ps1", "Code")]
    [InlineData("a.7z", "Archive")]
    [InlineData("a.flac", "Audio")]
    [InlineData("a.url", "WebLink")]
    [InlineData("a.heic", "Image")]
    [InlineData("a.mkv", "Video")]
    [InlineData("a.exe", "App")]
    [InlineData("a.appref-ms", "App")]
    [InlineData("a.xyz", "Other")]
    [InlineData("README", "Other")]
    public void TheExtension_DecidesTheType(string name, string expected) => Assert.Equal(expected, FolderKinds.OfFile(name).ToString());

    [Fact]
    public void TheLabelsOfDocumentTiles_AreTheExtensionInCapitals_AtMostFour()
    {
        Assert.Equal("DOCX", FolderKinds.ExtensionLabel("report.docx"));
        Assert.Equal("ODT", FolderKinds.ExtensionLabel("report.odt"));
        Assert.Equal("PAGE", FolderKinds.ExtensionLabel("report.pages"));
    }

    [Fact]
    public void AnOtherTile_ShowsTheExtensionAsItsSymbol_SizedByLength()
    {
        Assert.Equal("", FolderKinds.OtherSymbol("README"));
        Assert.Equal("C", FolderKinds.OtherSymbol("a.c"));
        Assert.Equal("ABCD", FolderKinds.OtherSymbol("a.abcd"));
        Assert.Equal("ABC…", FolderKinds.OtherSymbol("a.abcdef")); // more than 4: the first 3 and an ellipsis
        Assert.Equal(24, FolderKinds.OtherSymbolSize("A"));
        Assert.Equal(22, FolderKinds.OtherSymbolSize("AB"));
        Assert.Equal(19, FolderKinds.OtherSymbolSize("ABC"));
        Assert.Equal(15.5, FolderKinds.OtherSymbolSize("ABCD"));
        Assert.Equal(15.5, FolderKinds.OtherSymbolSize("ABC…"));
    }

    // ----- sorting -----

    [Fact]
    public void Newest_PutsTheMostRecentlyAddedFirst_ThenByName()
    {
        var items = new[] { Item("old.txt", daysAgo: 9), Item("b.txt", daysAgo: 1), Item("a.txt", daysAgo: 1), Item("new.txt", daysAgo: 0) };
        Assert.Equal(["new.txt", "a.txt", "b.txt", "old.txt"], Names(FolderSorter.Sort(items, FolderSort.Newest, [])));
    }

    [Fact]
    public void Name_UsesNaturalOrder_IgnoringCase()
    {
        var items = new[] { Item("file 10.txt"), Item("File 2.txt"), Item("file 1.txt"), Item("Beta.txt") };
        Assert.Equal(["Beta.txt", "file 1.txt", "File 2.txt", "file 10.txt"], Names(FolderSorter.Sort(items, FolderSort.Name, [])));
    }

    [Fact]
    public void Name_SortsByWhatTheUserReads()
    {
        // A shortcut's display name has no ".lnk": "Zoom" sorts as Z, not as "Zoom.lnk" vs "a.txt".
        var items = new[] { Item("Zoom.lnk", FolderItemKind.App, display: "Zoom"), Item("a.txt"), Item("Notes.lnk", FolderItemKind.App, display: "Notes") };
        Assert.Equal(["a.txt", "Notes.lnk", "Zoom.lnk"], Names(FolderSorter.Sort(items, FolderSort.Name, [])));
    }

    [Fact]
    public void Type_GroupsByTypeInTheOrderOfTheIcons_ThenByName()
    {
        var items = new[] { Item("z.pdf", FolderItemKind.Pdf), Item("m.docx", FolderItemKind.Document), Item("a.pdf", FolderItemKind.Pdf), Item("Docs", FolderItemKind.Folder), Item("x.bin") };
        Assert.Equal(["Docs", "m.docx", "a.pdf", "z.pdf", "x.bin"], Names(FolderSorter.Sort(items, FolderSort.Type, [])));
    }

    [Fact]
    public void Manual_KeepsTheSavedOrder_AndPutsNewArrivalsFirst()
    {
        var items = new[] { Item("b.txt", daysAgo: 5), Item("c.txt", daysAgo: 5), Item("a.txt", daysAgo: 5), Item("new1.txt", daysAgo: 2), Item("new2.txt", daysAgo: 0) };
        var sorted = FolderSorter.Sort(items, FolderSort.Manual, ["c.txt", "a.txt", "gone.txt", "b.txt"]);
        Assert.Equal(["new2.txt", "new1.txt", "c.txt", "a.txt", "b.txt"], Names(sorted));
    }

    [Fact]
    public void TheOptions_RoundTrip_AndFallBackToDefaults()
    {
        Assert.Equal(FolderSort.Name, FolderOptions.ParseSort(FolderOptions.SortText(FolderSort.Name)));
        Assert.Equal(FolderSort.Newest, FolderOptions.ParseSort(null));
        Assert.Equal(FolderSort.Newest, FolderOptions.ParseSort("nonsense"));
        var order = new[] { "a b.txt", "ü.txt", "中文.docx" };
        Assert.Equal(order, FolderOptions.ParseOrder(FolderOptions.OrderText(order)));
        Assert.Empty(FolderOptions.ParseOrder(null));
        Assert.Empty(FolderOptions.ParseOrder("{not json"));
    }

    // ----- origin log -----

    [Fact]
    public void TheOriginLog_RemembersWhereAnItemCameFrom_ButNotForAShortcutMadeHere()
    {
        var log = new OriginLog();
        log.Record("report.docx", @"C:\Users\asus\Desktop\report.docx", Day);
        log.Record("Notepad.lnk", null, Day.AddHours(1));

        Assert.Equal(@"C:\Users\asus\Desktop\report.docx", log.OriginOf("report.docx"));
        Assert.Null(log.OriginOf("Notepad.lnk")); // no Put back for it
        Assert.Equal(Day.AddHours(1), log.AddedUtc("Notepad.lnk"));
        Assert.Null(log.OriginOf("never.txt"));
        Assert.Equal(@"C:\Users\asus\Desktop\report.docx", log.OriginOf("REPORT.DOCX")); // Windows names ignore case
    }

    [Fact]
    public void TheOriginLog_FollowsARename_AndForgetsADeletedItem()
    {
        var log = new OriginLog();
        log.Record("a.txt", @"C:\x\a.txt", Day);
        log.Rename("a.txt", "b.txt");
        Assert.Null(log.OriginOf("a.txt"));
        Assert.Equal(@"C:\x\a.txt", log.OriginOf("b.txt"));
        log.Remove("b.txt");
        Assert.Equal(0, log.Count);
    }

    [Fact]
    public void TheOriginLog_DropsEntriesOfItemsThatAreGone()
    {
        var log = new OriginLog();
        log.Record("keep.txt", null, Day);
        log.Record("gone.txt", @"C:\x\gone.txt", Day);
        Assert.True(log.Prune(["keep.txt", "other.txt"]));
        Assert.Equal(1, log.Count);
        Assert.False(log.Prune(["keep.txt"]));
    }

    [Fact]
    public void TheOriginLog_SurvivesAJsonRoundTrip()
    {
        var log = new OriginLog();
        log.Record("文件.docx", @"C:\Users\asus\Desktop\文件.docx", Day);
        log.Record("x.lnk", null, Day);
        var back = OriginLog.FromJson(log.ToJson());
        Assert.Equal(@"C:\Users\asus\Desktop\文件.docx", back.OriginOf("文件.docx"));
        Assert.Null(back.OriginOf("x.lnk"));
        Assert.Equal(Day, back.AddedUtc("x.lnk"));
    }

    [Fact]
    public void EachFolder_HasItsOwnLogFile()
    {
        string a = OriginLog.PathFor(@"C:\Users\asus\Big Folder");
        Assert.Equal(a, OriginLog.PathFor(@"c:\users\asus\big folder\"));
        Assert.NotEqual(a, OriginLog.PathFor(@"C:\Users\asus\Big Folder (2)"));
    }

    // ----- pictures from the Shell -----

    [Fact]
    public void Premultiplying_ScalesTheColourByTheAlpha()
    {
        var pixels = new Pixels(2, 1, [200, 100, 50, 128, 10, 20, 30, 255]);
        pixels.Premultiply();
        Assert.Equal([100, 50, 25, 128, 10, 20, 30, 255], pixels.Bgra);
    }

    [Fact]
    public void AFullBleedIcon_IsAFilledSquare()
    {
        var solid = new Pixels(20, 20, Enumerable.Repeat((byte)255, 20 * 20 * 4).ToArray());
        Assert.True(solid.IsFullBleed());

        // A round icon is transparent at the corners.
        var round = new byte[20 * 20 * 4];
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                double dx = x - 9.5, dy = y - 9.5;
                round[(y * 20 + x) * 4 + 3] = dx * dx + dy * dy <= 9.5 * 9.5 ? (byte)255 : (byte)0;
            }
        }
        Assert.False(new Pixels(20, 20, round).IsFullBleed());
    }
}
