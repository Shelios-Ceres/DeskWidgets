using DeskWidgets.Dock;

namespace DeskWidgets.Tests;

/// <summary>Phase 15: where the Glass dock draws its bar, icons, gap and label, and the window it draws them in.</summary>
public class GlassDockLayoutTests
{
    [Fact]
    public void TheWindow_SpansTheWorkArea_AndSitsOnTheBarsBottom()
    {
        var work = new DockRect(0, 0, 2560, 1560);
        var bar = new DockRect(1000, 1400, 600, 108);
        var controllers = new DockRect(820, 1337, 960, 171);
        var window = GlassDockLayout.WindowFor(work, controllers, bar, 1.5);
        Assert.Equal(0, window.X);
        Assert.Equal(2560, window.Width);
        Assert.Equal(195, window.Height); // (42 + 88) x 1.5: room for a Large bar at every size
        Assert.Equal(bar.Bottom, window.Bottom);
    }

    [Fact]
    public void TheWindow_TakesInABarWiderThanTheWorkArea()
    {
        var work = new DockRect(0, 0, 800, 600);
        var bar = new DockRect(-50, 500, 900, 72);
        var window = GlassDockLayout.WindowFor(work, new DockRect(-50, 458, 900, 114), bar, 1);
        Assert.Equal(-50, window.X);
        Assert.Equal(900, window.Width);
    }

    [Fact]
    public void AGap_GrowsTheBarByOneIconAndOneGap_AboutItsCentre()
    {
        var bar = GlassDockLayout.Bar(DockIconSize.Medium, 3, 1, barLeft: 500, barTop: 100, windowWidth: 2000);
        Assert.Equal(192 + 60, bar.Width);
        Assert.Equal(470, bar.X);
        Assert.Equal(72, bar.Height);
        Assert.Equal(100, bar.Y);
    }

    [Fact]
    public void AGapOnAnEmptyBar_LeavesAOneIconBar_Centred()
    {
        var bar = GlassDockLayout.Bar(DockIconSize.Medium, 0, 0, barLeft: 500, barTop: 0, windowWidth: 2000);
        Assert.Equal(72, bar.Width);
        Assert.Equal(500 + (280 - 72) / 2.0, bar.X);
    }

    [Fact]
    public void TheDrawnBar_StaysInsideTheWindow()
    {
        var bar = GlassDockLayout.Bar(DockIconSize.Medium, 3, 0, barLeft: 10, barTop: 0, windowWidth: 2000);
        Assert.Equal(0, bar.X);
        var right = GlassDockLayout.Bar(DockIconSize.Medium, 3, 3, barLeft: 2000 - 192 - 10, barTop: 0, windowWidth: 2000);
        Assert.Equal(2000, right.Right);
    }

    [Fact]
    public void IconsFromTheGapsSlotOn_SitOnePlaceFurtherRight()
    {
        var bar = new DipRect(0, 0, 252, 72);
        Assert.Equal(12, GlassDockLayout.IconLeft(DockIconSize.Medium, bar, 0, 1));
        Assert.Equal(12 + 2 * 60, GlassDockLayout.IconLeft(DockIconSize.Medium, bar, 1, 1));
        Assert.Equal(12 + 60, GlassDockLayout.IconLeft(DockIconSize.Medium, bar, 1, -1));
        Assert.Equal(new DipRect(72, 12, 48, 48), GlassDockLayout.Gap(DockIconSize.Medium, bar, 1));
    }

    [Fact]
    public void TheLabel_IsCentredOnTheIcon_EightAboveTheBar_TextPlusTwentyWide()
    {
        var label = GlassDockLayout.Label(iconCentre: 500, textWidth: 40.2, barTop: 100, screenLeft: 0, screenRight: 2000);
        Assert.Equal(61, label.Width);
        Assert.Equal(500 - 30.5, label.X);
        Assert.Equal(100 - 8 - 26, label.Y);
        Assert.Equal(26, label.Height);
    }

    [Fact]
    public void TheLabel_IsAtMost240_AndKeptEightInsideTheScreen()
    {
        var wide = GlassDockLayout.Label(iconCentre: 1000, textWidth: 400, barTop: 100, screenLeft: 0, screenRight: 2000);
        Assert.Equal(240, wide.Width);
        var left = GlassDockLayout.Label(iconCentre: 20, textWidth: 100, barTop: 100, screenLeft: 0, screenRight: 2000);
        Assert.Equal(8, left.X);
        var right = GlassDockLayout.Label(iconCentre: 1990, textWidth: 100, barTop: 100, screenLeft: 0, screenRight: 2000);
        Assert.Equal(2000 - 8, right.Right);
        Assert.Equal(220, GlassDockLayout.LabelTextMaxDip);
    }

    [Fact]
    public void ADraggedIcon_FollowsThePointer_InsideTheBarsPadding()
    {
        var bar = new DipRect(100, 0, 192, 72);
        Assert.Equal(100 + 60 - 24, GlassDockLayout.DragLeft(DockIconSize.Medium, bar, barLeft: 100, centreX: 60));
        Assert.Equal(112, GlassDockLayout.DragLeft(DockIconSize.Medium, bar, barLeft: 100, centreX: -500));
        Assert.Equal(100 + 192 - 12 - 48, GlassDockLayout.DragLeft(DockIconSize.Medium, bar, barLeft: 100, centreX: 900));
    }

    [Fact]
    public void TheRegion_IsOnePixelLargerThanTheDrawing()
    {
        var pixels = GlassDockLayout.RegionPixels(new DipRect(10, 20, 100, 72), 1.5);
        Assert.Equal((14, 29, 166, 139), pixels);
    }

    [Fact]
    public void AUnion_CoversBothRectangles()
        => Assert.Equal(new DipRect(0, 0, 30, 40), DipRect.Union(new DipRect(0, 10, 10, 30), new DipRect(20, 0, 10, 10)));
}
