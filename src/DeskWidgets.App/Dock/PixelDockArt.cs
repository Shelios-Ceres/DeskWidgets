using DeskWidgets.Pixel;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI.Dispatching;

namespace DeskWidgets.Dock;

/// <summary>
/// The Pixel dock's icons (the Pixel dock design's HANDOFF section 2), as canvases of the icon size: a custom app's own tile from
/// <see cref="PixelDockSprites"/>, or tier 2, the program's real icon (asked of <see cref="IconService"/> at 64 px, as the Big Folder does)
/// box-filtered to (size − 2) on the Sweetie palette with a 1-pixel ink outline. Until a real icon arrives, or when there is none, the generic
/// program tile stands in. A missing program gets the Big Folder's dim. A running one (section 1) shows its awake drawing inside a 1-pixel halo
/// in its own colour. A group is a silver tile of its members' minis. Pictures are cached; nothing here draws a window or reads a file on the
/// UI thread.
/// </summary>
internal sealed class PixelDockArt
{
    /// <summary>As many as the icon service keeps.</summary>
    private const int MaxCached = 400;

    private readonly Action _repaint;
    private readonly Func<DockItem, Pixels?>? _source;
    private readonly Dictionary<(string Key, int Size), PixelCanvas> _custom = [];
    private readonly Dictionary<(string Key, int Size, IconImage? Image), PixelCanvas> _tier2 = [];
    private readonly Dictionary<(string Key, int Size), PixelCanvas> _direct = [];
    private readonly Dictionary<int, PixelCanvas> _placeholders = [];
    private readonly Dictionary<(PixelCanvas Icon, bool Dark), PixelCanvas> _dimmed = [];
    private readonly Dictionary<string, (long Stamp, string? Fallback)> _requests = [];
    private readonly Dictionary<(string Key, int Size), PixelCanvas> _customAwake = [];
    private readonly Dictionary<(string Key, PixelCanvas Icon, bool Dark), uint> _haloColors = [];
    private readonly Dictionary<(PixelCanvas Icon, uint Color), PixelCanvas> _rings = [];
    private readonly Dictionary<(int Size, PixelCanvas? A, PixelCanvas? B, PixelCanvas? C, PixelCanvas? D), PixelCanvas> _groups = [];
    private readonly Dictionary<(PixelCanvas Icon, int Size), PixelCanvas> _minis = [];
    private readonly HashSet<string> _stamping = [];
    private readonly DispatcherQueue? _queue = DispatcherQueue.GetForCurrentThread();

    /// <param name="repaint">Runs on the UI thread when a real icon has arrived.</param>
    /// <param name="source">Design QA only: gives a program's real picture at once instead of the icon service (no window, no callback).</param>
    public PixelDockArt(Action repaint, Func<DockItem, Pixels?>? source = null)
    {
        _repaint = repaint;
        _source = source;
    }

    /// <summary>The icon of a program at <paramref name="size"/> art pixels (20, 24 or 32), dimmed when it is missing; <paramref name="awake"/> gives a custom app's awake drawing.</summary>
    public PixelCanvas Icon(PixelPalette palette, DockItemView view, int size, bool awake = false)
    {
        if (view.IsGroup)
            return GroupTile(palette, view.Children!, size); // its minis carry their members' looks: never dimmed as a whole
        var icon = awake && AwakeTile(view, size) is { } tile ? tile : Plain(view, size);
        if (!view.Missing)
            return icon;
        bool dark = palette.Panel == PixelPalette.For(true).Panel;
        if (!_dimmed.TryGetValue((icon, dark), out var dimmed))
            dimmed = BoundedCache.Put(_dimmed, (icon, dark), Dimmed(palette, icon), MaxCached);
        return dimmed;
    }

    /// <summary>
    /// The running look (section 1), (size + 4) square: the awake drawing (a real icon or the generic tile keeps its one drawing) inside a 1-pixel
    /// ring that follows its shape 2 pixels out, in the halo colour. Its top-left is the slot box's, 2 pixels up and left of the icon.
    /// </summary>
    public PixelCanvas Running(PixelPalette palette, DockItemView view, int size)
    {
        var icon = Icon(palette, view, size, awake: true);
        uint color = HaloColorOf(palette, view, size);
        if (!_rings.TryGetValue((icon, color), out var ring))
            ring = BoundedCache.Put(_rings, (icon, color), Ring(icon, color), MaxCached);
        return ring;
    }

    /// <summary>A program's halo colour on this bar, worked out once per program and picture: a custom app's own, the generic tile's slate, else the real icon's rule.</summary>
    private uint HaloColorOf(PixelPalette palette, DockItemView view, int size)
    {
        bool dark = palette.Panel == PixelPalette.For(true).Panel;
        if (view.IsGroup)
            return palette.Sprite(ForBar('s', dark)); // section 1: a group's halo is slate, kept readable like any other
        var item = view.Item;
        var icon = Plain(view, size);
        if (_haloColors.TryGetValue((item.Key, icon, dark), out uint known))
            return known;
        char symbol = KeyOf(view) is { } key && PixelDockSprites.Halo(key) is { } own ? ForBar(own, dark)
            : _placeholders.TryGetValue(size, out var generic) && ReferenceEquals(icon, generic) ? ForBar(PixelDockSprites.Halo("generic") ?? 's', dark)
            : HaloSymbol(icon.Pixels, dark);
        return BoundedCache.Put(_haloColors, (item.Key, icon, dark), palette.Sprite(symbol), MaxCached);
    }

    private PixelCanvas? AwakeTile(DockItemView view, int size)
    {
        if (KeyOf(view) is not { } key || PixelDockSprites.Awake(key, size) is not { } sprite)
            return null;
        if (!_customAwake.TryGetValue((key, size), out var tile))
        {
            tile = new PixelCanvas(size, size);
            tile.Draw(sprite, 0, 0, PixelPalette.For(dark: false).Sprite);
            _customAwake[(key, size)] = tile;
        }
        return tile;
    }

    /// <summary>POP's first frame (D10): a 7 × 7 mini of the program's 32-pixel icon, box-filtered by the tier-2 rule, in an ink outline with its corners cut.</summary>
    public PixelCanvas Mini(PixelPalette palette, DockItemView view) => MiniOf(Icon(palette, view, 32), 7);

    /// <summary>
    /// A group's tile (section 2): the app tile's shape in silver (lip slate) holding up to four minis in a 2 × 2, each a member's 32-pixel icon
    /// (awake while it runs) by the tier-2 rule: 7 at Small, 9 at Medium, 12 at Large, 1 apart (2 at Large), the block centred.
    /// </summary>
    public PixelCanvas GroupTile(PixelPalette palette, IReadOnlyList<DockItemView> members, int size)
    {
        int m = GroupMiniSize(size);
        var shown = new PixelCanvas?[4];
        for (int k = 0; k < Math.Min(4, members.Count); k++)
        {
            var member = members[k];
            var big = Icon(palette, member, 32, awake: member.State == DockRunState.Running && !member.Missing);
            if (!_minis.TryGetValue((big, m), out var mini))
                mini = BoundedCache.Put(_minis, (big, m), MiniOf(big, m), MaxCached);
            shown[k] = mini;
        }
        // Keyed by the minis' pictures: a real icon that arrives, or a member that starts running, makes a new tile.
        var key = (size, shown[0], shown[1], shown[2], shown[3]);
        if (_groups.TryGetValue(key, out var cached))
            return cached;
        var tile = Tile(size, 'g', 's');
        var (x0, y0, pitch) = GroupMiniLayout(size);
        for (int k = 0; k < 4; k++)
        {
            if (shown[k] is { } mini)
                FolderPixelScene.Blit(mini, tile, x0 + k % 2 * pitch, y0 + k / 2 * pitch);
        }
        return BoundedCache.Put(_groups, key, tile, MaxCached);
    }

    /// <summary>D8: the target of a held drag drawn as a group of one, its own mini in the first place.</summary>
    public PixelCanvas FormingTile(PixelPalette palette, DockItemView target, int size) => GroupTile(palette, [target], size);

    internal static int GroupMiniSize(int size) => size switch { <= 20 => 7, <= 24 => 9, _ => 12 };

    /// <summary>The first mini's top-left and the step to the next (mini + gap): the 2 × 2 block centred, rounded up one row for the shade lip (the mock's rule).</summary>
    internal static (int X, int Y, int Pitch) GroupMiniLayout(int size)
    {
        int m = GroupMiniSize(size), gap = size >= 32 ? 2 : 1, block = 2 * m + gap;
        return ((size - block) / 2, (size - 1 - block) / 2, m + gap);
    }

    /// <summary>A custom app's own tile when the view carries its key (found off the UI thread), else the real icon, else the generic tile.</summary>
    private PixelCanvas Plain(DockItemView view, int size)
    {
        var item = view.Item;
        if (KeyOf(view) is { } key && PixelDockSprites.Idle(key, size) is { } sprite)
        {
            if (!_custom.TryGetValue((key, size), out var tile))
            {
                tile = new PixelCanvas(size, size);
                tile.Draw(sprite, 0, 0, PixelPalette.For(dark: false).Sprite);
                _custom[(key, size)] = tile;
            }
            return tile;
        }
        if (_source is not null)
        {
            if (!_direct.TryGetValue((item.Key, size), out var direct))
                direct = _direct[(item.Key, size)] = _source(item) is { } pixels ? Tier2(pixels, size) : Placeholder(size);
            return direct;
        }
        if (DockIconSource.PathFor(item) is { } path && RequestFor(item) is { } known)
        {
            var (stamp, fallback) = known;
            var request = new IconRequest(path, 64, IconFlavor.AppIcon, stamp, fallback);
            var icons = IconService.Shared;
            if (icons.TryGet(request, out var image, _repaint))
            {
                if (image is not null)
                {
                    if (!_tier2.TryGetValue((item.Key, size, image), out var tier2))
                        tier2 = BoundedCache.Put(_tier2, (item.Key, size, image), Tier2(image.Pixels, size), MaxCached);
                    return tier2;
                }
            }
            else
            {
                icons.Request(request, _repaint);
            }
        }
        return Placeholder(size);
    }

    /// <summary>The view's custom-tile key (found off the UI thread by the controller); design QA's views carry none, so it matches them here (no file read).</summary>
    private string? KeyOf(DockItemView view) => view.CustomKey ?? (_source is not null ? DockCustomKeys.Quick(view.Item) : null);

    /// <summary>
    /// The icon request's stamp (the exe's last write, so an updated program gets its new icon) and fallback. The stamp is a file read, which can
    /// take seconds on a network or sleeping drive: it is read on a worker, and until it is known (null) the generic tile stands in and nothing
    /// is asked of the icon service, which caches by stamp. When it comes the dock repaints.
    /// </summary>
    private (long Stamp, string? Fallback)? RequestFor(DockItem item)
    {
        // The exe path is part of the key: a classic app that was saved with only its AUMID gets its exe path later (DockBackfill), and then asks again.
        string key = item.Key + "|" + item.ExePath;
        if (_requests.TryGetValue(key, out var known))
            return known;
        string? fallback = DockIconSource.FallbackFor(item);
        if (string.IsNullOrEmpty(item.ExePath))
            return _requests[key] = (0, fallback);
        if (_queue is null)
            return _requests[key] = (ReadStamp(item.ExePath), fallback); // no UI thread to come back to: read here
        if (_stamping.Add(key))
        {
            string exe = item.ExePath;
            var queue = _queue;
            Task.Run(() =>
            {
                long stamp = ReadStamp(exe);
                queue.TryEnqueue(() =>
                {
                    _stamping.Remove(key);
                    _requests[key] = (stamp, fallback);
                    _repaint();
                });
            });
        }
        return null;
    }

    private static long ReadStamp(string exe)
    {
        try
        {
            return File.GetLastWriteTimeUtc(exe).Ticks;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// The stand-in for a program whose real icon has not come (or never will, a Steam link with no icon file): the design's generic
    /// program tile, a silver tile holding a small program window (a plain silver tile would read as an empty group).
    /// </summary>
    private PixelCanvas Placeholder(int size)
    {
        if (!_placeholders.TryGetValue(size, out var tile))
        {
            tile = new PixelCanvas(size, size);
            tile.Draw(PixelDockSprites.Generic(size), 0, 0, PixelPalette.For(dark: false).Sprite);
            _placeholders[size] = tile;
        }
        return tile;
    }

    // ----- drawing (ported from the design chat's mock, Dock.cs and DockScene.cs) -----

    private const string SpriteSymbols = "#proyLGtnblcwgsd";

    /// <summary>The Sweetie ramps, dark to light, after the ink (purple, red, orange, yellow, white; navy, blue, sky, cyan; teal, green, lime; dusk, slate, silver, white).</summary>
    private static readonly string[] Ramps = ["#proyw", "#nblcw", "#tGLw", "#dsgw"];

    /// <summary>
    /// The halo symbol of a real (tier-2) icon (section 1): its most common coloured pixel, not counting ink, white, silver, slate, dusk or any
    /// colour off the sprite palette; blue when there is none; then kept readable on the bar (<see cref="ForBar"/>). <paramref name="argb"/> is
    /// the icon's pixels as 0xAARRGGBB, 0 transparent (a <see cref="PixelCanvas.Pixels"/>); a tie goes to the colour met first, row by row.
    /// </summary>
    internal static char HaloSymbol(IReadOnlyList<uint> argb, bool dark)
    {
        var palette = PixelPalette.For(dark);
        var counts = new Dictionary<char, int>();
        foreach (uint value in argb)
        {
            if (value == 0)
                continue;
            char symbol = SymbolOf(value, palette);
            if (symbol is '#' or 'w' or 'g' or 's' or 'd' or '?')
                continue;
            counts[symbol] = counts.GetValueOrDefault(symbol) + 1;
        }
        char best = counts.Count == 0 ? 'b' : counts.MaxBy(kv => kv.Value).Key;
        return ForBar(best, dark);
    }

    /// <summary><see cref="HaloSymbol"/> as a colour (0xAARRGGBB): what the halo ring is drawn in.</summary>
    internal static uint HaloColor(IReadOnlyList<uint> argb, bool dark) => PixelPalette.For(dark).Sprite(HaloSymbol(argb, dark));

    /// <summary>Keeps a halo readable: on the light bar yellow steps down to orange and cyan or lime two places; on the dark bar purple, navy, teal and dusk step up one.</summary>
    internal static char ForBar(char symbol, bool dark)
    {
        if (!dark && symbol is 'y' or 'c' or 'L')
            return Step(symbol, symbol == 'y' ? -1 : -2);
        if (dark && symbol is 'p' or 'n' or 't' or 'd')
            return Step(symbol, 1);
        return symbol;
    }

    private static char Step(char symbol, int by)
    {
        foreach (string ramp in Ramps)
        {
            int at = ramp.IndexOf(symbol);
            if (at > 0)
                return ramp[Math.Clamp(at + by, 1, ramp.Length - 1)];
        }
        return symbol;
    }

    private static char SymbolOf(uint argb, PixelPalette palette)
    {
        foreach (char symbol in SpriteSymbols)
        {
            if (palette.Sprite(symbol) == argb)
                return symbol;
        }
        return '?';
    }

    /// <summary>The halo: a 1-pixel ring 2 pixels out from the icon's shape (1 pixel of the bar between), on a canvas 2 larger on every side, the icon in its middle.</summary>
    public static PixelCanvas Ring(PixelCanvas icon, uint color)
    {
        var ring = new PixelCanvas(icon.Width + 4, icon.Height + 4);
        bool On(int x, int y) => icon.Get(x - 2, y - 2) != 0;
        bool Near(int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (On(x + dx, y + dy))
                        return true;
                }
            }
            return false;
        }
        for (int y = 0; y < ring.Height; y++)
        {
            for (int x = 0; x < ring.Width; x++)
            {
                if (Near(x, y))
                    continue;
                // A ring pixel touches (4-way) a gap pixel: one next to the shape but not on it.
                if ((Near(x - 1, y) && !On(x - 1, y)) || (Near(x + 1, y) && !On(x + 1, y)) || (Near(x, y - 1) && !On(x, y - 1)) || (Near(x, y + 1) && !On(x, y + 1)))
                    ring.Set(x, y, color);
            }
        }
        FolderPixelScene.Blit(icon, ring, 2, 2);
        return ring;
    }

    /// <summary>Corner stair of an app tile, from the top row down.</summary>
    public static int[] Stair(int size) => size switch
    {
        <= 20 => [3, 2, 1],
        <= 24 => [4, 2, 1, 1],
        _ => [5, 3, 2, 1, 1],
    };

    /// <summary>An app tile with no motif: ink outline, the fill, and a 1-row shade lip above the bottom outline (sprites.txt's rule).</summary>
    public static PixelCanvas Tile(int size, char fill, char shade)
    {
        var palette = PixelPalette.For(dark: false);
        var stair = Stair(size);
        bool Inside(int x, int y)
        {
            if (x < 0 || y < 0 || x >= size || y >= size)
                return false;
            int edge = Math.Min(y, size - 1 - y);
            int inset = edge < stair.Length ? stair[edge] : 0;
            return x >= inset && x < size - inset;
        }
        bool Edge(int x, int y) => Inside(x, y) && (!Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1));
        var canvas = new PixelCanvas(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (Inside(x, y))
                    canvas.Set(x, y, palette.Sprite(Edge(x, y) ? '#' : Edge(x, y + 1) ? shade : fill));
            }
        }
        return canvas;
    }

    /// <summary>Tier 2 at the dock's size: the real icon box-filtered to (size − 2) with no dithering, a 1-pixel ink outline round its shape.</summary>
    public static PixelCanvas Tier2(Pixels source, int size)
    {
        var palette = PixelPalette.For(dark: false);
        var canvas = new PixelCanvas(size, size);
        var icon = PixelFolderIcons.Shrink(source, size - 2, 0, palette);
        for (int y = -1; y <= size - 2; y++)
        {
            for (int x = -1; x <= size - 2; x++)
            {
                if (icon.Get(x, y) == 0 && (icon.Get(x - 1, y) != 0 || icon.Get(x + 1, y) != 0 || icon.Get(x, y - 1) != 0 || icon.Get(x, y + 1) != 0))
                    canvas.Set(x + 1, y + 1, palette.SpriteInk);
            }
        }
        FolderPixelScene.Blit(icon, canvas, 1, 1);
        return canvas;
    }

    /// <summary>The Big Folder's missing look: ink becomes <c>Faint</c>, every other colour <c>Track</c>.</summary>
    public static PixelCanvas Dimmed(PixelPalette palette, PixelCanvas icon)
    {
        var canvas = new PixelCanvas(icon.Width, icon.Height);
        for (int i = 0; i < icon.Pixels.Length; i++)
        {
            uint value = icon.Pixels[i];
            canvas.Pixels[i] = value == 0 ? 0 : value == palette.SpriteInk ? palette.Faint : palette.Track;
        }
        return canvas;
    }

    /// <summary>An m × m mini of an icon: box-filtered by the tier-2 rule, with a clean ink outline and its four corner pixels cut.</summary>
    public static PixelCanvas MiniOf(PixelCanvas icon, int m)
    {
        var bgra = new byte[icon.Width * icon.Height * 4];
        for (int i = 0; i < icon.Pixels.Length; i++)
        {
            uint value = icon.Pixels[i];
            bgra[i * 4] = (byte)value;
            bgra[i * 4 + 1] = (byte)(value >> 8);
            bgra[i * 4 + 2] = (byte)(value >> 16);
            bgra[i * 4 + 3] = (byte)(value >> 24);
        }
        var palette = PixelPalette.For(dark: false);
        var mini = PixelFolderIcons.Shrink(new Pixels(icon.Width, icon.Height, bgra), m, 0, palette);
        for (int y = 0; y < m; y++)
        {
            for (int x = 0; x < m; x++)
            {
                bool corner = (x == 0 || x == m - 1) && (y == 0 || y == m - 1);
                if (corner)
                    mini.Set(x, y, 0);
                else if (x == 0 || y == 0 || x == m - 1 || y == m - 1)
                    mini.Set(x, y, palette.SpriteInk);
            }
        }
        return mini;
    }
}
