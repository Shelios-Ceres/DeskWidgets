using DeskWidgets.Core.Diagnostics;
using Microsoft.UI.Dispatching;

namespace DeskWidgets.Widgets.Folder;

/// <summary>Which picture to ask the Shell for.</summary>
internal enum IconFlavor
{
    /// <summary>A program's icon (SIIGBF_ICONONLY): the real icon that goes on a plate, or into Pixel's tier 2.</summary>
    AppIcon,

    /// <summary>A photo's or video's thumbnail.</summary>
    Thumbnail,
}

/// <param name="Path">The file, shortcut or folder.</param>
/// <param name="Size">Edge in physical pixels; the Shell may answer with a bigger cached size.</param>
/// <param name="Stamp">The file's last-write ticks, so a changed file asks again.</param>
/// <param name="Fallback">For an app shortcut with no path: its "shell:AppsFolder\&lt;aumid&gt;" name, tried when the shortcut gives no good icon.</param>
internal sealed record IconRequest(string Path, int Size, IconFlavor Flavor, long Stamp, string? Fallback = null);

/// <summary>A picture from the Shell: straight alpha, top-down BGRA, plus whether it is a full-bleed square.</summary>
internal sealed record IconImage(Pixels Pixels, bool FullBleed);

/// <summary>
/// Real icons and thumbnails from <c>IShellItemImageFactory</c>, fetched on two background STA threads (shell items are
/// created on the thread that uses them) and cached by path, size and date. <see cref="TryGet"/> answers at once from the cache;
/// otherwise the caller shows the type icon and <see cref="Request"/> calls back on the UI thread when the picture arrives.
/// A failed request is cached too, so a file without a thumbnail is not asked about again.
/// </summary>
internal sealed class IconService : IDisposable
{
    private const int CacheLimit = 400;

    private static IconService? s_shared;

    private readonly DispatcherQueue _ui;
    private readonly StaWorker[] _workers = [new("Big Folder icons 1"), new("Big Folder icons 2")];
    private readonly Dictionary<IconRequest, IconImage?> _cache = [];
    private readonly Queue<IconRequest> _order = new();
    private readonly HashSet<IconRequest> _pending = [];
    private readonly object _gate = new();
    private int _next;

    private static int s_started;

    /// <summary>How many pictures were asked of the Shell since the app started (the large-folder bench reads it).</summary>
    public static int RequestsStarted => Volatile.Read(ref s_started);

    private IconService(DispatcherQueue ui) => _ui = ui;

    /// <summary>One service for the app. First use must be on the UI thread.</summary>
    public static IconService Shared => s_shared ??= new IconService(DispatcherQueue.GetForCurrentThread());

    public bool TryGet(IconRequest request, out IconImage? image)
    {
        lock (_gate)
            return _cache.TryGetValue(request, out image);
    }

    /// <summary>Fetches in the background unless cached or already on its way; <paramref name="ready"/> runs on the UI thread afterwards.</summary>
    public void Request(IconRequest request, Action ready)
    {
        lock (_gate)
        {
            if (_cache.ContainsKey(request))
                return;
            if (!_pending.Add(request))
            {
                // Already on its way: the earlier caller's callback repaints; this caller's own view repaints on the same event.
                _waiting.Add((request, ready));
                return;
            }
        }
        Interlocked.Increment(ref s_started);
        _workers[Interlocked.Increment(ref _next) % _workers.Length].Post(() =>
        {
            IconImage? image = null;
            try
            {
                image = Load(request);
            }
            catch (Exception ex)
            {
                Log.Warn($"Loading an icon for '{request.Path}' failed", ex);
            }
            lock (_gate)
            {
                if (_cache.Count >= CacheLimit && _order.TryDequeue(out var oldest))
                    _cache.Remove(oldest);
                _cache[request] = image;
                _order.Enqueue(request);
                _pending.Remove(request);
            }
            _ui.TryEnqueue(() =>
            {
                ready();
                List<(IconRequest, Action)> due;
                lock (_gate)
                {
                    due = _waiting.Where(w => w.Request == request).Select(w => (w.Request, w.Ready)).ToList();
                    _waiting.RemoveAll(w => w.Request == request);
                }
                foreach (var (_, callback) in due)
                    callback();
            });
        });
    }

    private readonly List<(IconRequest Request, Action Ready)> _waiting = [];

    private static IconImage? Load(IconRequest request)
    {
        uint flags = ShellNative.SIIGBF_BIGGERSIZEOK | (request.Flavor == IconFlavor.AppIcon ? ShellNative.SIIGBF_ICONONLY : ShellNative.SIIGBF_THUMBNAILONLY);
        // A thumbnail never downloads a cloud file: ask the cache only.
        if (request.Flavor == IconFlavor.Thumbnail && IsCloudOnly(request.Path))
            flags |= ShellNative.SIIGBF_INCACHEONLY;
        var pixels = Fetch(request.Path, request.Size, flags);
        if (pixels is null || (request.Fallback is not null && pixels.Width * 2 < request.Size))
            pixels = Fetch(request.Fallback, request.Size, flags) ?? pixels;
        return pixels is null ? null : new IconImage(pixels, pixels.IsFullBleed());
    }

    private static Pixels? Fetch(string? path, int size, uint flags)
    {
        if (path is null)
            return null;
        try
        {
            var item = Shell.FromPath(path);
            if (item is not IShellItemImageFactory factory)
                return null;
            int hr = factory.GetImage(new SIZE { cx = size, cy = size }, flags, out var bitmap);
            if (hr < 0 || bitmap == IntPtr.Zero)
                return null;
            try
            {
                return Bitmaps.Read(bitmap);
            }
            finally
            {
                ShellNative.DeleteObject(bitmap);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A OneDrive-style placeholder whose content is not on this PC.</summary>
    private static bool IsCloudOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes)0x00400000 /* RECALL_ON_DATA_ACCESS */) != 0
                || (attributes & (FileAttributes)0x00040000 /* RECALL_ON_OPEN */) != 0
                || (attributes & FileAttributes.Offline) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var worker in _workers)
            worker.Dispose();
    }
}
