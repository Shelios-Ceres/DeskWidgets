using DeskWidgets.Core.Diagnostics;
using Microsoft.UI.Dispatching;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// The state of one Big Folder card, shared by both themes: which folder it shows, the sorted items, the sort and manual
/// order, the origin log, and the watcher that keeps them current. <see cref="Changed"/> is raised on the UI thread
/// after every rescan. All Shell reading happens on the model's own STA worker.
/// </summary>
internal sealed class FolderModel : IDisposable
{
    /// <summary>How often a missing folder is looked for, and how soon a folder that could not be read is read again.</summary>
    private static readonly TimeSpan MissingPoll = TimeSpan.FromSeconds(3);

    private readonly Action<Action> _toUi;
    private readonly Action _persist;
    private readonly Func<string, OriginLog, List<FolderItem>> _scan;
    private readonly StaWorker _worker = new("Big Folder scanner");
    private readonly object _gate = new();
    private FolderWatcher? _watcher;
    private Timer? _missingPoll;
    private Timer? _retry;
    private bool _scanning;
    private bool _again;
    private bool _disposed;
    private List<FolderItem> _all = [];
    private string _originPath;

    /// <param name="persist">Called after the folder, the sort or the manual order changed, so the widget can save its options.</param>
    public FolderModel(string path, FolderSort sort, IEnumerable<string> order, DispatcherQueue ui, Action persist)
        : this(path, sort, order, action => ui.TryEnqueue(() => action()), persist, FolderScanner.Scan)
    {
    }

    /// <param name="toUi">Runs a scan's result on the UI thread.</param>
    /// <param name="scan">Reads the folder (tests stand in for <see cref="FolderScanner.Scan"/>).</param>
    internal FolderModel(string path, FolderSort sort, IEnumerable<string> order, Action<Action> toUi, Action persist, Func<string, OriginLog, List<FolderItem>> scan)
    {
        Path = path;
        Sort = sort;
        Order = [.. order];
        _toUi = toUi;
        _persist = persist;
        _scan = scan;
        _originPath = OriginLog.PathFor(path);
        Origins = OriginLog.Load(_originPath);
    }

    public string Path { get; private set; }

    /// <summary>The folder's own name (what the header and the panel title show).</summary>
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : Path;

    public FolderSort Sort { get; private set; }

    public List<string> Order { get; private set; }

    public OriginLog Origins { get; private set; }

    public bool Exists { get; private set; } = true;

    /// <summary>The items in the current sort order. Replaced as a whole after each scan; safe to read on the UI thread.</summary>
    public IReadOnlyList<FolderItem> Items { get; private set; } = [];

    public event Action? Changed;

    public void Start()
    {
        Rescan();
    }

    /// <summary>Looks again, now (the debounced watcher calls this too).</summary>
    public void Rescan()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_scanning)
            {
                _again = true;
                return;
            }
            _scanning = true;
        }
        string path = Path;
        var origins = Origins;
        _worker.Post(() =>
        {
            bool exists = false;
            List<FolderItem>? items = null; // stays null when the folder is there but could not be read
            try
            {
                exists = Directory.Exists(path);
                items = exists ? _scan(path, origins) : [];
            }
            catch (Exception ex)
            {
                Log.Warn("Scanning a Big Folder failed; the card keeps what it showed and reads the folder again shortly", ex);
                exists = Directory.Exists(path);
            }
            _toUi(() => Apply(path, exists, items));
        });
    }

    /// <param name="items">What the scan found; null when the folder exists but could not be read this time.</param>
    private void Apply(string scannedPath, bool exists, List<FolderItem>? items)
    {
        bool again;
        lock (_gate)
        {
            _scanning = false;
            again = _again;
            _again = false;
        }
        if (_disposed)
            return;
        if (scannedPath == Path)
        {
            Exists = exists;
            if (!exists)
            {
                // A missing folder (a drive unplugged, the folder renamed elsewhere) shows nothing, but its origin log and its manual
                // order stay as they are for when it comes back.
                _all = [];
                Items = [];
            }
            else if (items is not null)
            {
                _all = items;
                Resort();
                // Only a folder that was read in full says which items are gone.
                if (Origins.Prune(items.Select(i => i.FileName)))
                    Origins.Save(_originPath);
            }
            // Otherwise the read failed (a network blip, a lock), which is not an empty folder: the items, the order and the origins stay.
            Watch(exists, readFailed: exists && items is null);
            Changed?.Invoke();
        }
        if (again || scannedPath != Path)
            Rescan();
    }

    private void Resort()
    {
        var sorted = FolderSorter.Sort(_all, Sort, Order);
        Items = sorted;
        if (Sort == FolderSort.Manual)
        {
            var names = sorted.Select(i => i.FileName).ToList();
            if (!names.SequenceEqual(Order, StringComparer.OrdinalIgnoreCase))
            {
                Order = names;
                _persist();
            }
        }
    }

    private void Watch(bool exists, bool readFailed)
    {
        if (exists && _watcher is null)
        {
            _missingPoll?.Dispose();
            _missingPoll = null;
            try
            {
                _watcher = new FolderWatcher(Path, Rescan);
            }
            catch (Exception ex)
            {
                Log.Warn("Watching a Big Folder failed; polling instead", ex);
                _missingPoll = new Timer(_ => Rescan(), null, MissingPoll, MissingPoll);
            }
        }
        else if (!exists)
        {
            _watcher?.Dispose();
            _watcher = null;
            _missingPoll ??= new Timer(_ => Rescan(), null, MissingPoll, MissingPoll);
        }
        if (readFailed) // the watcher only hears of changes, and a folder that failed to read may not change
            (_retry ??= new Timer(_ => Rescan())).Change(MissingPoll, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Points the card at another folder (or the same one renamed).</summary>
    public void SetFolder(string path)
    {
        _watcher?.Dispose();
        _watcher = null;
        Path = path;
        _originPath = OriginLog.PathFor(path);
        Origins = OriginLog.Load(_originPath);
        Order = [];
        Items = [];
        _all = [];
        _persist();
        Changed?.Invoke();
        Rescan();
    }

    /// <summary>The folder itself was renamed: the same card, the same origin log, a new path.</summary>
    public void FolderRenamed(string newPath)
    {
        _watcher?.Dispose();
        _watcher = null;
        Path = newPath;
        _originPath = OriginLog.PathFor(newPath);
        Origins.Save(_originPath);
        _persist();
        Changed?.Invoke();
        Rescan();
    }

    public void SetSort(FolderSort sort)
    {
        if (sort == Sort)
            return;
        if (sort == FolderSort.Manual)
            Order = Items.Select(i => i.FileName).ToList(); // freeze what is on screen
        Sort = sort;
        Resort();
        _persist();
        Changed?.Invoke();
    }

    public void SaveOrigins() => Origins.Save(_originPath);

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
        _watcher?.Dispose();
        _missingPoll?.Dispose();
        _retry?.Dispose();
        _worker.Dispose();
    }
}
