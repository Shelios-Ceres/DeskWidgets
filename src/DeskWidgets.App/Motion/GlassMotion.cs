using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace DeskWidgets.Motion;

internal enum GlassEase
{
    Out,
    In,
    Move,
}

/// <summary>
/// The Glass motion vocabulary (MOTION.md §1) and the few things that run it on an element's Composition visual. Everything here
/// animates the visual, never the XAML property (setting <c>Opacity</c> or <c>Translation</c> from code while an animation runs
/// cancels it), starts from the value the visual has now (a new state cuts the last animation short), and does nothing but set
/// the final value when "Animation effects" is off.
/// </summary>
internal static class GlassMotion
{
    public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Exit = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Enter = TimeSpan.FromMilliseconds(240);
    public static readonly TimeSpan Move = TimeSpan.FromMilliseconds(280);
    public static readonly TimeSpan Press = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan Release = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan Blend = TimeSpan.FromMilliseconds(250);

    /// <summary>The scale an item or a card has while it is not there (an item 85 %, a card 96 %, the panel 97 %).</summary>
    public const float ItemScale = 0.85f, CardScale = 0.96f, PanelScale = 0.97f, PanelCloseScale = 0.98f, PressScale = 0.94f;

    public static Bezier Curve(GlassEase ease) => ease switch
    {
        GlassEase.Out => Bezier.Out,
        GlassEase.In => Bezier.In,
        _ => Bezier.Move,
    };

    public static Visual VisualOf(UIElement element) => ElementCompositionPreview.GetElementVisual(element);

    private static CubicBezierEasingFunction Easing(Compositor compositor, GlassEase ease)
    {
        var curve = Curve(ease);
        return compositor.CreateCubicBezierEasingFunction(new Vector2((float)curve.X1, (float)curve.Y1), new Vector2((float)curve.X2, (float)curve.Y2));
    }

    /// <summary>Animates the visual's opacity to <paramref name="to"/> from wherever it is.</summary>
    public static void FadeTo(UIElement element, float to, TimeSpan duration, GlassEase ease, TimeSpan delay = default)
    {
        var visual = VisualOf(element);
        if (!MotionSettings.Enabled || duration <= TimeSpan.Zero)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = to;
            return;
        }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1f, to, Easing(visual.Compositor, ease));
        animation.Duration = duration;
        animation.DelayTime = delay;
        visual.StartAnimation("Opacity", animation);
    }

    /// <summary>Animates the visual's scale (both axes) to <paramref name="to"/> about the point (in DIPs), from wherever it is.</summary>
    public static void ScaleTo(UIElement element, float to, TimeSpan duration, GlassEase ease, Vector2 center, TimeSpan delay = default)
    {
        var visual = VisualOf(element);
        visual.CenterPoint = new Vector3(center, 0);
        if (!MotionSettings.Enabled || duration <= TimeSpan.Zero)
        {
            visual.StopAnimation("Scale");
            visual.Scale = new Vector3(to, to, 1);
            return;
        }
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1f, new Vector3(to, to, 1), Easing(visual.Compositor, ease));
        animation.Duration = duration;
        animation.DelayTime = delay;
        visual.StartAnimation("Scale", animation);
    }

    /// <summary>Puts the visual's opacity and scale at their starting values without a frame in between (call it before the element is shown).</summary>
    public static void Preset(UIElement element, float opacity, float scale, Vector2 center)
    {
        var visual = VisualOf(element);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Scale");
        visual.CenterPoint = new Vector3(center, 0);
        visual.Opacity = opacity;
        visual.Scale = new Vector3(scale, scale, 1);
    }

    /// <summary>
    /// Slides an element that was laid out in its new place from <paramref name="from"/> (the offset back to the old place, in DIPs)
    /// to where it is, with a Composition key frame on the visual's Translation.
    /// </summary>
    public static void SlideFrom(UIElement element, Vector2 from, TimeSpan duration, GlassEase ease)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = VisualOf(element);
        if (!MotionSettings.Enabled)
        {
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0f, new Vector3(from, 0));
        animation.InsertKeyFrame(1f, Vector3.Zero, Easing(visual.Compositor, ease));
        animation.Duration = duration;
        visual.StartAnimation("Translation", animation);
    }

    /// <summary>A slide in progress, so that a new move starts from where the element is now (a new state cuts the last animation short).</summary>
    internal sealed class SlideState
    {
        public Vector2 From;
        public long Started;
        public bool Active;

        /// <summary>The offset the element is at now, estimated from its own curve.</summary>
        public Vector2 Now()
        {
            if (!Active)
                return Vector2.Zero;
            double t = (System.Diagnostics.Stopwatch.GetTimestamp() - Started) / (double)System.Diagnostics.Stopwatch.Frequency * 1000 / Move.TotalMilliseconds;
            if (t >= 1)
            {
                Active = false;
                return Vector2.Zero;
            }
            return From * (1 - (float)Bezier.Move.At(t));
        }
    }

    /// <summary>
    /// A4: an element laid out in a new place glides there from the old place (Move). <paramref name="delta"/> is the old place minus the
    /// new one in DIPs. Without <paramref name="animate"/> (out of view, or no motion) it settles at once.
    /// </summary>
    public static void Slide(UIElement element, SlideState state, Vector2 delta, bool animate)
    {
        if (!animate)
        {
            if (state.Active)
            {
                state.Active = false;
                SlideFrom(element, Vector2.Zero, TimeSpan.Zero, GlassEase.Move);
            }
            return;
        }
        var from = state.Now() + delta;
        state.From = from;
        state.Started = System.Diagnostics.Stopwatch.GetTimestamp();
        state.Active = true;
        SlideFrom(element, from, Move, GlassEase.Move);
    }

    /// <summary>A2b/A4: a new item enters: opacity 0 → 1 and scale 85 % → 100 % about <paramref name="center"/> (Enter). It is in place from the start.</summary>
    public static void EnterItem(UIElement element, Vector2 center, TimeSpan delay = default)
    {
        Preset(element, 0, ItemScale, center);
        FadeTo(element, 1, Enter, GlassEase.Out, delay);
        ScaleTo(element, 1, Enter, GlassEase.Out, center, delay);
    }

    /// <summary>A2c/A4: an item leaves: opacity 0 and scale 85 % (Exit). The caller removes the element when it has gone.</summary>
    public static void LeaveItem(UIElement element, Vector2 center)
    {
        element.IsHitTestVisible = false;
        FadeTo(element, 0, Exit, GlassEase.In);
        ScaleTo(element, ItemScale, Exit, GlassEase.In, center);
    }

    /// <summary>How long after <see cref="LeaveItem"/> the element can be removed.</summary>
    public static TimeSpan LeaveDuration => Exit + TimeSpan.FromMilliseconds(40);

    /// <summary>Back to rest at once: full opacity, full scale, no translation.</summary>
    public static void Rest(UIElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true); // a visual that cannot translate refuses to be stopped on it
        var visual = VisualOf(element);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Scale");
        visual.StopAnimation("Translation");
        visual.Opacity = 1;
        visual.Scale = Vector3.One;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
    }
}
