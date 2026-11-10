using DeskWidgets.Dock;
using DeskWidgets.Pixel;

namespace DeskWidgets.Tests;

/// <summary>Phase 16.3 and 16.4: the drag picture's size, and auto-scroll in a group's panel with more than twelve members.</summary>
public class DockPanelDragTests
{
    // ----- 16.3 Glass: the picture is scaled about the pointer -----

    [Theory]
    [InlineData(48, 40, 0.8333)] // Small: the 48 tile shrinks to 40
    [InlineData(48, 48, 1.0)]
    [InlineData(48, 64, 1.3333)] // Large: it grows to 64
    public void TheScaleForAnIconEdge_IsTheEdgeOverTheRenderedTile(int rendered, int edge, double expected)
        => Assert.Equal(expected, GlassDockDragImage.ScaleFor(rendered, edge), 3);

    [Fact]
    public void NoEdge_MeansAsRendered_AndNothingRenderedIsNeverZeroDivided()
    {
        Assert.Equal(1.0, GlassDockDragImage.ScaleFor(72, null));
        Assert.Equal(1.0, GlassDockDragImage.ScaleFor(0, 60));
    }

    [Fact]
    public void ScalingAboutThePointer_KeepsTheHeldPointWhereItWas()
    {
        // A 72 px picture whose top-left is at 500 and the pointer 20 px into it: shrunk to 60 px (x0.8333) the same point is under the pointer.
        int pointer = 520, left = 500;
        double scale = 60 / 72.0;
        int scaledLeft = GlassDockDragImage.ScaleAbout(left, pointer, scale);
        Assert.Equal(pointer - (int)Math.Round(20 * scale), scaledLeft);
        Assert.Equal(left, GlassDockDragImage.ScaleAbout(left, pointer, 1)); // scale 1 is where the panel put it
    }

    [Theory]
    [InlineData(72, 1.0, 72)]
    [InlineData(72, 0.8333, 60)]
    [InlineData(72, 1.3333, 96)]
    [InlineData(1, 0.1, 1)] // never vanishes
    public void TheDrawnEdge_IsTheRenderedEdgeTimesTheScale(int edge, double scale, int expected)
        => Assert.Equal(expected, GlassDockDragImage.ScaledEdge(edge, scale));

    [Fact]
    public void Resampling_KeepsAFlatPictureFlat_AtAnySize()
    {
        var source = Enumerable.Range(0, 6 * 6).SelectMany(_ => new byte[] { 10, 20, 30, 40 }).ToArray(); // premultiplied BGRA, all one colour
        foreach (int size in new[] { 3, 4, 6, 8, 11 })
        {
            var result = GlassDockDragImage.Resample(source, 6, 6, size, size);
            Assert.Equal(size * size * 4, result.Length);
            for (int i = 0; i < result.Length; i += 4)
                Assert.Equal(new byte[] { 10, 20, 30, 40 }, result.AsSpan(i, 4).ToArray());
        }
    }

    [Fact]
    public void Resampling_ToTheSameSize_ChangesNothing()
    {
        var source = new byte[4 * 4 * 4];
        for (int i = 0; i < source.Length; i++)
            source[i] = (byte)(i * 3);
        Assert.Equal(source, GlassDockDragImage.Resample(source, 4, 4, 4, 4));
    }

    [Fact]
    public void Resampling_Down_BlendsNeighboursAndKeepsTheCornersAndTheOrder()
    {
        // A left-to-right ramp of alpha: 0, 85, 170, 255 across 4 pixels, one row. Halved, it is still a ramp that starts at 0 and ends at 255 is not required, but it must rise.
        var source = new byte[4 * 1 * 4];
        for (int x = 0; x < 4; x++)
            source[x * 4 + 3] = (byte)(85 * x);
        var result = GlassDockDragImage.Resample(source, 4, 1, 2, 1);
        Assert.True(result[3] < result[7]);
        Assert.InRange(result[3], 0, 85);
        Assert.InRange(result[7], 170, 255);
    }

    // ----- 16.3 Pixel: the picture steps from the panel's icon to the bar's -----

    [Theory]
    [InlineData(24, 20, new[] { 22, 20 })] // Small
    [InlineData(24, 32, new[] { 28, 32 })] // Large
    [InlineData(24, 24, new int[0])] // Medium: nothing to step
    public void ThePixelPictureStepsHalfwayThenToTheBarsSize(int from, int to, int[] expected)
        => Assert.Equal(expected, PixelDockPainter.FloatSizes(from, to));

    [Fact]
    public void TheInBetweenPixelFrame_IsTheSameArtAtAnotherSize_ByNearestPixel()
    {
        var source = new PixelCanvas(4, 4);
        source.Fill(0, 0, 2, 4, 0xFF112233); // the left half
        source.Fill(2, 0, 2, 4, 0xFF445566); // the right half
        var bigger = PixelDockPainter.Resized(source, 8);
        Assert.Equal(8, bigger.Width);
        Assert.Equal(0xFF112233u, bigger.Get(3, 5));
        Assert.Equal(0xFF445566u, bigger.Get(4, 5));
        var smaller = PixelDockPainter.Resized(source, 2);
        Assert.Equal(0xFF112233u, smaller.Get(0, 0));
        Assert.Equal(0xFF445566u, smaller.Get(1, 1));
    }

    // ----- 16.4 Glass: the speed rises towards the edge -----

    private const double Top = GlassDockPanelLayout.GridTop;
    private static readonly double Bottom = Top + 3 * GlassDockPanelLayout.CellHeight;

    [Fact]
    public void InTheMiddleOfTheList_NothingScrolls()
        => Assert.Equal(0, GlassDockPanelLayout.AutoScrollSpeed((Top + Bottom) / 2, Top, Bottom));

    [Fact]
    public void NearTheTop_ScrollsUp_FasterTheCloserToTheEdge_AndAtTheMaximumPastIt()
    {
        double start = GlassDockPanelLayout.AutoScrollSpeed(Top + GlassDockPanelLayout.AutoScrollZone - 1, Top, Bottom);
        double deeper = GlassDockPanelLayout.AutoScrollSpeed(Top + 8, Top, Bottom);
        double edge = GlassDockPanelLayout.AutoScrollSpeed(Top, Top, Bottom);
        double past = GlassDockPanelLayout.AutoScrollSpeed(Top - 30, Top, Bottom); // over the title row
        Assert.True(start < 0 && deeper < start && edge < deeper);
        Assert.Equal(-GlassDockPanelLayout.AutoScrollMaxSpeed, past, 6);
        Assert.InRange(-start, GlassDockPanelLayout.AutoScrollMinSpeed, GlassDockPanelLayout.AutoScrollMinSpeed + 30);
    }

    [Fact]
    public void NearTheBottom_ScrollsDown_WithTheSameRamp()
    {
        double start = GlassDockPanelLayout.AutoScrollSpeed(Bottom - GlassDockPanelLayout.AutoScrollZone + 1, Top, Bottom);
        double edge = GlassDockPanelLayout.AutoScrollSpeed(Bottom, Top, Bottom);
        double past = GlassDockPanelLayout.AutoScrollSpeed(Bottom + 20, Top, Bottom); // the panel's padding below
        Assert.True(start > 0 && edge > start);
        Assert.Equal(GlassDockPanelLayout.AutoScrollMaxSpeed, past, 6);
    }

    [Fact]
    public void TheTopAndBottomZones_AreMirrorImages()
        => Assert.Equal(-GlassDockPanelLayout.AutoScrollSpeed(Top + 5, Top, Bottom), GlassDockPanelLayout.AutoScrollSpeed(Bottom - 5, Top, Bottom), 6);

    [Fact]
    public void ACrossingFromTheFirstRowToTheLast_TakesAboutAQuarterToHalfASecond_AtTheEdge()
    {
        // 20 members in 4 columns are 5 rows: 2 rows of 92 DIP out of sight. At the edge's speed that is under half a second.
        double hidden = (5 - 3) * GlassDockPanelLayout.CellHeight;
        double seconds = hidden / GlassDockPanelLayout.AutoScrollSpeed(Bottom, Top, Bottom);
        Assert.InRange(seconds, 0.2, 0.6);
    }

    // ----- 16.4 Pixel: one row at a time, only when the list has more rows than shown -----

    private static readonly int GridTop = PixelDockPanelLayout.GridY;

    private static int GridBottom(int count) => GridTop + PixelDockPanelLayout.VisibleRows(count) * PixelDockPanelLayout.CellHeight;

    [Theory]
    [InlineData(12)] // 4 columns x 3 rows: all in sight
    [InlineData(5)]
    [InlineData(1)]
    public void AListThatFits_NeverScrolls(int count)
    {
        Assert.Equal(0, PixelDockPanelLayout.AutoScrollRows(count, 0, GridTop));
        Assert.Equal(0, PixelDockPanelLayout.AutoScrollRows(count, 0, GridBottom(count) - 1));
    }

    [Fact]
    public void AtTheBottomEdgeOfALongList_ItGoesDownARow_UntilTheLast()
    {
        const int count = 20; // 5 rows, 3 visible: first row 0 to 2
        int y = GridBottom(count) - 1;
        Assert.Equal(1, PixelDockPanelLayout.AutoScrollRows(count, 0, y));
        Assert.Equal(1, PixelDockPanelLayout.AutoScrollRows(count, 1, y));
        Assert.Equal(0, PixelDockPanelLayout.AutoScrollRows(count, PixelDockPanelLayout.MaxFirstRow(count), y));
    }

    [Fact]
    public void AtTheTopEdgeOfALongList_ItGoesUpARow_UntilTheFirst()
    {
        const int count = 20;
        Assert.Equal(-1, PixelDockPanelLayout.AutoScrollRows(count, 2, GridTop));
        Assert.Equal(-1, PixelDockPanelLayout.AutoScrollRows(count, 1, GridTop + PixelDockPanelLayout.AutoScrollBand - 1)); // the title row counts too
        Assert.Equal(0, PixelDockPanelLayout.AutoScrollRows(count, 0, GridTop));
    }

    [Fact]
    public void InTheMiddleOfTheGrid_NothingScrolls()
        => Assert.Equal(0, PixelDockPanelLayout.AutoScrollRows(20, 1, (GridTop + GridBottom(20)) / 2));

    [Fact]
    public void TheFirstRowToTheLastOfTwentyMembers_IsReachableByScrollingTwoRows()
    {
        // The drag starts in row 0 and the grid shows three rows: two scroll steps bring row 4, which holds the last member, into the bottom row.
        const int count = 20;
        int first = 0, steps = 0;
        while (PixelDockPanelLayout.AutoScrollRows(count, first, GridBottom(count) - 1) is 1 && steps < 10)
        {
            first++;
            steps++;
        }
        Assert.Equal(2, steps);
        Assert.Equal(PixelDockPanelLayout.MaxFirstRow(count), first);
        Assert.NotNull(PixelDockPanelLayout.CellRect(count, first, count - 1)); // the last member is in sight
    }
}
