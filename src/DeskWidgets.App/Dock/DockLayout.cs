namespace DeskWidgets.Dock;

/// <summary>Where the bar and its window are on one screen, in physical pixels.</summary>
/// <param name="Window">The window: the bar plus the headroom above it, wide enough for a label at either end.</param>
/// <param name="Bar">The bar, inside the window.</param>
/// <param name="HiddenTop">The window's top while the dock is hidden: below the screen's bottom edge.</param>
/// <param name="Offset">The bar's offset from the centre of the work area after clamping.</param>
internal readonly record struct DockLayout(DockRect Window, DockRect Bar, int HiddenTop, int Offset);

/// <summary>Placing the bar on a screen's bottom edge, centred unless slid, and keeping it on the screen (PROPOSAL 3.1).</summary>
internal static class DockPlacement
{
    /// <summary>
    /// How far the bar's centre may sit from the work area's centre so that the whole bar stays at least <paramref name="margin"/> inside it.
    /// A bar longer than the work area has no room and stays centred.
    /// </summary>
    public static int MaxOffset(int workWidth, int barLength, int margin) => Math.Max(0, (workWidth - barLength) / 2 - margin);

    public static int ClampOffset(int offset, int workWidth, int barLength, int margin)
    {
        int max = MaxOffset(workWidth, barLength, margin);
        return Math.Clamp(offset, -max, max);
    }

    /// <summary>The layout for a bar of the given size on a work area. All values are physical pixels.</summary>
    /// <param name="work">The screen's work area.</param>
    /// <param name="monitorBottom">The screen's bottom edge, where a hidden dock's window starts.</param>
    /// <param name="labelOverhang">How much room a label may need beyond the bar at either end.</param>
    public static DockLayout Compute(DockRect work, int monitorBottom, int barLength, int barHeight, int headroom, int edgeGap, int labelOverhang, int offset)
    {
        int clamped = ClampOffset(offset, work.Width, barLength, edgeGap);
        int barX = work.X + (work.Width - barLength) / 2 + clamped;
        int barY = work.Bottom - edgeGap - barHeight;
        var bar = new DockRect(barX, barY, barLength, barHeight);

        int left = Math.Max(work.X, barX - labelOverhang);
        int right = Math.Min(work.Right, barX + barLength + labelOverhang);
        var window = new DockRect(left, barY - headroom, right - left, headroom + barHeight);
        return new DockLayout(window, bar, monitorBottom, clamped);
    }
}
