using DeskWidgets.Widgets.Folder;
using DeskWidgets.Dock;

namespace DeskWidgets.Tests;

/// <summary>Review-Main's batch 2 on the Dock (B2-1, B2-3, B2-16): the pure rules behind the fixes.</summary>
public class DockBatch2Tests
{
    private static readonly DockRect Work = new(0, 0, 2560, 1560);

    // ----- B2-16: the strip is only the stretch under the dock -----

    [Fact]
    public void TheStrip_IsUnderTheBarPlusAMarginEachSide_AtTheWorkAreasBottom()
    {
        var bar = new DockRect(1000, 1400, 560, 100);
        var strip = DockVisibility.EdgeStrip(Work, bar, 9, 90);
        Assert.Equal(new DockRect(910, 1551, 740, 9), strip);
    }

    [Fact]
    public void TheStrip_StaysInsideTheWorkArea_WhenTheBarIsAtAnEnd()
    {
        var atLeft = DockVisibility.EdgeStrip(Work, new DockRect(12, 1400, 560, 100), 9, 90);
        Assert.Equal(0, atLeft.X);
        var atRight = DockVisibility.EdgeStrip(Work, new DockRect(2560 - 572, 1400, 560, 100), 9, 90);
        Assert.Equal(2560, atRight.Right);
    }

    [Fact]
    public void ThePointerOffTheStretch_IsNotOnTheStrip()
    {
        var bar = new DockRect(1000, 1400, 560, 100);
        var strip = DockVisibility.EdgeStrip(Work, bar, 9, 90);
        Assert.False(strip.Contains(100, 1555)); // the bottom-left corner brings nothing up
        Assert.True(strip.Contains(1280, 1555));
    }

    // ----- B2-1: a dock that came up stays only while the pointer is on the bar or the strip, or something is in use -----

    [Theory]
    [InlineData(false, 1280, 1450, true)] // over the bar
    [InlineData(false, 1280, 1555, true)] // resting on the strip
    [InlineData(false, 100, 1200, false)] // away from both
    [InlineData(true, 100, 1200, true)] // a menu or a drag is in use
    [InlineData(false, 1280, 1530, false)] // between the strip and the bar's bottom: neither
    public void ARevealedDock_StaysOnlyWhileTheBarOrTheStripHasThePointer(bool inUse, int x, int y, bool stays)
    {
        var bar = new DockRect(1000, 1400, 560, 100);
        var strip = DockVisibility.EdgeStrip(Work, bar, 9, 90);
        Assert.Equal(stays, DockVisibility.StaysRevealed(inUse, bar, strip, x, y));
    }

    // ----- B2-3: one dock per screen, nothing is ever dropped -----

    private static readonly string[] TwoScreens = [@"\\.\DISPLAY1", @"\\.\DISPLAY2"];

    [Fact]
    public void EachDockOnItsOwnScreen_Opens()
    {
        var plan = DockScreens.Plan([@"\\.\DISPLAY1", @"\\.\DISPLAY2"], TwoScreens, @"\\.\DISPLAY1");
        Assert.All(plan, p => Assert.Equal(DockFate.Open, p.Fate));
    }

    [Fact]
    public void TwoDocksSavedForOneScreen_MergeIntoTheFirst()
    {
        var plan = DockScreens.Plan([@"\\.\DISPLAY2", @"\\.\display2"], TwoScreens, @"\\.\DISPLAY1");
        Assert.Equal((DockFate.Open, -1), plan[0]);
        Assert.Equal((DockFate.Merge, 0), plan[1]);
    }

    [Fact]
    public void ANullScreen_MeansThePrimary()
    {
        var plan = DockScreens.Plan([null, @"\\.\DISPLAY1"], TwoScreens, @"\\.\DISPLAY1");
        Assert.Equal((DockFate.Open, -1), plan[0]);
        Assert.Equal((DockFate.Merge, 0), plan[1]);
    }

    [Fact]
    public void ADockWhoseScreenIsGone_FallsBackToThePrimary_OrWaitsWhenThatIsTaken()
    {
        // Screen 2 is unplugged. The dock saved for it takes the primary only when the primary has no dock of its own.
        string[] one = [@"\\.\DISPLAY1"];
        var free = DockScreens.Plan([@"\\.\DISPLAY2"], one, @"\\.\DISPLAY1");
        Assert.Equal(DockFate.Open, free[0].Fate);

        var taken = DockScreens.Plan([@"\\.\DISPLAY2", @"\\.\DISPLAY1"], one, @"\\.\DISPLAY1");
        Assert.Equal(DockFate.Dormant, taken[0].Fate); // the dock that belongs on the primary goes first
        Assert.Equal(DockFate.Open, taken[1].Fate);
    }

    [Fact]
    public void TwoDocksWhoseScreensAreBothGone_DoNotOverlap_TheSecondWaits()
    {
        string[] one = [@"\\.\DISPLAY1"];
        var plan = DockScreens.Plan([@"\\.\DISPLAY2", @"\\.\DISPLAY3"], one, @"\\.\DISPLAY1");
        Assert.Equal(DockFate.Open, plan[0].Fate);
        Assert.Equal(DockFate.Dormant, plan[1].Fate);
    }

    [Fact]
    public void ADormantDock_OpensAgainWhenItsScreenComesBack()
    {
        var plan = DockScreens.Plan([@"\\.\DISPLAY2", @"\\.\DISPLAY1"], TwoScreens, @"\\.\DISPLAY1");
        Assert.All(plan, p => Assert.Equal(DockFate.Open, p.Fate));
    }

    // ----- B2-4 / B2-11: an opening state is taken back when the start fails -----

    [Fact]
    public void AStartThatFails_TakesTheOpeningStateBack_AtOnce()
    {
        var states = new DockRunStates();
        states.Started("word", TimeSpan.Zero);
        Assert.True(states.Cancel("word"));
        Assert.Equal(DockRunState.Idle, states.StateOf("word"));
        Assert.Null(states.NextExpiry());
        Assert.False(states.Cancel("word")); // nothing left to cancel
    }

    [Fact]
    public void ARunningProgram_IsNotCancelled()
    {
        var states = new DockRunStates();
        states.WindowsNow(new HashSet<string> { "chrome" }, ["chrome"]);
        Assert.False(states.Cancel("chrome"));
        Assert.Equal(DockRunState.Running, states.StateOf("chrome"));
    }
}

/// <summary>B2-12: the custom-tile key is found off the UI thread, and a quick match never reads a file.</summary>
public class DockCustomKeysTests
{
    private static AppIdentity Never(string path) => throw new InvalidOperationException("the exe must not be read");

    [Fact]
    public void TheQuickMatch_ReadsNoFile()
    {
        var item = new DockItem { Name = "Chrome", Aumid = "Chrome", ExePath = @"C:\x\chrome.exe" };
        Assert.Equal("chrome", DockCustomKeys.Quick(item));
        Assert.Equal("chrome", DockCustomKeys.Resolve(item, Never));
    }

    [Fact]
    public void TheExeName_IsMatchedIgnoringCase()
        => Assert.Equal("word", DockCustomKeys.Quick(new DockItem { Name = "Word", ExePath = @"C:\x\WINWORD.EXE" }));

    [Fact]
    public void AnItemWithNoExe_IsNeverRead()
    {
        Assert.Null(DockCustomKeys.Resolve(new DockItem { Name = "Some app", Aumid = "Vendor.App_x!App" }, Never));
        Assert.Null(DockCustomKeys.Resolve(new DockItem { Name = "Game", Url = "steam://rungameid/1" }, Never));
    }

    [Fact]
    public void ARenamedExe_IsFoundByItsProductAndDescription()
    {
        var item = new DockItem { Name = "Sheet", ExePath = @"C:\x\renamed.exe" };
        Assert.Equal("excel", DockCustomKeys.Resolve(item, _ => new AppIdentity(ProductName: "Microsoft Office", FileDescription: "Microsoft Excel")));
    }

    [Fact]
    public void AnExeThatSaysNothing_HasNoCustomTile()
        => Assert.Null(DockCustomKeys.Resolve(new DockItem { Name = "x", ExePath = @"C:\x\unknown.exe" }, _ => new AppIdentity()));
}
