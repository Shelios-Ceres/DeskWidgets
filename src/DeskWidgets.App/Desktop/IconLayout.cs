using DeskWidgets.Interop;

namespace DeskWidgets.Desktop;

/// <summary>What the icon arranger remembers between runs (same JSON shape as DesktopRightAlign's settings).</summary>
internal sealed class IconLayoutSettings
{
    /// <summary>Icons in arrangement order: known icons keep their place, new ones are added at the end.</summary>
    public List<string> Order { get; set; } = [];

    /// <summary>The monitor each icon belongs to.</summary>
    public Dictionary<string, string> Monitors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per monitor, the gaps kept at the right and top edges, as fractions of a grid cell.</summary>
    public Dictionary<string, Insets> Margins { get; set; } = new();
}

internal sealed record Insets(double Right, double Top);

/// <summary>
/// The right-aligned icon layout: from the top-right corner downwards, then
/// column by column to the left, on Explorer's own grid spacing. Cells that a
/// widget's card covers are skipped, so no icon ends up hidden under a widget.
/// </summary>
internal static class IconLayout
{
    public static readonly Insets DefaultInsets = new(0.08, 0.013333333);

    public static (int Right, int Top) MarginPixels(int spacingX, int spacingY, Insets insets)
        => (Math.Max(2, (int)Math.Round(spacingX * insets.Right)), Math.Max(2, (int)Math.Round(spacingY * insets.Top)));

    /// <summary>
    /// Adds what the settings do not know yet: each new monitor's margins (as
    /// found on the desktop, within reason) and each new icon, appended in a
    /// stable order. On the very first run, icons keep their current reading
    /// order. Returns whether anything was added.
    /// </summary>
    public static bool Learn(DesktopSnapshot snapshot, IconLayoutSettings settings)
    {
        if (snapshot.Displays.Count == 0)
            throw new InvalidOperationException(Strings.IconsDesktopNotReady);
        bool initial = settings.Order.Count == 0, changed = false;
        var known = new HashSet<string>(settings.Order, StringComparer.OrdinalIgnoreCase);
        var primary = snapshot.Displays.FirstOrDefault(d => d.Primary) ?? snapshot.Displays[0];
        foreach (var display in snapshot.Displays)
        {
            if (settings.Margins.ContainsKey(display.Device))
                continue;
            var onScreen = snapshot.Items.Where(p => Contains(display, p.X, p.Y)).ToList();
            double right = DefaultInsets.Right, top = DefaultInsets.Top;
            if (onScreen.Count > 0)
            {
                double observedRight = (double)(display.Left + display.Width - snapshot.SpacingX - onScreen.Max(p => p.X)) / snapshot.SpacingX;
                double observedTop = (double)(onScreen.Min(p => p.Y) - display.Top) / snapshot.SpacingY;
                if (observedRight is >= 0 and <= 0.5)
                    right = observedRight;
                if (observedTop is >= 0 and <= 0.25)
                    top = observedTop;
            }
            settings.Margins.Add(display.Device, new Insets(right, top));
            changed = true;
        }

        IEnumerable<IconPosition> additions = snapshot.Items.Where(i => !known.Contains(i.Key));
        additions = initial
            ? additions.OrderBy(p => p.X).ThenBy(p => p.Y)
            : additions.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var icon in additions)
        {
            settings.Order.Add(icon.Key);
            var display = initial ? snapshot.Displays.FirstOrDefault(d => Contains(d, icon.X, icon.Y)) ?? primary : primary;
            settings.Monitors[icon.Key] = display.Device;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Where every icon belongs. Each monitor fills from its top-right cell,
    /// down the column, then the next column to the left, skipping cells that
    /// overlap <paramref name="blocked"/> (widget cards, in the desktop view's
    /// coordinates). Throws, before anything moves, when the icons do not fit.
    /// </summary>
    public static List<IconPosition> Plan(DesktopSnapshot snapshot, IconLayoutSettings settings, IReadOnlyList<Win32.RECT> blocked)
    {
        var existing = snapshot.Items.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);
        var primary = snapshot.Displays.FirstOrDefault(d => d.Primary) ?? snapshot.Displays[0];
        var nextCell = new Dictionary<string, int>();
        var targets = new List<IconPosition>();
        foreach (string key in settings.Order.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!existing.ContainsKey(key))
                continue;
            settings.Monitors.TryGetValue(key, out string? monitor);
            var screen = snapshot.Displays.FirstOrDefault(d => d.Device == monitor) ?? primary;
            var (right, top) = MarginPixels(snapshot.SpacingX, snapshot.SpacingY, settings.Margins.GetValueOrDefault(screen.Device, DefaultInsets));
            int rows = (screen.Height - top) / snapshot.SpacingY;
            int columns = (screen.Width - right) / snapshot.SpacingX;
            if (rows < 1 || columns < 1)
                throw new InvalidOperationException(Strings.IconsNoRoom);

            int index = nextCell.GetValueOrDefault(screen.Device);
            int x, y;
            while (true)
            {
                if (index >= rows * columns)
                    throw new InvalidOperationException(Strings.IconsNoRoom);
                x = screen.Left + screen.Width - right - snapshot.SpacingX * (index / rows + 1);
                y = screen.Top + top + snapshot.SpacingY * (index % rows);
                if (!blocked.Any(b => b.Left < x + snapshot.SpacingX && x < b.Right && b.Top < y + snapshot.SpacingY && y < b.Bottom))
                    break;
                index++;
            }
            nextCell[screen.Device] = index + 1;
            targets.Add(new IconPosition(key, x, y));
        }
        if (targets.Count != snapshot.Items.Count)
            throw new InvalidOperationException(Strings.IconsChanging);
        return targets;
    }

    public static bool SamePosition(IconPosition a, IconPosition b) => a.X == b.X && a.Y == b.Y;

    private static bool Contains(DisplayArea d, int x, int y) => x >= d.Left && x < d.Left + d.Width && y >= d.Top && y < d.Top + d.Height;
}
