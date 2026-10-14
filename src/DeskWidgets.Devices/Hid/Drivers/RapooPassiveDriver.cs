using DeskWidgets.Core.Devices;
using DeskWidgets.Core.Diagnostics;

namespace DeskWidgets.Devices.Hid.Drivers;

/// <summary>
/// Rapoo 2.4 GHz receivers managed by Rapoo's "A HUB" driver (VT series).
/// Purely passive: while the mouse is active the receiver pushes a status
/// input report every few seconds; this driver only listens on the vendor
/// collection with a read-only handle and never sends anything, so it cannot
/// change settings and coexists with A HUB.
///
///   07 40 [2]=DPI gear [3..4]=DPI X (LE) [5..6]=DPI Y [7]=battery state [8]=battery % … [18]=signal
///
/// Offsets come from A HUB's RapooDevice.dll (its report-7 handler reads the
/// battery state at [7] and the percentage at [8]) and from a passive capture
/// of a VT3 MAX MASTER V2 whose DPI fields matched A HUB's saved profile and
/// whose level (57%) matched what A HUB displayed; plugging the cable in
/// produced the charging state.
/// </summary>
public sealed class RapooPassiveDriver : IHidBatteryDriver, INotifyingHidDriver, IDisposable
{
    private const ushort VendorId = 0x24AE;
    private const byte StatusReportId = 0x07;
    private const byte StatusPush = 0x40;
    private const byte BatteryReply = 0x01;

    /// <summary>No report for this long (mouse off or asleep): keep the device but stop showing a level.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    private static readonly Dictionary<ushort, string> Models = new()
    {
        [0x146D] = "VT3 MAX MASTER V2",
    };

    private readonly object _gate = new();
    private ReportListener? _listener;
    private DeviceStatus? _latest;
    private DateTime _latestUtc;

    public string Name => "Rapoo";

    public event EventHandler? Changed;

    public bool Supports(ushort vendorId, ushort productId)
        => vendorId == VendorId && Models.ContainsKey(productId);

    public DeviceStatus? Read(IReadOnlyList<HidInterface> collections, CancellationToken cancellationToken)
    {
        var statusCollection = collections.FirstOrDefault(c => c.UsagePage == 0xFF00 && c.Usage == 0x02 && c.InputReportLength >= 9);
        if (statusCollection is null)
            return null;
        EnsureListening(statusCollection);

        var receiver = collections[0];
        lock (_gate)
        {
            if (_latest is null)
            {
                // Receiver present, mouse has not reported yet (off, asleep or just started).
                return new DeviceStatus
                {
                    Id = DeviceId(receiver),
                    Name = Models[receiver.ProductId],
                    Kind = DeviceKind.Mouse,
                    Connection = ConnectionKind.Wireless24G,
                    IsConnected = false,
                    Source = Name,
                };
            }
            return DateTime.UtcNow - _latestUtc < StaleAfter
                ? _latest
                : _latest with { IsConnected = false, BatteryPercent = null, IsCharging = false };
        }
    }

    private static string DeviceId(HidInterface receiver) => $"hid:{receiver.VendorId:X4}:{receiver.ProductId:X4}";

    private void EnsureListening(HidInterface collection)
    {
        lock (_gate)
        {
            if (_listener is { IsRunning: true } && _listener.Path == collection.Path)
                return;
            _listener?.Dispose();
            _listener = ReportListener.Start(collection, report => OnReport(collection, report));
            if (_listener is null)
                Log.Warn("Rapoo: cannot open the status collection for reading");
        }
    }

    private void OnReport(HidInterface collection, byte[] report)
    {
        if (report.Length < 9 || report[0] != StatusReportId || report[1] is not (StatusPush or BatteryReply) || report[8] > 100)
            return;

        int dpi = report[1] == StatusPush ? report[3] | (report[4] << 8) : 0;
        var status = new DeviceStatus
        {
            Id = DeviceId(collection),
            Name = Models[collection.ProductId],
            Kind = DeviceKind.Mouse,
            Connection = ConnectionKind.Wireless24G,
            IsConnected = true,
            BatteryPercent = report[8],
            // 1 = running on battery (what the driver also reports for BLE); 2/3 = on the cable.
            IsCharging = report[7] is 2 or 3,
            Detail = dpi > 0 ? $"{dpi} DPI" : null,
            Source = Name,
        };

        bool changed;
        lock (_gate)
        {
            changed = _latest != status || DateTime.UtcNow - _latestUtc >= StaleAfter;
            _latest = status;
            _latestUtc = DateTime.UtcNow;
        }
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _listener?.Dispose();
            _listener = null;
        }
    }

    /// <summary>Reads input reports from one collection on a background task until disposed or unplugged.</summary>
    private sealed class ReportListener : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly FileStream _stream;
        private readonly Task _loop;

        private ReportListener(FileStream stream, string path, int reportLength, Action<byte[]> onReport)
        {
            _stream = stream;
            Path = path;
            _loop = Task.Run(() => LoopAsync(reportLength, onReport));
        }

        public string Path { get; }

        public bool IsRunning => !_loop.IsCompleted;

        public static ReportListener? Start(HidInterface collection, Action<byte[]> onReport)
        {
            var handle = HidNative.OpenReadOnlyOverlapped(collection.Path);
            if (handle is null)
                return null;
            var stream = new FileStream(handle, FileAccess.Read, 0, isAsync: true);
            return new ReportListener(stream, collection.Path, collection.InputReportLength, onReport);
        }

        private async Task LoopAsync(int reportLength, Action<byte[]> onReport)
        {
            var buffer = new byte[reportLength];
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    int read = await _stream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                    if (read <= 0)
                        return;
                    onReport(buffer[..read]);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
                // Receiver unplugged; the next scan starts a new listener if it comes back.
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _stream.Dispose();
        }
    }
}
