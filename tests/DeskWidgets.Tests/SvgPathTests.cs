using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

public class SvgPathTests
{
    [Fact]
    public void RelativeAndAbsoluteCommands_BecomeAbsolutePoints()
    {
        var ops = SvgPath.Parse("M10 10l5 0 0 5H30V40h-5z");
        Assert.Equal(
            [PathOpKind.Move, PathOpKind.Line, PathOpKind.Line, PathOpKind.Line, PathOpKind.Line, PathOpKind.Line, PathOpKind.Close],
            ops.Select(o => o.Kind).ToArray());
        Assert.Equal((15.0, 10.0), (ops[1].X, ops[1].Y));
        Assert.Equal((15.0, 15.0), (ops[2].X, ops[2].Y));
        Assert.Equal((30.0, 15.0), (ops[3].X, ops[3].Y));
        Assert.Equal((30.0, 40.0), (ops[4].X, ops[4].Y));
        Assert.Equal((25.0, 40.0), (ops[5].X, ops[5].Y));
    }

    [Fact]
    public void CompactNumbers_AreSplitTheWaySvgDoes()
    {
        // "2.2.96" is 2.2 and .96; "4-4" is 4 and -4.
        var ops = SvgPath.Parse("M14 22a4 4 0 0 1 4-4h9.2a3 3 0 0 1 2.2.96");
        Assert.Equal((18.0, 18.0), (ops[1].X, ops[1].Y));
        Assert.Equal(PathOpKind.Arc, ops[1].Kind);
        Assert.Equal((4.0, 4.0), (ops[1].Rx, ops[1].Ry));
        Assert.False(ops[1].LargeArc);
        Assert.True(ops[1].Sweep);
        Assert.Equal((27.2, 18.0), (ops[2].X, ops[2].Y));
        Assert.Equal(29.4, ops[3].X, 6);
        Assert.Equal(18.96, ops[3].Y, 6);
    }

    [Fact]
    public void ArcFlags_MayRunTogetherWithTheNextNumber()
    {
        var ops = SvgPath.Parse("M0 0a1 1 0 011 1");
        Assert.True(ops[1].Sweep);
        Assert.False(ops[1].LargeArc);
        Assert.Equal((1.0, 1.0), (ops[1].X, ops[1].Y));
    }

    [Fact]
    public void ExtraPairsAfterAMove_AreLines()
    {
        var ops = SvgPath.Parse("M0 0 10 0 10 10");
        Assert.Equal([PathOpKind.Move, PathOpKind.Line, PathOpKind.Line], ops.Select(o => o.Kind).ToArray());
    }

    [Fact]
    public void ASmoothCurve_ReflectsTheLastControlPoint()
    {
        var ops = SvgPath.Parse("M0 0C0 10 10 10 10 0S20 -10 20 0");
        Assert.Equal(PathOpKind.Cubic, ops[2].Kind);
        Assert.Equal((10.0, -10.0), (ops[2].X1, ops[2].Y1)); // 2 * (10,0) - (10,10)
    }

    [Fact]
    public void ABadPath_Throws()
    {
        Assert.Throws<FormatException>(() => SvgPath.Parse("10 10"));
        Assert.Throws<FormatException>(() => SvgPath.Parse("M10"));
        Assert.Throws<FormatException>(() => SvgPath.Parse("M0 0X1 1"));
    }

    [Fact]
    public void EveryPathOfTheGlassArt_Parses_AndStaysInsideTheBox()
    {
        Assert.Equal(27, GlassTileArt.All.Count); // 14 file types and 13 apps
        foreach (var (key, art) in GlassTileArt.All)
        {
            foreach (var layer in art.Layers)
            {
                var ops = SvgPath.Parse(layer.Path);
                Assert.NotEmpty(ops);
                Assert.Equal(PathOpKind.Move, ops[0].Kind);
                foreach (var op in ops.Where(o => o.Kind != PathOpKind.Close))
                    Assert.InRange(op.X, -1, 65);
                Assert.True(layer.Kind == TileLayerKind.Stroke ? layer.StrokeWidth > 0 : layer.StrokeWidth == 0, key);
            }
        }
    }

    [Fact]
    public void EveryFileType_AndEveryAppOfTheTable_HasItsArt()
    {
        foreach (var kind in Enum.GetValues<FolderItemKind>())
            Assert.NotNull(GlassTileArt.For(kind));
        foreach (var row in AppIconPack.Rows)
            Assert.NotNull(GlassTileArt.ForApp(row.Key));
    }
}
