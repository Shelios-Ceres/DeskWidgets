using DeskWidgets.Interop;
using Microsoft.UI.Dispatching;

namespace DeskWidgets.Hosting;

/// <summary>
/// Tells the host when the foreground window changes, which is when Win+D
/// (Show desktop) lifts the desktop over the widgets and when the next window
/// puts it back down. The notification is a documented WinEvent hook, delivered
/// on the UI thread; bursts of changes are folded into one callback a moment later.
/// </summary>
internal sealed class ShowDesktopWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private readonly Win32.WinEventProc _callback; // must outlive the hook
    private readonly DispatcherQueueTimer _settle;
    private readonly nint _hook;

    public ShowDesktopWatcher(DispatcherQueue dispatcher, Action foregroundChanged)
    {
        _settle = dispatcher.CreateTimer();
        _settle.Interval = TimeSpan.FromMilliseconds(80);
        _settle.IsRepeating = false;
        _settle.Tick += (_, _) => foregroundChanged();
        _callback = (_, _, _, _, _, _, _) =>
        {
            _settle.Stop();
            _settle.Start();
        };
        _hook = Win32.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public void Dispose()
    {
        _settle.Stop();
        if (_hook != 0)
            Win32.UnhookWinEvent(_hook);
    }
}
