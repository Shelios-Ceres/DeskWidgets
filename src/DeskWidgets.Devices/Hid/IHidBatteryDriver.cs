using DeskWidgets.Core.Devices;

namespace DeskWidgets.Devices.Hid;

/// <summary>
/// A vendor protocol for one family of 2.4 GHz receivers. Drivers must stay
/// read-only: only send frames that were verified against the vendor's own
/// driver traffic, never configuration, pairing or firmware commands.
/// </summary>
public interface IHidBatteryDriver
{
    string Name { get; }

    /// <summary>True for the receiver VID/PIDs this driver understands.</summary>
    bool Supports(ushort vendorId, ushort productId);

    /// <summary>
    /// Reads the mouse/keyboard behind one receiver. <paramref name="collections"/>
    /// are all HID collections of that receiver. Returns null when the receiver
    /// does not answer at all; returns a status with IsConnected=false when the
    /// receiver answers but the device is off.
    /// </summary>
    DeviceStatus? Read(IReadOnlyList<HidInterface> collections, CancellationToken cancellationToken);
}
