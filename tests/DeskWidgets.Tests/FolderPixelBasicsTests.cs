using DeskWidgets.Pixel;

namespace DeskWidgets.Tests;

public class FolderPixelBasicsTests
{
    [Fact]
    public void ThePixelFonts_CanSpellAPath()
    {
        var path = PixelText.Layout(@"C:\Users\asus\工作".Replace("工作", "Work"), PixelFonts.Small);
        Assert.False(path.IsFallback); // the backslash glyph keeps a path out of the SimSun fallback
        Assert.Equal(@"C:\USERS\ASUS\WORK", path.Lines[0]);
        Assert.False(PixelText.Layout(@"D:\Cards", PixelFonts.Regular).IsFallback);
    }

    [Fact]
    public void ALineNeverEndsWithAnOpeningMark()
    {
        // "移动到“工作”" in a cell that fits four characters used to break as "移动到“ / 工作”".
        var text = PixelText.Layout("移动到“工作”", PixelFonts.System, maxWidth: 12 * 4, maxLines: 2);
        Assert.Equal(["移动到", "“工作”"], text.Lines);
        foreach (var (open, close) in new[] { ("“", "”"), ("（", "）"), ("「", "」"), ("『", "』"), ("《", "》") })
        {
            var text2 = PixelText.Layout("移动到" + open + "工作" + close, PixelFonts.System, maxWidth: 12 * 4, maxLines: 2);
            Assert.Equal(["移动到", open + "工作" + close], text2.Lines);
        }
    }

    [Fact]
    public void TheSixteenSpriteRoles_AreTheSweetieColours_TheSameByDayAndByNight()
    {
        var day = PixelPalette.For(dark: false);
        var night = PixelPalette.For(dark: true);
        (char Symbol, uint Argb)[] expected =
        [
            ('#', 0xFF1A1C2C), ('p', 0xFF5D275D), ('r', 0xFFB13E53), ('o', 0xFFEF7D57), ('y', 0xFFFFCD75), ('L', 0xFFA7F070),
            ('G', 0xFF38B764), ('t', 0xFF257179), ('n', 0xFF29366F), ('b', 0xFF3B5DC9), ('l', 0xFF41A6F6), ('c', 0xFF73EFF7),
            ('w', 0xFFF4F4F4), ('g', 0xFF94B0C2), ('s', 0xFF566C86), ('d', 0xFF333C57),
        ];
        foreach (var (symbol, argb) in expected)
        {
            Assert.Equal(argb, day.Sprite(symbol));
            Assert.Equal(argb, night.Sprite(symbol));
        }
        Assert.Equal(0u, day.Sprite('.'));
        Assert.Equal(0u, day.Sprite('?'));
    }
}
