using DeskWidgets.Core.Devices;

namespace DeskWidgets.Tests;

public class DeviceHubTests
{
    [Fact]
    public async Task MergesEverySource_AndRaisesDevicesChangedOnlyForChanges()
    {
        var mouse = new FakeSource("mouse") { Result = [Device("mouse", "50%")] };
        var keyboard = new FakeSource("keyboard") { Result = [Device("keyboard", "80%")] };
        using var hub = new DeviceHub([mouse, keyboard]);
        int changes = 0;
        hub.DevicesChanged += (_, _) => Interlocked.Increment(ref changes);

        hub.RefreshAll();
        await WaitUntil(() => Volatile.Read(ref changes) == 2);
        Assert.Equal(new[] { "mouse", "keyboard" }, hub.Devices.Select(d => d.Id));

        hub.RefreshAll(); // the same devices again
        await WaitUntil(() => mouse.Scans == 2 && keyboard.Scans == 2);
        await Task.Delay(100);
        Assert.Equal(2, Volatile.Read(ref changes));
    }

    [Fact]
    public async Task ARequestThatArrivesDuringAScan_GetsAScanOfItsOwn()
    {
        var firstScanMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int version = 1;
        var source = new FakeSource("mouse")
        {
            OnScan = async () =>
            {
                int seen = Volatile.Read(ref version);
                await firstScanMayFinish.Task;
                return [Device("mouse", $"v{seen}")];
            },
        };
        using var hub = new DeviceHub([source]);

        hub.RefreshAll();
        await WaitUntil(() => source.Scans == 1); // the first scan has read version 1 and waits
        Volatile.Write(ref version, 2);
        hub.RefreshAll();
        firstScanMayFinish.SetResult();

        await WaitUntil(() => hub.Devices.SingleOrDefault()?.Detail == "v2");
    }

    [Fact]
    public async Task ScansOfOneSourceNeverOverlap_AndTheLastRequestAlwaysLands()
    {
        int version = 0, running = 0, overlaps = 0;
        var source = new FakeSource("mouse")
        {
            OnScan = async () =>
            {
                if (Interlocked.Increment(ref running) > 1)
                    Interlocked.Increment(ref overlaps);
                int seen = Volatile.Read(ref version);
                await Task.Yield();
                Interlocked.Decrement(ref running);
                return [Device("mouse", $"v{seen}")];
            },
        };
        using var hub = new DeviceHub([source]);

        for (int i = 1; i <= 500; i++)
        {
            Volatile.Write(ref version, i);
            hub.RefreshAll();
            if (i % 25 == 0)
                await Task.Delay(1); // let a scan finish now and then, so requests also land on finishing scans
        }

        await WaitUntil(() => hub.Devices.SingleOrDefault()?.Detail == "v500");
        Assert.Equal(0, Volatile.Read(ref overlaps));
    }

    private static DeviceStatus Device(string id, string detail) => new() { Id = id, Name = id, BatteryPercent = 50, Detail = detail };

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not met within 10 s.");
            await Task.Delay(5);
        }
    }

    private sealed class FakeSource(string name) : IDeviceSource
    {
        private int _scans;

        public IReadOnlyList<DeviceStatus> Result { get; init; } = [];

        public Func<Task<IReadOnlyList<DeviceStatus>>>? OnScan { get; init; }

        /// <summary>Scans started so far. Counted after the scan has begun, so it has read its inputs.</summary>
        public int Scans => Volatile.Read(ref _scans);

        public string Name => name;

        public TimeSpan PollInterval => TimeSpan.FromHours(1);

        public event EventHandler? Invalidated
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<DeviceStatus>> ScanAsync(CancellationToken cancellationToken)
        {
            var scan = OnScan?.Invoke() ?? Task.FromResult(Result);
            Interlocked.Increment(ref _scans);
            return scan;
        }

        public void Dispose()
        {
        }
    }
}
