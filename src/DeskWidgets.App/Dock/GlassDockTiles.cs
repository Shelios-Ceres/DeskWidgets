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
    private readonly Action _repaint;
    private readonly Dictionary<(IconImage Image, int Size), WriteableBitmap> _bitmaps = [];

    public GlassDockTiles(Action repaint) => _repaint = repaint;

    /// <summary>
    /// What the tile for <paramref name="item"/> would show now, as a key (a tile is rebuilt only when it changes), how to build its idle look
    /// and its running look (HANDOFF 4). <c>Loading</c> is true while the picture is still on its way. The custom tile's key comes with the view
    /// (<see cref="DockItemView.CustomKey"/>, found off the UI thread); until it does, the real icon stands in and the tile changes when it comes.
    /// </summary>
    public (string State, Func<FrameworkElement> Build, Func<FrameworkElement> BuildRunning, bool Loading) Describe(DockItemView view, GlassPalette palette, bool dark, double size, double scale)
    {
        var item = view.Item;
        string theme = dark ? "d" : "l";
        if (view.CustomKey is { } key && GlassTileArt.ForApp(key) is { } art)
            return ($"art|{key}|{theme}|{size}", () => GlassTiles.Designed(art, dark, size), () => GlassDockRunning.Designed(key, art, dark, size), false);

        if (IconPath(item) is { } path)
        {
            int pixels = GlassTiles.IconPixels(size, scale);
            var request = new IconRequest(path, GlassTileProvider.AskSize(pixels), IconFlavor.AppIcon, StampOf(path), Fallback(item, path));
            var icons = IconService.Shared;
            if (!icons.TryGet(request, out var image))
            {
                icons.Request(request, _repaint);
                return ($"loading|{theme}|{size}", () => GlassTiles.Loading(palette, size), () => GlassTiles.Loading(palette, size), true);
            }
            if (image is not null)
            {
                return ($"real|{path}|{pixels}|{theme}|{image.GetHashCode()}",
                    () => GlassTiles.RealIcon(palette, Bitmap(image, pixels), image.FullBleed, size, scale),
                    () => GlassDockRunning.RealIcon(Bitmap(image, pixels), GlassDockRunning.AccentFor(image, dark), size, scale),
                    false);
            }
        }
        var generic = GlassTileArt.For(FolderItemKind.App);
        return ($"app|{theme}|{size}", () => GlassTiles.Designed(generic, dark, size), () => GlassDockRunning.Designed(null, generic, dark, size), false);
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
