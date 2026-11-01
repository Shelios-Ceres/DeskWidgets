namespace DeskWidgets.Dock;

/// <summary>A rectangle in DIPs, in the Glass dock window's own coordinates (kept free of WinUI so the maths can be tested).</summary>
internal readonly record struct DipRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public static DipRect Union(DipRect a, DipRect b)
    {
        double left = Math.Min(a.X, b.X), top = Math.Min(a.Y, b.Y);
        return new DipRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>
/// Where the Glass dock draws things (HANDOFF sections 1 and 3). The window is wider and taller than the controller's: the whole width of the
/// work area and the height of a Large bar plus the headroom, bottom on the bar's bottom. So a bar that grows, shrinks, changes size or is
/// dragged along the edge never moves or resizes the window (each of those would put the frost and the XAML a frame apart); only the slide
/// moves it, as the spike recommends. A window region keeps everything but the bar and the label click-through.
/// </summary>
internal static class GlassDockLayout
{
    /// <summary>The label stays this far inside the screen's edges.</summary>
    public const double ScreenMarginDip = 8;

    /// <summary>Room around the label's text: width = text + 20.</summary>
    public const double LabelPaddingDip = 10;

    /// <summary>The label rises this far as it fades in (D1).</summary>
    public const double LabelRiseDip = 4;

    /// <summary>
    /// The real window for the controller's <paramref name="window"/> and <paramref name="bar"/> (screen pixels): from the work area's left
    /// to its right (or wider, if the controller's window is), and as tall as the headroom plus a Large bar, its bottom on the bar's bottom.
    /// </summary>
    public static DockRect WindowFor(DockRect work, DockRect window, DockRect bar, double scale)
    {
        int left = Math.Min(work.X, Math.Min(window.X, bar.X));
        int right = Math.Max(work.Right, Math.Max(window.Right, bar.Right));
        int tallest = (int)Math.Ceiling((DockGeometry.HeadroomDip + DockGeometry.BarHeightDip(DockIconSize.Large)) * scale);
        int height = Math.Max(tallest, Math.Max(window.Height, bar.Bottom - window.Y));
        return new DockRect(left, bar.Bottom - height, right - left, height);
    }

    /// <summary>
    /// The bar as drawn, in window DIPs. <paramref name="barLeft"/> and <paramref name="barTop"/> are the controller's bar; an open gap
    /// (<paramref name="gap"/> ≥ 0) makes it one icon and one gap longer, about its centre (as the bar will be once the program is dropped),
    /// kept inside the window.
    /// </summary>
    public static DipRect Bar(DockIconSize size, int count, int gap, double barLeft, double barTop, double windowWidth)
    {
        double logical = DockGeometry.BarLengthDip(size, count);
        double length = DockGeometry.BarLengthDip(size, gap >= 0 ? count + 1 : count);
        double left = barLeft - (length - logical) / 2;
        if (length <= windowWidth)
            left = Math.Clamp(left, 0, windowWidth - length);
        return new DipRect(left, barTop, length, DockGeometry.BarHeightDip(size));
    }

    /// <summary>The left edge of icon <paramref name="index"/> on the drawn bar: icons from the gap's slot on sit one place further right.</summary>
    public static double IconLeft(DockIconSize size, DipRect bar, int index, int gap)
        => bar.X + DockGeometry.IconLeftDip(size, gap >= 0 && index >= gap ? index + 1 : index);

    /// <summary>The top edge of every icon on the drawn bar.</summary>
    public static double IconTop(DipRect bar) => bar.Y + DockGeometry.PaddingDip;

    /// <summary>The gap's slot (a tile-sized plate) on the drawn bar.</summary>
    public static DipRect Gap(DockIconSize size, DipRect bar, int gap)
    {
        double icon = DockGeometry.IconDip(size);
        return new DipRect(bar.X + DockGeometry.IconLeftDip(size, gap), IconTop(bar), icon, icon);
    }

    /// <summary>
    /// A dragged icon's left edge for a pointer at <paramref name="centreX"/> (DIPs from the controller's bar left at <paramref name="barLeft"/>),
    /// kept on the drawn bar between its paddings.
    /// </summary>
    public static double DragLeft(DockIconSize size, DipRect bar, double barLeft, double centreX)
    {
        double icon = DockGeometry.IconDip(size);
        double low = bar.X + DockGeometry.PaddingDip, high = bar.Right - DockGeometry.PaddingDip - icon;
        double left = barLeft + centreX - icon / 2;
        return high < low ? low : Math.Clamp(left, low, high);
    }

    /// <summary>
    /// The hover label: 26 tall, its bottom 8 above the bar, centred on the icon, text + 20 wide (at most 240) and kept 8 inside the screen
    /// (<paramref name="screenLeft"/> to <paramref name="screenRight"/>, window DIPs).
    /// </summary>
    public static DipRect Label(double iconCentre, double textWidth, double barTop, double screenLeft, double screenRight)
    {
        double width = Math.Min(DockGeometry.LabelMaxWidthDip, Math.Ceiling(textWidth) + 2 * LabelPaddingDip);
        double left = iconCentre - width / 2;
        double low = screenLeft + ScreenMarginDip, high = screenRight - ScreenMarginDip - width;
        left = high < low ? low : Math.Clamp(left, low, high);
        return new DipRect(left, barTop - DockGeometry.LabelGapDip - DockGeometry.LabelHeightDip, width, DockGeometry.LabelHeightDip);
    }

    /// <summary>The widest the label's text may be before it is middle-shortened.</summary>
    public static double LabelTextMaxDip => DockGeometry.LabelMaxWidthDip - 2 * LabelPaddingDip;

    /// <summary>A window-DIP rectangle as window pixels for the region, one pixel larger on every side so an anti-aliased edge is never cut.</summary>
    public static (int Left, int Top, int Right, int Bottom) RegionPixels(DipRect rect, double scale)
        => ((int)Math.Floor(rect.X * scale) - 1, (int)Math.Floor(rect.Y * scale) - 1, (int)Math.Ceiling(rect.Right * scale) + 1, (int)Math.Ceiling(rect.Bottom * scale) + 1);
}
