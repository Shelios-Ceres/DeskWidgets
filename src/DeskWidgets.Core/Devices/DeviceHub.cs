using DeskWidgets.Core.Diagnostics;

namespace DeskWidgets.Core.Devices;

/// <summary>
/// Merges every <see cref="IDeviceSource"/> into one device list. Each source
/// is polled on its own interval and rescanned early when it raises
/// <see cref="IDeviceSource.Invalidated"/>. Scans of one source never overlap;
/// a request that arrives mid-scan causes exactly one follow-up scan.
/// A scan that throws keeps the source's previous result; the source is then
/// retried sooner than its poll interval, and a run of failures is logged as
/// one warning plus one line when the source recovers.
/// </summary>
public sealed class DeviceHub : IDisposable
{
    private static readonly TimeSpan InvalidationDebounce = TimeSpan.FromMilliseconds(400);

    /// <summary>First retry after a failed scan; doubles per further failure, capped at the poll interval.</summary>
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(5);

    private sealed class SourceSlot(IDeviceSource source)
    {
        public IDeviceSource Source { get; } = source;
        public IReadOnlyList<DeviceStatus> Devices = [];
        public int Running;
        public int Pending;
        public int DebounceArmed;

        /// <summary>Failed scans in the current run; written only by the scan that owns the slot.</summary>
        public int Failures;
        public int RetryArmed;
        public DateTime FailingSinceUtc;
    }

    private readonly SourceSlot[] _slots;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _sync = new();
    private IReadOnlyList<DeviceStatus> _devices = [];
    private readonly Action<string, Exception?> _warn;
    private readonly Action<string> _info;
    private readonly TimeSpan _retryDelay;
    private bool _started;

    public DeviceHub(IEnumerable<IDeviceSource> sources)
        : this(sources, Log.Warn, Log.Info, DefaultRetryDelay)
    {
    }

    internal DeviceHub(IEnumerable<IDeviceSource> sources, Action<string, Exception?> warn, Action<string> info, TimeSpan retryDelay)
    {
        _slots = sources.Select(s => new SourceSlot(s)).ToArray();
        _warn = warn;
        _info = info;
        _retryDelay = retryDelay;
    }

    /// <summary>Raised on a background thread whenever <see cref="Devices"/> changes.</summary>
    public event EventHandler? DevicesChanged;

    public IReadOnlyList<DeviceStatus> Devices
    {
        get { lock (_sync) return _devices; }
    }

    public void Start()
    {
        if (_started)
            return;
        _started = true;
        foreach (var slot in _slots)
        {
            slot.Source.Invalidated += (_, _) => ScheduleScan(slot);
            _ = Task.Run(() => PollLoopAsync(slot));
        }
    }

    /// <summary>Rescans every source now (e.g. user pressed "refresh").</summary>
    public void RefreshAll()
    {
        foreach (var slot in _slots)
            _ = Task.Run(() => ScanAsync(slot));
    }

    private async Task PollLoopAsync(SourceSlot slot)
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            await ScanAsync(slot).ConfigureAwait(false);
            try
            {
                await Task.Delay(slot.Source.PollInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>After a failed scan, try again sooner than the next poll: 5 s, then 10 s, 20 s, ... up to the poll interval.</summary>
    private void ScheduleRetry(SourceSlot slot)
    {
        if (Interlocked.Exchange(ref slot.RetryArmed, 1) == 1)
            return;
        var delay = _retryDelay * Math.Pow(2, Math.Min(slot.Failures - 1, 16));
        if (delay > slot.Source.PollInterval)
            delay = slot.Source.PollInterval;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            Volatile.Write(ref slot.RetryArmed, 0);
            await ScanAsync(slot).ConfigureAwait(false);
        });
    }

    private void ScheduleScan(SourceSlot slot)
    {
        // Sources often fire bursts (one PnP event per HID collection); coalesce them.
        if (Interlocked.Exchange(ref slot.DebounceArmed, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(InvalidationDebounce, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            Volatile.Write(ref slot.DebounceArmed, 0);
            await ScanAsync(slot).ConfigureAwait(false);
        });
    }

    private async Task ScanAsync(SourceSlot slot)
    {
        // Ask first, then try to become the scanner. Whoever scans checks for
        // requests again after letting go of the slot, so a request that lands
        // while a scan is finishing is picked up instead of lost.
        Volatile.Write(ref slot.Pending, 1);
        while (Volatile.Read(ref slot.Pending) == 1 && !_cts.IsCancellationRequested)
        {
            if (Interlocked.Exchange(ref slot.Running, 1) == 1)
                return; // the running scan sees the request
            try
            {
                while (Interlocked.Exchange(ref slot.Pending, 0) == 1 && !_cts.IsCancellationRequested)
                {
                    IReadOnlyList<DeviceStatus> devices;
                    try
                    {
                        devices = await slot.Source.ScanAsync(_cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Keep the previous result; one failed scan should not blank the widget.
                        // Only the first failure of a run is logged.
                        if (slot.Failures == 0)
                        {
                            slot.FailingSinceUtc = DateTime.UtcNow;
                            _warn($"Device source '{slot.Source.Name}' scan failed, retrying shortly; more failures in this run are not logged", ex);
                        }
                        Volatile.Write(ref slot.Failures, slot.Failures + 1);
                        ScheduleRetry(slot);
                        continue;
                    }
                    if (slot.Failures > 0)
                    {
                        _info($"Device source '{slot.Source.Name}' recovered after {slot.Failures} failed scan(s) over {(DateTime.UtcNow - slot.FailingSinceUtc).TotalSeconds:0} s");
                        Volatile.Write(ref slot.Failures, 0);
                    }
                    Publish(slot, devices);
                }
            }
            finally
            {
                // A full fence (unlike a volatile write): the Pending check that
                // follows cannot be read before the slot is released.
                Interlocked.Exchange(ref slot.Running, 0);
            }
        }
    }

    private void Publish(SourceSlot slot, IReadOnlyList<DeviceStatus> devices)
    {
        lock (_sync)
        {
            if (slot.Devices.SequenceEqual(devices))
                return;
            slot.Devices = devices;
            _devices = _slots.SelectMany(s => s.Devices).ToArray();
        }
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var slot in _slots)
        {
            try
            {
                slot.Source.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn($"Disposing device source '{slot.Source.Name}' failed", ex);
            }
        }
    }
}
