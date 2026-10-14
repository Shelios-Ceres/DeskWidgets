using DeskWidgets.Core.Devices;
using Windows.System.Power;

namespace DeskWidgets.Devices;

/// <summary>
/// The computer's own battery (laptops). Event-driven through
/// <see cref="PowerManager"/>; the poll interval is only a safety net.
/// Desktops without a battery report nothing.
/// </summary>
public sealed class SystemBatterySource : IDeviceSource
{
    private readonly EventHandler<object> _onPowerChanged;

    public SystemBatterySource()
    {
        _onPowerChanged = (_, _) => Invalidated?.Invoke(this, EventArgs.Empty);
        PowerManager.RemainingChargePercentChanged += _onPowerChanged;
        PowerManager.BatteryStatusChanged += _onPowerChanged;
        PowerManager.PowerSupplyStatusChanged += _onPowerChanged;
        PowerManager.EnergySaverStatusChanged += _onPowerChanged;
    }

    public string Name => "System";

    public TimeSpan PollInterval => TimeSpan.FromMinutes(1);

    public event EventHandler? Invalidated;

    public Task<IReadOnlyList<DeviceStatus>> ScanAsync(CancellationToken cancellationToken)
    {
        if (PowerManager.BatteryStatus == BatteryStatus.NotPresent)
            return Task.FromResult<IReadOnlyList<DeviceStatus>>([]);

        var host = new DeviceStatus
        {
            Id = "host",
            Name = Environment.MachineName,
            Kind = DeviceKind.Laptop,
            Connection = ConnectionKind.Internal,
            IsHost = true,
            BatteryPercent = Math.Clamp(PowerManager.RemainingChargePercent, 0, 100),
            // iOS shows the bolt whenever the phone is on power, even when full.
            IsCharging = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent,
            IsPowerSaving = PowerManager.EnergySaverStatus == EnergySaverStatus.On,
            Source = Name,
        };
        return Task.FromResult<IReadOnlyList<DeviceStatus>>([host]);
    }

    public void Dispose()
    {
        PowerManager.RemainingChargePercentChanged -= _onPowerChanged;
        PowerManager.BatteryStatusChanged -= _onPowerChanged;
        PowerManager.PowerSupplyStatusChanged -= _onPowerChanged;
        PowerManager.EnergySaverStatusChanged -= _onPowerChanged;
    }
}
