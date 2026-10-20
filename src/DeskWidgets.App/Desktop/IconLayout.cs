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
    /// Learns from icons the user has just put somewhere by hand (a drag on the
    /// desktop, or a file dropped onto it): every icon that sits away from its
    /// planned cell was placed by the user. Each such icon moves to the monitor
    /// it was dropped on and takes the place of the cell under it, counted the
    /// way <see cref="Plan"/> counts (a cell under a widget gives way to the
    /// next free one; past the last icon means the end), so the icons already
    /// there shift along. Icons dragged together go in by the order of their
    /// cells. Throws, before changing anything, when the icons do not fit.
    /// Call it only after a user drag: without one, icons that moved on their
    /// own (Explorer reflowing after a display change) must go back. Returns
    /// how many icons were placed.
    /// </summary>
    public static int LearnDrops(DesktopSnapshot snapshot, IconLayoutSettings settings, IReadOnlyList<Win32.RECT> blocked)
    {
        var planned = Plan(snapshot, settings, blocked);
        var positions = snapshot.Items.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);
        var placed = planned.Where(p => !SamePosition(p, positions[p.Key])).Select(p => positions[p.Key]).ToList();
        if (placed.Count == 0)
            return 0;

        var cells = snapshot.Displays.ToDictionary(d => d.Device, d => new Cells(snapshot, settings, d, blocked));
        var drops = placed
            .Select(icon =>
            {
                var screen = ScreenAt(snapshot, icon);
                var grid = cells[screen.Device];
                int cell = grid.IndexAt(icon.X, icon.Y);
                return (icon.Key, screen.Device, Rank: grid.FreeRank(cell), Cell: cell);
            })
            .OrderBy(d => d.Device, StringComparer.Ordinal).ThenBy(d => d.Rank).ThenBy(d => d.Cell)
            .ThenBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var moved = new HashSet<string>(placed.Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        settings.Order.RemoveAll(key => moved.Contains(key) || !listed.Add(key)); // also drops duplicates
        var present = new HashSet<string>(positions.Keys.Where(key => !moved.Contains(key)), StringComparer.OrdinalIgnoreCase);
        string? lastDevice = null;
        int lastRank = -1;
        foreach (var drop in drops)
        {
            int rank = drop.Device == lastDevice ? Math.Max(drop.Rank, lastRank + 1) : drop.Rank;
            InsertAtRank(snapshot, settings, present, drop.Key, drop.Device, rank);
            present.Add(drop.Key);
            (lastDevice, lastRank) = (drop.Device, rank);
        }
        return placed.Count;
    }

    /// <summary>
    /// Takes the icons' current order as the arrangement: per monitor, the cell
    /// each icon sits in read the way <see cref="Plan"/> fills them (top-right
    /// first, down the column, then the next column to the left). For when the
    /// user turns arranging back on after placing icons freely. Icons that are
    /// not on the desktop any more are forgotten.
    /// </summary>
    public static void Adopt(DesktopSnapshot snapshot, IconLayoutSettings settings)
    {
        var cells = snapshot.Displays.ToDictionary(d => d.Device, d => new Cells(snapshot, settings, d, []));
        var read = snapshot.Items
            .Select(icon =>
            {
                var screen = ScreenAt(snapshot, icon);
                return (Icon: icon, screen.Device, Cell: cells[screen.Device].IndexAt(icon.X, icon.Y));
            })
            .ToList();
        var order = new List<string>();
        foreach (var display in snapshot.Displays)
        {
            var onScreen = read.Where(r => r.Device == display.Device)
                .OrderBy(r => r.Cell).ThenByDescending(r => r.Icon.X).ThenBy(r => r.Icon.Y)
                .ThenBy(r => r.Icon.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var r in onScreen)
            {
                order.Add(r.Icon.Key);
                settings.Monitors[r.Icon.Key] = display.Device;
            }
        }
        settings.Order = order;
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
        var grids = new Dictionary<string, Cells>();
        var nextCell = new Dictionary<string, int>();
        var targets = new List<IconPosition>();
        foreach (string key in settings.Order.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!existing.ContainsKey(key))
                continue;
            var screen = ScreenOf(snapshot, settings, key);
            if (!grids.TryGetValue(screen.Device, out var grid))
            {
                grid = new Cells(snapshot, settings, screen, blocked);
                if (grid.Rows < 1 || grid.Columns < 1)
                    throw new InvalidOperationException(Strings.IconsNoRoom);
                grids.Add(screen.Device, grid);
            }

            int index = nextCell.GetValueOrDefault(screen.Device);
            while (true)
            {
                if (index >= grid.Count)
                    throw new InvalidOperationException(Strings.IconsNoRoom);
                if (!grid.IsBlocked(index))
                    break;
                index++;
            }
            nextCell[screen.Device] = index + 1;
            var (x, y) = grid.Origin(index);
            targets.Add(new IconPosition(key, x, y));
        }
        if (targets.Count != snapshot.Items.Count)
            throw new InvalidOperationException(Strings.IconsChanging);
        return targets;
    }

    public static bool SamePosition(IconPosition a, IconPosition b) => a.X == b.X && a.Y == b.Y;

    private static bool Contains(DisplayArea d, int x, int y) => x >= d.Left && x < d.Left + d.Width && y >= d.Top && y < d.Top + d.Height;

    private static DisplayArea PrimaryOf(DesktopSnapshot snapshot) => snapshot.Displays.FirstOrDefault(d => d.Primary) ?? snapshot.Displays[0];

    /// <summary>The monitor an icon is assigned to; the primary one when it is not connected any more.</summary>
    private static DisplayArea ScreenOf(DesktopSnapshot snapshot, IconLayoutSettings settings, string key)
    {
        settings.Monitors.TryGetValue(key, out string? monitor);
        return snapshot.Displays.FirstOrDefault(d => d.Device == monitor) ?? PrimaryOf(snapshot);
    }

    /// <summary>The monitor an icon sits on now, judged by its centre; the nearest one when it sits on none.</summary>
    private static DisplayArea ScreenAt(DesktopSnapshot snapshot, IconPosition icon)
    {
        int x = icon.X + snapshot.SpacingX / 2, y = icon.Y + snapshot.SpacingY / 2;
        return snapshot.Displays.FirstOrDefault(d => Contains(d, x, y))
            ?? snapshot.Displays.MinBy(d =>
            {
                long dx = Math.Max(Math.Max(d.Left - x, 0), x - (d.Left + d.Width - 1));
                long dy = Math.Max(Math.Max(d.Top - y, 0), y - (d.Top + d.Height - 1));
                return dx * dx + dy * dy;
            })!;
    }

    /// <summary>
    /// Puts <paramref name="key"/> into the order so that it is the <paramref name="rank"/>th
    /// (from 0) of the icons in <paramref name="present"/> on <paramref name="device"/>;
    /// past the last one means after it.
    /// </summary>
    private static void InsertAtRank(DesktopSnapshot snapshot, IconLayoutSettings settings, HashSet<string> present,
        string key, string device, int rank)
    {
        int seen = 0, at = -1, last = -1;
        for (int i = 0; i < settings.Order.Count; i++)
        {
            string other = settings.Order[i];
            if (!present.Contains(other) || ScreenOf(snapshot, settings, other).Device != device)
                continue;
            if (seen == rank)
            {
                at = i;
                break;
            }
            seen++;
            last = i;
        }
        settings.Order.Insert(at >= 0 ? at : last >= 0 ? last + 1 : settings.Order.Count, key);
        settings.Monitors[key] = device;
    }

    /// <summary>
    /// One monitor's grid of icon cells, numbered the way icons fill them: 0 is
    /// the top-right cell, then down the column, then the next column to the left.
    /// </summary>
    private sealed class Cells
    {
        private readonly DisplayArea _screen;
        private readonly int _right, _top, _spacingX, _spacingY;
        private readonly IReadOnlyList<Win32.RECT> _blocked;

        public Cells(DesktopSnapshot snapshot, IconLayoutSettings settings, DisplayArea screen, IReadOnlyList<Win32.RECT> blocked)
        {
            _screen = screen;
            _blocked = blocked;
            _spacingX = snapshot.SpacingX;
            _spacingY = snapshot.SpacingY;
            (_right, _top) = MarginPixels(_spacingX, _spacingY, settings.Margins.GetValueOrDefault(screen.Device, DefaultInsets));
            Rows = (screen.Height - _top) / _spacingY;
            Columns = (screen.Width - _right) / _spacingX;
        }

        public int Rows { get; }

        public int Columns { get; }

        public int Count => Math.Max(0, Rows) * Math.Max(0, Columns);

        /// <summary>The top-left corner of cell <paramref name="index"/>, in the desktop view's coordinates.</summary>
        public (int X, int Y) Origin(int index)
            => (_screen.Left + _screen.Width - _right - _spacingX * (index / Rows + 1), _screen.Top + _top + _spacingY * (index % Rows));

        /// <summary>Whether a widget's card covers any part of the cell.</summary>
        public bool IsBlocked(int index)
        {
            var (x, y) = Origin(index);
            return _blocked.Any(b => b.Left < x + _spacingX && x < b.Right && b.Top < y + _spacingY && y < b.Bottom);
        }

        /// <summary>The cell under the middle of an icon whose top-left corner is at (<paramref name="x"/>, <paramref name="y"/>); the nearest cell when it lies outside the grid.</summary>
        public int IndexAt(int x, int y)
        {
            int fromRight = _screen.Left + _screen.Width - _right - (x + _spacingX / 2);
            int fromTop = y + _spacingY / 2 - (_screen.Top + _top);
            int column = Math.Clamp(Math.Max(0, fromRight) / _spacingX, 0, Math.Max(0, Columns - 1));
            int row = Math.Clamp(Math.Max(0, fromTop) / _spacingY, 0, Math.Max(0, Rows - 1));
            return column * Math.Max(1, Rows) + row;
        }

        /// <summary>
        /// How many free cells (not under a widget) come before cell
        /// <paramref name="index"/>, or before the first free one after it when
        /// that cell is covered. When no free cell follows, all of them.
        /// </summary>
        public int FreeRank(int index)
        {
            int free = 0;
            for (int i = 0; i < Count; i++)
            {
                if (IsBlocked(i))
                    continue;
                if (i >= index)
                    return free;
                free++;
            }
            return free;
        }
    }
}