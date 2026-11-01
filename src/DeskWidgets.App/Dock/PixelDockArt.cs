using DeskWidgets.Pixel;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>
/// The Pixel dock's icons (the Pixel dock design's HANDOFF section 2), as canvases of the icon size: a custom app's own tile from
/// <see cref="PixelDockSprites"/>, or tier 2, the program's real icon (asked of <see cref="IconService"/> at 64 px, as the Big Folder does)
/// box-filtered to (size − 2) on the Sweetie palette with a 1-pixel ink outline. Until a real icon arrives, or when there is none, a plain
/// silver tile of the app shape stands in. A missing program gets the Big Folder's dim. Pictures are cached; nothing here draws a window.
/// </summary>
internal sealed class PixelDockArt
{
    /// <summary>As many as the icon service keeps.</summary>
    private const int MaxCached = 400;

    private readonly Action _repaint;
    private readonly Func<DockItem, Pixels?>? _source;
    private readonly Dictionary<string, string?> _customKeys = [];
    private readonly Dictionary<(string Key, int Size), PixelCanvas> _custom = [];
    private readonly Dictionary<(string Key, int Size, IconImage? Image), PixelCanvas> _tier2 = [];
    private readonly Dictionary<(string Key, int Size), PixelCanvas> _direct = [];
    private readonly Dictionary<int, PixelCanvas> _placeholders = [];
    private readonly Dictionary<(PixelCanvas Icon, bool Dark), PixelCanvas> _dimmed = [];
    private readonly Dictionary<string, (long Stamp, string? Fallback)> _requests = [];

    /// <param name="repaint">Runs on the UI thread when a real icon has arrived.</param>
    /// <param name="source">Design QA only: gives a program's real picture at once instead of the icon service (no window, no callback).</param>
    public PixelDockArt(Action repaint, Func<DockItem, Pixels?>? source = null)
    {
        _repaint = repaint;
        _source = source;
    }

    /// <summary>The icon of a program at <paramref name="size"/> art pixels (20, 24 or 32), dimmed when it is missing.</summary>
    public PixelCanvas Icon(PixelPalette palette, DockItemView view, int size)
    {
        var icon = Plain(view.Item, size);
        if (!view.Missing)
            return icon;
        bool dark = palette.Panel == PixelPalette.For(true).Panel;
        if (!_dimmed.TryGetValue((icon, dark), out var dimmed))
            dimmed = BoundedCache.Put(_dimmed, (icon, dark), Dimmed(palette, icon), MaxCached);
        return dimmed;
    }

    /// <summary>POP's first frame (D10): a 7 × 7 mini of the program's 32-pixel icon, box-filtered by the tier-2 rule, in an ink outline with its corners cut.</summary>
    public PixelCanvas Mini(PixelPalette palette, DockItemView view) => MiniOf(Icon(palette, view, 32), 7);

    /// <summary>Forgets the custom-icon matches (a program's identity can change only by being dropped again, but a language switch is a cheap moment to start over).</summary>
    public void Forget() => _customKeys.Clear();

    private PixelCanvas Plain(DockItem item, int size)
    {
        if (CustomKey(item) is { } key && PixelDockSprites.Idle(key, size) is { } sprite)
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
        if (IconPath(item) is { } path)
        {
            var (stamp, fallback) = RequestFor(item);
            var request = new IconRequest(path, 64, IconFlavor.AppIcon, stamp, fallback);
            var icons = IconService.Shared;
            if (icons.TryGet(request, out var image))
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

    /// <summary>The custom tile's key, matched once per program: AUMID and exe name first (no file read), then the exe's product and description.</summary>
    private string? CustomKey(DockItem item)
    {
        if (_customKeys.TryGetValue(item.Key, out var cached))
            return cached;
        string? key = AppIconPack.Match(item.Identity());
        if (key is null && !string.IsNullOrEmpty(item.ExePath))
        {
            var exe = FolderScanner.ExeIdentity(item.ExePath);
            key = AppIconPack.Match(item.Identity(exe.ProductName, exe.FileDescription));
        }
        return _customKeys[item.Key] = key;
    }

    private static string? IconPath(DockItem item)
        => !string.IsNullOrEmpty(item.ExePath) ? item.ExePath
            : !string.IsNullOrEmpty(item.Aumid) ? @"shell:AppsFolder\" + item.Aumid
            : !string.IsNullOrEmpty(item.IconPath) ? item.IconPath // a link's own icon file
            : null;

    private (long Stamp, string? Fallback) RequestFor(DockItem item)
    {
        if (_requests.TryGetValue(item.Key, out var known))
            return known;
        long stamp = 0;
        if (!string.IsNullOrEmpty(item.ExePath))
        {
            try
            {
                stamp = File.GetLastWriteTimeUtc(item.ExePath).Ticks;
            }
            catch (Exception)
            {
            }
        }
        string? fallback = !string.IsNullOrEmpty(item.ExePath) && !string.IsNullOrEmpty(item.Aumid) ? @"shell:AppsFolder\" + item.Aumid : null;
        return _requests[item.Key] = (stamp, fallback);
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
