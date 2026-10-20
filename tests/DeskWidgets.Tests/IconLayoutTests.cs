using DeskWidgets.Desktop;
using DeskWidgets.Interop;

namespace DeskWidgets.Tests;

public class IconLayoutTests
{
    private const int SpacingX = 115, SpacingY = 150;

    // The user's monitor in the desktop view's coordinates: 2560×1552 below a 48 px top taskbar.
    private static readonly DisplayArea Screen = new(@"\\.\DISPLAY1", true, 0, 48, 2560, 1552);

    private static DesktopSnapshot Desktop(params IconPosition[] icons)
        => new(DateTimeOffset.Now, 0, SpacingX, SpacingY, 48, 0, [Screen], [.. icons]);

    private static IconPosition[] Icons(params string[] keys) => keys.Select(key => new IconPosition(key, 0, 0)).ToArray();

    /// <summary>A layout that already knows the screen (default margins) and the icons, in this order.</summary>
    private static IconLayoutSettings Known(params string[] order)
    {
        var settings = new IconLayoutSettings { Order = [.. order] };
        settings.Margins[Screen.Device] = IconLayout.DefaultInsets;
        foreach (string key in order)
            settings.Monitors[key] = Screen.Device;
        return settings;
    }

    [Fact]
    public void Plan_FillsTheRightColumnFromTheTop()
    {
        var plan = IconLayout.Plan(Desktop(Icons("a", "b", "c")), Known("a", "b", "c"), []);

        Assert.Equal(new[] { new IconPosition("a", 2436, 50), new("b", 2436, 200), new("c", 2436, 350) }, plan);
    }

    [Fact]
    public void Plan_ContinuesInTheNextColumnToTheLeft()
    {
        // The 1552 px screen minus the 2 px top margin holds 10 rows of 150 px.
        string[] keys = Enumerable.Range(0, 11).Select(i => $"icon{i:00}").ToArray();

        var plan = IconLayout.Plan(Desktop(Icons(keys)), Known(keys), []);

        Assert.Equal(new IconPosition("icon09", 2436, 1400), plan[9]);
        Assert.Equal(new IconPosition("icon10", 2321, 50), plan[10]);
    }

    [Fact]
    public void Plan_SkipsCellsUnderAWidget()
    {
        var card = new Win32.RECT { Left = 2400, Top = 40, Right = 2560, Bottom = 190 }; // over the top-right cell

        var plan = IconLayout.Plan(Desktop(Icons("a", "b")), Known("a", "b"), [card]);

        Assert.Equal(new[] { new IconPosition("a", 2436, 200), new("b", 2436, 350) }, plan);
    }

    [Fact]
    public void Plan_LeavesNoGapForIconsThatAreGone()
    {
        var plan = IconLayout.Plan(Desktop(Icons("b")), Known("a", "b"), []);

        Assert.Equal(new IconPosition("b", 2436, 50), Assert.Single(plan));
    }

    [Fact]
    public void Plan_MovesNothingWhenTheIconsDoNotFit()
    {
        var small = new DisplayArea("small", true, 0, 0, 300, 200); // room for 2 columns × 1 row
        var desktop = new DesktopSnapshot(DateTimeOffset.Now, 0, SpacingX, SpacingY, 48, 0, [small], [.. Icons("a", "b", "c")]);

        Assert.Throws<InvalidOperationException>(() => IconLayout.Plan(desktop, new IconLayoutSettings { Order = ["a", "b", "c"] }, []));
    }

    [Fact]
    public void Learn_OnTheFirstRun_KeepsTheReadingOrderAndTheMargins()
    {
        var settings = new IconLayoutSettings();
        var desktop = Desktop(new("right-top", 2436, 50), new("right-second", 2436, 200), new("left", 21, 50));

        Assert.True(IconLayout.Learn(desktop, settings));

        Assert.Equal(new[] { "left", "right-top", "right-second" }, settings.Order);
        Assert.Equal(Screen.Device, settings.Monitors["left"]);
        Assert.Equal(9.0 / SpacingX, settings.Margins[Screen.Device].Right, 6);
        Assert.Equal(2.0 / SpacingY, settings.Margins[Screen.Device].Top, 6);
    }

    [Fact]
    public void Learn_AppendsNewIconsByName_AndSaysWhetherAnythingChanged()
    {
        var settings = Known("b");
        var desktop = Desktop(new("b", 2436, 50), new("z", 100, 100), new("a", 500, 500));

        Assert.True(IconLayout.Learn(desktop, settings));
        Assert.Equal(new[] { "b", "a", "z" }, settings.Order);
        Assert.False(IconLayout.Learn(desktop, settings));
    }

    [Fact]
    public void Learn_RefusesADesktopWithoutDisplays()
    {
        var empty = new DesktopSnapshot(DateTimeOffset.Now, 0, SpacingX, SpacingY, 48, 0, [], []);

        Assert.Throws<InvalidOperationException>(() => IconLayout.Learn(empty, new IconLayoutSettings()));
    }

    // Cell n of the user's screen (10 rows per column): x = 2436 - 115 * (n / 10), y = 50 + 150 * (n % 10).
    private static readonly DisplayArea Second = new(@"\\.\DISPLAY2", false, 2560, 0, 1920, 1080);

    /// <summary>The icons of <paramref name="settings"/> sitting where the layout puts them.</summary>
    private static List<IconPosition> Settled(IconLayoutSettings settings, IReadOnlyList<Win32.RECT> blocked, params DisplayArea[] screens)
        => IconLayout.Plan(Snapshot([.. settings.Order.Select(key => new IconPosition(key, 0, 0))], screens), settings, blocked);

    private static DesktopSnapshot Snapshot(List<IconPosition> icons, params DisplayArea[] screens)
        => new(DateTimeOffset.Now, 0, SpacingX, SpacingY, 48, 0, [.. screens], icons);

    private static void Move(List<IconPosition> icons, string key, int x, int y)
    {
        int at = icons.FindIndex(p => p.Key == key);
        icons[at] = icons[at] with { X = x, Y = y };
    }

    [Fact]
    public void LearnDrops_ADropOnAnOccupiedCell_ShiftsTheIconsThere()
    {
        var settings = Known("a", "b", "c", "d", "e");
        var icons = Settled(settings, [], Screen);
        Move(icons, "a", 2440, 510); // dropped a little off the grid, over cell 3 where d sits
        var desktop = Snapshot(icons, Screen);

        Assert.Equal(1, IconLayout.LearnDrops(desktop, settings, []));

        Assert.Equal(new[] { "b", "c", "d", "a", "e" }, settings.Order);
        var plan = IconLayout.Plan(desktop, settings, []);
        Assert.Equal(new IconPosition("a", 2436, 500), plan.Single(p => p.Key == "a"));
        Assert.Equal(new IconPosition("d", 2436, 350), plan.Single(p => p.Key == "d"));
    }

    [Fact]
    public void LearnDrops_ADropPastTheLastIcon_GoesToTheEnd()
    {
        var settings = Known("a", "b", "c");
        var icons = Settled(settings, [], Screen);
        Move(icons, "a", 2321, 800); // cell 15, far beyond the three icons
        var desktop = Snapshot(icons, Screen);

        IconLayout.LearnDrops(desktop, settings, []);

        Assert.Equal(new[] { "b", "c", "a" }, settings.Order);
        Assert.Equal(new IconPosition("a", 2436, 350), IconLayout.Plan(desktop, settings, []).Single(p => p.Key == "a"));
    }

    [Fact]
    public void LearnDrops_ADropOnACellUnderAWidget_TakesTheNextFreeCell()
    {
        var card = new Win32.RECT { Left = 2400, Top = 480, Right = 2560, Bottom = 640 }; // covers cells 2 and 3
        var settings = Known("a", "b", "c", "d", "e");
        var icons = Settled(settings, [card], Screen); // a0 b1 c4 d5 e6
        Move(icons, "e", 2436, 500); // dropped on cell 3, under the widget
        var desktop = Snapshot(icons, Screen);

        IconLayout.LearnDrops(desktop, settings, [card]);

        Assert.Equal(new[] { "a", "b", "e", "c", "d" }, settings.Order);
        Assert.Equal(new IconPosition("e", 2436, 650), IconLayout.Plan(desktop, settings, [card]).Single(p => p.Key == "e"));
    }

    [Fact]
    public void LearnDrops_ADropOnTheSecondMonitor_MovesTheIconThere()
    {
        var settings = Known("a", "b", "c");
        var icons = Settled(settings, [], Screen, Second);
        Move(icons, "b", 4300, 100);
        var desktop = Snapshot(icons, Screen, Second);

        IconLayout.LearnDrops(desktop, settings, []);

        Assert.Equal(Second.Device, settings.Monitors["b"]);
        Assert.Equal(new[] { "a", "c", "b" }, settings.Order);
        var plan = IconLayout.Plan(desktop, settings, []);
        Assert.Equal(new IconPosition("b", 4356, 2), plan.Single(p => p.Key == "b"));
        Assert.Equal(new IconPosition("c", 2436, 200), plan.Single(p => p.Key == "c"));
    }

    [Fact]
    public void LearnDrops_SeveralIconsDraggedTogether_GoInByTheOrderOfTheirCells()
    {
        var settings = Known("a", "b", "c", "d", "e", "f");
        var icons = Settled(settings, [], Screen);
        Move(icons, "a", 2436, 650); // to cell 4
        Move(icons, "c", 2436, 500); // to cell 3
        var desktop = Snapshot(icons, Screen);

        Assert.Equal(2, IconLayout.LearnDrops(desktop, settings, []));

        Assert.Equal(new[] { "b", "d", "e", "c", "a", "f" }, settings.Order);
        var plan = IconLayout.Plan(desktop, settings, []);
        Assert.Equal(new IconPosition("c", 2436, 500), plan.Single(p => p.Key == "c"));
        Assert.Equal(new IconPosition("a", 2436, 650), plan.Single(p => p.Key == "a"));
    }

    [Fact]
    public void LearnDrops_TwoIconsDroppedOnOneCell_KeepTheirOrderOfCells()
    {
        var settings = Known("a", "b", "c", "d");
        var icons = Settled(settings, [], Screen);
        Move(icons, "a", 2321, 50);
        Move(icons, "b", 2321, 50);
        var desktop = Snapshot(icons, Screen);

        IconLayout.LearnDrops(desktop, settings, []);

        Assert.Equal(new[] { "c", "d", "a", "b" }, settings.Order);
    }

    [Fact]
    public void LearnDrops_WhenNothingSitsAwayFromItsCell_ChangesNothing()
    {
        var settings = Known("a", "b", "c");
        var desktop = Snapshot(Settled(settings, [], Screen), Screen);

        Assert.Equal(0, IconLayout.LearnDrops(desktop, settings, []));
        Assert.Equal(new[] { "a", "b", "c" }, settings.Order);
    }

    [Fact]
    public void LearnDrops_ThrowsBeforeChangingAnythingWhenTheIconsDoNotFit()
    {
        var small = new DisplayArea("small", true, 0, 0, 300, 200); // room for 2 columns x 1 row
        var settings = new IconLayoutSettings { Order = ["a", "b", "c"] };
        var desktop = Snapshot([.. Icons("a", "b", "c")], small);

        Assert.Throws<InvalidOperationException>(() => IconLayout.LearnDrops(desktop, settings, []));
        Assert.Equal(new[] { "a", "b", "c" }, settings.Order);
    }

    [Fact]
    public void Learn_NeverReordersKnownIcons_SoWithoutADragTheyGoBack()
    {
        var settings = Known("a", "b", "c");
        var icons = Settled(settings, [], Screen);
        Move(icons, "a", 1000, 700); // moved by something other than the user's drag
        var desktop = Snapshot(icons, Screen);

        Assert.False(IconLayout.Learn(desktop, settings));

        Assert.Equal(new[] { "a", "b", "c" }, settings.Order);
        Assert.Equal(new IconPosition("a", 2436, 50), IconLayout.Plan(desktop, settings, [])[0]);
    }

    [Fact]
    public void Adopt_ReadsTheIconsTopRightFirst_AndForgetsIconsThatAreGone()
    {
        var settings = Known("gone", "w", "x", "y", "z");
        var desktop = Snapshot([new("w", 21, 50), new("x", 2321, 50), new("y", 2436, 200), new("z", 2436, 52)], Screen);

        IconLayout.Adopt(desktop, settings);

        Assert.Equal(new[] { "z", "y", "x", "w" }, settings.Order);
    }

    [Fact]
    public void Adopt_CountsEachMonitorOnItsOwn()
    {
        var settings = Known("a", "b", "c");
        var desktop = Snapshot([new("a", 2436, 200), new("b", 4356, 2), new("c", 2436, 50)], Screen, Second);

        IconLayout.Adopt(desktop, settings);

        Assert.Equal(new[] { "c", "a", "b" }, settings.Order);
        Assert.Equal(Screen.Device, settings.Monitors["a"]);
        Assert.Equal(Second.Device, settings.Monitors["b"]);
    }
}
