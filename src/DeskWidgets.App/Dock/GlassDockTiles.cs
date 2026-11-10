using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace DeskWidgets.Dock;

/// <summary>
/// A dock icon's Glass tile (HANDOFF 2), the Big Folder's way: one of the 13 custom tiles when <see cref="AppIconPack"/> knows the program,
/// else its real icon on the tile plate from <see cref="IconService"/> (the loading plate until it comes), else the generic app tile; a group is
/// a Hover plate with its first four members as minis. <paramref name="repaint"/> runs on the UI thread when an icon (or a file's write time)
/// arrives. Nothing here takes pointer input. Make it on the UI thread.
/// </summary>
internal sealed class GlassDockTiles
{
    /// <summary>A file's write time is read again after this long, at the next draw that needs it (a program updated while the dock is up).</summary>
    private const long StampMaxAgeMs = 60_000;

    private readonly Action _repaint;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Dictionary<(IconImage Image, int Size), WriteableBitmap> _bitmaps = [];
    private readonly Dictionary<string, (long Stamp, long ReadAt)> _stamps = new(StringComparer.OrdinalIgnoreCase); // UI thread only
    private readonly HashSet<string> _reading = new(StringComparer.OrdinalIgnoreCase);

    public GlassDockTiles(Action repaint) => _repaint = repaint;

    /// <summary>
    /// What the tile for <paramref name="view"/> would show now, as a key (a tile is rebuilt only when it changes), how to build its idle look
    /// and its running look (HANDOFF 4; null for a group, which has no running art of its own: its running members' minis show theirs).
    /// <c>Loading</c> is true while a picture is still on its way. The custom tile's key comes with the view (<see cref="DockItemView.CustomKey"/>,
    /// found off the UI thread); until it does, the real icon stands in and the tile changes when it comes.
    /// </summary>
    public (string State, Func<FrameworkElement> Build, Func<FrameworkElement>? BuildRunning, bool Loading) Describe(DockItemView view, GlassPalette palette, bool dark, double size, double scale)
    {
        if (view.IsGroup)
            return DescribeGroup(view, palette, dark, size, scale);
        var item = view.Item;
        string theme = dark ? "d" : "l";
        if (view.CustomKey is { } key && GlassTileArt.ForApp(key) is { } art)
            return ($"art|{key}|{theme}|{size}", () => GlassTiles.Designed(art, dark, size), () => GlassDockRunning.Designed(key, art, dark, size), false);

        if (DockIconSource.PathFor(item) is { } path)
        {
            // The icon cache is keyed by the file's write time too: until it is known (read off the UI thread) nothing is asked for, so the
            // picture is never fetched twice under two stamps.
            if (!TryStamp(path, out long stamp))
                return ($"loading|{theme}|{size}", () => GlassTiles.Loading(palette, size), () => GlassTiles.Loading(palette, size), true);
            int pixels = GlassTiles.IconPixels(size, scale);
            var request = new IconRequest(path, GlassTileProvider.AskSize(pixels), IconFlavor.AppIcon, stamp, DockIconSource.FallbackFor(item));
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

    /// <summary>
    /// HANDOFF 2: a tile-sized plate in Hover (radius 0.225) with the first four members as minis in a 2 x 2, no text; a running member's mini
    /// shows its running art, a missing one is at 40 %.
    /// </summary>
    private (string, Func<FrameworkElement>, Func<FrameworkElement>?, bool) DescribeGroup(DockItemView view, GlassPalette palette, bool dark, double size, double scale)
    {
        var (mini, _, _) = GlassDockLayout.Minis(size);
        var minis = view.Children!.Take(4).Select(child =>
        {
            var tile = Describe(child, palette, dark, mini, scale);
            bool running = child.State == DockRunState.Running && tile.BuildRunning is not null;
            return (tile, running, child.Missing);
        }).ToList();
        string state = $"group|{(dark ? "d" : "l")}|{size}|" + string.Join(";", minis.Select(m => $"{m.tile.State}|{m.running}|{m.Missing}"));
        bool loading = minis.Any(m => m.tile.Loading);
        FrameworkElement Build()
        {
            var canvas = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
            canvas.Children.Add(new Rectangle { Width = size, Height = size, RadiusX = size * 0.225, RadiusY = size * 0.225, Fill = palette.Hover });
            for (int i = 0; i < minis.Count; i++)
            {
                var (tile, running, missing) = minis[i];
                var element = running ? tile.BuildRunning!() : tile.Build();
                if (missing)
                    element.Opacity = 0.4;
                var rect = GlassDockLayout.MiniRect(size, i);
                Canvas.SetLeft(element, rect.X);
                Canvas.SetTop(element, rect.Y);
                canvas.Children.Add(element);
            }
            return new Grid { Width = size, Height = size, IsHitTestVisible = false, Children = { canvas } };
        }
        return (state, Build, null, loading);
    }

    /// <summary>
    /// The file's write time for the icon cache's key, from a cache filled off the UI thread (a stat of an exe on a slow or sleeping disk
    /// must not hold up a draw). False while it was never read: the read is started and the tiles are repainted when it is in. A known
    /// stamp older than <see cref="StampMaxAgeMs"/> is still used, and read again behind it; the tiles repaint only if it changed.
    /// </summary>
    private bool TryStamp(string path, out long stamp)
    {
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            stamp = 0;
            return true;
        }
        bool known = _stamps.TryGetValue(path, out var entry);
        stamp = entry.Stamp;
        if ((!known || Environment.TickCount64 - entry.ReadAt > StampMaxAgeMs) && _reading.Add(path))
        {
            Task.Run(() =>
            {
                long read = ReadStamp(path);
                _dispatcher.TryEnqueue(() =>
                {
                    _reading.Remove(path);
                    bool changed = !_stamps.TryGetValue(path, out var old) || old.Stamp != read;
                    BoundedCache.Put(_stamps, path, (read, Environment.TickCount64), max: 256);
                    if (changed)
                        _repaint();
                });
            });
        }
        return known;
    }

    private static long ReadStamp(string path)
    {
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
