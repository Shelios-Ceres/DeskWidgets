namespace DeskWidgets.Core.Devices;

public enum DeviceKind
{
    Unknown,
    Laptop,
    Desktop,
    Mouse,
    Keyboard,
    Headphones,
    Headset,
    Earbuds,
    Speaker,
    GameController,
    Phone,
    Tablet,
    Watch,
    Pen,
}

public enum ConnectionKind
{
    Unknown,
    Internal,
    Bluetooth,
    BluetoothLE,
    Wireless24G,
    Usb,
}

/// <summary>
/// One device as seen by a <see cref="IDeviceSource"/>. Value semantics on
/// purpose: the hub compares snapshots with record equality to decide whether
/// anything changed, so keep every member a plain value.
/// </summary>
public sealed record DeviceStatus
{
    /// <summary>Stable across scans, e.g. "host", "bt:{container}", "hid:37B0:002E".</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public DeviceKind Kind { get; init; }

    public ConnectionKind Connection { get; init; }

    /// <summary>False when the device is known (paired / receiver present) but not reachable right now.</summary>
    public bool IsConnected { get; init; } = true;

    /// <summary>0-100, or null when the device does not report a level.</summary>
    public int? BatteryPercent { get; init; }

    /// <summary>Charging, or plugged in for devices that only report "on external power".</summary>
    public bool IsCharging { get; init; }

    /// <summary>The computer the app runs on.</summary>
    public bool IsHost { get; init; }

    /// <summary>A power-saving mode is active (shown as a yellow ring, like iOS Low Power Mode).</summary>
    public bool IsPowerSaving { get; init; }

    /// <summary>Optional extra line such as "8000 Hz".</summary>
    public string? Detail { get; init; }

    /// <summary>Name of the source that produced this entry (diagnostics).</summary>
    public string Source { get; init; } = "";
}
