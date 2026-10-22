using DeskWidgets.Desktop;
using DeskWidgets.Hosting;
using DeskWidgets.Interop;
using DeskWidgets.Widgets;
using Windows.Graphics;

namespace DeskWidgets.Tests;

public class DesktopGridTests
{
    // The user's desktop: 2560×1600 at 150 %, a 48 px taskbar at the top, icon cells of 115×150.
    private static readonly Win32.RECT WorkArea = new() { Left = 0, Top = 48, Right = 2560, Bottom = 1600 };

    private static readonly Win32.RECT Square = new() { Left = 0, Top = 0, Right = 1000, Bottom = 1000 };

    [Fact]
    public void Snap_CentresTheCardInTheCellsItCovers()
    {
        var grid = new DesktopGrid(Square, 100, 100);

        // 150×150 covers 2×2 cells and keeps 25 px to their edges.
        Assert.Equal(new PointInt32(125, 225), grid.Snap(130, 240, 150, 150));
    }

    [Fact]
    public void Snap_KeepsTheCardInsideTheArea()
    {
        var grid = new DesktopGrid(Square, 100, 100);

        Assert.Equal(new PointInt32(825, 25), grid.Snap(5000, -300, 150, 150));
    }

    [Fact]
    public void Slots_RunColumnByColumnFromTheRight_OrFromTheLeft()
    {
        var grid = new DesktopGrid(Square, 100, 100);

        var fromRight = grid.Slots(150, 150).ToList();
        var fromLeft = grid.Slots(150, 150, fromLeft: true).ToList();

        Assert.Equal(81, fromRight.Count); // 9 columns × 9 rows of 2×2-cell blocks
        Assert.Equal(new PointInt32(825, 25), fromRight[0]);
        Assert.Equal(new PointInt32(825, 125), fromRight[1]);
        Assert.Equal(new PointInt32(25, 25), fromLeft[0]);
    }

    [Fact]
    public void MediumPixelCard_SitsCentredInFiveByTwoCells()
    {
        // 172×82 art pixels × 3 = 516×246 screen pixels: 5×2 cells, inset 29×27.
        var grid = DesktopGrid.Create(WorkArea, 115, 150);

        Assert.Equal(new PointInt32(29, 75), grid.Snap(0, 48, 516, 246));
    }

    [Theory]
    [InlineData(WidgetTheme.Glass)]
    [InlineData(WidgetTheme.Pixel)]
    public void LargeCard_SitsCentredInFiveByFourCells_WithTheMediumsInset(WidgetTheme theme)
    {
        // 344×364 DIP (Pixel: 172×182 art pixels × 3) = 516×546 screen pixels: 5×4 cells, inset 29×27.
        var large = WidgetLayout.GetCardPixels(WidgetSize.Large, theme, 1.5);
        var grid = DesktopGrid.Create(WorkArea, 115, 150);

        Assert.Equal(new SizeInt32(516, 546), large);
        Assert.Equal(new PointInt32(29, 75), grid.Snap(0, 48, large.Width, large.Height));
    }

    [Fact]
    public void LargeCard_FillsTheSpanOfTwoStackedMediums()
    {
        var (right, top) = IconLayout.MarginPixels(115, 150, IconLayout.DefaultInsets);
        var grid = DesktopGrid.Create(WorkArea, 115, 150, new GridAnchor(right, top));
        var medium = WidgetLayout.GetCardPixels(WidgetSize.Medium, WidgetTheme.Pixel, 1.5);
        var large = WidgetLayout.GetCardPixels(WidgetSize.Large, WidgetTheme.Pixel, 1.5);

        // The user's column: World clock above Battery, a Big Folder beside them.
        var upper = grid.Snap(0, 48, medium.Width, medium.Height);
        var lower = grid.Snap(50, 360, medium.Width, medium.Height);
        // A Big Folder saved at y 92 (the old, shorter Large's inset) lands level with the column.
        var folder = grid.Snap(600, 92, large.Width, large.Height);

        Assert.Equal(upper.Y, folder.Y);
        Assert.Equal(lower.Y + medium.Height, folder.Y + large.Height);
    }

    [Fact]
    public void WhileIconsAreArranged_WidgetsShareTheIconColumns()
    {
        var (right, top) = IconLayout.MarginPixels(115, 150, IconLayout.DefaultInsets);
        var grid = DesktopGrid.Create(WorkArea, 115, 150, new GridAnchor(right, top));

        // Where the battery and world clock widgets sit on the user's desktop.
        Assert.Equal(new PointInt32(50, 77), grid.Snap(0, 48, 516, 246));
        Assert.Equal(new PointInt32(50, 377), grid.Snap(50, 360, 516, 246));
    }
}
