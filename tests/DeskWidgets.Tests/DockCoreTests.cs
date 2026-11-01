using DeskWidgets.Dock;
using DeskWidgets.Hosting;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

/// <summary>Phase 15.3: the dock's pure logic: the bar's numbers, where it sits, when it hides, what a program is, and what is saved.</summary>
public class DockCoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deskwidgets-dock-" + Guid.NewGuid().ToString("N"));

    public DockCoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ----- the bar's length and the three sizes -----

    [Theory]
    [InlineData(0, 1, 64)] // sizes: 0 Small, 1 Medium, 2 Large
    [InlineData(1, 1, 72)]
    [InlineData(2, 1, 88)]
    [InlineData(1, 3, 192)] // 24 + 3 x 48 + 2 x 12
    [InlineData(0, 12, 636)] // 24 + 12 x 40 + 11 x 12
    public void TheBarsLength_IsPaddingTwiceIconsAndGaps(int size, int count, double expected)
        => Assert.Equal(expected, DockGeometry.BarLengthDip((DockIconSize)size, count));

    [Theory]
    [InlineData(0, 40, 64, 240)]
    [InlineData(1, 48, 72, 280)]
    [InlineData(2, 64, 88, 340)]
    public void TheThreeSizes_AreIconSizesWithTheirBarHeightsAndEmptyLengths(int sizeIndex, double icon, double height, double empty)
    {
        var size = (DockIconSize)sizeIndex;
        Assert.Equal(icon, DockGeometry.IconDip(size));
        Assert.Equal(height, DockGeometry.BarHeightDip(size));
        Assert.Equal(empty, DockGeometry.BarLengthDip(size, 0));
    }

    [Fact]
    public void PixelAndGlassBars_AreTheSameLengthAtEverySizeAndCount()
    {
        // Art pixel = 2 DIPs: a theme switch never changes the dock's length (HANDOFFs, shared geometry).
        foreach (var size in Enum.GetValues<DockIconSize>())
        {
            for (int count = 0; count <= 20; count++)
            {
                Assert.Equal(DockGeometry.BarLengthDip(size, count), DockGeometry.PixelBarLengthArt(size, count) * 2.0);
            }
            Assert.Equal(DockGeometry.BarHeightDip(size), DockGeometry.PixelBarHeightArt(size) * 2.0);
        }
    }

    [Fact]
    public void TheIconUnderThePointer_IsFoundInTheIconAndNotInTheGap()
    {
        var size = DockIconSize.Medium; // icons at 12..60, 72..120, 132..180
        Assert.Equal(0, DockGeometry.IconAt(size, 3, 30));
        Assert.Equal(1, DockGeometry.IconAt(size, 3, 90));
        Assert.Equal(-1, DockGeometry.IconAt(size, 3, 66)); // in the gap
        Assert.Equal(-1, DockGeometry.IconAt(size, 3, 5)); // in the padding
        Assert.Equal(2, DockGeometry.IconAt(size, 3, 179));
    }

    [Fact]
    public void ADraggedIconsSlot_FollowsTheMiddlesOfTheOthers()
    {
        var size = DockIconSize.Medium; // middles at 36, 96, 156
        Assert.Equal(0, DockGeometry.SlotAt(size, 3, 10));
        Assert.Equal(0, DockGeometry.SlotAt(size, 3, 36));
        Assert.Equal(1, DockGeometry.SlotAt(size, 3, 37));
        Assert.Equal(2, DockGeometry.SlotAt(size, 3, 120));
        Assert.Equal(3, DockGeometry.SlotAt(size, 3, 500));
        Assert.Equal(0, DockGeometry.SlotAt(size, 0, 50));
    }

    // ----- where the bar sits -----

    private static readonly DockRect Work = new(0, 48, 2560, 1552); // the user's screen: the taskbar is at the top

    [Fact]
    public void TheBar_IsCentredOnTheBottomEdgeAboveTheGap()
    {
        var layout = DockPlacement.Compute(Work, 1600, barLength: 612, barHeight: 108, headroom: 63, edgeGap: 12, labelOverhang: 180, offset: 0);
        Assert.Equal(new DockRect((2560 - 612) / 2, 1600 - 12 - 108, 612, 108), layout.Bar);
        Assert.Equal(layout.Bar.Y - 63, layout.Window.Y);
        Assert.Equal(63 + 108, layout.Window.Height);
        Assert.Equal(612 + 360, layout.Window.Width); // room for a label at either end
        Assert.Equal(1600, layout.HiddenTop);
    }

    [Fact]
    public void ASlidBar_StaysWhereItWasPutButNeverLeavesTheScreen()
    {
        var inside = DockPlacement.Compute(Work, 1600, 612, 108, 63, 12, 180, offset: 300);
        Assert.Equal((2560 - 612) / 2 + 300, inside.Bar.X);
        Assert.Equal(300, inside.Offset);

        var far = DockPlacement.Compute(Work, 1600, 612, 108, 63, 12, 180, offset: 100000);
        Assert.Equal(2560 - 12 - 612, far.Bar.X); // 12 px from the right edge
        var farLeft = DockPlacement.Compute(Work, 1600, 612, 108, 63, 12, 180, offset: -100000);
        Assert.Equal(12, farLeft.Bar.X);
        Assert.True(far.Window.Right <= Work.Right && farLeft.Window.X >= Work.X);
    }

    [Fact]
    public void ABarLongerThanTheScreen_StaysCentred()
    {
        Assert.Equal(0, DockPlacement.MaxOffset(workWidth: 800, barLength: 900, margin: 12));
        Assert.Equal(0, DockPlacement.ClampOffset(50, 800, 900, 12));
    }

    [Fact]
    public void TheBarsFloor_FollowsTheWorkAreaNotTheMonitor()
    {
        // A taskbar at the bottom lowers the work area's bottom: the dock sits above the taskbar, and hides below the monitor's edge.
        var work = new DockRect(0, 0, 2560, 1552);
        var layout = DockPlacement.Compute(work, 1600, 612, 108, 63, 12, 180, 0);
        Assert.Equal(1552 - 12 - 108, layout.Bar.Y);
        Assert.Equal(1600, layout.HiddenTop);
    }

    // ----- when it hides -----

    [Theory]
    [InlineData(true, false, 5, true)] // a maximized window hides it
    [InlineData(false, false, 5, false)]
    [InlineData(false, true, 5, false)] // covering the screen alone is not full screen
    [InlineData(false, false, 2, false)] // BUSY alone (an overlay) is not full screen
    [InlineData(false, true, 2, true)] // a window over the whole screen while the Shell says busy
    [InlineData(false, true, 3, true)]
    [InlineData(false, true, 4, true)]
    [InlineData(true, true, 5, true)]
    public void TheDockHides_ForAMaximizedWindowOrAFullScreenOne(bool maximized, bool covers, int state, bool hides)
        => Assert.Equal(hides, DockVisibility.ShouldHide(maximized, covers, state));

    [Fact]
    public void TheEdge_BringsNothingBackOverAFullScreenWindow()
    {
        Assert.False(DockVisibility.MayReveal(foregroundCoversScreen: true, notificationState: 3));
        Assert.True(DockVisibility.MayReveal(foregroundCoversScreen: true, notificationState: 5));
        Assert.True(DockVisibility.MayReveal(foregroundCoversScreen: false, notificationState: 2));
    }

    // ----- what a program is, and how it starts -----

    [Fact]
    public void AnExe_IsStartedByItsPathWithTheArgumentsAndFolderItHad()
    {
        var item = new DockItem { Name = "Edge", ExePath = @"C:\Edge\msedge.exe", Arguments = "--profile-directory=Default", WorkingDir = @"C:\Edge", Aumid = "MSEdge" };
        var start = item.PlanStart();
        Assert.Equal(DockStartKind.Exe, start.Kind);
        Assert.Equal(@"C:\Edge\msedge.exe", start.Target);
        Assert.Equal("--profile-directory=Default", start.Arguments);
        Assert.Equal(@"C:\Edge", start.WorkingDir);
        Assert.False(item.IsStoreApp);
    }

    [Fact]
    public void AStoreApp_IsActivatedByItsAumidAndGetsNoRunAsAdministrator()
    {
        var item = new DockItem { Name = "Claude", Aumid = "Claude_pzs8sxrjxfjjc!Claude" };
        Assert.True(item.IsStoreApp);
        Assert.Equal(new DockStart(DockStartKind.Activate, "Claude_pzs8sxrjxfjjc!Claude"), item.PlanStart());
    }

    [Fact]
    public void AClassicStartEntryWithNoPath_GoesThroughTheApplicationsFolder()
    {
        var item = new DockItem { Name = "Word", Aumid = "Microsoft.Office.WINWORD.EXE.15" };
        Assert.False(item.IsStoreApp);
        Assert.Equal(new DockStart(DockStartKind.AppsFolder, @"shell:AppsFolder\Microsoft.Office.WINWORD.EXE.15"), item.PlanStart());
    }

    [Fact]
    public void ASteamLink_IsStartedAsAUrl_AndMatchesItsIconBySteamId()
    {
        var item = new DockItem { Name = "Game", Url = "steam://rungameid/431960" };
        Assert.Equal(new DockStart(DockStartKind.Url, "steam://rungameid/431960"), item.PlanStart());
        Assert.Equal("431960", item.Identity().SteamId);
    }

    [Fact]
    public void AnItemWithNothing_HasNothingToStart()
        => Assert.Equal(DockStartKind.None, new DockItem { Name = "x" }.PlanStart().Kind);

    [Fact]
    public void TheSameProgram_HasOneKey_NoMatterHowItWasDropped()
    {
        var fromStart = new DockItem { Name = "Chrome", Aumid = "Chrome" };
        var fromShortcut = new DockItem { Name = "Google Chrome", Aumid = "chrome", ExePath = @"C:\Chrome\chrome.exe" };
        var other = new DockItem { Name = "Notepad", ExePath = @"C:\Windows\notepad.exe" };
        Assert.Equal(fromStart.Key, fromShortcut.Key);
        Assert.NotEqual(fromStart.Key, other.Key);
        Assert.Equal(other.Key, new DockItem { Name = "n", ExePath = @"c:\windows\NOTEPAD.EXE" }.Key);
    }

    // ----- what is dropped -----

    [Theory]
    [InlineData(@"C:\Apps\tool.exe", false, true)]
    [InlineData(@"C:\Users\me\Desktop\Word.lnk", false, true)]
    [InlineData(@"C:\Games\Game.url", false, true)]
    [InlineData(@"C:\Tools\run.bat", false, true)]
    [InlineData(@"C:\Users\me\notes.docx", false, false)]
    [InlineData(@"C:\Users\me\photo.png", false, false)]
    [InlineData(@"C:\Users\me\Projects", true, false)]
    public void ADrop_IsAProgramCandidateOnlyIfItLooksLikeOne(string path, bool isFolder, bool candidate)
        => Assert.Equal(candidate ? DockDropKind.Candidate : DockDropKind.NotAProgram, DockIdentity.Classify(new DropEntry(path, isFolder, Path.GetFileName(path))));

    [Fact]
    public void AStartOrApplicationsFolderItem_HasNoPathAndIsACandidate()
        => Assert.Equal(DockDropKind.Candidate, DockIdentity.Classify(new DropEntry(null, false, "Word")));

    [Fact]
    public void AParsingName_IsASteamAddressAKnownFolderPathOrAnAumid()
    {
        var steam = DockIdentity.FromParsingName("steam://rungameid/2622000", "吉星派对");
        Assert.Equal("steam://rungameid/2622000", steam.Url);
        Assert.Null(steam.Aumid);

        var store = DockIdentity.FromParsingName("Claude_pzs8sxrjxfjjc!Claude", "Claude");
        Assert.Equal("Claude_pzs8sxrjxfjjc!Claude", store.Aumid);
        Assert.True(store.IsStoreApp);

        var split = DockIdentity.SplitKnownFolderName(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Tencent\Weixin\Weixin.exe");
        Assert.NotNull(split);
        Assert.Equal(new Guid("6D809377-6AF0-444B-8957-A3773F02200E"), split.Value.Folder);
        Assert.Equal(@"Tencent\Weixin\Weixin.exe", split.Value.Remainder);
        Assert.Null(DockIdentity.SplitKnownFolderName("Chrome"));
    }

    [Fact]
    public void AnExeThatIsNotThere_IsStillAProgram_NamedByItsFile()
    {
        string path = Path.Combine(_folder, "Gone.exe");
        var item = DockIdentity.FromPath(path);
        Assert.NotNull(item);
        Assert.Equal("Gone", item!.Name);
        Assert.Equal(path, item.ExePath);
        Assert.Null(DockIdentity.FromPath(Path.Combine(_folder, "notes.txt")));
    }

    [Fact]
    public void ASteamUrlFile_IsAProgram_AndAWebLinkIsNot()
    {
        string steam = Path.Combine(_folder, "Game.url");
        File.WriteAllText(steam, "[InternetShortcut]\r\nURL=steam://rungameid/431960\r\nIconFile=x.ico\r\n");
        var item = DockIdentity.FromUrlFile(steam);
        Assert.NotNull(item);
        Assert.Equal("steam://rungameid/431960", item!.Url);
        Assert.Equal("Game", item.Name);
        Assert.Equal("x.ico", item.IconPath);

        string web = Path.Combine(_folder, "Site.url");
        File.WriteAllText(web, "[InternetShortcut]\r\nURL=https://example.com/\r\n");
        Assert.Null(DockIdentity.FromUrlFile(web));
    }

    // ----- the taskbar's pins -----

    private static byte[] Entry(int size, byte fill = 7)
    {
        var bytes = new byte[4 + size];
        BitConverter.GetBytes((uint)size).CopyTo(bytes, 0);
        Array.Fill(bytes, fill, 4, size);
        return bytes;
    }

    [Fact]
    public void FavoritesResolve_SplitsIntoItsShortcutStreams_InOrder()
    {
        // A synthetic value (never a copy of anyone's registry): three entries of 100, 200 and 76 bytes.
        byte[] blob = [.. Entry(100, 1), .. Entry(200, 2), .. Entry(76, 3)];
        var entries = TaskbandPins.Split(blob);
        Assert.Equal([100, 200, 76], entries.Select(e => e.Length));
        Assert.All(entries[0], b => Assert.Equal(1, b));
        Assert.All(entries[1], b => Assert.Equal(2, b));
        Assert.All(entries[2], b => Assert.Equal(3, b));
    }

    [Fact]
    public void ADamagedOrNewerValue_GivesWhatWasReadSoFar_NeverAnException()
    {
        byte[] good = Entry(100);
        Assert.Empty(TaskbandPins.Split([]));
        Assert.Empty(TaskbandPins.Split([1, 2, 3]));
        Assert.Single(TaskbandPins.Split([.. good, .. new byte[] { 9, 9 }])); // trailing bytes: ignored
        Assert.Single(TaskbandPins.Split([.. good, .. Entry(500)[..300]])); // a size that runs past the end stops the list
        Assert.Empty(TaskbandPins.Split(Entry(10))); // smaller than a shortcut header
        byte[] huge = new byte[8];
        BitConverter.GetBytes(uint.MaxValue).CopyTo(huge, 0);
        Assert.Empty(TaskbandPins.Split(huge));
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Google Chrome.lnk", true)]
    [InlineData(@"c:\users\me\appdata\roaming\microsoft\internet explorer\quick launch\user pinned\taskbar\Kimi.lnk", true)]
    [InlineData(@"C:\Program Files\Google\Chrome\chrome.exe", false)]
    [InlineData(null, false)]
    public void APinsTarget_IsAFileInTheTaskbarsFolderOrNot(string? path, bool expected)
        => Assert.Equal(expected, TaskbandPins.IsPinnedFolderFile(path));

    // ----- what is saved -----

    [Fact]
    public void ADock_RoundTripsThroughTheSettingsFile()
    {
        var store = new SettingsStore(_folder);
        var dock = new DockSettings { Monitor = @"\\.\DISPLAY1", OffsetDip = -120.5, IconSize = DockIconSize.Large };
        dock.Items.Add(new DockItem { Name = "Edge", ExePath = @"C:\Edge\msedge.exe", Arguments = "--x", WorkingDir = @"C:\Edge", Aumid = "MSEdge" });
        dock.Items.Add(new DockItem { Name = "Claude", Aumid = "Claude_pzs8sxrjxfjjc!Claude" });
        dock.Items.Add(new DockItem { Name = "Game", Url = "steam://rungameid/1" });
        store.Current.Docks.Add(dock);
        Assert.True(store.Save());

        var loaded = SettingsStore.Peek(_folder).Docks;
        var back = Assert.Single(loaded);
        Assert.Equal(dock.Id, back.Id);
        Assert.Equal(@"\\.\DISPLAY1", back.Monitor);
        Assert.Equal(-120.5, back.OffsetDip);
        Assert.Equal(DockIconSize.Large, back.IconSize);
        Assert.Equal(3, back.Items.Count);
        Assert.Equal(("Edge", @"C:\Edge\msedge.exe", "--x", @"C:\Edge", "MSEdge"), (back.Items[0].Name, back.Items[0].ExePath, back.Items[0].Arguments, back.Items[0].WorkingDir, back.Items[0].Aumid));
        Assert.Null(back.Items[1].ExePath);
        Assert.Equal("steam://rungameid/1", back.Items[2].Url);
    }

    [Fact]
    public void ASettingsFileFromBeforeTheDock_OpensWithNoDocks()
    {
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{ \"Version\": 1, \"Initialized\": true, \"Widgets\": [] }");
        Assert.Empty(SettingsStore.Peek(_folder).Docks);
    }
}
