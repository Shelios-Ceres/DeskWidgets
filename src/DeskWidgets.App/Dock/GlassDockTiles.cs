using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeskWidgets.Dock;

/// <summary>
/// A dock icon's Glass tile (HANDOFF 2), the Big Folder's way: one of the 13 custom tiles when <see cref="AppIconPack"/> knows the program,
/// else its real icon on the tile plate from <see cref="IconService"/> (the loading plate until it comes), else the generic app tile.
/// <paramref name="repaint"/> runs on the UI thread when an icon arrives. Nothing here takes pointer input.
/// </summary>
internal sealed class GlassDockTiles
{
    // The art key per program, found once per run (an exe's version info is read for it): null means "no custom tile".
    private static readonly Dictionary<string, string?> s_art = [];

    private readonly Action _repaint;
    private readonly Dictionary<(IconImage Image, int Size), WriteableBitmap> _bitmaps = [];

    public GlassDockTiles(Action repaint) => _repaint = repaint;

    /// <summary>
    /// What the tile for <paramref name="item"/> would show now, as a key (a tile is rebuilt only when it changes), and how to build it.
    /// <paramref name="loading"/> is true while the picture is still on its way.
    /// </summary>
    public (string State, Func<FrameworkElement> Build, bool Loading) Describe(DockItem item, GlassPalette palette, bool dark, double size, double scale)
    {
        string theme = dark ? "d" : "l";
        if (ArtFor(item) is { } key && GlassTileArt.ForApp(key) is { } art)
            return ($"art|{key}|{theme}|{size}", () => GlassTiles.Designed(art, dark, size), false);

        if (IconPath(item) is { } path)
        {
            int pixels = GlassTiles.IconPixels(size, scale);
            var request = new IconRequest(path, GlassTileProvider.AskSize(pixels), IconFlavor.AppIcon, StampOf(path), Fallback(item, path));
            var icons = IconService.Shared;
            if (!icons.TryGet(request, out var image))
            {
                icons.Request(request, _repaint);
                return ($"loading|{theme}|{size}", () => GlassTiles.Loading(palette, size), true);
            }
            if (image is not null)
                return ($"real|{path}|{pixels}|{theme}|{image.GetHashCode()}", () => GlassTiles.RealIcon(palette, Bitmap(image, pixels), image.FullBleed, size, scale), false);
        }
        return ($"app|{theme}|{size}", () => GlassTiles.Designed(GlassTileArt.For(FolderItemKind.App), dark, size), false);
    }

    /// <summary>The custom tile's key: by AUMID, exe name or Steam id first (no file read), then by the exe's product and description.</summary>
    private static string? ArtFor(DockItem item)
    {
        if (s_art.TryGetValue(item.Key, out var known))
            return known;
        string? key = AppIconPack.Match(item.Identity());
        if (key is null && !string.IsNullOrEmpty(item.ExePath))
        {
            var exe = FolderScanner.ExeIdentity(item.ExePath);
            key = AppIconPack.Match(item.Identity(exe.ProductName, exe.FileDescription));
        }
        s_art[item.Key] = key;
        return key;
    }

    /// <summary>What to ask the Shell for: the exe, else the Start entry (<c>shell:AppsFolder\&lt;AUMID&gt;</c>); a bare link has no icon of its own.</summary>
    private static string? IconPath(DockItem item)
        => !string.IsNullOrEmpty(item.ExePath) ? item.ExePath
            : !string.IsNullOrEmpty(item.Aumid) ? @"shell:AppsFolder\" + item.Aumid
            : DockIdentity.SafeIconPath(item.IconPath) is { } linkIcon ? linkIcon // a link's own icon file, local only (a UNC path would send credentials)
            : null;

    /// <summary>An exe that also has an AUMID: its Start entry, tried when the exe gives no good icon.</summary>
    private static string? Fallback(DockItem item, string path)
        => !string.IsNullOrEmpty(item.Aumid) && path != @"shell:AppsFolder\" + item.Aumid ? @"shell:AppsFolder\" + item.Aumid : null;

    private static long StampOf(string path)
    {
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return 0;
        try
        {
            return File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private WriteableBitmap Bitmap(IconImage image, int size)
    {
        if (_bitmaps.TryGetValue((image, size), out var cached))
            return cached;
        return BoundedCache.Put(_bitmaps, (image, size), GlassTiles.ToBitmap(PixelScaler.ToPremultiplied(image.Pixels, size, size), size, size), max: 64);
    }
}
