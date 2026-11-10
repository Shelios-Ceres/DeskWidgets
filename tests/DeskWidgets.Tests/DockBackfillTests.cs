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
    public void ApplyingAStartEntry_KeepsTheAumidAndAddsTheExe()
    {
        var item = AumidOnly();
        Assert.True(DockBackfill.Apply(item, Exe, null));
        Assert.Equal("com.anysphere.sand", item.Aumid);
        Assert.Equal(Exe, item.ExePath);
        Assert.Null(item.Arguments);
        Assert.Equal("aumid:com.anysphere.sand", item.Key); // the identity does not change, so custom tiles and running state still follow it
        Assert.Equal(new DockStart(DockStartKind.Exe, Exe), item.PlanStart());
        Assert.True(item.SupportsRunning);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void AnEntryWithNoRealArguments_IsTaken(string? arguments)
        => Assert.True(DockBackfill.Apply(AumidOnly(), Exe, arguments));

    // B5-1: a Start entry with arguments runs a launcher or a host exe with them. Real ones on the user's PC:
    // Sticky Notes -> ONENOTE.EXE "/memoryWindow start", a game -> its launcher with a game id, Windows SDK -> explorer.exe "<folder>",
    // a developer prompt -> cmd.exe /k ..., IDLE -> pythonw.exe idle.pyw.
    [Theory]
    [InlineData("/memoryWindow start")]
    [InlineData("nxl://launch/10100")]
    [InlineData("--game=hkrpg_cn")]
    [InlineData("/k vsdevcmd.bat")]
    [InlineData("idle.pyw")]
    public void AnEntryWithArguments_IsNotTaken_TheItemStaysAumidOnly(string arguments)
    {
        var item = AumidOnly("Microsoft.Office.OneNote.MemoryPreview");
        Assert.False(DockBackfill.Apply(item, Exe, arguments));
        Assert.Null(item.ExePath);
        Assert.Null(item.Arguments);
        Assert.True(DockBackfill.Wants(item)); // asked again on a later load: a cheap re-read
        Assert.Equal(new DockStart(DockStartKind.AppsFolder, @"shell:AppsFolder\Microsoft.Office.OneNote.MemoryPreview"), item.PlanStart());
        Assert.Equal(@"shell:AppsFolder\Microsoft.Office.OneNote.MemoryPreview", DockIconSource.PathFor(item)); // its own Start-entry icon, not the host's
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
        Assert.False(DockBackfill.Apply(item, target, null));
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

    // ----- a failed Start-entry icon is asked again (B5-3: without changing what is shown, and without growing the order) -----

    private static readonly IconRequest StartEntry = new(@"shell:AppsFolder\com.anysphere.sand", 64, IconFlavor.AppIcon, 0);
    private static readonly IconImage Picture = new(new Pixels(1, 1, [0xFF, 0, 0, 0xFF]), false);

    [Fact]
    public void AFailedStartEntryRequest_IsDueAfterTheDelay_AndNotBefore()
    {
        var cache = new IconResultCache(10);
        cache.Store(StartEntry, null, now: 1000);
        Assert.True(cache.TryGet(StartEntry, 1000 + IconResultCache.RetryFailedAfterMs - 1, out _, out bool early));
        Assert.False(early);
        Assert.True(cache.TryGet(StartEntry, 1000 + IconResultCache.RetryFailedAfterMs, out _, out bool due));
        Assert.True(due);
    }

    [Fact]
    public void ADueRetry_StillAnswersWithTheCachedFailure_SoTheTileDoesNotChange()
    {
        var cache = new IconResultCache(10);
        cache.Store(StartEntry, null, now: 0);
        bool hit = cache.TryGet(StartEntry, 10 * IconResultCache.RetryFailedAfterMs, out var image, out bool due);
        Assert.True(hit); // a hit: the caller shows what it showed, not a loading state
        Assert.Null(image);
        Assert.True(due);
        Assert.True(cache.Contains(StartEntry));
    }

    [Fact]
    public void AfterARetry_TheOrderStillHoldsOneEntryPerRequest()
    {
        var cache = new IconResultCache(10);
        cache.Store(StartEntry, null, now: 0);
        for (int retry = 1; retry <= 5; retry++)
            cache.Store(StartEntry, null, now: retry * IconResultCache.RetryFailedAfterMs); // each failed retry is stored again
        Assert.Equal(1, cache.OrderCount);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ARetryThatFailsAgain_WaitsAnotherFullDelay()
    {
        var cache = new IconResultCache(10);
        cache.Store(StartEntry, null, now: 0);
        cache.Store(StartEntry, null, now: 40_000);
        cache.TryGet(StartEntry, 40_000 + IconResultCache.RetryFailedAfterMs - 1, out _, out bool early);
        Assert.False(early);
    }

    [Fact]
    public void ARetryThatSucceeds_ReplacesTheFailure_AndIsNeverDueAgain()
    {
        var cache = new IconResultCache(10);
        cache.Store(StartEntry, null, now: 0);
        cache.Store(StartEntry, Picture, now: 31_000);
        Assert.True(cache.TryGet(StartEntry, 1_000_000, out var image, out bool due));
        Assert.Same(Picture, image);
        Assert.False(due);
    }

    [Fact]
    public void ARetryDoesNotEvictAnotherEntry_AndEvictionStillDropsTheOldest()
    {
        var cache = new IconResultCache(2);
        var other = new IconRequest(@"C:\a.exe", 64, IconFlavor.AppIcon, 1);
        var third = new IconRequest(@"C:\b.exe", 64, IconFlavor.AppIcon, 2);
        cache.Store(StartEntry, null, now: 0);
        cache.Store(other, Picture, now: 0);
        cache.Store(StartEntry, null, now: 40_000); // a retry while full: nothing leaves
        Assert.True(cache.Contains(other));
        cache.Store(third, Picture, now: 41_000); // a new request while full: the oldest (the Start entry) leaves
        Assert.False(cache.Contains(StartEntry));
        Assert.True(cache.Contains(other));
        Assert.Equal(2, cache.OrderCount);
    }

    [Fact]
    public void AFailedFileRequest_IsNeverRetried_SoABigFolderOfBrokenThumbnailsIsNotAskedAgain()
    {
        var cache = new IconResultCache(10);
        var broken = new IconRequest(@"C:\Pictures\broken.jpg", 64, IconFlavor.Thumbnail, 5);
        cache.Store(broken, null, now: 0);
        cache.TryGet(broken, 100 * IconResultCache.RetryFailedAfterMs, out _, out bool due);
        Assert.False(due);
    }

    [Fact]
    public void ARequestWhoseFallbackIsAStartEntry_IsRetriedToo()
    {
        var cache = new IconResultCache(10);
        var request = new IconRequest(@"C:\Chrome\chrome.exe", 64, IconFlavor.AppIcon, 5, @"shell:AppsFolder\Chrome");
        cache.Store(request, null, now: 0);
        cache.TryGet(request, IconResultCache.RetryFailedAfterMs, out _, out bool due);
        Assert.True(due);
    }
}
