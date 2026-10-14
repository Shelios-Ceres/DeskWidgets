using DeskWidgets.Core.Diagnostics;

namespace DeskWidgets.Core.Devices;

/// <summary>
/// Merges every <see cref="IDeviceSource"/> into one device list. Each source
/// is polled on its own interval and rescanned early when it raises
/// <see cref="IDeviceSource.Invalidated"/>. Scans of one source never overlap;
/// a request that arrives mid-scan causes exactly one follow-up scan.
/// </summary>
public sealed class DeviceHub : IDisposable
{
    private static readonly TimeSpan InvalidationDebounce = TimeSpan.FromMilliseconds(400);

    private sealed class SourceSlot(IDeviceSource source)
    {
        public IDeviceSource Source { get; } = source;
        public IReadOnlyList<DeviceStatus> Devices = [];
        public int Running;
        public int Pending;
        public int DebounceArmed;
    }

    private readonly SourceSlot[] _slots;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _sync = new();
    private IReadOnlyList<DeviceStatus> _devices = [];
    private bool _started;

    public DeviceHub(IEnumerable<IDeviceSource> sources)
    {
        _slots = sources.Select(s => new SourceSlot(s)).ToArray();
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
                        Log.Warn($"Device source '{slot.Source.Name}' scan failed", ex);
                        continue;
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
