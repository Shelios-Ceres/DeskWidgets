namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// One background STA thread that runs queued jobs in order. Shell items are created on the thread that uses them
/// (the spike showed that sharing them across apartments is asking for trouble), so every job that touches the Shell
/// runs here, never on the UI thread.
/// </summary>
internal sealed class StaWorker : IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _jobs = new();
    private readonly Thread _thread;

    public StaWorker(string name)
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Loop()
    {
        foreach (var job in _jobs.GetConsumingEnumerable())
        {
            try
            {
                job();
            }
            catch (Exception ex)
            {
                Core.Diagnostics.Log.Warn("A Big Folder background job failed", ex);
            }
        }
    }

    public void Post(Action job)
    {
        if (!_jobs.IsAddingCompleted)
            _jobs.Add(job);
    }

    public Task<T> Run<T>(Func<T> job)
    {
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                source.SetResult(job());
            }
            catch (Exception ex)
            {
                source.SetException(ex);
            }
        });
        return source.Task;
    }

    public void Dispose() => _jobs.CompleteAdding();
}
