using DeskWidgets.Pixel;

namespace DeskWidgets.Motion;

/// <summary>
/// The Pixel motion vocabulary (MOTION.md): whole pictures on whole art pixels in palette colours, no alpha, no sub-pixel positions,
/// one tick of <see cref="StepAnimator.FrameMs"/> per frame. This holds the pieces the scenes share: the 4×4 Bayer mask (DISSOLVE, LEAVE),
/// the empty frame of GROW and SHRINK, and the three-frame MOVE/POP/LEAVE plan of a list that changed.
/// </summary>
internal static class PixelMotion
{
    private static readonly int[] Bayer =
    [
        0, 8, 2, 10,
        12, 4, 14, 6,
        3, 11, 1, 9,
        15, 7, 13, 5,
    ];

    /// <summary>The 4×4 Bayer value at a canvas pixel (0 to 15).</summary>
    public static int BayerAt(int x, int y) => Bayer[(y & 3) * 4 + (x & 3)];

    /// <summary>Copies the pixels of <paramref name="source"/> to <paramref name="target"/> at (x, y), but only where the Bayer value at the target pixel is at least <paramref name="minBayer"/> (LEAVE: 8 is half, 12 a quarter).</summary>
    public static void BlitMasked(PixelCanvas source, PixelCanvas target, int x, int y, int minBayer)
    {
        for (int sy = 0; sy < source.Height; sy++)
        {
            for (int sx = 0; sx < source.Width; sx++)
            {
                uint value = source.Get(sx, sy);
                if (value != 0 && BayerAt(x + sx, y + sy) >= minBayer)
                    target.Set(x + sx, y + sy, value);
            }
        }
    }

    /// <summary>A copy of a rectangle of a canvas (what a leaving cell looked like).</summary>
    public static PixelCanvas Snapshot(PixelCanvas source, int x, int y, int width, int height)
    {
        var copy = new PixelCanvas(width, height);
        for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
                copy.Set(column, row, source.Get(x + column, y + row));
        return copy;
    }

    /// <summary>
    /// A popup's empty frame for GROW and SHRINK: its own outline, fill, highlight and shade with its corner stair and its hard shadow, at
    /// <paramref name="share"/> of its size along the growing axis, anchored where it grows from, on a transparent canvas of the whole popup.
    /// </summary>
    public static PixelCanvas EmptyPanel(PixelPalette p, int panelWidth, int panelHeight, int[] corner, int shadowOffset, GrowFrom from, double share)
    {
        bool horizontal = from is GrowFrom.Left or GrowFrom.Right or GrowFrom.Center;
        bool vertical = from is GrowFrom.Top or GrowFrom.Bottom or GrowFrom.Center;
        int width = horizontal ? Math.Clamp((int)Math.Round(panelWidth * share), 8, panelWidth) : panelWidth;
        int height = vertical ? Math.Clamp((int)Math.Round(panelHeight * share), 8, panelHeight) : panelHeight;
        var panel = new PixelCanvas(width, height);
        PixelFrame.Draw(panel, 0, 0, width, height, corner, p.Outline, p.Panel, p.Highlight, p.Shade, 2);
        var framed = PixelFrame.WithShadow(panel, p.Shadow, shadowOffset);
        var full = new PixelCanvas(panelWidth + shadowOffset, panelHeight + shadowOffset);
        int left = from switch { GrowFrom.Right => panelWidth - width, GrowFrom.Center => (panelWidth - width) / 2, _ => 0 };
        int top = from switch { GrowFrom.Bottom => panelHeight - height, GrowFrom.Center => (panelHeight - height) / 2, _ => 0 };
        for (int y = 0; y < framed.Height; y++)
            for (int x = 0; x < framed.Width; x++)
                if (framed.Get(x, y) != 0)
                    full.Set(left + x, top + y, framed.Get(x, y));
        return full;
    }

    /// <summary>
    /// GROW and SHRINK's empty frame: the target's own outline, fill, highlight and shade with the same corner stair, at the given
    /// share of its width and full height, anchored at its left or right edge (the edge next to the card), on a transparent canvas of the final size.
    /// </summary>
    public static PixelCanvas EmptyFrame(PixelPalette p, int panelWidth, int panelHeight, int shadowOffset, double share, bool anchorRight)
    {
        int width = Math.Clamp((int)Math.Round(panelWidth * share), 8, panelWidth);
        var frame = new PixelCanvas(width, panelHeight);
        PixelFrame.Draw(frame, 0, 0, width, panelHeight, PixelFrame.DialogCorner, p.Outline, p.Panel, p.Highlight, p.Shade, 2);
        var framed = PixelFrame.WithShadow(frame, p.Shadow, shadowOffset);
        var full = new PixelCanvas(panelWidth + shadowOffset, panelHeight + shadowOffset);
        int offset = anchorRight ? panelWidth - width : 0;
        for (int y = 0; y < framed.Height; y++)
            for (int x = 0; x < framed.Width; x++)
                if (framed.Get(x, y) != 0)
                    full.Set(offset + x, y, framed.Get(x, y));
        return full;
    }
}

/// <summary>The edge a popup grows from (and shrinks back to): a menu from the pointer's corner, a submenu from its parent, a dialog from its centre.</summary>
internal enum GrowFrom
{
    Top,
    Bottom,
    Left,
    Right,
    Center,
}

/// <summary>
/// B5's GROW and SHRINK for a popup: the shares of its size (of its height for Top and Bottom, of its width for Left and Right, of both for
/// Center) shown before the whole popup comes on. GROW plays the shares in order and then the popup; SHRINK plays them back to front and then it is gone.
/// </summary>
internal sealed record GrowSpec(GrowFrom From, double[] Shares)
{
    /// <summary>A menu: height 1/3, 2/3, then the rows.</summary>
    public static GrowSpec Menu(bool above) => new(above ? GrowFrom.Bottom : GrowFrom.Top, [1.0 / 3, 2.0 / 3]);

    /// <summary>A submenu: half its width, then all of it.</summary>
    public static GrowSpec Submenu(bool toTheLeft) => new(toTheLeft ? GrowFrom.Right : GrowFrom.Left, [0.5]);

    /// <summary>A dialog: from the centre on both axes, 1/3, 2/3, then all of it.</summary>
    public static GrowSpec Dialog() => new(GrowFrom.Center, [1.0 / 3, 2.0 / 3]);
}

internal enum CellEffect
{
    None,
    Move,
    Pop,
}

/// <summary>A cell that left, as it looked: its pixels and where its top-left was on the canvas.</summary>
internal sealed record LeavingCell(PixelCanvas Pixels, int X, int Y);

/// <summary>
/// What a changed list does for three frames (A2 and A4): the cells that stayed in their row MOVE (positions at 1/3 and 2/3 of the way,
/// then final), the ones that changed rows, or are new, POP in at their new place, and the old cells of the ones that changed rows or are
/// gone LEAVE where they were (half their pixels, a quarter, then gone). <see cref="Frame"/> runs 1 to 3.
/// </summary>
internal sealed class ListMotion
{
    public const int Frames = 3;

    private readonly Dictionary<int, (CellEffect Effect, int Dx)> _cells = [];

    public int Frame { get; set; } = 1;

    public List<LeavingCell> Leaving { get; } = [];

    public bool IsEmpty => _cells.Count == 0 && Leaving.Count == 0;

    /// <summary>What the cell at <paramref name="newIndex"/> does now: how far back it still has to go (MOVE), or that it pops in.</summary>
    public (CellEffect Effect, int Dx) Of(int newIndex) => _cells.GetValueOrDefault(newIndex);

    /// <summary>MOVE's offset at the current frame: the distance back to the old place, shrinking to 0 at frame 3.</summary>
    public int MoveOffset(int dx) => (int)Math.Round(dx * (Frames - Frame) / (double)Frames);

    /// <summary>
    /// Works out the effects from the old and the new keys in cell order. <paramref name="oldPlace"/> and <paramref name="newPlace"/> give a
    /// cell's top-left on the canvas, or null when it is out of view ("only cells in view animate"); <paramref name="snapshotOld"/>
    /// takes a picture of an old cell that has to leave. Returns null when nothing animates.
    /// </summary>
    public static ListMotion? Plan(IReadOnlyList<string> oldKeys, IReadOnlyList<string> newKeys, Func<int, (int X, int Y)?> oldPlace, Func<int, (int X, int Y)?> newPlace, Func<int, LeavingCell?> snapshotOld)
    {
        var motion = new ListMotion();
        var oldIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < oldKeys.Count; i++)
            oldIndex[oldKeys[i]] = i;
        var newKeysSet = new HashSet<string>(newKeys, StringComparer.OrdinalIgnoreCase);
        var leftBehind = new HashSet<int>();
        for (int j = 0; j < newKeys.Count; j++)
        {
            if (newPlace(j) is not { } to)
                continue;
            if (!oldIndex.TryGetValue(newKeys[j], out int i))
            {
                motion._cells[j] = (CellEffect.Pop, 0);
                continue;
            }
            if (i == j)
                continue;
            var from = oldPlace(i);
            if (from is null)
            {
                motion._cells[j] = (CellEffect.Pop, 0); // it comes into view from out of view
            }
            else if (from.Value.Y == to.Y)
            {
                if (from.Value.X != to.X)
                    motion._cells[j] = (CellEffect.Move, from.Value.X - to.X);
            }
            else
            {
                motion._cells[j] = (CellEffect.Pop, 0);
                leftBehind.Add(i); // it leaves its old cell
            }
        }
        for (int i = 0; i < oldKeys.Count; i++)
        {
            if (!leftBehind.Contains(i) && newKeysSet.Contains(oldKeys[i]))
                continue;
            if (oldPlace(i) is not null && snapshotOld(i) is { } cell)
                motion.Leaving.Add(cell);
        }
        return motion.IsEmpty ? null : motion;
    }
}
