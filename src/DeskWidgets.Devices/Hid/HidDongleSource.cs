using DeskWidgets.Core.Devices;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Devices.Hid.Drivers;

namespace DeskWidgets.Devices.Hid;

/// <summary>
/// 2.4 GHz receivers that speak a vendor protocol. Enumerates HID once per
/// scan, groups collections by receiver and hands each receiver to the first
/// <see cref="IHidBatteryDriver"/> that supports it. Receivers without a
/// driver are ignored (the DeviceProbe tool lists them).
/// </summary>
public sealed class HidDongleSource : IDeviceSource
{
    private readonly IHidBatteryDriver[] _drivers;
    private readonly HidArrivalWatcher _watcher = new();

    public HidDongleSource()
        : this(DefaultDrivers())
    {
    }

    public HidDongleSource(IEnumerable<IHidBatteryDriver> drivers)
    {
        _drivers = drivers.ToArray();
        foreach (var driver in _drivers.OfType<INotifyingHidDriver>())
            driver.Changed += (_, _) => Invalidated?.Invoke(this, EventArgs.Empty);
        _watcher.Changed += () => Invalidated?.Invoke(this, EventArgs.Empty);
        _watcher.Start();
    }

    /// <summary>Register new receiver protocols here.</summary>
    public static IReadOnlyList<IHidBatteryDriver> DefaultDrivers() =>
    [
        new RapooPassiveDriver(),
        new LamzuAuroraDriver(),
        new WlMouseDriver(),
    ];

    public string Name => "2.4G receivers";

    public TimeSpan PollInterval => TimeSpan.FromSeconds(20);

    public event EventHandler? Invalidated;

    public Task<IReadOnlyList<DeviceStatus>> ScanAsync(CancellationToken cancellationToken)
        => Task.Run(() => Scan(cancellationToken), cancellationToken);

    private IReadOnlyList<DeviceStatus> Scan(CancellationToken cancellationToken)
    {
        var collections = HidInventory.Enumerate((vid, pid) => _drivers.Any(d => d.Supports(vid, pid)));
        if (collections.Count == 0)
            return [];

        var result = new List<DeviceStatus>();
        foreach (var receiver in collections.GroupBy(c => (c.VendorId, c.ProductId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var driver = _drivers.First(d => d.Supports(receiver.Key.VendorId, receiver.Key.ProductId));
            try
            {
                if (driver.Read(receiver.ToList(), cancellationToken) is { } status)
                    result.Add(status);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"{driver.Name}: reading {receiver.Key.VendorId:X4}:{receiver.Key.ProductId:X4} failed", ex);
            }
        }
        return result;
    }

    public void Dispose()
    {
        _watcher.Dispose();
        foreach (var driver in _drivers.OfType<IDisposable>())
            driver.Dispose();
    }
}
