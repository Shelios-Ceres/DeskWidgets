using DeskWidgets.Pixel;

namespace DeskWidgets.Tests;

public class PixelTextTests
{
    [Fact]
    public void Text_IsSetInCapitalsWithoutAccents()
    {
        var text = PixelText.Layout("São Paulo", PixelFonts.Small);

        Assert.Equal(new[] { "SAO PAULO" }, text.Lines);
        Assert.False(text.IsFallback);
    }

    [Fact]
    public void Width_IsTheGlyphsPlusOneColumnBetweenThem()
    {
        // In the small font A, B and C are 3 columns wide.
        Assert.Equal(11, PixelText.Layout("ABC", PixelFonts.Small).Width);
        Assert.Equal(22, PixelText.Layout("ABC", PixelFonts.Small, scale: 2).Width);
    }

    [Fact]
    public void LongText_WrapsAtSpaces()
    {
        // "LOS ANGELES" is 43 columns wide in the small font.
        var text = PixelText.Layout("Los Angeles", PixelFonts.Small, maxWidth: 30, maxLines: 2);

        Assert.Equal(new[] { "LOS", "ANGELES" }, text.Lines);
    }

    [Fact]
    public void AWordTooWideForTheLine_IsCutWithAnEllipsis_NotSplit()
    {
        var text = PixelText.Layout("Headphones", PixelFonts.Small, maxWidth: 30, maxLines: 2);

        Assert.Equal("HEADPH…", Assert.Single(text.Lines));
        Assert.True(text.Width <= 30);
    }

    [Fact]
    public void TextThePixelFontsCannotSpell_IsSetWholeInTheSystemFont()
    {
        var text = PixelText.Layout("蓝牙键盘 2", PixelFonts.Regular, scale: 2);

        Assert.True(text.IsFallback);
        Assert.Equal(1, text.Scale); // the fallback glyphs are already large
        Assert.Equal(new[] { "蓝牙键盘 2" }, text.Lines);
    }
}
