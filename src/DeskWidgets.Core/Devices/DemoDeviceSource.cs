namespace DeskWidgets.Core.Devices;

/// <summary>
/// Fixed sample devices for previewing layouts (start the app with --demo),
/// named in the UI language.
/// </summary>
public sealed class DemoDeviceSource : IDeviceSource
{
    private readonly Func<bool> _chinese;

    private readonly int _count;

    /// <param name="chinese">Whether the UI is currently Chinese; asked on every scan.</param>
    /// <param name="count">How many of the seven sample devices (the controller is disconnected, so Glass lists six at most of seven) to show (design QA: <c>--demo-devices=N</c>).</param>
    public DemoDeviceSource(Func<bool> chinese, int count = 4)
    {
        _chinese = chinese;
        _count = Math.Clamp(count, 1, 7);
    }

    /// <summary>Design QA (<c>--motion-qa</c>): changes what the sample devices report, so that a level change or charging can be shown.</summary>
    public static Func<DeviceStatus, DeviceStatus>? Tweak { get; set; }

    /// <summary>Design QA (<c>--demo-wiggle</c>): the mouse's level steps down and the headphones start and stop charging every few seconds, so a trial can see the battery's motion.</summary>
    public static bool Wiggle { get; set; }

    public string Name => "Demo";

    public TimeSpan PollInterval => Wiggle ? TimeSpan.FromSeconds(6) : TimeSpan.FromMinutes(5);

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
            new() { Id = "demo:earbuds", Name = zh ? "无线耳塞" : "Earbuds", Kind = DeviceKind.Earbuds, Connection = ConnectionKind.Bluetooth, BatteryPercent = 92, Source = Name },
            new() { Id = "demo:speaker", Name = zh ? "蓝牙音箱" : "Bluetooth Speaker", Kind = DeviceKind.Speaker, Connection = ConnectionKind.Bluetooth, BatteryPercent = 34, Source = Name },
            new() { Id = "demo:phone", Name = zh ? "手机" : "Phone", Kind = DeviceKind.Phone, Connection = ConnectionKind.Bluetooth, BatteryPercent = 71, Source = Name },
        ];
        if (Wiggle)
        {
            long step = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond / 6;
            devices = [.. devices.Select(d => d.Id switch
            {
                "demo:mouse" => d with { BatteryPercent = 90 - (int)(step % 6) * 14 },
                "demo:headphones" => d with { IsCharging = step % 2 == 0 },
                _ => d,
            })];
        }
        devices = [.. devices.Take(_count).Select(d => Tweak?.Invoke(d) ?? d)];
        return Task.FromResult(devices);
    }

    public void Dispose()
    {
    }
}
