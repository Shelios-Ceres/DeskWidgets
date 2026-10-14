namespace DeskWidgets.Devices.Hid;

/// <summary>
/// Optional for <see cref="IHidBatteryDriver"/>: drivers that learn about
/// changes on their own (e.g. by listening to reports the device pushes)
/// raise <see cref="Changed"/> so the source rescans right away instead of at
/// the next poll. May be raised on any thread.
/// </summary>
public interface INotifyingHidDriver
{
    event EventHandler? Changed;
}
