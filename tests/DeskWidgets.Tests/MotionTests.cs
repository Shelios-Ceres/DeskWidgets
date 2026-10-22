using DeskWidgets.Motion;
using DeskWidgets.Pixel;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

/// <summary>The pure parts of Phase 12's motion: the easings, the Bayer mask, and what a changed list does for three frames.</summary>
public class MotionTests
{
    private static FolderItem Item(string name) => new() { Path = @"D:\Cards\" + name, DisplayName = name, Kind = FolderItemKind.Document, LabelName = name, AddedUtc = DateTime.UtcNow };

    // ----- Bezier -----

    [Theory]
    [InlineData(0.2, 0.8, 0.2, 1)]
    [InlineData(0.4, 0, 1, 1)]
    [InlineData(0.3, 0, 0.1, 1)]
    public void TheGlassEasings_StartAtZero_EndAtOne_AndNeverGoBackwards(double x1, double y1, double x2, double y2)
    {
        var curve = new Bezier(x1, y1, x2, y2);
        Assert.Equal(0, curve.At(0));
        Assert.Equal(1, curve.At(1));
        double previous = 0;
        for (int i = 1; i <= 100; i++)
        {
            double value = curve.At(i / 100.0);
            Assert.InRange(value, previous - 1e-9, 1 + 1e-9);
            previous = value;
        }
    }

    [Fact]
    public void OutIsFastAtTheStart_AndInIsSlow()
    {
        Assert.True(Bezier.Out.At(0.25) > 0.5);
        Assert.True(Bezier.In.At(0.25) < 0.25);
    }

    // ----- the Bayer mask -----

    [Fact]
    public void TheBayerMask_IsTheDesignsMatrix_AndRepeatsEveryFourPixels()
    {
        int[] rows = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(rows[(y % 4) * 4 + x % 4], PixelMotion.BayerAt(x, y));
    }

    [Theory]
    [InlineData(8, 8)]
    [InlineData(12, 4)]
    [InlineData(0, 16)]
    public void ALeavingCell_KeepsHalfThenAQuarterOfItsPixels(int minBayer, int keptOfSixteen)
    {
        var source = new PixelCanvas(4, 4);
        source.Fill(0, 0, 4, 4, 0xFF123456);
        var target = new PixelCanvas(4, 4);
        PixelMotion.BlitMasked(source, target, 0, 0, minBayer);
        Assert.Equal(keptOfSixteen, target.Pixels.Count(p => p != 0));
    }

    [Fact]
    public void TheEmptyFrame_IsAShareOfTheWidth_OnTheCardsSide()
    {
        var palette = PixelPalette.For(dark: false);
        var left = PixelMotion.EmptyFrame(palette, 90, 40, 2, 1.0 / 3, anchorRight: false);
        var right = PixelMotion.EmptyFrame(palette, 90, 40, 2, 1.0 / 3, anchorRight: true);
        Assert.Equal((92, 42), (left.Width, left.Height)); // the window's size, shadow included
        Assert.NotEqual(0u, left.Get(10, 20)); // grown from the left edge: the first third is the box
        Assert.Equal(0u, left.Get(60, 20)); // and the rest is transparent
        Assert.Equal(0u, right.Get(10, 20));
        Assert.NotEqual(0u, right.Get(80, 20));
    }

    // ----- what a changed list does -----

    private static (int X, int Y)? Place(int index) => (7 + index % 5 * 41, 20 + index / 5 * 33);

    [Fact]
    public void AnUnchangedList_Animates_Nothing()
    {
        string[] keys = ["a", "b", "c"];
        Assert.Null(ListMotion.Plan(keys, keys, Place, Place, _ => null));
    }

    [Fact]
    public void ACellThatStaysInItsRow_Moves_ByTheDistanceBack()
    {
        var motion = ListMotion.Plan(["a", "b", "c"], ["c", "a", "b"], Place, Place, _ => null)!;
        var (effect, dx) = motion.Of(0); // "c" was at index 2, now at 0: 82 art pixels back
        Assert.Equal(CellEffect.Move, effect);
        Assert.Equal(2 * 41, dx);
        Assert.Equal(CellEffect.Move, motion.Of(1).Effect);
        Assert.Empty(motion.Leaving);
        motion.Frame = 1;
        Assert.Equal(55, motion.MoveOffset(82)); // two thirds of the way still to go, rounded
        motion.Frame = 2;
        Assert.Equal(27, motion.MoveOffset(82));
        motion.Frame = 3;
        Assert.Equal(0, motion.MoveOffset(82));
    }

    [Fact]
    public void ACellThatChangesRows_Pops_AtItsNewPlace_AndItsOldCellLeaves()
    {
        var motion = ListMotion.Plan(["a", "b", "c", "d", "e", "x"], ["x", "b", "c", "d", "e", "a"], Place, Place, index => new LeavingCell(new PixelCanvas(2, 2), index, 0))!;
        // "x" moved from row 1 (index 5) to row 0 (index 0), and "a" from row 0 to row 1.
        Assert.Equal(CellEffect.Pop, motion.Of(0).Effect);
        Assert.Equal(CellEffect.Pop, motion.Of(5).Effect);
        Assert.Equal(2, motion.Leaving.Count); // their old cells, as they were
        Assert.Contains(motion.Leaving, cell => cell.X == 0);
        Assert.Contains(motion.Leaving, cell => cell.X == 5);
    }

    [Fact]
    public void ANewItemPops_AndARemovedItemLeaves()
    {
        var motion = ListMotion.Plan(["a", "b", "c"], ["a", "b", "n"], Place, Place, index => new LeavingCell(new PixelCanvas(2, 2), index, 0))!;
        Assert.Equal(CellEffect.Pop, motion.Of(2).Effect);
        Assert.Equal(CellEffect.None, motion.Of(0).Effect);
        Assert.Single(motion.Leaving);
        Assert.Equal(2, motion.Leaving[0].X);
    }

    [Fact]
    public void OnlyCellsInView_Animate()
    {
        // Index 3 is scrolled out of sight both before and after: nothing to show for it.
        (int, int)? InView(int index) => index < 3 ? Place(index) : null;
        Assert.Null(ListMotion.Plan(["a", "b", "c", "d"], ["a", "b", "c", "e"], InView, InView, _ => null));
        // A cell that comes into view from out of view pops; one that goes out of view just goes (no leaving picture).
        var motion = ListMotion.Plan(["a", "b", "c", "d"], ["d", "a", "b", "c"], InView, InView, index => new LeavingCell(new PixelCanvas(1, 1), index, 0))!;
        Assert.Equal(CellEffect.Pop, motion.Of(0).Effect);
        Assert.Equal(CellEffect.Move, motion.Of(1).Effect);
        Assert.Empty(motion.Leaving);
    }

    // ----- the Pixel scenes -----

    [Fact]
    public void AHoveredCardCell_LiftsItsIconOnePixel_AndAPressedOneDropsIt()
    {
        var palette = PixelPalette.For(dark: false);
        var items = new[] { "Budget", "Report", "Notes" }.Select(Item).ToList();
        var icons = new PixelFolderIcons(() => { });
        PixelCanvas Draw(FolderHit hover, FolderHit pressed, FolderHit flash = default)
        {
            var view = new FolderPixelView("Projects", @"D:\Work\Projects", items, true, WidgetSize.Medium, hover, pressed, null, icons, 0, null, flash);
            var plan = FolderPixelScene.Plan(view);
            var canvas = new PixelCanvas(plan.Width, plan.Height);
            FolderPixelScene.Draw(canvas, palette, view, plan);
            return canvas;
        }
        var first = new FolderHit(FolderHitKind.Item, 0);
        var plain = Draw(FolderHit.None, FolderHit.None);
        var hover = Draw(first, FolderHit.None);
        var pressed = Draw(FolderHit.None, first);
        var flash = Draw(first, FolderHit.None, first);
        // The icon's top row (its outline) is at y 19 at rest, y 18 hovered, y 20 pressed; the plate's colour differs but the icon's own pixels move.
        Func<PixelCanvas, int, int, uint> at = (canvas, x, y) => canvas.Get(x, y);
        uint iconPixel = plain.Get(15 + 8, 19 + 4); // a pixel inside the icon at rest
        Assert.Equal(iconPixel, at(hover, 15 + 8, 18 + 4));
        Assert.Equal(iconPixel, at(pressed, 15 + 8, 20 + 4));
        Assert.NotEqual(plain.Pixels, flash.Pixels);
        Assert.Equal(palette.Select, flash.Get(4, 19)); // the flash is the Select plate
    }

    [Fact]
    public void TheDropTint_IsSparseAtFirst_AndHasNoLabel()
    {
        var palette = PixelPalette.For(dark: false);
        var items = new[] { "Budget", "Report", "Notes" }.Select(Item).ToList();
        var icons = new PixelFolderIcons(() => { });
        int Tinted(int phase)
        {
            var view = new FolderPixelView("Projects", @"D:\Work\Projects", items, true, WidgetSize.Medium, FolderHit.None, FolderHit.None, DropVerb.Move, icons, phase);
            var plan = FolderPixelScene.Plan(view);
            var canvas = new PixelCanvas(plan.Width, plan.Height);
            FolderPixelScene.Draw(canvas, palette, view, plan);
            return canvas.Pixels.Count(p => p == palette.Select);
        }
        int none = Tinted(0), sparse = Tinted(1) - none, full = Tinted(2) - none; // some Select pixels belong to the card itself
        Assert.InRange(sparse, 1, full / 3); // 1 dot in 16 against 1 in 4, and no label plate
        Assert.True(full > sparse * 3);
    }
}
