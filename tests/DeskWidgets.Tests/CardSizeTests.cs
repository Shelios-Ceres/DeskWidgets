using DeskWidgets.Pixel;
using DeskWidgets.Widgets;
using Windows.Graphics;

namespace DeskWidgets.Tests;

public class CardSizeTests
{
    [Theory]
    [InlineData(1.0, 2)]
    [InlineData(1.25, 3)]
    [InlineData(1.5, 3)]
    [InlineData(1.75, 4)]
    [InlineData(2.0, 4)]
    [InlineData(0.5, 1)]
    public void ArtPixels_AreAWholeNumberOfScreenPixels(double scale, int factor)
        => Assert.Equal(factor, PixelCard.Factor(scale));

    [Fact]
    public void AtTheUsersScale_BothThemesHaveTheSameCardSize()
    {
        Assert.Equal(new SizeInt32(516, 246), WidgetLayout.GetCardPixels(WidgetSize.Medium, WidgetTheme.Glass, 1.5));
        Assert.Equal(new SizeInt32(516, 246), WidgetLayout.GetCardPixels(WidgetSize.Medium, WidgetTheme.Pixel, 1.5));
    }

    [Theory]
    [InlineData(1.0, 344, 364)]
    [InlineData(1.5, 516, 546)]
    [InlineData(2.0, 688, 728)]
    public void LargeCard_IsAMediumPlusTwoGridRowsTall_InBothThemes(double scale, int width, int height)
    {
        Assert.Equal(new SizeInt32(width, height), WidgetLayout.GetCardPixels(WidgetSize.Large, WidgetTheme.Glass, scale));
        Assert.Equal(new SizeInt32(width, height), WidgetLayout.GetCardPixels(WidgetSize.Large, WidgetTheme.Pixel, scale));
        Assert.Equal((172, 182), PixelCard.ArtSize(WidgetSize.Large));
    }

    [Fact]
    public void AtAnOddScale_ThePixelCardComesOutALittleLarger()
    {
        Assert.Equal(new SizeInt32(430, 205), WidgetLayout.GetCardPixels(WidgetSize.Medium, WidgetTheme.Glass, 1.25));
        Assert.Equal(new SizeInt32(516, 246), WidgetLayout.GetCardPixels(WidgetSize.Medium, WidgetTheme.Pixel, 1.25));
    }
}
