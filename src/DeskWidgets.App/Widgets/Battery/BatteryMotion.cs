using DeskWidgets.Core.Devices;
using DeskWidgets.Motion;

namespace DeskWidgets.Widgets.Battery;

/// <summary>
/// The Pixel battery widget's motion (Phase 12, B6): a gauge's fill STEPs to a new level in whole pixels (at most 6 frames, the step being
/// ceil(distance / frames)), the number and the level colour change on its last frame, and the bolt POPs in over 2 frames when charging
/// starts (the small bolt, then the outlined full one); when charging stops it is gone at once. It holds the state the scene needs while
/// it runs and one <see cref="StepAnimator"/>; nothing runs at rest. A new change cuts a running one short: the gauges start from the fill on screen.
/// </summary>
internal sealed class BatteryMotion : IDisposable
{
    private const int MaxFrames = 6;
    private const int NominalGaugeWidth = 25; // art pixels inside a typical gauge: how many frames a change of some percent needs

    private sealed class Run
    {
        public required DeviceStatus From { get; init; }
        public double FromFraction { get; init; }
        public int Frames { get; init; }
        public bool Pop { get; init; }
    }

    private readonly StepAnimator _steps = new();
    private readonly Dictionary<string, DeviceStatus> _last = [];
    private readonly Dictionary<string, double> _shown = []; // the fill (0 to 1) on screen for each gauge
    private Dictionary<string, Run> _runs = [];
    private Action? _repaint;

    /// <summary>The frame of the running motion (1 to its last), 0 at rest.</summary>
    public int Frame { get; private set; }

    /// <summary>
    /// Looks at the devices as they are now. If a level changed or charging started on a device that was shown before, starts the motion
    /// (<paramref name="repaint"/> paints each frame) and returns true.
    /// </summary>
    public bool Observe(IReadOnlyList<DeviceStatus> devices, Action repaint)
    {
        var runs = new Dictionary<string, Run>();
        int frames = 0;
        foreach (var device in devices)
        {
            if (_last.TryGetValue(device.Id, out var previous))
            {
                bool levelChanged = Known(previous) && Known(device) && previous.BatteryPercent != device.BatteryPercent;
                bool popNow = Known(device) && device.IsCharging && !(Known(previous) && previous.IsCharging);
                if (levelChanged || popNow)
                {
                    var from = _runs.TryGetValue(device.Id, out var running) && Frame > 0 ? running.From : previous;
                    int distance = levelChanged ? Math.Abs(device.BatteryPercent!.Value - from.BatteryPercent.GetValueOrDefault()) : 0;
                    int own = levelChanged ? FramesFor(distance) : 1;
                    runs[device.Id] = new Run
                    {
                        From = from,
                        FromFraction = _shown.TryGetValue(device.Id, out var fraction) ? fraction : from.BatteryPercent.GetValueOrDefault() / 100.0,
                        Frames = own,
                        Pop = popNow,
                    };
                    frames = Math.Max(frames, popNow ? Math.Max(own, 2) : own);
                }
            }
            _last[device.Id] = device;
        }
        var present = devices.Select(d => d.Id).ToHashSet();
        foreach (string id in _last.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _last.Remove(id);
            _shown.Remove(id);
        }
        if (runs.Count == 0)
            return false;
        _runs = runs;
        _repaint = repaint;
        Frame = 1;
        _steps.Start(frames, frame =>
        {
            Frame = frame + 1;
                _repaint?.Invoke();
        }, End);
        return true;
    }

    private void End()
    {
        Frame = 0;
        _runs = [];
        _repaint?.Invoke(); // the settled picture
    }

    /// <summary>The device as it is drawn now: its number and colour stay the old ones until the gauge's last frame.</summary>
    public DeviceStatus Display(DeviceStatus device)
        => Frame > 0 && _runs.TryGetValue(device.Id, out var run) && Frame < run.Frames ? device with { BatteryPercent = run.From.BatteryPercent, IsPowerSaving = run.From.IsPowerSaving } : device;

    /// <summary>
    /// The width in pixels of a gauge's fill this frame: the old fill moving to <paramref name="finalPx"/> in whole steps of
    /// ceil(distance / frames). At rest it is <paramref name="finalPx"/>. It also remembers what is on screen.
    /// </summary>
    public int FillPx(string id, int innerWidth, int finalPx)
    {
        int px = finalPx;
        if (Frame > 0 && _runs.TryGetValue(id, out var run) && Frame < run.Frames && innerWidth > 0)
        {
            // The status being drawn still shows the old number (see Display); the fill goes to the real new level.
            int real = _last.TryGetValue(id, out var now) && now.BatteryPercent is { } percent
                ? (percent == 0 ? 0 : Math.Max(1, (int)Math.Round(innerWidth * Math.Clamp(percent, 0, 100) / 100.0)))
                : finalPx;
            int from = (int)Math.Round(run.FromFraction * innerWidth);
            px = StepTo(from, real, run.Frames, Frame);
        }
        if (innerWidth > 0)
            _shown[id] = px / (double)innerWidth;
        return px;
    }

    /// <summary>How many frames a change of <paramref name="percent"/> points takes: its distance in pixels of a typical gauge, at most 6.</summary>
    internal static int FramesFor(int percent) => Math.Clamp((int)Math.Ceiling(Math.Abs(percent) * NominalGaugeWidth / 100.0), 1, MaxFrames);

    /// <summary>STEP: the fill at <paramref name="frame"/> (1 to <paramref name="frames"/>) moving from <paramref name="from"/> to <paramref name="to"/> in whole steps of ceil(distance / frames); the last frame is <paramref name="to"/>.</summary>
    internal static int StepTo(int from, int to, int frames, int frame)
    {
        int distance = Math.Abs(to - from);
        int step = (int)Math.Ceiling(distance / (double)Math.Max(1, frames));
        return frame >= frames ? to : from + Math.Sign(to - from) * Math.Min(distance, step * frame);
    }

    /// <summary>1 while the small bolt is shown (the first POP frame), 0 for the finished bolt.</summary>
    public int BoltFrame(string id) => Frame == 1 && _runs.TryGetValue(id, out var run) && run.Pop ? 1 : 0;

    private static bool Known(DeviceStatus device) => device.IsConnected && device.BatteryPercent is not null;

    public void Dispose() => _steps.Dispose();
}
