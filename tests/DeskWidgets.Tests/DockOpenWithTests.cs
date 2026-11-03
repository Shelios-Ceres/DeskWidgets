using DeskWidgets.Dock;

namespace DeskWidgets.Tests;

/// <summary>Phase 15 Build C: which dock app a registered handler is (PROPOSAL 3.6, SPIKE 6), with strings from data/handlers.*.txt.</summary>
public class DockOpenWithTests
{
    private const string WordExe = @"C:\Program Files\Microsoft Office\Root\Office16\WINWORD.EXE";
    private const string DoubaoIcon = @"D:\Doubao\app\Doubao.exe,0";
    private const string PhotosIcon = "@{Microsoft.Windows.Photos_2026.11090.22001.0_x64__8wekyb3d8bbwe?ms-resource://Microsoft.Windows.Photos/Files/Assets/Retail/PhotosAppList.png},0";
    private const string ChatGptIcon = "@{OpenAI.Codex_26.1027.3384.0_x64__2p2nqsd0c76g0?ms-resource://OpenAI.Codex/Files/assets/Square44x44Logo.png},0";

    private static DockItem Exe(string path) => new() { Name = "App", ExePath = path };

    private static DockItem Store(string aumid) => new() { Name = "App", Aumid = aumid };

    // ----- the three forms -----

    [Fact]
    public void ANameThatIsTheExePath_Matches_IgnoringCase()
        => Assert.True(DockOpenWith.Matches(Exe(@"c:\program files\microsoft office\root\office16\winword.exe"), WordExe, WordExe + ",0"));

    [Fact]
    public void ADifferentNamedPath_DoesNotMatch()
        => Assert.False(DockOpenWith.Matches(Exe(WordExe), @"C:\Program Files\LibreOffice\program\swriter.exe", @"C:\Program Files\LibreOffice\program\swriter.exe,0"));

    [Fact]
    public void AnExeInTheIconLocation_Matches_WhenTheNameIsPlain()
        => Assert.True(DockOpenWith.Matches(Exe(@"d:\doubao\app\doubao.exe"), "豆包", DoubaoIcon));

    [Fact]
    public void AnExeInTheIconLocation_DoesNotMatchAnotherExe()
        => Assert.False(DockOpenWith.Matches(Exe(@"D:\Doubao\app\Other.exe"), "豆包", DoubaoIcon));

    [Fact]
    public void TheIconLocation_CanBeQuotedHaveNoIndexOrUseEnvironmentVariables()
    {
        string windir = Environment.GetEnvironmentVariable("WINDIR")!;
        string notepad = Path.Combine(windir, "notepad.exe");
        Assert.Equal(notepad, DockOpenWith.IconExe("\"" + notepad + "\",0"));
        Assert.Equal(notepad, DockOpenWith.IconExe(notepad));
        Assert.Equal(notepad, DockOpenWith.IconExe(@"%WINDIR%\notepad.exe,0"));
        Assert.Equal(@"C:\a,b\x.exe", DockOpenWith.IconExe(@"C:\a,b\x.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"shell32.dll,3")]
    [InlineData(@"@C:\Windows\x.dll,-100")]
    public void AnIconLocationWithoutAnAbsoluteExe_GivesNoPath(string location)
        => Assert.Null(DockOpenWith.IconExe(location));

    [Fact]
    public void AStoreAppMatches_ThroughThePackageNameInTheIconLocation()
    {
        Assert.True(DockOpenWith.Matches(Store("Microsoft.Windows.Photos_8wekyb3d8bbwe!App"), "照片", PhotosIcon));
        Assert.True(DockOpenWith.Matches(Store("openai.codex_2p2nqsd0c76g0!App"), "ChatGPT", ChatGptIcon)); // case-insensitive
    }

    [Fact]
    public void AStoreAppDoesNotMatch_AnotherPackage()
    {
        Assert.False(DockOpenWith.Matches(Store("Microsoft.Windows.Photos_8wekyb3d8bbwe!App"), "ChatGPT", ChatGptIcon));
        Assert.False(DockOpenWith.Matches(Store("Microsoft.Windows.Photos_8wekyb3d8bbwe!App"), "Word", WordExe + ",0"));
    }

    [Fact]
    public void AClassicProgram_DoesNotMatchAStoreHandler()
        => Assert.False(DockOpenWith.Matches(Exe(WordExe), "照片", PhotosIcon));

    [Fact]
    public void AStoreAppWithAnExePath_UsesTheExeForms()
    {
        var app = new DockItem { Name = "Paint", ExePath = @"C:\Program Files\WindowsApps\Microsoft.Paint_11.2605.81.0_x64__8wekyb3d8bbwe\PaintApp\mspaint.exe", Aumid = "Microsoft.Paint_8wekyb3d8bbwe!App" };
        Assert.True(DockOpenWith.Matches(app, app.ExePath, "@{Microsoft.Paint_11.2605.81.0_x64__8wekyb3d8bbwe?ms-resource://x.png},0"));
    }

    // ----- what never matches -----

    [Fact]
    public void AGroup_NeverMatches()
    {
        var group = new DockItem { Name = "G", GroupId = "g1", ExePath = WordExe, Children = [Exe(WordExe), Exe(@"C:\x.exe")] };
        Assert.False(DockOpenWith.Matches(group, WordExe, WordExe + ",0"));
    }

    [Fact]
    public void AnAppWithNoExeAndNoAumid_NeverMatches()
    {
        var link = new DockItem { Name = "Game", Url = "steam://rungameid/1" };
        Assert.False(DockOpenWith.Matches(link, "", ""));
        Assert.False(DockOpenWith.Matches(link, WordExe, PhotosIcon));
    }

    [Fact]
    public void AHandlerRecord_MatchesThroughTheThinOverload()
        => Assert.True(DockOpenWith.Matches(Exe(WordExe), new DockHandler(WordExe, "Word", WordExe + ",0")));

    // ----- package names -----

    [Theory]
    [InlineData(PhotosIcon, "Microsoft.Windows.Photos")]
    [InlineData(ChatGptIcon, "OpenAI.Codex")]
    [InlineData(@"C:\x.exe,0", null)]
    [InlineData("@{NoUnderscore?ms-resource://x}", null)]
    [InlineData("", null)]
    public void TheStorePackageName_IsWhatIsBeforeTheFirstUnderscore(string icon, string? expected)
        => Assert.Equal(expected, DockOpenWith.StorePackageName(icon));

    [Theory]
    [InlineData("Microsoft.Windows.Photos_8wekyb3d8bbwe!App", "Microsoft.Windows.Photos")]
    [InlineData("Chrome", null)]
    [InlineData(null, null)]
    public void TheAumidPackageName_IsWhatIsBeforeTheUnderscore(string? aumid, string? expected)
        => Assert.Equal(expected, DockOpenWith.AumidPackageName(aumid));

    // ----- extensions -----

    [Fact]
    public void Extensions_AreDistinctLowerCaseWithTheDotAndKeepAnEmptyOne()
    {
        var list = DockOpenWith.ExtensionsOf([@"C:\a\One.PNG", @"C:\a\two.png", @"D:\b\three.Docx", @"C:\a\README", @"C:\a\archive.tar.GZ"]);
        Assert.Equal([".png", ".docx", "", ".gz"], list);
    }

    [Fact]
    public void ExtensionsOfNothing_AreNone()
        => Assert.Empty(DockOpenWith.ExtensionsOf([]));

    [Fact]
    public void EnumeratingNoRealExtension_GivesNoHandlersWithoutAskingTheShell()
        => Assert.Empty(DockOpenWith.Enumerate([""]));
}
