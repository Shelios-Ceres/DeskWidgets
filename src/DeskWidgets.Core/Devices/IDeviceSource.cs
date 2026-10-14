namespace DeskWidgets.Core.Devices;

/// <summary>
/// A provider of device states (system battery, Bluetooth, a vendor 2.4 GHz
/// protocol, ...). Sources are polled by <see cref="DeviceHub"/>; they must be
/// read-only monitors and must not throw for expected conditions such as a
/// missing receiver — return an empty list instead.
/// </summary>
public interface IDeviceSource : IDisposable
{
    string Name { get; }

    /// <summary>Delay between two regular scans.</summary>
    TimeSpan PollInterval { get; }

    /// <summary>
    /// Raised when the source learns that its devices changed (plug/unplug,
    /// connection or power events). The hub rescans this source shortly after.
    /// May be raised on any thread.
    /// </summary>
    event EventHandler? Invalidated;

    Task<IReadOnlyList<DeviceStatus>> ScanAsync(CancellationToken cancellationToken);
}
