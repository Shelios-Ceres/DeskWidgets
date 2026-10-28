using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace DeskWidgets.Motion;

/// <summary>
/// The one clock every motion shares: a single <see cref="DispatcherQueueTimer"/> for the whole app, re-armed for whichever animation
/// is due next, running only while an animation is. It holds a 1 ms timer resolution for that time only (the default tick comes up to
/// ~8 ms late, a quarter of a Pixel frame). Animations register a <see cref="Ticket"/> instead of owning a timer: a timer per animator
/// leaked two kernel handles with every panel instance that held one (Phase 12 spike), and there are far more animators than panels.
/// All of it runs on the UI thread.
/// </summary>
internal static class FrameClock
{
    /// <summary>A registered recurring tick. <see cref="Stop"/> ends it; it also ends when its owner says so from inside the tick.</summary>
    internal sealed class Ticket
    {
        internal Action<double> Tick = null!;
        internal double Due;
        internal int IntervalMs;
        internal bool Active;

        public void Stop()
        {
            if (!Active)
                return;
            Active = false;
            s_tickets.Remove(this);
            Reschedule();
        }
    }

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly List<Ticket> s_tickets = [];
    private static DispatcherQueueTimer? s_timer;
    private static bool s_hires;

    /// <summary>Milliseconds on the clock's own stopwatch.</summary>
    public static double Now => Clock.Elapsed.TotalMilliseconds;

    /// <summary>Calls <paramref name="tick"/> (with <see cref="Now"/>) every <paramref name="intervalMs"/>, the first one after that long, until the ticket is stopped.</summary>
    public static Ticket Every(int intervalMs, Action<double> tick)
    {
        var ticket = new Ticket { Tick = tick, IntervalMs = intervalMs, Due = Now + intervalMs, Active = true };
        s_tickets.Add(ticket);
        Reschedule();
        return ticket;
    }

    /// <summary>Calls <paramref name="action"/> once, after <paramref name="delayMs"/>, unless the ticket is stopped first.</summary>
    public static Ticket After(int delayMs, Action action)
    {
        Ticket? ticket = null;
        ticket = Every(delayMs, _ =>
        {
            ticket!.Stop();
            action();
        });
        return ticket;
    }

    private static void Reschedule()
    {
        // The fine timer resolution is for animations (frames tens of milliseconds apart); a slow poll does not need it.
        bool wantHires = s_tickets.Any(t => t.IntervalMs < 100);
        if (!wantHires && s_hires)
        {
            s_hires = false;
            timeEndPeriod(1);
        }
        if (s_tickets.Count == 0)
        {
            s_timer?.Stop();
            return;
        }
        if (s_timer is null)
        {
            s_timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            s_timer.IsRepeating = false;
            s_timer.Tick += (_, _) => OnTick();
        }
        if (wantHires && !s_hires)
            s_hires = timeBeginPeriod(1) == 0;
        double next = s_tickets.Min(t => t.Due);
        s_timer.Stop();
        s_timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, next - Now));
        s_timer.Start();
    }

    private static void OnTick()
    {
        try
        {
            RunDue(s_tickets.ToList(), Now);
        }
        finally
        {
            Reschedule(); // the one timer is armed again whatever a tick did, or every clock and animation would stop with it
        }
    }

    /// <summary>Runs the tickets that are due at <paramref name="now"/>. A tick that throws is logged and its ticket stopped; the others still run.</summary>
    internal static void RunDue(IReadOnlyList<Ticket> tickets, double now)
    {
        foreach (var ticket in tickets)
        {
            if (!ticket.Active || ticket.Due > now + 0.5)
                continue;
            ticket.Due += ticket.IntervalMs;
            if (ticket.Due < now)
                ticket.Due = now + ticket.IntervalMs; // fell behind: do not burst
            try
            {
                ticket.Tick(now);
            }
            catch (Exception ex)
            {
                Core.Diagnostics.Log.Warn("A timed step failed and was stopped", ex);
                ticket.Stop(); // once: a broken animation must not log every frame
            }
        }
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint ms);
}
