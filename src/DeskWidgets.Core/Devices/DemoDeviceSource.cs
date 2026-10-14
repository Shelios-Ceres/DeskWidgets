namespace DeskWidgets.Core.Devices;

/// <summary>
/// Fixed sample devices for previewing layouts (start the app with --demo),
/// named in the UI language.
/// </summary>
public sealed class DemoDeviceSource : IDeviceSource
{
    private readonly Func<bool> _chinese;

    /// <param name="chinese">Whether the UI is currently Chinese; asked on every scan.</param>
    public DemoDeviceSource(Func<bool> chinese) => _chinese = chinese;

    public string Name => "Demo";

    public TimeSpan PollInterval => TimeSpan.FromMinutes(5);

    public event EventHandler? Invalidated
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<DeviceStatus>> ScanAsync(CancellationToken cancellationToken)
    {
        bool zh = _chinese();
        IReadOnlyList<DeviceStatus> devices =
        [
            new() { Id = "demo:mouse", Name = zh ? "无线鼠标" : "Wireless Mouse", Kind = DeviceKind.Mouse, Connection = ConnectionKind.Wireless24G, BatteryPercent = 76, Detail = "8000 Hz", Source = Name },
            new() { Id = "demo:keyboard", Name = zh ? "蓝牙键盘" : "Bluetooth Keyboard", Kind = DeviceKind.Keyboard, Connection = ConnectionKind.Bluetooth, BatteryPercent = 45, IsCharging = true, Source = Name },
            new() { Id = "demo:headphones", Name = zh ? "头戴式耳机" : "Headphones", Kind = DeviceKind.Headphones, Connection = ConnectionKind.Bluetooth, BatteryPercent = 18, Source = Name },
            new() { Id = "demo:controller", Name = zh ? "游戏手柄" : "Game Controller", Kind = DeviceKind.GameController, Connection = ConnectionKind.Bluetooth, BatteryPercent = 63, IsConnected = false, Source = Name },
        ];
        return Task.FromResult(devices);
    }

    public void Dispose()
    {
    }
}
