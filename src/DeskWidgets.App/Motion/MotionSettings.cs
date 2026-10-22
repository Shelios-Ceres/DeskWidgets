using Windows.UI.ViewManagement;

namespace DeskWidgets.Motion;

/// <summary>
/// Whether motion runs at all. With Windows' "Animation effects" off (<see cref="UISettings.AnimationsEnabled"/>), every moment
/// snaps to its final state, as before Phase 12. The setting is read when each animation starts (its change event isn't relied on).
/// <c>--motion-off</c> forces it off, to check the snapping without touching the system setting.
/// </summary>
internal static class MotionSettings
{
    private static readonly UISettings s_ui = new();

    /// <summary>Design QA (<c>--motion-off</c>): behave as if "Animation effects" were off.</summary>
    public static bool ForcedOff { get; set; }

    public static bool Enabled
    {
        get
        {
            if (ForcedOff)
                return false;
            try
            {
                return s_ui.AnimationsEnabled;
            }
            catch (Exception)
            {
                return true; // an older Windows without the setting: animations are on
            }
        }
    }
}
