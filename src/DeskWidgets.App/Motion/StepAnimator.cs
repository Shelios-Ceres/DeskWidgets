namespace DeskWidgets.Motion;

/// <summary>
/// The Pixel theme's stepped animator: whole frames at a set frame time, no tweening. It is only state: the ticks come from the shared
/// <see cref="FrameClock"/>, so an animator costs no timer of its own and nothing runs after the last frame. It is cut short by
/// <see cref="Cancel"/> or by the next <see cref="Start"/>. Frame 0 is shown at once.
/// </summary>
internal sealed class StepAnimator : IDisposable
{
    /// <summary>The Pixel tick (MOTION.md: T = 40 ms).</summary>
    public const int FrameMs = 40;

    private FrameClock.Ticket? _ticket;
    private Action<int>? _onFrame;
    private Action? _onDone;
    private int _frame, _frames;

    public bool Running => _ticket is { Active: true };

    /// <summary>The frame shown last (0 until the first tick).</summary>
    public int Frame => _frame;

    /// <summary>
    /// Shows frame 0 now, then frames 1 to <paramref name="frames"/> − 1 every <paramref name="frameMs"/>; <paramref name="onDone"/> runs
    /// after the last one. With motion off, only the last frame is shown, at once.
    /// </summary>
    public void Start(int frames, Action<int> onFrame, Action? onDone = null, int frameMs = FrameMs)
    {
        Cancel();
        _frames = Math.Max(1, frames);
        _onFrame = onFrame;
        _onDone = onDone;
        if (!MotionSettings.Enabled || _frames == 1)
        {
            _frame = _frames - 1;
            onFrame(_frame);
            Finish();
            return;
        }
        _frame = 0;
        onFrame(0);
        if (_onFrame is null)
            return; // the owner went away in that frame
        _ticket = FrameClock.Every(frameMs, _ => OnTick());
    }

    /// <summary>Stops where it is, without the completion call: a new state cut the animation short.</summary>
    public void Cancel()
    {
        _ticket?.Stop();
        _ticket = null;
    }

    private void OnTick()
    {
        _frame++;
        _onFrame?.Invoke(_frame);
        if (_frame >= _frames - 1)
            Finish();
    }

    private void Finish()
    {
        Cancel();
        var done = _onDone;
        _onDone = null;
        done?.Invoke();
    }

    public void Dispose()
    {
        Cancel();
        _onFrame = null;
        _onDone = null;
    }
}
