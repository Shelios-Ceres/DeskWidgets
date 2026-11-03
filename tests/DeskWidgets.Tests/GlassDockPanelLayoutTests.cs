using DeskWidgets.Dock;

namespace DeskWidgets.Tests;

/// <summary>Phase 15 Build C: the Glass group icon's minis, the forming and lit plates, and the group panel's geometry (Glass HANDOFF 1 and 8).</summary>
public class GlassDockPanelLayoutTests
{
    [Theory]
    [InlineData(40, 15, 4, 2)]
    [InlineData(48, 18, 5, 2)]
    [InlineData(64, 24, 6, 4)]
    public void TheMinis_FillTheTile_WithTheirInsetAndGap(double icon, double mini, double inset, double gap)
    {
        Assert.Equal((mini, inset, gap), GlassDockLayout.Minis(icon));
        var last = GlassDockLayout.MiniRect(icon, 3);
        Assert.Equal(inset + mini + gap, last.X);
        Assert.Equal(inset + mini + gap, last.Y);
        Assert.Equal(icon - inset, last.Right); // the inset on the far side too
    }

    [Fact]
    public void ThePlates_AreLargerOnEachSide_WithTheRadiusOfTheirSide()
    {
        var (forming, formingRadius) = GlassDockLayout.Plate(48, 6);
        Assert.Equal(new DipRect(-6, -6, 60, 60), forming);
        Assert.Equal(0.225 * 60, formingRadius, 6);
        var (lit, litRadius) = GlassDockLayout.Plate(48, 4);
        Assert.Equal(56, lit.Width);
        Assert.Equal(0.225 * 56, litRadius, 6);
    }

    [Theory]
    [InlineData(2, 3, 280, 152)]
    [InlineData(4, 4, 360, 152)]
    [InlineData(5, 4, 360, 244)]
    [InlineData(12, 4, 360, 336)]
    [InlineData(20, 4, 360, 336)] // three rows shown, then it scrolls
    public void ThePanel_HasThreeOrFourColumns_AndAtMostThreeRows(int count, int columns, double width, double height)
    {
        Assert.Equal(columns, GlassDockPanelLayout.Columns(count));
        Assert.Equal(width, GlassDockPanelLayout.Width(count));
        Assert.Equal(height, GlassDockPanelLayout.Height(count));
    }

    [Fact]
    public void TheContent_ScrollsPastThreeRows()
    {
        Assert.Equal(5 * 92 + 8, GlassDockPanelLayout.ContentHeight(20));
        Assert.Equal(3 * 92 + 8, GlassDockPanelLayout.ViewportHeight(20));
    }

    [Fact]
    public void ThePanel_IsCentredOnTheGroup_TwelveAboveTheBar()
    {
        var (x, y) = GlassDockPanelLayout.Place(iconCentre: 1000, barTop: 900, screenLeft: 0, screenRight: 1700, screenTop: 0, width: 280, height: 152);
        Assert.Equal(860, x);
        Assert.Equal(900 - 12 - 152, y);
    }

    [Fact]
    public void ThePanel_StaysEightInsideTheScreen_AtTheBarsEnds()
    {
        Assert.Equal(8, GlassDockPanelLayout.Place(30, 900, 0, 1700, 0, 360, 152).X);
        Assert.Equal(1700 - 8 - 360, GlassDockPanelLayout.Place(1690, 900, 0, 1700, 0, 360, 152).X);
        Assert.Equal(8, GlassDockPanelLayout.Place(800, 100, 0, 1700, 0, 360, 336).Y); // no room above the bar: kept on the screen
    }

    [Fact]
    public void ThePanel_GrowsFromTheGroupIcon_KeptOnThePanel()
    {
        Assert.Equal(140, GlassDockPanelLayout.OriginX(1000, 860, 280));
        Assert.Equal(0, GlassDockPanelLayout.OriginX(0, 8, 360));
        Assert.Equal(360, GlassDockPanelLayout.OriginX(2000, 8, 360));
    }

    [Fact]
    public void Cells_AreHitFromTheGeometry()
    {
        Assert.Equal(0, GlassDockPanelLayout.CellAt(20, 0, 5, 4));
        Assert.Equal(3, GlassDockPanelLayout.CellAt(20 + 3 * 80 + 79, 91, 5, 4));
        Assert.Equal(4, GlassDockPanelLayout.CellAt(25, 92, 5, 4));
        Assert.Equal(-1, GlassDockPanelLayout.CellAt(105, 92, 5, 4)); // past the last member
        Assert.Equal(-1, GlassDockPanelLayout.CellAt(19, 10, 5, 4)); // the side margin
        Assert.Equal(-1, GlassDockPanelLayout.CellAt(20 + 4 * 80, 10, 5, 4));
    }

    [Fact]
    public void ADraggedMember_LandsOnTheNearestPlace()
    {
        Assert.Equal(0, GlassDockPanelLayout.SlotAt(-50, -50, 5, 4));
        Assert.Equal(4, GlassDockPanelLayout.SlotAt(1000, 1000, 5, 4)); // clamped to the last member
        Assert.Equal(2, GlassDockPanelLayout.SlotAt(20 + 2 * 80 + 10, 40, 5, 4));
    }

    [Fact]
    public void ThePlacesWhileDragging_AreTheOrderTheReorderLeaves()
    {
        Assert.Equal([2, 0, 1, 3], GlassDockPanelLayout.Places(4, from: 0, to: 2));
        Assert.Equal([1, 2, 0, 3], GlassDockPanelLayout.Places(4, from: 2, to: 0));
        Assert.Equal([0, 1, 2], GlassDockPanelLayout.Places(3, from: -1, to: -1));
    }
}
