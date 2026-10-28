using DeskWidgets.Pixel;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

/// <summary>Phase 8: Fusion Pixel 8 px for the Big Folder's Chinese, the three metric sets, and emoji as 7 × 7 tiles.</summary>
[Collection("PixelEmoji")]
public class FolderFusionAndEmojiTests
{
    private static FolderItem Item(string name) => new()
    {
        Path = @"D:\Cards\Projects\" + name,
        DisplayName = name,
        Kind = FolderItemKind.Other,
        LabelName = name,
        AddedUtc = new DateTime(2026, 10, 21),
    };

    private static FolderPixelView CardView(WidgetSize size, string folder, params string[] names)
        => new(folder, @"D:\Work\Projects", names.Select(Item).ToList(), true, size, FolderHit.None, FolderHit.None, null, new PixelFolderIcons(() => { }));

    // ----- the font -----

    [Fact]
    public void Fusion_SpellsHanziLatinDigitsAndPunctuation_OnEightPixelAdvances()
    {
        Assert.True(PixelFonts.Fusion.Covers("腾讯会议项目资料季度报告 Report 2026 (2)…"));
        var hanzi = PixelFonts.Fusion.Glyph('会')!;
        Assert.Equal((8, 9), (hanzi.Width, hanzi.Height)); // a 9-row cell, 7 × 7 ink on an 8-pixel advance
        Assert.Equal(32, PixelFonts.Fusion.Measure("腾讯会议")); // four hanzi in a 36-pixel cell
        Assert.True(PixelFonts.Fusion.Glyph('R') is { Width: < 8 }); // proportional Latin
    }

    [Fact]
    public void Fusion_DoesNotSpellACharacterOutsideItsTable()
    {
        Assert.False(PixelFonts.Fusion.Covers("\U00020000")); // only the BMP is packed
    }

    // ----- the font chain: pixel font, then Fusion, then SimSun, one font per text -----

    [Fact]
    public void TheFontChain_PicksTheFirstFontThatSpellsAllOfTheText()
    {
        Assert.Same(PixelFonts.Small, FolderPixelScene.FontFor("Budget 2026", PixelFonts.Small));
        Assert.Same(PixelFonts.Fusion, FolderPixelScene.FontFor("季度报告", PixelFonts.Small));
        Assert.Same(PixelFonts.Fusion, FolderPixelScene.FontFor("Report 报告", PixelFonts.Small)); // Latin too: mixed case
        Assert.Same(PixelFonts.Fusion, FolderPixelScene.FontFor("🔥 Projects", PixelFonts.Regular)); // the pixel fonts never cover an emoji
        Assert.Same(PixelFonts.System, FolderPixelScene.FontFor("报告\U00020000", PixelFonts.Small));
    }

    [Fact]
    public void AFusionName_StaysInFusion_WhenShortened()
    {
        var name = FolderPixelScene.OneLine("2026年度报告审计结果", PixelFonts.Small, 36);
        Assert.Equal(1, name.Rank);
        Assert.True(name.Width <= 36);
        Assert.Equal("腾讯会议", FolderPixelScene.OneLine("腾讯会议", PixelFonts.Small, 36).Lines[0]); // 4 hanzi fit
    }

    // ----- the three metric sets of the card -----

    [Theory]
    [InlineData(WidgetSize.Small, 0, 7, 7, 2, 5, new[] { 19, 52 })]
    [InlineData(WidgetSize.Small, 1, 7, 9, 1, 9, new[] { 19, 49 })]
    [InlineData(WidgetSize.Small, 2, 5, 12, 1, 12, new[] { 19, 48 })]
    [InlineData(WidgetSize.Large, 0, 7, 7, 2, 5, new[] { 19, 61, 103, 145 })]
    [InlineData(WidgetSize.Large, 1, 7, 9, 1, 9, new[] { 19, 59, 99, 139 })]
    [InlineData(WidgetSize.Large, 2, 7, 12, 1, 12, new[] { 22, 63, 105, 146 })]
    public void TheCard_TakesTheMetricSetOfItsHighestRankedText(WidgetSize size, int rank, int headerY, int headerHeight, int nameGap, int nameHeight, int[] rows)
    {
        string name = rank switch { 0 => "Budget", 1 => "预算表", _ => "预算\U00020000" };
        var plan = FolderPixelScene.Plan(CardView(size, "Projects", name));
        Assert.Equal(rank, plan.Rank);
        Assert.Equal((headerY, headerHeight, nameGap, nameHeight), (plan.HeaderY, plan.HeaderHeight, plan.NameGap, plan.NameHeight));
        Assert.Equal(rows, plan.Rows);
    }

    // ----- Phase 11: two-line names on the Large card -----

    [Theory]
    [InlineData(0, 30, 18, 25)] // pixel font: cell 16 + 2 + 5 + 2 + 5, lines at icon y + 18 and + 25
    [InlineData(1, 36, 17, 27)] // Fusion: cell 16 + 1 + 9 + 1 + 9, lines at + 17 and + 27
    public void TheLargeCard_WrapsNamesOntoTwoLines_ToTheRowsAndCellOfItsSet(int rank, int cell, int firstLine, int secondLine)
    {
        string longName = rank == 0 ? "Quarterly report 2026" : "二零二六年第三季度财务报告";
        string shortName = rank == 0 ? "Notes" : "预算表";
        var plan = FolderPixelScene.Plan(CardView(WidgetSize.Large, "Projects", longName, shortName));

        Assert.Equal(rank, plan.Rank);
        Assert.True(plan.TwoLines);
        Assert.Equal(2, plan.Names[0].Length);
        Assert.Single(plan.Names[1]); // a name that fits stays one line
        Assert.Equal(cell, plan.CellHeight);
        Assert.Equal(firstLine, 16 + plan.NameGap);
        Assert.Equal(secondLine, 16 + plan.NameGap + (rank == 0 ? 7 : 10));
        // The click rect is (x - 1, icon y - 2, 38, cell + 4) and it ends inside the card, 7 above the bottom edge or less.
        var (x, y, w, h, _) = plan.Regions[1];
        Assert.Equal((plan.Columns[0] - 1, plan.Rows[0] - 2, 38, cell + 4), (x, y, w, h));
        Assert.True(plan.Rows[^1] + cell <= plan.Height - 7);
    }

    [Fact]
    public void TheLargeCard_KeepsOneLineInTheSimSunSet_AndSmallAndMediumKeepOneLine()
    {
        var simsun = FolderPixelScene.Plan(CardView(WidgetSize.Large, "Projects", "预算𠀀报告报告报告报告报告"));
        Assert.Equal(2, simsun.Rank);
        Assert.False(simsun.TwoLines);
        Assert.Single(simsun.Names[0]);
        Assert.Equal(12 + 1 + 16, simsun.CellHeight);

        foreach (var size in new[] { WidgetSize.Small, WidgetSize.Medium })
        {
            var plan = FolderPixelScene.Plan(CardView(size, "Projects", "Quarterly report 2026"));
            Assert.False(plan.TwoLines);
            Assert.Single(plan.Names[0]);
        }
    }

    [Fact]
    public void TheLargeCard_StaysInsideItsHeight_InEveryMetricSet()
    {
        foreach (var name in new[] { "Quarterly report 2026", "二零二六年第三季度财务报告", "预算𠀀报告" })
        {
            var plan = FolderPixelScene.Plan(CardView(WidgetSize.Large, "Projects", name));
            Assert.Equal(182, plan.Height);
            Assert.True(plan.Rows[^1] + plan.CellHeight <= 175, $"rank {plan.Rank}");
        }
    }

    [Fact]
    public void AFusionHeader_AloneSetsTheFusionMetrics_AndASimSunNameOutranksIt()
    {
        Assert.Equal(1, FolderPixelScene.Plan(CardView(WidgetSize.Medium, "项目资料", "Budget")).Rank);
        Assert.Equal(2, FolderPixelScene.Plan(CardView(WidgetSize.Medium, "项目资料", "Budget\U00020000")).Rank);
    }

    [Fact]
    public void TheFusionCard_KeepsItsContentInsideTheCard()
    {
        // 82 tall: the last row's name ends 7 pixels above the bottom edge (49 + 17 + 9 = 75).
        var plan = FolderPixelScene.Plan(CardView(WidgetSize.Medium, "项目", "预算表"));
        Assert.Equal(82, plan.Height);
        Assert.Equal(75, plan.Rows[^1] + 16 + plan.NameGap + plan.NameHeight);
    }

    // ----- emoji -----

    [Fact]
    public void ALoneSurrogate_IsNoEmoji_AndDoesNotStopThePaint()
    {
        // A file name NTFS kept with an unpaired surrogate: Encode ran on every paint of the Pixel card and threw there.
        Assert.False(PixelEmoji.IsEmojiCluster("\uD83D"));
        string encoded = PixelEmoji.Encode("Notes \uD83D 🔥");
        Assert.Contains('\uD83D', encoded);
        Assert.True(PixelEmoji.IsEmoji(encoded[^1]));
        Assert.StartsWith("NOTES", PixelFonts.Small.Normalize("Notes \uD83D"), StringComparison.OrdinalIgnoreCase); // the capitals font's normalizing no longer throws
    }

    [Fact]
    public void AnEmojiIsOneCharacter_WholeClustersAndNothingElse()
    {
        string encoded = PixelEmoji.Encode("🔥 Hot");
        Assert.Equal(5, encoded.Length);
        Assert.True(PixelEmoji.IsEmoji(encoded[0]));
        Assert.Equal("🔥", PixelEmoji.Cluster(encoded[0]));
        Assert.Equal(" Hot", encoded[1..]);

        Assert.Single(PixelEmoji.Encode("👨‍👩‍👧")); // a ZWJ family
        Assert.Single(PixelEmoji.Encode("👍🏽")); // a skin tone
        Assert.Single(PixelEmoji.Encode("❤️")); // asks for emoji presentation
        Assert.Single(PixelEmoji.Encode("1️⃣")); // a keycap
        Assert.Single(PixelEmoji.Encode("🇨🇳")); // a flag
        Assert.Single(PixelEmoji.Encode("✅")); // emoji by default
        Assert.Equal("❤", PixelEmoji.Encode("❤")); // the plain heart is text
        Assert.Equal("你好 Report 2026", PixelEmoji.Encode("你好 Report 2026"));
    }

    [Fact]
    public void Encoding_IsStable_AndTheSameEmojiGetsTheSameCharacter()
    {
        string once = PixelEmoji.Encode("😀 Fun");
        Assert.Equal(once, PixelEmoji.Encode(once));
        Assert.Equal(PixelEmoji.Encode("😀")[0], PixelEmoji.Encode("a😀b")[1]);
    }

    [Fact]
    public void AnEmoji_TakesOneEightPixelAdvance_InFusion_AndTwelveInSimSun()
    {
        var fusion = FolderPixelScene.Layout("🔥 Projects", PixelFonts.Regular);
        Assert.Equal(1, fusion.Rank);
        Assert.Equal(8, PixelFonts.Fusion.Measure(PixelEmoji.Encode("🔥")));
        var simSun = FolderPixelScene.Layout("\U00020000🔥", PixelFonts.Regular);
        Assert.Equal(2, simSun.Rank);
        Assert.Equal(12, PixelFonts.System.Glyph(PixelEmoji.Encode("🔥")[0])!.Width);
    }

    [Fact]
    public void TheShortener_NeverCutsAnEmoji()
    {
        var shortened = FolderPixelScene.OneLine("🔥🔥🔥🔥🔥🔥🔥🔥 Plan 2026", PixelFonts.Small, 36);
        foreach (char c in shortened.Lines[0])
            Assert.False(char.IsSurrogate(c));
        Assert.True(shortened.Width <= 36);
    }

    private static byte[] Picture(Func<int, int, (byte B, byte G, byte R, byte A)> pixel)
    {
        var bytes = new byte[64 * 64 * 4];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                var (b, g, r, a) = pixel(x, y);
                int i = (y * 64 + x) * 4;
                (bytes[i], bytes[i + 1], bytes[i + 2], bytes[i + 3]) = (b, g, r, a);
            }
        }
        return bytes;
    }

    [Fact]
    public void TheTile_IsCroppedToTheInk_AndEveryPixelTakesTheNearestSweetieColour()
    {
        // A 20 × 20 red square off-centre: cropped to its ink, it fills the whole 7 × 7 tile.
        var picture = Picture((x, y) => x is >= 8 and < 28 && y is >= 30 and < 50 ? ((byte)0x53, (byte)0x3E, (byte)0xB1, (byte)255) : default);
        var tile = PixelEmoji.Shrink(picture, 64, 64, 7)!;
        Assert.All(tile, pixel => Assert.Equal(0xFFB13E53u, pixel));
        // At 11 × 11 as well.
        Assert.All(PixelEmoji.Shrink(picture, 64, 64, 11)!, pixel => Assert.Equal(0xFFB13E53u, pixel));
    }

    [Fact]
    public void TheTile_IsTransparentWhereTheAverageAlphaIsUnderOneHalf_AndTakesTheMostCommonColour()
    {
        // A yellow disc with a small red spot: the corners of the square round it stay empty, the spot does not win the middle.
        var picture = Picture((x, y) =>
        {
            double dx = x - 31.5, dy = y - 31.5;
            if (dx * dx + dy * dy > 28 * 28)
                return default;
            bool spot = (x - 40) * (x - 40) + (y - 31) * (y - 31) < 25;
            return spot ? ((byte)0x53, (byte)0x3E, (byte)0xB1, (byte)255) : ((byte)0x75, (byte)0xCD, (byte)0xFF, (byte)255);
        });
        var tile = PixelEmoji.Shrink(picture, 64, 64, 7)!;
        Assert.Equal(0u, tile[0]); // a corner
        Assert.Equal(0u, tile[6]);
        Assert.Equal(0xFFFFCD75u, tile[3 * 7 + 3]); // the centre is yellow
        Assert.Contains(tile, pixel => pixel == 0xFFB13E53u); // the spot shows where it is the most common colour
    }

    [Fact]
    public void APictureWithNoInk_HasNoTile()
    {
        Assert.Null(PixelEmoji.Shrink(new byte[64 * 64 * 4], 64, 64, 7));
    }

    [Fact]
    public void WithoutAPicture_TheEmojiIsDrawnAsAPlaceholderBox()
    {
        var before = PixelEmoji.Renderer;
        try
        {
            PixelEmoji.Renderer = _ => null;
            char c = PixelEmoji.Encode("🦄")[0];
            var canvas = new PixelCanvas(9, 9);
            PixelEmoji.Draw(canvas, c, 7, 1, 1, 0xFF112233u);
            Assert.Equal(0u, canvas.Get(1, 1)); // the corner stair
            Assert.Equal(0xFF112233u, canvas.Get(3, 1));
            Assert.Equal(0xFF112233u, canvas.Get(1, 3));
            Assert.Equal(0u, canvas.Get(3, 3)); // empty inside
        }
        finally
        {
            PixelEmoji.Renderer = before;
        }
    }

    [Fact]
    public void ATilePictureIsDrawnOverItsCell_WhenTheRendererHasOne()
    {
        var before = PixelEmoji.Renderer;
        try
        {
            PixelEmoji.Renderer = _ => Picture((x, y) => x is >= 4 and < 60 && y is >= 4 and < 60 ? ((byte)0x75, (byte)0xCD, (byte)0xFF, (byte)255) : default);
            var text = FolderPixelScene.Layout("🐙 ok", PixelFonts.Small);
            var canvas = new PixelCanvas(40, 12);
            text.Draw(canvas, 1, 1, 0xFF000000u);
            Assert.Equal(0xFFFFCD75u, canvas.Get(4, 4)); // inside the 7 × 7 tile at the text's origin
            Assert.Equal(0u, canvas.Get(10, 1)); // the cell after it is the space and text
        }
        finally
        {
            PixelEmoji.Renderer = before;
        }
    }

    [Fact]
    public void TheSystemEmojiFont_DrawsAColourPicture()
    {
        // The real renderer (Win2D, Segoe UI Emoji). Skipped where the machine has no way to render: the app then draws a placeholder.
        byte[]? picture;
        try
        {
            picture = EmojiRenderer.Render("😀");
        }
        catch (Exception)
        {
            return;
        }
        Assert.NotNull(picture);
        var tile = PixelEmoji.Shrink(picture!, 64, 64, 7)!;
        Assert.Contains(tile, pixel => pixel != 0);
        Assert.Contains(tile, pixel => pixel == 0xFFFFCD75u); // the face is yellow
    }
}
