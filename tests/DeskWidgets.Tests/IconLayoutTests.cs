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
}
