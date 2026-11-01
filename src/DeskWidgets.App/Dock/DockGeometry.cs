namespace DeskWidgets.Dock;

/// <summary>What an icon's size setting means (PROPOSAL 3.2): Small, Medium and Large are icon sizes, not card sizes.</summary>
internal enum DockIconSize
{
    Small,
    Medium,
    Large,
}

/// <summary>A rectangle in physical screen pixels (a plain record, so the layout maths needs no Win32 and can be tested).</summary>
internal readonly record struct DockRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>
/// The numbers the Glass and Pixel dock designs share (HANDOFFs 15.1, section 1 and 3). Glass is in DIPs; Pixel in art pixels, where an art
/// pixel is two DIPs, so a bar is the same length in both themes and a theme switch never changes it.
/// </summary>
internal static class DockGeometry
{
    /// <summary>Padding at the bar's ends and above and below the icons, and the gap between icons, in DIPs: 12 at every size.</summary>
    public const double PaddingDip = 12;

    public const double GapDip = 12;

    /// <summary>The bar sits this far above the screen's bottom edge.</summary>
    public const double EdgeGapDip = 8;

    /// <summary>Transparent room above the bar, for the hover label (Pixel: 21 art px).</summary>
    public const double HeadroomDip = 42;

    public const double LabelHeightDip = 26;

    /// <summary>The label's bottom is this far above the bar's top.</summary>
    public const double LabelGapDip = 8;

    /// <summary>The widest a hover label gets.</summary>
    public const double LabelMaxWidthDip = 240;

    /// <summary>How far the edge strip reaches up from the screen's bottom (SPIKE: 3 px was missed by a real drag).</summary>
    public const double EdgeStripDip = 6;

    public static double IconDip(DockIconSize size) => size switch { DockIconSize.Small => 40, DockIconSize.Medium => 48, _ => 64 };

    public static double CornerDip(DockIconSize size) => size switch { DockIconSize.Small => 20, DockIconSize.Medium => 22, _ => 28 };

    /// <summary>How far the picture rises on hover (Glass). Pixel lifts 2 art pixels at every size.</summary>
    public static double LiftDip(DockIconSize size) => size switch { DockIconSize.Small => 4, DockIconSize.Medium => 5, _ => 6 };

    public static double BarHeightDip(DockIconSize size) => IconDip(size) + 2 * PaddingDip;

    public static double EmptyLengthDip(DockIconSize size) => size switch { DockIconSize.Small => 240, DockIconSize.Medium => 280, _ => 340 };

    /// <summary>2 x padding + n icons + (n - 1) gaps; the empty bar has a length of its own.</summary>
    public static double BarLengthDip(DockIconSize size, int count)
        => count <= 0 ? EmptyLengthDip(size) : 2 * PaddingDip + count * IconDip(size) + (count - 1) * GapDip;

    /// <summary>Distance from the bar's left edge to the left edge of icon <paramref name="index"/>.</summary>
    public static double IconLeftDip(DockIconSize size, int index) => PaddingDip + index * (IconDip(size) + GapDip);

    // ---- Pixel, in art pixels ----

    public static int PixelIconArt(DockIconSize size) => size switch { DockIconSize.Small => 20, DockIconSize.Medium => 24, _ => 32 };

    /// <summary>The icon plus 2 art pixels of halo room on each side.</summary>
    public static int PixelSlotArt(DockIconSize size) => PixelIconArt(size) + 4;

    public const int PixelPadArt = 4;

    public const int PixelSlotGapArt = 2;

    public const int PixelHeadroomArt = 21;

    public const int PixelEdgeGapArt = 4;

    public static int PixelBarHeightArt(DockIconSize size) => 2 * PixelPadArt + PixelSlotArt(size);

    public static int PixelEmptyLengthArt(DockIconSize size) => size switch { DockIconSize.Small => 120, DockIconSize.Medium => 140, _ => 170 };

    public static int PixelBarLengthArt(DockIconSize size, int count)
        => count <= 0 ? PixelEmptyLengthArt(size) : 2 * PixelPadArt + count * PixelSlotArt(size) + (count - 1) * PixelSlotGapArt;

    /// <summary>Distance from the bar's left edge to the left edge of icon <paramref name="index"/>'s drawing (slot + halo room).</summary>
    public static int PixelIconLeftArt(DockIconSize size, int index) => PixelPadArt + index * (PixelSlotArt(size) + PixelSlotGapArt) + 2;

    /// <summary>The icon under the pointer, or -1 (over the padding, a gap or nothing). <paramref name="x"/> is from the bar's left edge, in DIPs.</summary>
    public static int IconAt(DockIconSize size, int count, double x)
    {
        for (int i = 0; i < count; i++)
        {
            double left = IconLeftDip(size, i);
            if (x >= left && x < left + IconDip(size))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Where a dragged icon would land: the number of icons whose middle is left of <paramref name="x"/> (0 to <paramref name="count"/>),
    /// so a pointer past the middle of the next icon moves it over, as D7 says.
    /// </summary>
    public static int SlotAt(DockIconSize size, int count, double x)
    {
        int slot = 0;
        for (int i = 0; i < count; i++)
        {
            if (x > IconLeftDip(size, i) + IconDip(size) / 2)
                slot = i + 1;
        }
        return slot;
    }
}
