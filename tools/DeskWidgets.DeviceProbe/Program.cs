using System.Text;
using DeskWidgets.Core.Devices;
using DeskWidgets.Devices;
using DeskWidgets.Devices.Bluetooth;
using DeskWidgets.Devices.Hid;

// Diagnostics for the device layer, without the UI.
//   DeskWidgets.DeviceProbe            HID inventory + one scan of every source
//   DeskWidgets.DeviceProbe --hid-all  also list every collection of every HID device
// The inventory opens HID devices query-only; only receivers with a driver
// (marked "->") receive the driver's verified read-only frames during the scan.

Console.OutputEncoding = Encoding.UTF8;
bool listCollections = args.Contains("--hid-all", StringComparer.OrdinalIgnoreCase);

if (args.Contains("--listen", StringComparer.OrdinalIgnoreCase))
{
    // Passive sniffing: every open handle gets its own copy of each input report,
    // so reading the vendor collections shows the replies another app (the vendor
    // driver) receives. Handles are read-only; nothing is sent to the device.
    //   --listen [seconds] --vid 24AE --pid 146D
    int seconds = ArgAfter("--listen") is { } s && int.TryParse(s, out int parsed) ? parsed : 60;
    ushort vid = Convert.ToUInt16(ArgAfter("--vid") ?? "24AE", 16);
    ushort pid = Convert.ToUInt16(ArgAfter("--pid") ?? "146D", 16);
    var collections = HidInventory.Enumerate((v, p) => v == vid && p == pid)
        .Where(c => c.UsagePage >= 0xFF00 && c.InputReportLength > 0)
        .ToList();
    Console.WriteLine($"listening {seconds}s on {collections.Count} vendor collection(s) of {vid:X4}:{pid:X4}");
    var gate = new object();
    foreach (var collection in collections)
    {
        var handle = HidNative.OpenReadOnly(collection.Path);
        if (handle is null)
        {
            Console.WriteLine($"  UP=0x{collection.UsagePage:X4} U=0x{collection.Usage:X2}: cannot open for reading");
            continue;
        }
        string tag = $"UP{collection.UsagePage:X4}/U{collection.Usage:X2}";
        Console.WriteLine($"  {tag} in={collection.InputReportLength} opened");
        new Thread(() =>
        {
            using var stream = new FileStream(handle, FileAccess.Read, 0, false);
            var buffer = new byte[collection.InputReportLength];
            while (true)
            {
                int n;
                try { n = stream.Read(buffer, 0, buffer.Length); }
                catch (IOException) { return; }
                if (n <= 0)
                    return;
                lock (gate)
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {tag} {Convert.ToHexString(buffer, 0, n)}");
            }
        }) { IsBackground = true }.Start();
    }
    Thread.Sleep(TimeSpan.FromSeconds(seconds));
    Console.WriteLine("done");
    return;
}

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    // Runs the Bluetooth container query against any present device so its
    // AQS and property keys are validated even with nothing paired.
    var nodes = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
        "System.Devices.Present:=System.StructuredQueryType.Boolean#True",
        ["System.Devices.ContainerId"],
        Windows.Devices.Enumeration.DeviceInformationKind.Device);
    var sample = nodes.FirstOrDefault(n => n.Properties.TryGetValue("System.Devices.ContainerId", out var v) && v is Guid g && g != Guid.Empty);
    if (sample is null)
    {
        Console.WriteLine("self-test: no devnode with a container id");
        return;
    }
    var container = (Guid)sample.Properties["System.Devices.ContainerId"];
    var (battery, kind) = await BluetoothBatterySource.ReadContainerAsync(container, CancellationToken.None);
    Console.WriteLine($"self-test: container query OK for '{sample.Name}' {{{container}}} -> battery={battery?.ToString() ?? "-"} kind={kind}");
    return;
}

var drivers = HidDongleSource.DefaultDrivers();
Console.WriteLine("=== HID devices ===");
var devices = HidInventory.Enumerate()
    .GroupBy(c => (c.VendorId, c.ProductId))
    .OrderBy(g => g.Key.VendorId)
    .ThenBy(g => g.Key.ProductId);
foreach (var device in devices)
{
    var first = device.First();
    var driver = drivers.FirstOrDefault(d => d.Supports(device.Key.VendorId, device.Key.ProductId));
    Console.WriteLine($"{device.Key.VendorId:X4}:{device.Key.ProductId:X4}  {first.Manufacturer} / {first.Product}  ({device.Count()} collections){(driver is null ? "" : $"  -> {driver.Name}")}");
    if (listCollections)
    {
        foreach (var c in device)
            Console.WriteLine($"    UsagePage=0x{c.UsagePage:X4} Usage=0x{c.Usage:X2} in={c.InputReportLength} out={c.OutputReportLength} feature={c.FeatureReportLength}");
    }
}

IDeviceSource[] sources = [new SystemBatterySource(), new HidDongleSource(), new BluetoothBatterySource()];
foreach (var source in sources)
{
    Console.WriteLine();
    try
    {
        var found = await source.ScanAsync(CancellationToken.None);
        if (found.Any(d => !d.IsConnected) && source is HidDongleSource)
        {
            // Passive drivers only know the level once the device pushes a report: give it a moment.
            var pushed = new TaskCompletionSource();
            source.Invalidated += (_, _) => pushed.TrySetResult();
            await Task.WhenAny(pushed.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            found = await source.ScanAsync(CancellationToken.None);
        }
        Console.WriteLine($"=== {source.Name}: {found.Count} device(s) ===");
        foreach (var d in found)
        {
            string battery = d.BatteryPercent is int p ? $"{p}%" : "-";
            Console.WriteLine($"  {d.Name} | {d.Kind} | {d.Connection} | connected={d.IsConnected} | battery={battery} | charging={d.IsCharging}{(d.Detail is null ? "" : " | " + d.Detail)}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"=== {source.Name}: FAILED {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
        source.Dispose();
    }
}

string? ArgAfter(string name)
{
    int index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--") ? args[index + 1] : null;
}
