using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

public class FolderPanelFocusTests
{
    private const double Settled = 5000; // well past the opening guard

    // ----- the decision: stay or close -----

    [Theory]
    [InlineData((int)ForegroundKind.Own)] // our menu, dialog, or a Shell window we started
    [InlineData((int)ForegroundKind.Other)]
    [InlineData((int)ForegroundKind.None)]
    public void WhileAnOperationIsUnderWay_ThePanelAlwaysStays(int kind)
    {
        var foreground = (ForegroundKind)kind;
        Assert.Equal(PanelFocusDecision.Stay, PanelFocusPolicy.Decide(PanelFocusEvent.Deactivated, foreground, busy: true, Settled));
        Assert.Equal(PanelFocusDecision.Stay, PanelFocusPolicy.Decide(PanelFocusEvent.Rechecked, foreground, busy: true, Settled));
    }

    [Fact]
    public void OurOwnWindowInTheForeground_IsNotLeavingThePanel()
        => Assert.Equal(PanelFocusDecision.Stay, PanelFocusPolicy.Decide(PanelFocusEvent.Deactivated, ForegroundKind.Own, busy: false, Settled));

    [Fact]
    public void AnotherProgramInTheForeground_ClosesThePanel()
    {
        Assert.Equal(PanelFocusDecision.Close, PanelFocusPolicy.Decide(PanelFocusEvent.Deactivated, ForegroundKind.Other, busy: false, Settled));
        Assert.Equal(PanelFocusDecision.Close, PanelFocusPolicy.Decide(PanelFocusEvent.Rechecked, ForegroundKind.Other, busy: false, Settled));
    }

    [Fact]
    public void AnEmptyForeground_GetsASecondLook_ThenCloses()
    {
        Assert.Equal(PanelFocusDecision.Recheck, PanelFocusPolicy.Decide(PanelFocusEvent.Deactivated, ForegroundKind.None, busy: false, Settled));
        Assert.Equal(PanelFocusDecision.Close, PanelFocusPolicy.Decide(PanelFocusEvent.Rechecked, ForegroundKind.None, busy: false, Settled));
        // ...and the second look finds our own window if the switch was only passing through one.
        Assert.Equal(PanelFocusDecision.Stay, PanelFocusPolicy.Decide(PanelFocusEvent.Rechecked, ForegroundKind.Own, busy: false, Settled));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(399)]
    public void InTheFirstMoments_TheForegroundIsStillSettling_SoTheQuestionIsAskedAgainLater(double openMilliseconds)
    {
        Assert.Equal(PanelFocusDecision.Stay, PanelFocusPolicy.Decide(PanelFocusEvent.Deactivated, ForegroundKind.Own, busy: false, openMilliseconds));
        foreach (var foreground in new[] { ForegroundKind.None, ForegroundKind.Other })
        {
            Assert.Equal(PanelFocusDecision.Recheck, PanelFocusPolicy.Decide(PanelFocusEvent.Deactivated, foreground, busy: false, openMilliseconds));
            Assert.Equal(PanelFocusDecision.Recheck, PanelFocusPolicy.Decide(PanelFocusEvent.Rechecked, foreground, busy: false, openMilliseconds));
        }
    }

    [Fact]
    public void AfterTheGuard_AnotherProgramCloses()
        => Assert.Equal(PanelFocusDecision.Close, PanelFocusPolicy.Decide(PanelFocusEvent.Deactivated, ForegroundKind.Other, busy: false, PanelFocusPolicy.GuardMilliseconds));

    [Fact]
    public void ClassifyingTheForeground_ByItsProcess()
    {
        Assert.Equal(ForegroundKind.None, PanelFocusPolicy.Classify(0, 0, 100));
        Assert.Equal(ForegroundKind.Own, PanelFocusPolicy.Classify(0x1234, 100, 100));
        Assert.Equal(ForegroundKind.Other, PanelFocusPolicy.Classify(0x1234, 200, 100));
    }

    // ----- taking the focus back after our own work -----

    [Fact]
    public void AfterOurOwnWork_ThePanelTakesTheFocusBack_UnlessAnotherOfOurWindowsHasIt()
    {
        Assert.Equal(ReclaimAction.Take, PanelFocusPolicy.Reclaim(ForegroundKind.None, panelHasForeground: false, afterOperation: false, userActedSince: false));
        Assert.Equal(ReclaimAction.Take, PanelFocusPolicy.Reclaim(ForegroundKind.Own, panelHasForeground: true, afterOperation: false, userActedSince: false));
        Assert.Equal(ReclaimAction.Keep, PanelFocusPolicy.Reclaim(ForegroundKind.Own, panelHasForeground: false, afterOperation: true, userActedSince: false)); // Properties, a warning
    }

    [Fact]
    public void AfterAMenu_AnotherProgramInTheForegroundClosesThePanel()
        => Assert.Equal(ReclaimAction.Close, PanelFocusPolicy.Reclaim(ForegroundKind.Other, panelHasForeground: false, afterOperation: false, userActedSince: false));

    [Fact]
    public void AfterAnOperation_ADroppedForegroundIsTakenBack_ButNotOnceTheUserActed()
    {
        // Windows gave the activation to the next window when the operation's own window closed.
        Assert.Equal(ReclaimAction.Take, PanelFocusPolicy.Reclaim(ForegroundKind.Other, panelHasForeground: false, afterOperation: true, userActedSince: false));
        // The user clicked or typed elsewhere after the operation ended: they left.
        Assert.Equal(ReclaimAction.Close, PanelFocusPolicy.Reclaim(ForegroundKind.Other, panelHasForeground: false, afterOperation: true, userActedSince: true));
    }

    // ----- the busy flag -----

    [Fact]
    public void TheBusyFlag_CountsNestedOperations_AndEachScopeEndsOnce()
    {
        Assert.False(PanelBusy.IsBusy);
        var outer = PanelBusy.Enter();
        var inner = PanelBusy.Enter();
        Assert.True(PanelBusy.IsBusy);
        inner.Dispose();
        inner.Dispose(); // a second dispose must not end the outer scope
        Assert.True(PanelBusy.IsBusy);
        outer.Dispose();
        Assert.False(PanelBusy.IsBusy);
    }

    // ----- what is selected after the panel's own work -----

    private static readonly DateTime T0 = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private static string[] View(params string[] names) => names;

    [Fact]
    public void AfterARename_TheRenamedItemIsSelected_OnceItShowsUp()
    {
        var aftermath = new PanelAftermath();
        aftermath.Renamed("b-new.txt", T0);
        Assert.Null(aftermath.Resolve(View("a.txt", "b.txt", "c.txt"), T0.AddMilliseconds(50))); // the rescan has not landed yet
        Assert.Equal("b-new.txt", aftermath.Resolve(View("a.txt", "b-new.txt", "c.txt"), T0.AddMilliseconds(200)));
        Assert.Null(aftermath.Resolve(View("a.txt", "b-new.txt", "c.txt"), T0.AddMilliseconds(300))); // once only
    }

    [Theory]
    [InlineData(0, "b.txt")] // the first goes: the one after it takes its place
    [InlineData(1, "c.txt")]
    [InlineData(2, "d.txt")]
    [InlineData(3, "c.txt")] // the last goes: the one before it
    public void AfterARemoval_TheNextItemIsSelected(int removedIndex, string expected)
    {
        string[] before = ["a.txt", "b.txt", "c.txt", "d.txt"];
        var aftermath = new PanelAftermath();
        aftermath.Removing(before, [before[removedIndex]], T0);
        var now = before.Where((_, i) => i != removedIndex).ToArray();
        Assert.Equal(expected, aftermath.Resolve(now, T0.AddMilliseconds(100)));
    }

    [Fact]
    public void AfterRemovingSeveral_TheItemAfterTheLastOneIsSelected()
    {
        string[] before = ["a", "b", "c", "d", "e", "f"];
        var aftermath = new PanelAftermath();
        aftermath.Removing(before, ["b", "c", "e"], T0);
        Assert.Equal("f", aftermath.Resolve(View("a", "d", "f"), T0.AddMilliseconds(100)));
    }

    [Fact]
    public void ARemovalThatDidNotHappen_SelectsNothing_AndExpires()
    {
        var aftermath = new PanelAftermath();
        aftermath.Removing(View("a", "b", "c"), ["b"], T0);
        Assert.Null(aftermath.Resolve(View("a", "b", "c"), T0.AddMilliseconds(100))); // cancelled: b is still there
        Assert.Null(aftermath.Resolve(View("a", "c"), T0.AddSeconds(6))); // expired: an unrelated change later is not ours
    }

    [Fact]
    public void RemovingTheLastItem_LeavesNothingToSelect()
    {
        var aftermath = new PanelAftermath();
        aftermath.Removing(View("only"), ["only"], T0);
        Assert.Null(aftermath.Resolve(View(), T0.AddMilliseconds(100)));
    }

    [Fact]
    public void FileNamesAreMatchedWithoutCase()
    {
        var aftermath = new PanelAftermath();
        aftermath.Renamed("Report.TXT", T0);
        Assert.Equal("report.txt", aftermath.Resolve(View("report.txt"), T0));
    }
}
