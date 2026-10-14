using DeskWidgets.Core.Devices;
using DeskWidgets.Core.Diagnostics;
using Windows.Devices.Enumeration;

namespace DeskWidgets.Devices.Bluetooth;

/// <summary>
/// Paired Bluetooth devices and their battery level, read from the device
/// property Windows itself fills in for the "Bluetooth &amp; devices" page
/// (hands-free battery indicators on classic devices, the GATT battery
/// service on LE devices). Nothing is sent to the devices by this source.
/// </summary>
public sealed class BluetoothBatterySource : IDeviceSource
{
    // DEVPKEY_Bluetooth_Battery {104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2 — a byte, 0-100.
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    private const string ContainerIdKey = "System.Devices.Aep.ContainerId";
    private const string ClassGuidKey = "System.Devices.ClassGuid";

    private const string ClassicProtocolId = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string LowEnergyProtocolId = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";

    private static readonly string PairedDevicesAqs =
        $"(System.Devices.Aep.ProtocolId:=\"{ClassicProtocolId}\" OR System.Devices.Aep.ProtocolId:=\"{LowEnergyProtocolId}\")" +
        " AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";

    private static readonly string[] EndpointProperties = [IsConnectedKey, ContainerIdKey];
    private static readonly string[] NodeProperties = [BatteryKey, ClassGuidKey];

    private static readonly Guid MouseClass = new("4D36E96F-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid KeyboardClass = new("4D36E96B-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid AudioEndpointClass = new("C166523C-FE0C-4A94-A586-F1A80CFBBF3E");
    private static readonly Guid MediaClass = new("4D36E96C-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid XboxPeripheralClass = new("D61CA365-5AF4-4486-998B-9DB4734C6CA3");

    private readonly DeviceWatcher? _watcher;

    public BluetoothBatterySource()
    {
        // Connect/disconnect shows up as an endpoint property update; rescan
        // right away instead of waiting for the next poll.
        try
        {
            _watcher = DeviceInformation.CreateWatcher(PairedDevicesAqs, EndpointProperties, DeviceInformationKind.AssociationEndpoint);
            _watcher.Added += (_, _) => RaiseInvalidated();
            _watcher.Updated += (_, _) => RaiseInvalidated();
            _watcher.Removed += (_, _) => RaiseInvalidated();
            _watcher.Start();
        }
        catch (Exception ex)
        {
            Log.Warn("Bluetooth watcher unavailable, polling only", ex);
            _watcher = null;
        }
    }

    public string Name => "Bluetooth";

    public TimeSpan PollInterval => TimeSpan.FromSeconds(60);

    public event EventHandler? Invalidated;

    private void RaiseInvalidated() => Invalidated?.Invoke(this, EventArgs.Empty);

    public async Task<IReadOnlyList<DeviceStatus>> ScanAsync(CancellationToken cancellationToken)
    {
        var endpoints = await DeviceInformation
            .FindAllAsync(PairedDevicesAqs, EndpointProperties, DeviceInformationKind.AssociationEndpoint)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<DeviceStatus>();
        var byContainer = new Dictionary<Guid, int>();
        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool connected = endpoint.Properties.TryGetValue(IsConnectedKey, out var c) && c is true;
            Guid container = endpoint.Properties.TryGetValue(ContainerIdKey, out var g) && g is Guid guid ? guid : Guid.Empty;
            bool lowEnergy = endpoint.Id.StartsWith("BluetoothLE#", StringComparison.OrdinalIgnoreCase);

            (int? battery, DeviceKind kind) = container == Guid.Empty
                ? ((int?)null, DeviceKind.Unknown)
                : await ReadContainerAsync(container, cancellationToken).ConfigureAwait(false);
            if (kind == DeviceKind.Unknown)
                kind = GuessKindFromName(endpoint.Name);

            var status = new DeviceStatus
            {
                Id = container == Guid.Empty ? "bt:" + endpoint.Id : "bt:" + container.ToString("N"),
                Name = string.IsNullOrWhiteSpace(endpoint.Name) ? "Bluetooth" : endpoint.Name,
                Kind = kind,
                Connection = lowEnergy ? ConnectionKind.BluetoothLE : ConnectionKind.Bluetooth,
                IsConnected = connected,
                // Windows keeps the last level after a disconnect; only trust it while connected.
                BatteryPercent = connected ? battery : null,
                Source = Name,
            };

            // Dual-mode devices are paired once per transport but share one container.
            if (container != Guid.Empty && byContainer.TryGetValue(container, out int index))
            {
                if (Score(status) > Score(result[index]))
                    result[index] = status;
                continue;
            }
            if (container != Guid.Empty)
                byContainer[container] = result.Count;
            result.Add(status);
        }
        return result;
    }

    private static int Score(DeviceStatus s) => (s.IsConnected ? 2 : 0) + (s.BatteryPercent is null ? 0 : 1);

    /// <summary>Reads the battery level and device class from the PnP nodes of one physical device.</summary>
    internal static async Task<(int? Battery, DeviceKind Kind)> ReadContainerAsync(Guid container, CancellationToken cancellationToken)
    {
        string aqs = $"System.Devices.ContainerId:=\"{{{container}}}\"";
        var nodes = await DeviceInformation
            .FindAllAsync(aqs, NodeProperties, DeviceInformationKind.Device)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        int? battery = null;
        var kind = DeviceKind.Unknown;
        foreach (var node in nodes)
        {
            if (node.Properties.TryGetValue(BatteryKey, out var value) && ToPercent(value) is int level)
                battery = Math.Max(battery ?? 0, level);
            if (node.Properties.TryGetValue(ClassGuidKey, out var cls) && cls is Guid classGuid)
            {
                var candidate = KindFromClass(classGuid);
                if (Priority(candidate) > Priority(kind))
                    kind = candidate;
            }
        }
        return (battery, kind);
    }

    private static int? ToPercent(object? value) => value switch
    {
        byte b => Math.Min((int)b, 100),
        ushort u => Math.Min((int)u, 100),
        uint u => (int)Math.Min(u, 100u),
        int i when i >= 0 => Math.Min(i, 100),
        _ => null,
    };

    private static DeviceKind KindFromClass(Guid classGuid)
    {
        if (classGuid == MouseClass) return DeviceKind.Mouse;
        if (classGuid == KeyboardClass) return DeviceKind.Keyboard;
        if (classGuid == XboxPeripheralClass) return DeviceKind.GameController;
        if (classGuid == AudioEndpointClass || classGuid == MediaClass) return DeviceKind.Headphones;
        return DeviceKind.Unknown;
    }

    // An input class beats the audio class: a headset may expose media keys, never a pointer.
    private static int Priority(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse or DeviceKind.Keyboard or DeviceKind.GameController => 2,
        DeviceKind.Headphones => 1,
        _ => 0,
    };

    private static DeviceKind GuessKindFromName(string? name)
    {
        string n = (name ?? string.Empty).ToLowerInvariant();
        if (ContainsAny(n, "mouse", "鼠标", "mx master", "mx anywhere")) return DeviceKind.Mouse;
        if (ContainsAny(n, "keyboard", "键盘", "keys")) return DeviceKind.Keyboard;
        if (ContainsAny(n, "buds", "airpods", "earbud", "freebuds", "earphone")) return DeviceKind.Earbuds;
        if (ContainsAny(n, "headset")) return DeviceKind.Headset;
        if (ContainsAny(n, "headphone", "耳机", "wh-1000", "quietcomfort")) return DeviceKind.Headphones;
        if (ContainsAny(n, "controller", "gamepad", "手柄", "xbox", "dualsense", "dualshock", "pro controller")) return DeviceKind.GameController;
        if (ContainsAny(n, "speaker", "音箱", "soundlink", "jbl")) return DeviceKind.Speaker;
        if (ContainsAny(n, "pencil", "stylus", "触控笔")) return DeviceKind.Pen;
        if (ContainsAny(n, "watch", "手表", "手环")) return DeviceKind.Watch;
        if (ContainsAny(n, "phone", "手机", "iphone", "galaxy")) return DeviceKind.Phone;
        return DeviceKind.Unknown;
    }

    private static bool ContainsAny(string text, params string[] needles) => needles.Any(text.Contains);

    public void Dispose()
    {
        if (_watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted })
            _watcher.Stop();
    }
}
