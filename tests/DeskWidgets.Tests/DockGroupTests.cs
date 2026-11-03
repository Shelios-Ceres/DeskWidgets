using DeskWidgets.Dock;
using DeskWidgets.Hosting;

namespace DeskWidgets.Tests;

/// <summary>Phase 15.5 (Build C): making, changing, saving and dissolving groups, Open all, the 500 ms hold, and the drop decision.</summary>
public class DockGroupTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deskwidgets-dock-groups-" + Guid.NewGuid().ToString("N"));

    public DockGroupTests() => Directory.CreateDirectory(_folder);

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

    private static DockItem App(string name) => new() { Name = name, ExePath = @"C:\Apps\" + name + ".exe" };

    private static List<DockItem> Bar(params string[] names) => [.. names.Select(App)];

    private static string[] Names(IEnumerable<DockItem> items) => [.. items.Select(i => i.IsGroup ? "[" + string.Join(",", i.Children!.Select(c => c.Name)) + "]" : i.Name)];

    // ----- making a group -----

    [Fact]
    public void HoldingAnIconOverAnother_MakesAGroupAtTheTargetsPlace_TargetFirst()
    {
        var items = Bar("A", "B", "C", "D");
        int at = DockGroups.MakeGroup(items, dragged: 3, target: 1);
        Assert.Equal(1, at);
        Assert.Equal(["A", "[B,D]", "C"], Names(items));
        Assert.False(string.IsNullOrEmpty(items[1].GroupId));
    }

    [Fact]
    public void TheDraggedIconBeforeTheTarget_LeavesTheGroupAtTheTargetsShiftedPlace()
    {
        var items = Bar("A", "B", "C", "D");
        int at = DockGroups.MakeGroup(items, dragged: 0, target: 2);
        Assert.Equal(1, at);
        Assert.Equal(["B", "[C,A]", "D"], Names(items));
    }

    [Fact]
    public void ADroppedOnGroup_TakesTheProgramInAtTheEnd()
    {
        var items = Bar("A", "B", "C");
        DockGroups.MakeGroup(items, 1, 0); // [A,B], C
        int at = DockGroups.MakeGroup(items, 1, 0);
        Assert.Equal(0, at);
        Assert.Equal(["[A,B,C]"], Names(items));
    }

    [Fact]
    public void AGroupCannotBeDraggedIntoAnything_AndNothingGroupsWithItself()
    {
        var items = Bar("A", "B", "C");
        DockGroups.MakeGroup(items, 1, 0);
        Assert.Equal(-1, DockGroups.MakeGroup(items, 0, 1));
        Assert.Equal(-1, DockGroups.MakeGroup(items, 1, 1));
        Assert.Equal(-1, DockGroups.MakeGroup(items, 5, 0));
        Assert.Equal(["[A,B]", "C"], Names(items));
    }

    [Fact]
    public void AProgramDroppedFromOutside_JoinsAGroup_UnlessItIsAlreadyOnTheBar()
    {
        var items = Bar("A", "B", "C");
        DockGroups.MakeGroup(items, 1, 0);
        Assert.True(DockGroups.AddToGroup(items, 0, App("D")));
        Assert.False(DockGroups.AddToGroup(items, 0, App("C"))); // on the bar already
        Assert.False(DockGroups.AddToGroup(items, 0, App("A"))); // in the group already
        Assert.False(DockGroups.AddToGroup(items, 1, App("E"))); // not a group
        Assert.Equal(["[A,B,D]", "C"], Names(items));
    }

    // ----- taking out, ungrouping, dissolving -----

    [Fact]
    public void ATakenOutProgram_GoesToTheSlot_AndALargerGroupStays()
    {
        var items = Bar("A", "B", "C", "D");
        DockGroups.MakeGroup(items, 1, 0);
        DockGroups.MakeGroup(items, 1, 0); // [A,B,C], D
        int at = DockGroups.TakeOut(items, group: 0, child: 1, slot: 1); // the bar is [G, D]: slot 1 is between them
        Assert.Equal(["[A,C]", "B", "D"], Names(items));
        Assert.Equal(1, at);
    }

    [Fact]
    public void AGroupLeftWithOneProgram_BecomesThatProgram()
    {
        var items = Bar("A", "B", "C");
        DockGroups.MakeGroup(items, 1, 0); // [A,B], C
        int at = DockGroups.TakeOut(items, 0, 0, slot: 2); // A to the end
        Assert.Equal(["B", "C", "A"], Names(items));
        Assert.Equal(2, at);
    }

    [Fact]
    public void ASlotPastTheEnd_MeansTheEnd()
    {
        var items = Bar("A", "B", "C");
        DockGroups.MakeGroup(items, 1, 0);
        DockGroups.TakeOut(items, 0, 1, slot: 99);
        Assert.Equal(["A", "C", "B"], Names(items));
    }

    [Fact]
    public void Ungroup_PutsTheProgramsBackInPlace_InOrder()
    {
        var items = Bar("A", "B", "C", "D");
        DockGroups.MakeGroup(items, 2, 1); // A, [B,C], D
        Assert.Equal(2, DockGroups.Ungroup(items, 1));
        Assert.Equal(["A", "B", "C", "D"], Names(items));
        Assert.Equal(0, DockGroups.Ungroup(items, 0)); // not a group
    }

    [Fact]
    public void Normalize_RepairsWhatAHandEditedFileMayHold()
    {
        var items = new List<DockItem>
        {
            new() { Name = "g1", Children = [App("A")] },                                    // one program: becomes it
            new() { Name = "g2", Children = [] },                                            // empty: goes
            new() { Name = "g3", Children = [App("B"), new DockItem { Name = "inner", Children = [App("C"), App("D")] }] }, // nested: flattened
            App("E"),
        };
        Assert.True(DockGroups.Normalize(items));
        Assert.Equal(["A", "[B,C,D]", "E"], Names(items));
        Assert.False(string.IsNullOrEmpty(items[1].GroupId));
        Assert.False(DockGroups.Normalize(items)); // nothing left to repair
    }

    [Fact]
    public void ReorderInsideAGroup_MovesOneMember()
    {
        var items = Bar("A", "B", "C");
        DockGroups.MakeGroup(items, 1, 0);
        DockGroups.MakeGroup(items, 1, 0); // [A,B,C]
        Assert.True(DockGroups.Reorder(items[0], 2, 0));
        Assert.Equal(["[C,A,B]"], Names(items));
        Assert.False(DockGroups.Reorder(items[0], 1, 1));
        Assert.False(DockGroups.Reorder(items[0], 0, 7));
    }

    // ----- what the bar knows -----

    [Fact]
    public void Programs_AndContains_SeeInsideGroups()
    {
        var items = Bar("A", "B", "C");
        DockGroups.MakeGroup(items, 1, 0);
        Assert.Equal(["A", "B", "C"], DockGroups.Programs(items).Select(i => i.Name));
        Assert.True(DockGroups.Contains(items, App("B").Key));
        Assert.False(DockGroups.Contains(items, App("Z").Key));
    }

    [Fact]
    public void AGroupsKey_IsItsIdAndSurvivesARename()
    {
        var items = Bar("A", "B");
        DockGroups.MakeGroup(items, 1, 0);
        string key = items[0].Key;
        items[0].Name = "Work";
        Assert.Equal(key, items[0].Key);
        Assert.StartsWith("group:", key);
        Assert.False(items[0].SupportsRunning);
    }

    [Theory]
    [InlineData(new[] { 0, 0 }, 0)]
    [InlineData(new[] { 0, 1 }, 1)]
    [InlineData(new[] { 1, 2 }, 2)]
    [InlineData(new[] { 0, 1, 2 }, 2)]
    public void AGroupIsRunning_WhileAnyMemberIs(int[] states, int expected)
        => Assert.Equal((DockRunState)expected, DockGroups.GroupState(states.Select(s => (DockRunState)s)));

    // ----- Open all -----

    [Fact]
    public void OpenAll_StartsOnlyTheProgramsThatAreNotRunning_EachOnce()
    {
        var items = Bar("A", "B", "C");
        DockGroups.MakeGroup(items, 1, 0);
        DockGroups.MakeGroup(items, 1, 0);
        items[0].Children!.Add(App("A")); // a duplicate a hand edit might leave
        var running = new HashSet<string> { App("B").Key };
        var start = DockGroups.OpenAll(items[0], running.Contains);
        Assert.Equal(["A", "C"], start.Select(i => i.Name));
    }

    // ----- the 500 ms hold -----

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void AHold_IsReadyAfterHalfASecondOnOneTarget()
    {
        var hold = new DockHold();
        Assert.False(hold.Ready(Ms(0)));
        Assert.True(hold.Over(2, Ms(100)));
        Assert.Equal(Ms(600), hold.DueAt);
        Assert.False(hold.Over(2, Ms(300))); // the same target: no new wait
        Assert.False(hold.Ready(Ms(599)));
        Assert.True(hold.Ready(Ms(600)));
    }

    [Fact]
    public void AHold_StartsAgainOnAnotherTarget_AndClearsOnNone()
    {
        var hold = new DockHold();
        hold.Over(1, Ms(0));
        Assert.True(hold.Over(3, Ms(400)));
        Assert.False(hold.Ready(Ms(800)));
        Assert.True(hold.Ready(Ms(900)));
        Assert.True(hold.Over(-1, Ms(950)));
        Assert.False(hold.Ready(Ms(5000)));
        Assert.Null(hold.DueAt);
        hold.Over(0, Ms(0));
        hold.Clear();
        Assert.Equal(-1, hold.Target);
    }

    [Fact]
    public void TheGroupingTarget_IsAnotherIconsMiddleZone_NeverTheDraggedOne()
    {
        // Medium: icon 48, gap 12, padding 12: icon i's middle is 12 + 60 i + 24.
        Assert.Equal(-1, DockGroups.TargetAt(DockIconSize.Medium, 4, 300, dragged: 0)); // between icons 3 and the end: no zone
        Assert.Equal(1, DockGroups.TargetAt(DockIconSize.Medium, 4, 12 + 60 + 24, dragged: 0)); // exactly the middle of icon 1
        Assert.Equal(1, DockGroups.TargetAt(DockIconSize.Medium, 4, 12 + 60 + 24 + 14, dragged: 0)); // inside the zone (0.3 x 48 = 14.4)
        Assert.Equal(-1, DockGroups.TargetAt(DockIconSize.Medium, 4, 12 + 60 + 24 + 16, dragged: 0)); // just outside
        Assert.Equal(-1, DockGroups.TargetAt(DockIconSize.Medium, 4, 12 + 60 + 24, dragged: 1)); // the dragged icon is never its own target
    }

    // ----- what a drop does -----

    [Theory]
    [InlineData(true, -1, false, 1)] // a program is always added, over a gap or an icon
    [InlineData(true, 2, true, 1)]
    [InlineData(false, 2, true, 2)] // other files open with the app under the pointer
    [InlineData(false, 2, false, 0)] // an app that did not register for them
    [InlineData(false, -1, true, 0)] // nothing happens on a gap
    public void TheDropDecision(bool allPrograms, int overIcon, bool canOpen, int expected)
        => Assert.Equal((DockDropAction)expected, DockDrop.Decide(allPrograms, overIcon, canOpen));

    // ----- what is saved -----

    [Fact]
    public void AGroupRoundTripsThroughTheSettingsFile_NameAndMembersAndId()
    {
        var store = new SettingsStore(_folder);
        var dock = new DockSettings { Monitor = @"\\.\DISPLAY1" };
        dock.Items.AddRange(Bar("A", "B", "C"));
        DockGroups.MakeGroup(dock.Items, 1, 0);
        dock.Items[0].Name = "Work";
        string id = dock.Items[0].GroupId!;
        store.Current.Docks.Add(dock);
        Assert.True(store.Save());

        var back = Assert.Single(SettingsStore.Peek(_folder).Docks);
        Assert.Equal(2, back.Items.Count);
        var group = back.Items[0];
        Assert.True(group.IsGroup);
        Assert.Equal("Work", group.Name);
        Assert.Equal(id, group.GroupId);
        Assert.Equal(["A", "B"], group.Children!.Select(c => c.Name));
        Assert.False(back.Items[1].IsGroup);
        Assert.Null(back.Items[1].Children); // a program saves no "Children"
    }

    [Fact]
    public void CopyingAGroup_CopiesItsMembersToo()
    {
        var items = Bar("A", "B");
        DockGroups.MakeGroup(items, 1, 0);
        var copy = items[0].Copy();
        copy.Children![0].Name = "changed";
        Assert.Equal("A", items[0].Children![0].Name);
        Assert.Equal(items[0].Key, copy.Key);
    }
}
