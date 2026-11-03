using DeskWidgets.Dock;

namespace DeskWidgets.Tests;

/// <summary>Phase 15.5: the Pixel group panel's geometry, the grid reorder rule and the group tile's minis, as pure numbers.</summary>
public class PixelDockPanelLayoutTests
{
    [Theory]
    [InlineData(2, 3, 140, 1)]
    [InlineData(3, 3, 140, 1)]
    [InlineData(4, 4, 180, 1)]
    [InlineData(5, 4, 180, 2)]
    [InlineData(13, 4, 180, 4)]
    public void TheFrame_HasThreeOrFourColumns_AndItsWidthFollows(int count, int columns, int width, int rows)
    {
        Assert.Equal(columns, PixelDockPanelLayout.Columns(count));
        Assert.Equal(width, PixelDockPanelLayout.Width(count));
        Assert.Equal(rows, PixelDockPanelLayout.TotalRows(count));
    }

    [Fact]
    public void AtMostThreeRowsShow_TheRestScrolls()
    {
        Assert.Equal(3, PixelDockPanelLayout.VisibleRows(13));
        Assert.Equal(1, PixelDockPanelLayout.MaxFirstRow(13));
        Assert.Equal(0, PixelDockPanelLayout.MaxFirstRow(6));
        Assert.Equal(8 + 14 + 4 + 3 * 46 + 4, PixelDockPanelLayout.Height(13));
    }

    [Fact]
    public void ACellIsFoundByItsArtPixel_AndNothingOutsideTheGrid()
    {
        Assert.Equal(0, PixelDockPanelLayout.CellAt(5, 0, 12, 30));
        Assert.Equal(1, PixelDockPanelLayout.CellAt(5, 0, 10 + 40 + 2, 30));
        Assert.Equal(4, PixelDockPanelLayout.CellAt(5, 0, 12, 26 + 46 + 2)); // the second row's first cell
        Assert.Equal(-1, PixelDockPanelLayout.CellAt(5, 0, 5, 30)); // in the side margin
        Assert.Equal(-1, PixelDockPanelLayout.CellAt(5, 0, 12, 10)); // over the title row
        Assert.Equal(-1, PixelDockPanelLayout.CellAt(5, 0, 10 + 3 * 40 + 2, 26 + 46 + 2)); // an empty cell after the last member
    }

    [Fact]
    public void ADraggedCell_MovesOnlyOncePastTheMiddleOfTheCellItGoesOver()
    {
        // Three members, dragged one is 0: the pointer in cell 1 before its middle (x < 10 + 40 + 20) keeps 0; past it, 1.
        Assert.Equal(0, PixelDockPanelLayout.ReorderTarget(3, 0, 10 + 40 + 10, 30, from: 0));
        Assert.Equal(1, PixelDockPanelLayout.ReorderTarget(3, 0, 10 + 40 + 30, 30, from: 0));
        // Going back: member 2 dragged left over cell 1: past the middle to the left (x < middle) takes cell 1.
        Assert.Equal(2, PixelDockPanelLayout.ReorderTarget(3, 0, 10 + 40 + 30, 30, from: 2));
        Assert.Equal(1, PixelDockPanelLayout.ReorderTarget(3, 0, 10 + 40 + 10, 30, from: 2));
        // Past the last member: the end. Over its own cell: unchanged.
        Assert.Equal(2, PixelDockPanelLayout.ReorderTarget(3, 0, 10 + 2 * 40 + 20, 30, from: 2));
        Assert.Equal(-1, PixelDockPanelLayout.ReorderTarget(0, 0, 12, 30, from: 0));
    }

    [Fact]
    public void ThePreviewOrder_TakesTheMemberOutAndPutsItBack()
    {
        Assert.Equal([1, 2, 0, 3], PixelDockPanelLayout.Preview(4, from: 0, to: 2));
        Assert.Equal([0, 3, 1, 2], PixelDockPanelLayout.Preview(4, from: 3, to: 1));
        Assert.Equal([0, 1, 2], PixelDockPanelLayout.Preview(3, from: 1, to: 1));
        Assert.Equal([0, 1, 2], PixelDockPanelLayout.Preview(3, from: 5, to: 0));
    }

    [Theory]
    [InlineData(20, 7)]
    [InlineData(24, 9)]
    [InlineData(32, 12)]
    public void TheGroupsMinis_AreSevenNineOrTwelveArtPixels(int iconSize, int mini)
        => Assert.Equal(mini, PixelDockArt.GroupMiniSize(iconSize));
}
