using System.Runtime.InteropServices;

namespace DeskWidgets.Motion;

/// <summary>
/// Fades a whole top-level window with layered-window alpha: DWM blends the frost and the XAML together, so they stay in step
/// (the Phase 12 spike measured it within 3 %), and each tick is one cheap call. It ticks on the shared <see cref="FrameClock"/> only
/// while a fade runs. A new fade starts from the alpha the last one reached.
/// </summary>
internal sealed class WindowFader : IDisposable
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x80000, WsExTransparent = 0x20;

    private readonly nint _hwnd;
    private FrameClock.Ticket? _ticket;
    private double _from, _to, _begin, _ms;
    private Bezier _ease;
    private Action? _done;

    public WindowFader(nint hwnd) => _hwnd = hwnd;

    /// <summary>The alpha last applied, 0 to 255.</summary>
    public double Alpha { get; private set; } = 255;

    public bool Running => _ticket is { Active: true };

    /// <summary>Applies an alpha at once, and stops a running fade (without its completion).</summary>
    public void Set(double alpha)
    {
        Cancel(runDone: false);
        Apply(alpha);
    }

    /// <summary>Fades from the current alpha to <paramref name="to"/>; <paramref name="done"/> runs when it arrives. With motion off, it arrives at once.</summary>
    public void Fade(double to, TimeSpan duration, Bezier ease, Action? done = null)
    {
        Cancel(runDone: false);
        if (!MotionSettings.Enabled || duration <= TimeSpan.Zero)
        {
            Apply(to);
            done?.Invoke();
            return;
        }
        _from = Alpha;
        _to = to;
        _ms = duration.TotalMilliseconds;
        _ease = ease;
        _done = done;
        _begin = FrameClock.Now;
        _ticket = FrameClock.Every(16, OnTick);
    }

    /// <summary>Stops a running fade where it is; its completion runs only when asked.</summary>
    public void Cancel(bool runDone)
    {
        bool running = Running;
        _ticket?.Stop();
        _ticket = null;
        var done = _done;
        _done = null;
        if (running && runDone)
            done?.Invoke();
    }

    /// <summary>Clicks pass through the window (while it fades out, so a "closed" panel takes none).</summary>
    public void SetClickThrough(bool through)
    {
        int style = GetWindowLong(_hwnd, GwlExStyle);
        int wanted = through ? style | WsExTransparent : style & ~WsExTransparent;
        if (wanted != style)
            SetWindowLong(_hwnd, GwlExStyle, wanted);
    }

    private void OnTick(double now)
    {
        double t = Math.Clamp((now - _begin) / _ms, 0, 1);
        Apply(_from + (_to - _from) * _ease.At(t));
        if (t < 1)
            return;
        _ticket?.Stop();
        _ticket = null;
        var done = _done;
        _done = null;
        done?.Invoke();
    }

    private void Apply(double alpha)
    {
        Alpha = Math.Clamp(alpha, 0, 255);
        int style = GetWindowLong(_hwnd, GwlExStyle);
        if ((style & WsExLayered) == 0)
            SetWindowLong(_hwnd, GwlExStyle, style | WsExLayered);
        SetLayeredWindowAttributes(_hwnd, 0, (byte)Math.Round(Alpha), 2 /* LWA_ALPHA */);
    }

    public void Dispose() => Cancel(runDone: false);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
}
