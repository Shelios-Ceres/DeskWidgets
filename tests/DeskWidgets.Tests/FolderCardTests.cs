using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

public class FolderCardTests
{
    [Theory]
    [InlineData(WidgetSize.Small, 4)]
    [InlineData(WidgetSize.Medium, 8)]
    [InlineData(WidgetSize.Large, 16)]
    public void ACardHasFourEightOrSixteenCells(WidgetSize size, int cells) => Assert.Equal(cells, FolderCards.CellCount(size));

    [Theory]
    [InlineData(WidgetSize.Small, 0)]
    [InlineData(WidgetSize.Small, 3)]
    [InlineData(WidgetSize.Small, 4)]
    [InlineData(WidgetSize.Medium, 8)]
    [InlineData(WidgetSize.Large, 16)]
    public void WithNoMoreItemsThanCells_EveryItemIsShown_AndThereIsNoStack(WidgetSize size, int total)
    {
        var layout = FolderCards.Layout(size, total);
        Assert.Equal(total, layout.Shown);
        Assert.False(layout.HasStack);
        Assert.Empty(layout.Minis);
    }

    [Fact]
    public void OneItemTooMany_TurnsTheLastCellIntoAStackOfTwo()
    {
        var layout = FolderCards.Layout(WidgetSize.Small, 5);
        Assert.Equal(3, layout.Shown);
        Assert.True(layout.HasStack);
        Assert.Equal(2, layout.StackMore); // "+2": the item that no longer fits and the one the stack's cell took
        Assert.Equal([3, 4], layout.Minis);
    }

    [Fact]
    public void TheStackShowsTheNextFourItems_AndCountsEverythingFromItsOwnCell()
    {
        var layout = FolderCards.Layout(WidgetSize.Large, 23);
        Assert.Equal(15, layout.Shown);
        Assert.Equal(8, layout.StackMore);
        Assert.Equal([15, 16, 17, 18], layout.Minis);

        var medium = FolderCards.Layout(WidgetSize.Medium, 9);
        Assert.Equal(7, medium.Shown);
        Assert.Equal(2, medium.StackMore);
        Assert.Equal([7, 8], medium.Minis);
    }
}
