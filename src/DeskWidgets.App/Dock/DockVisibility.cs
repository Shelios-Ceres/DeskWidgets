namespace DeskWidgets.Dock;

/// <summary>When the dock gets out of the way (PROPOSAL 3.1), as one decision the tests can check without any window.</summary>
internal static class DockVisibility
{
    /// <summary><c>SHQUERY_USER_NOTIFICATION_STATE</c>: busy, a full-screen Direct3D app, presentation mode.</summary>
    public static bool IsFullScreenState(int notificationState) => notificationState is 2 or 3 or 4;

    /// <summary>
    /// A maximized window on the dock's screen hides it; so does a full-screen one in front. "Full screen" needs both signals: the foreground
    /// window covers the whole monitor and the Shell says busy, D3D full screen or presentation mode. The Shell alone said "busy" for a
    /// harmless full-screen overlay (SPIKE section 2), and a window that covers the monitor alone is not necessarily a game.
    /// </summary>
    public static bool ShouldHide(bool maximizedOnScreen, bool foregroundCoversScreen, int notificationState)
        => maximizedOnScreen || (foregroundCoversScreen && IsFullScreenState(notificationState));

    /// <summary>Whether a pointer or a file drag at the screen edge may bring a hidden dock back: never over a full-screen window.</summary>
    public static bool MayReveal(bool foregroundCoversScreen, int notificationState)
        => !(foregroundCoversScreen && IsFullScreenState(notificationState));
}
