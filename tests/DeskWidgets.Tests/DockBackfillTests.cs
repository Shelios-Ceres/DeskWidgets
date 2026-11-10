using DeskWidgets.Dock;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

/// <summary>Phase 16.1: a classic app saved with only its AUMID gets its exe path, the icon request order, and the retry of a failed Start-entry icon.</summary>
public class DockBackfillTests
{
    // A rooted program path on a fixed local drive (the temp folder is on one); the file need not exist.
    private static readonly string Exe = Path.Combine(Path.GetTempPath(), "Grok Bot", "Grok Bot.exe");

    private static DockItem AumidOnly(string aumid = "com.anysphere.sand") => new() { Name = "Grok Bot", Aumid = aumid };

    // ----- who asks for an exe -----

    [Fact]
    public void AnAumidOnlyClassicApp_WantsItsExe()
        => Assert.True(DockBackfill.Wants(AumidOnly()));

    [Theory]
    [InlineData("Claude_pzs8sxrjxfjjc!Claude")] // a Store app: its exe is in a package folder
    [InlineData("OpenAI.Codex_2p2nqsd0c76g0!App")]
    public void AStoreApp_NeverWantsAnExe(string aumid)
        => Assert.False(DockBackfill.Wants(AumidOnly(aumid)));

    [Fact]
    public void AnItemThatHasAnExeAlready_AValueLinkAndAGroup_WantNothing()
    {
        Assert.False(DockBackfill.Wants(new DockItem { Name = "x", Aumid = "Chrome", ExePath = @"C:\Chrome\chrome.exe" }));
        Assert.False(DockBackfill.Wants(new DockItem { Name = "Game", Url = "steam://rungameid/1" }));
        Assert.False(DockBackfill.Wants(new DockItem { Name = "g", GroupId = "g", Children = [AumidOnly()] }));
        Assert.False(DockBackfill.Wants(new DockItem { Name = "none" }));
    }

    // ----- what is taken -----

    [Fact]
    public void ApplyingAStartEntry_KeepsTheAumidAndAddsTheExeAndArguments()
    {
        var item = AumidOnly();
        Assert.True(DockBackfill.Apply(item, Exe, "--profile=work"));
        Assert.Equal("com.anysphere.sand", item.Aumid);
        Assert.Equal(Exe, item.ExePath);
        Assert.Equal("--profile=work", item.Arguments);
        Assert.Equal("aumid:com.anysphere.sand", item.Key); // the identity does not change, so custom tiles and running state still follow it
        // Starting goes through the exe with the shortcut's own arguments, as the Start entry would.
        Assert.Equal(new DockStart(DockStartKind.Exe, Exe, "--profile=work"), item.PlanStart());
        Assert.True(item.SupportsRunning);
    }

    [Fact]
    public void AnEntryWithNoArguments_LeavesThemNull()
    {
        var item = AumidOnly();
        Assert.True(DockBackfill.Apply(item, Exe, "  "));
        Assert.Null(item.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\\server\share\app.exe")] // a network path would be asked for its icon with the user's credentials
    [InlineData(@"C:\Docs\readme.txt")] // not a program
    [InlineData("app.exe")] // not rooted
    public void AnUnusableTarget_ChangesNothing(string? target)
    {
        var item = AumidOnly();
        Assert.False(DockBackfill.Apply(item, target, "--x"));
        Assert.Null(item.ExePath);
        Assert.Null(item.Arguments);
    }

    [Fact]
    public void ApplyingTwice_ChangesNothingTheSecondTime()
    {
        var item = AumidOnly();
        Assert.True(DockBackfill.Apply(item, Exe, null));
        Assert.False(DockBackfill.Apply(item, Path.Combine(Path.GetTempPath(), "other.exe"), null));
        Assert.Equal(Exe, item.ExePath);
    }

    [Fact]
    public void AStoreAppIsNeverChanged_EvenWithATarget()
    {
        var item = AumidOnly("Claude_pzs8sxrjxfjjc!Claude");
        Assert.False(DockBackfill.Apply(item, Exe, null));
        Assert.Null(item.ExePath);
        Assert.True(item.IsStoreApp);
    }

    // ----- the backfill decision over a whole dock -----

    [Fact]
    public void TheDecision_FindsTheAumidOnlyProgramsIncludingThoseInGroups_AndNoOtherItem()
    {
        var grok = AumidOnly();
        var antigravity = AumidOnly("electron.app.Antigravity");
        var chrome = new DockItem { Name = "Chrome", Aumid = "Chrome", ExePath = @"C:\Chrome\chrome.exe" };
        var claude = AumidOnly("Claude_pzs8sxrjxfjjc!Claude");
        var group = new DockItem { Name = "AI", GroupId = "g1", Children = [antigravity, claude] };

        var needing = DockBackfill.Needing([grok, chrome, group]);

        Assert.Equal(new[] { grok, antigravity }, needing);
    }

    [Fact]
    public void AfterTheBackfill_NothingIsLeftToLookUp_SoTheNextLoadDoesNotAskAgain()
    {
        var items = new List<DockItem> { AumidOnly(), AumidOnly("electron.app.Antigravity") };
        foreach (var item in DockBackfill.Needing(items))
            Assert.True(DockBackfill.Apply(item, Exe, null));
        Assert.Empty(DockBackfill.Needing(items));
    }

    [Fact]
    public void TheBackfilledFieldsAreWhatIsSaved_AndOnlyThose()
    {
        var item = AumidOnly();
        DockBackfill.Apply(item, Exe, null);
        string json = System.Text.Json.JsonSerializer.Serialize(item);
        Assert.Contains("\"Aumid\"", json);
        Assert.Contains("\"ExePath\"", json);
        Assert.DoesNotContain("Arguments", json);
        Assert.DoesNotContain("WorkingDir", json);
        Assert.DoesNotContain("Url", json);
    }

    // ----- the icon request order -----

    [Fact]
    public void AnExe_IsAskedFirst_AndItsStartEntryIsTheFallback()
    {
        var item = new DockItem { Name = "x", Aumid = "Chrome", ExePath = @"C:\Chrome\chrome.exe" };
        Assert.Equal(@"C:\Chrome\chrome.exe", DockIconSource.PathFor(item));
        Assert.Equal(@"shell:AppsFolder\Chrome", DockIconSource.FallbackFor(item));
    }

    [Fact]
    public void AnAumidOnlyItem_AsksItsStartEntry_BeforeTheGenericTile()
    {
        var item = AumidOnly();
        Assert.Equal(@"shell:AppsFolder\com.anysphere.sand", DockIconSource.PathFor(item));
        Assert.Null(DockIconSource.FallbackFor(item)); // nothing else to try
    }

    [Fact]
    public void AnItemWithNoIdentityAtAll_AsksForNothing_SoTheGenericTileShows()
    {
        var item = new DockItem { Name = "x" };
        Assert.Null(DockIconSource.PathFor(item));
        Assert.Null(DockIconSource.FallbackFor(item));
    }

    [Fact]
    public void ALinkWithAnIconFile_AsksThatFile_ButNeverARemoteOne()
    {
        string icon = Path.Combine(Path.GetTempPath(), "game.ico");
        Assert.Equal(icon, DockIconSource.PathFor(new DockItem { Name = "g", Url = "steam://rungameid/1", IconPath = icon }));
        Assert.Null(DockIconSource.PathFor(new DockItem { Name = "g", Url = "steam://rungameid/1", IconPath = @"\\server\share\game.ico" }));
    }

    // ----- a failed Start-entry icon is asked again -----

    [Fact]
    public void AFailedStartEntryRequest_IsRetriedAfterTheDelay_AndNotBefore()
    {
        var request = new IconRequest(@"shell:AppsFolder\com.anysphere.sand", 64, IconFlavor.AppIcon, 0);
        Assert.False(IconService.ShouldRetry(request, IconService.RetryFailedAfterMs - 1));
        Assert.True(IconService.ShouldRetry(request, IconService.RetryFailedAfterMs));
    }

    [Fact]
    public void ARequestWhoseFallbackIsAStartEntry_IsRetriedToo()
    {
        var request = new IconRequest(@"C:\Chrome\chrome.exe", 64, IconFlavor.AppIcon, 5, @"shell:AppsFolder\Chrome");
        Assert.True(IconService.ShouldRetry(request, IconService.RetryFailedAfterMs + 1));
    }

    [Fact]
    public void AFailedFileRequest_IsNeverRetried_SoABigFolderOfBrokenThumbnailsIsNotAskedAgain()
    {
        var request = new IconRequest(@"C:\Pictures\broken.jpg", 64, IconFlavor.Thumbnail, 5);
        Assert.False(IconService.ShouldRetry(request, 10 * IconService.RetryFailedAfterMs));
    }
}
