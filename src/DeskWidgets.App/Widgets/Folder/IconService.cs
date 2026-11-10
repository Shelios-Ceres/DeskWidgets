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
    private readonly IconResultCache _cache = new(CacheLimit);
    private readonly HashSet<IconRequest> _pending = [];
    private readonly object _gate = new();
    private int _next;

    private static int s_started;

    /// <summary>How many pictures were asked of the Shell since the app started (the large-folder bench reads it).</summary>
    public static int RequestsStarted => Volatile.Read(ref s_started);

    private IconService(DispatcherQueue ui) => _ui = ui;

    /// <summary>One service for the app. First use must be on the UI thread.</summary>
    public static IconService Shared => s_shared ??= new IconService(DispatcherQueue.GetForCurrentThread());

    /// <summary>
    /// The cached answer, at once. A failed Start-entry request that is due again (<see cref="IconResultCache.RetryFailedAfterMs"/>) still answers
    /// with its failure, so the tile does not change while it is asked again; the re-fetch runs in the background and <paramref name="retryReady"/>
    /// runs on the UI thread only if it now has a picture.
    /// </summary>
    public bool TryGet(IconRequest request, out IconImage? image, Action? retryReady = null)
    {
        bool due;
        bool hit;
        lock (_gate)
            hit = _cache.TryGet(request, Environment.TickCount64, out image, out due);
        if (hit && due && retryReady is not null)
            Fetch(request, retryReady, retry: true);
        return hit;
    }

    /// <summary>Fetches in the background unless cached or already on its way; <paramref name="ready"/> runs on the UI thread afterwards.</summary>
    public void Request(IconRequest request, Action ready)
    {
        lock (_gate)
        {
            if (_cache.Contains(request))
                return;
        }
        Fetch(request, ready, retry: false);
    }

    private void Fetch(IconRequest request, Action ready, bool retry)
    {
        lock (_gate)
        {
            if (!_pending.Add(request))
            {
                // Already on its way: the earlier caller's callback repaints; this caller's own view repaints on the same event.
                if (!retry)
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
                _cache.Store(request, image, Environment.TickCount64);
                _pending.Remove(request);
            }
            // A retry that failed again changes nothing on screen: no repaint.
            if (retry && image is null)
                return;
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

/// <summary>
/// The icon service's results, with no threads or Shell in it so it can be tested: results by request, the order they came in (to evict the oldest
/// once full), and when each failure happened. A failed Start-entry request (<c>shell:AppsFolder\...</c>) is due again after
/// <see cref="RetryFailedAfterMs"/>: the Applications folder can be empty right after sign-in, and a failure kept for the whole session left a tile
/// blank until the app restarted. A request is in the order once, however many times it is stored.
/// </summary>
internal sealed class IconResultCache
{
    /// <summary>How long a failed Start-entry request waits before it is asked again.</summary>
    internal const long RetryFailedAfterMs = 30_000;

    private readonly int _limit;
    private readonly Dictionary<IconRequest, IconImage?> _results = [];
    private readonly Queue<IconRequest> _order = new();
    private readonly Dictionary<IconRequest, long> _failedAt = [];

    public IconResultCache(int limit) => _limit = limit;

    /// <summary>How many requests the eviction order holds (one per cached request).</summary>
    public int OrderCount => _order.Count;

    public int Count => _results.Count;

    public bool Contains(IconRequest request) => _results.ContainsKey(request);

    /// <summary>The cached result (a failure is a cached null). <paramref name="retryDue"/>: it is a failure that is worth asking again now; the answer is still the failure.</summary>
    public bool TryGet(IconRequest request, long now, out IconImage? image, out bool retryDue)
    {
        retryDue = false;
        if (!_results.TryGetValue(request, out image))
            return false;
        retryDue = image is null && _failedAt.TryGetValue(request, out long failed) && ShouldRetry(request, now - failed);
        return true;
    }

    /// <summary>Keeps a result. A request already cached is updated in place: no new place in the order, and nothing is evicted for it.</summary>
    public void Store(IconRequest request, IconImage? image, long now)
    {
        if (!_results.ContainsKey(request))
        {
            if (_results.Count >= _limit && _order.TryDequeue(out var oldest))
            {
                _results.Remove(oldest);
                _failedAt.Remove(oldest);
            }
            _order.Enqueue(request);
        }
        _results[request] = image;
        if (image is null)
            _failedAt[request] = now;
        else
            _failedAt.Remove(request);
    }

    /// <summary>Whether a failed request is worth asking again after <paramref name="ageMs"/>: only a Start-entry one, and not before <see cref="RetryFailedAfterMs"/>.</summary>
    internal static bool ShouldRetry(IconRequest request, long ageMs)
        => ageMs >= RetryFailedAfterMs && (IsStartEntry(request.Path) || IsStartEntry(request.Fallback));

    private static bool IsStartEntry(string? path) => path is not null && path.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase);
}
