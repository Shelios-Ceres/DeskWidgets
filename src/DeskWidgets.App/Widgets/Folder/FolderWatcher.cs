namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// A FileSystemWatcher that folds a burst of events into one "rescan" call after the folder has been quiet for
/// <see cref="Quiet"/> (150 ms). The events themselves are never applied: the caller always rescans the whole folder,
/// which also covers a buffer overflow or an error (both just trigger the same rescan).
/// </summary>
internal sealed class FolderWatcher : IDisposable
{
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(150);

    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly Action _rescan;
    private int _disposed;

    public FolderWatcher(string folder, Action rescan)
    {
        _rescan = rescan;
        _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
            InternalBufferSize = 65536,
        };
        _watcher.Created += (_, _) => Touch();
        _watcher.Deleted += (_, _) => Touch();
        _watcher.Renamed += (_, _) => Touch();
        _watcher.Changed += (_, _) => Touch();
        _watcher.Error += (_, _) => Touch();
        _watcher.EnableRaisingEvents = true;
    }

    private void Touch()
    {
        if (Volatile.Read(ref _disposed) == 0)
            _timer.Change(Quiet, Timeout.InfiniteTimeSpan); // restarts the quiet period
    }

    private void Fire()
    {
        if (Volatile.Read(ref _disposed) == 0)
            _rescan();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _timer.Dispose();
    }
}
