using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// Chooses and builds the tile of a Big Folder item in the Glass theme (the card and the panel both use it): a designed tile, a
/// real app icon on its plate, a photo or video frame, or the plate shown while the picture is on its way. The pictures come from
/// <see cref="IconService"/>; <paramref name="repaint"/> runs on the UI thread when one arrives.
/// </summary>
internal sealed class GlassTileProvider
{
    private readonly Func<double> _scale;
    private readonly Func<bool> _dark;
    private readonly Action _repaint;
    private readonly Dictionary<(IconImage Image, int Width, int Height, bool Cover), WriteableBitmap> _bitmaps = [];
    private readonly HashSet<IconRequest> _pending = [];

    public GlassTileProvider(Func<double> scale, Func<bool> dark, Action repaint)
    {
        _scale = scale;
        _dark = dark;
        _repaint = repaint;
    }

    public FrameworkElement Tile(GlassPalette palette, FolderItem item, double size)
    {
        var tile = Build(palette, item, size, out var request);
        if (request is not null && _pending.Remove(request) && Motion.MotionSettings.Enabled)
        {
            // The picture was on its way when this view was drawn: it fades in over the plate that stood in for it.
            tile.Opacity = 0;
            tile.OpacityTransition = new Microsoft.UI.Xaml.ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
            tile.Loaded += (_, _) => tile.Opacity = 1;
        }
        return tile;
    }

    private FrameworkElement Build(GlassPalette palette, FolderItem item, double size, out IconRequest? request)
    {
        request = null;
        double scale = _scale();
        bool dark = _dark();
        var kind = item.Kind;

        if (item.CustomIcon is { } key && GlassTileArt.ForApp(key) is { } customArt)
            return GlassTiles.Designed(customArt, dark, size);

        if (kind == FolderItemKind.App || (kind is FolderItemKind.Image or FolderItemKind.Video && !item.IsShortcut))
        {
            bool app = kind == FolderItemKind.App;
            int pixels = app ? GlassTiles.IconPixels(size, scale) : (int)Math.Ceiling(size * scale);
            request = new IconRequest(item.IconPath, AskSize(pixels), app ? IconFlavor.AppIcon : IconFlavor.Thumbnail, StampOf(item), Fallback(item));
            var icons = IconService.Shared;
            if (icons.TryGet(request, out var image))
            {
                if (image is not null)
                {
                    int target = app ? pixels : (int)Math.Ceiling(size * scale);
                    if (!app)
                        return GlassTiles.Frame(Bitmap(image, target, cover: true), size, play: kind == FolderItemKind.Video);
                    return GlassTiles.RealIcon(palette, Bitmap(image, pixels, cover: false), image.FullBleed, size, scale);
                }
            }
            else
            {
                icons.Request(request, _repaint);
                _pending.Add(request);
                return GlassTiles.Loading(palette, size);
            }
        }
        string? label = kind is FolderItemKind.Document or FolderItemKind.Spreadsheet or FolderItemKind.Presentation ? FolderKinds.ExtensionLabel(item.LabelName) : null;
        string? symbol = kind == FolderItemKind.Other ? FolderKinds.OtherSymbol(item.LabelName) : null;
        return GlassTiles.Designed(GlassTileArt.For(kind), dark, size, label, symbol);
    }

    private static long StampOf(FolderItem item)
    {
        try
        {
            return File.GetLastWriteTimeUtc(item.Path).Ticks;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>For an app shortcut that has no path (a Store app): the AppsFolder item to ask for the icon if the shortcut gives none.</summary>
    private static string? Fallback(FolderItem item)
        => item.Identity?.Aumid is { } aumid && item.IsShortcut && item.Identity.ExePath is null ? @"shell:AppsFolder\" + aumid : null;

    /// <summary>The size to ask the Shell for: the next of the usual cache sizes, so one request serves several tile sizes.</summary>
    internal static int AskSize(int pixels) => pixels <= 32 ? 32 : pixels <= 48 ? 48 : pixels <= 64 ? 64 : pixels <= 96 ? 96 : pixels <= 128 ? 128 : 256;

    private WriteableBitmap Bitmap(IconImage image, int size, bool cover)
    {
        if (_bitmaps.TryGetValue((image, size, size, cover), out var cached))
            return cached;
        var bytes = cover ? PixelScaler.CoverToPremultiplied(image.Pixels, size) : PixelScaler.ToPremultiplied(image.Pixels, size, size);
        return BoundedCache.Put(_bitmaps, (image, size, size, cover), GlassTiles.ToBitmap(bytes, size, size), max: 121);
    }
}
