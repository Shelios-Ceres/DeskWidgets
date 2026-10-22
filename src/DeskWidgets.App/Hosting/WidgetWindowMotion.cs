using System.Numerics;
using DeskWidgets.Motion;
using DeskWidgets.Widgets;

namespace DeskWidgets.Hosting;

/// <summary>
/// The widget window's motion (Phase 12, B7 and B8). Glass fades the whole window with layered alpha and scales the frost and the card
/// together (so they stay in step); Pixel draws stepped frames on the card itself. Everything is cut short by the next state, nothing
/// runs when the card is still, and with "Animation effects" off each moment snaps to its end.
/// </summary>
internal sealed partial class WidgetWindow
{
    /// <summary>GROW's first two frames (SHRINK's two, reversed): the share of the card that is drawn.</summary>
    private static readonly double[] GrowShares = [1.0 / 3, 2.0 / 3];

    private WindowFader? _fader;
    private StepAnimator? _steps;
    private bool _leaving;

    private WindowFader Fader => _fader ??= new WindowFader(_hwnd);

    private StepAnimator Steps => _steps ??= new StepAnimator();

    private Vector2 CardSizeDip => new((float)_frame.Width, (float)_frame.Height);

    /// <summary>B8, a widget added: puts the entrance's first frame up before the window is shown, so there is no flash of the finished card.</summary>
    internal void PrepareEnter()
    {
        if (!MotionSettings.Enabled)
            return;
        if (_theme == WidgetTheme.Pixel)
        {
            _frame.SetPixelShare(GrowShares[0]);
            return;
        }
        Fader.Set(0);
        GlassMotion.ScaleWindow(_glass, _frame, CardSizeDip, _scale, GlassMotion.CardScale, TimeSpan.Zero, GlassEase.Out);
    }

    /// <summary>
    /// B8, a widget added. Glass: the card fades in and scales from 96 % about its centre (Enter), frost, tint and content together.
    /// Pixel: GROW from the centre on both axes, 1/3, 2/3, then the whole card with its content on the third frame.
    /// </summary>
    internal void PlayEnter()
    {
        if (_theme == WidgetTheme.Pixel)
        {
            Steps.Start(3, frame => _frame.SetPixelShare(frame < GrowShares.Length ? GrowShares[frame] : 1));
            return;
        }
        Fader.Fade(255, GlassMotion.Enter, Bezier.Out, Fader.Clear);
        GlassMotion.ScaleWindow(_glass, _frame, CardSizeDip, _scale, 1f, GlassMotion.Enter, GlassEase.Out);
    }

    /// <summary>
    /// B8, a widget removed. Glass: to opacity 0 and 96 % (Exit), then the window closes. Pixel: SHRINK to the centre, 2/3, 1/3, then it closes.
    /// The window takes no clicks while it leaves.
    /// </summary>
    internal void Remove()
    {
        if (_leaving)
            return;
        _leaving = true;
        Fader.SetClickThrough(true);
        if (!MotionSettings.Enabled)
        {
            CloseFromHost();
            return;
        }
        if (_theme == WidgetTheme.Pixel)
        {
            Steps.Start(3, frame =>
            {
                if (frame < GrowShares.Length)
                    _frame.SetPixelShare(GrowShares[GrowShares.Length - 1 - frame]);
            }, CloseFromHost);
            return;
        }
        Fader.Fade(0, GlassMotion.Exit, Bezier.In, CloseFromHost);
        GlassMotion.ScaleWindow(_glass, _frame, CardSizeDip, _scale, GlassMotion.CardScale, GlassMotion.Exit, GlassEase.In);
    }

    // ----- B7: a theme switch and a size change -----

    /// <summary>
    /// Glass ↔ Pixel. Glass: the old card goes at once and the new one fades in (Enter, opacity only). Pixel: its content dissolves in
    /// (see <see cref="Pixel.PixelSurface.DissolveIn"/>). Anything still running on the old card stops.
    /// </summary>
    private void BeginThemeMotion(WidgetTheme next)
    {
        FinishMorph();
        _steps?.Cancel();
        _frame.SetPixelShare(1);
        _frame.RefreshBlend();
        if (next == WidgetTheme.Glass && MotionSettings.Enabled && !_leaving)
        {
            Fader.Set(0);
            GlassMotion.ScaleWindow(_glass, _frame, CardSizeDip, _scale, 1f, TimeSpan.Zero, GlassEase.Out);
        }
        else if (_fader is not null)
        {
            _fader.Clear();
        }
    }

    private void EndThemeMotion()
    {
        if (_theme == WidgetTheme.Glass && MotionSettings.Enabled && !_leaving)
            Fader.Fade(255, GlassMotion.Enter, Bezier.Out, Fader.Clear);
    }

    private readonly List<FrameClock.Ticket> _morph = [];
    private Windows.UI.Composition.CompositionRoundedRectangleGeometry? _frostClip;
    private Microsoft.UI.Composition.CompositionRoundedRectangleGeometry? _xamlClip;
    private bool _morphing;

    /// <summary>
    /// B7, a size change in Glass. The old content fades out (Quick). The window takes the union of both sizes, and the frost's clip and a
    /// matching rounded clip on the card move from the old rectangle to the new one, anchored top-left (Move). The new content is laid out
    /// at its own size when the old has gone, and fades in (Enter) as the Move passes half-way; then the window settles at the new size.
    /// A new size cuts it short: the running one is finished first.
    /// </summary>
    private void MorphTo(WidgetSize size, Windows.Graphics.SizeInt32 old)
    {
        FinishMorph();
        _morphing = true;
        var fresh = WidgetLayout.GetCardPixels(size, _theme, _scale);
        float radius = (float)(WidgetLayout.CornerRadius * _scale);
        var union = new Vector2(Math.Max(old.Width, fresh.Width), Math.Max(old.Height, fresh.Height));
        var oldPx = new Vector2(old.Width, old.Height);
        var newPx = new Vector2(fresh.Width, fresh.Height);
        var unionDip = union / (float)_scale;
        var oldDip = oldPx / (float)_scale;
        var newDip = newPx / (float)_scale;

        // The old content keeps its old size, top-left, while it fades (it would re-flow in the union window); the tint and the sheen are
        // laid out at the new size from the first frame, inside the clip.
        _frame.PinContent(oldDip);
        _frame.PinLayers(newDip);
        // The window and the frost at the union, the clips at the old rectangle.
        _frame.Width = unionDip.X;
        _frame.Height = unionDip.Y;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)union.X, (int)union.Y));
        _glass.SetMorphStart(union, oldPx, radius);
        var content = _frame.ContentElement;

        var xaml = GlassMotion.VisualOf(_frame);
        var compositor = xaml.Compositor;
        _xamlClip = compositor.CreateRoundedRectangleGeometry();
        _xamlClip.Size = oldDip;
        _xamlClip.CornerRadius = new Vector2((float)WidgetLayout.CornerRadius);
        xaml.Clip = compositor.CreateGeometricClip(_xamlClip);

        var curve = GlassMotion.Curve(GlassEase.Move);
        var xamlEase = compositor.CreateCubicBezierEasingFunction(new Vector2((float)curve.X1, (float)curve.Y1), new Vector2((float)curve.X2, (float)curve.Y2));
        var xamlSize = compositor.CreateVector2KeyFrameAnimation();
        xamlSize.InsertKeyFrame(1f, newDip, xamlEase);
        xamlSize.Duration = GlassMotion.Move;
        _xamlClip.StartAnimation("Size", xamlSize);
        if (_glass.ClipGeometry is { } frostClip)
        {
            _frostClip = frostClip;
            var frostCompositor = GlassBackdrop.Compositor;
            var frostEase = frostCompositor.CreateCubicBezierEasingFunction(new Vector2((float)curve.X1, (float)curve.Y1), new Vector2((float)curve.X2, (float)curve.Y2));
            var frostSize = frostCompositor.CreateVector2KeyFrameAnimation();
            frostSize.InsertKeyFrame(1f, newPx, frostEase);
            frostSize.Duration = GlassMotion.Move;
            frostClip.StartAnimation("Size", frostSize);
        }

        GlassMotion.FadeTo(content, 0, GlassMotion.Quick, GlassEase.In);
        _morph.Add(FrameClock.After((int)GlassMotion.Quick.TotalMilliseconds, () =>
        {
            _frame.PinContent(newDip);
            _frame.SetCard(_theme, size);
            Widget.ApplySize(size);
        }));
        _morph.Add(FrameClock.After((int)GlassMotion.Move.TotalMilliseconds / 2, () => GlassMotion.FadeTo(content, 1, GlassMotion.Enter, GlassEase.Out)));
        _morph.Add(FrameClock.After((int)GlassMotion.Move.TotalMilliseconds + 40, FinishMorph));
    }

    /// <summary>Ends a size change where it is going: the new size, content shown, clips gone. Cheap when none runs.</summary>
    private void FinishMorph()
    {
        if (!_morphing)
            return;
        _morphing = false;
        foreach (var ticket in _morph)
            ticket.Stop();
        _morph.Clear();
        var size = Placement.Size;
        _frame.PinContent(null);
        _frame.PinLayers(null);
        _frame.SetCard(_theme, size);
        Widget.ApplySize(size);
        _frostClip?.StopAnimation("Size");
        _xamlClip?.StopAnimation("Size");
        var xaml = GlassMotion.VisualOf(_frame);
        xaml.Clip = null;
        _xamlClip?.Dispose();
        _xamlClip = null;
        _frostClip = null;
        GlassMotion.Rest(_frame.ContentElement);
        ResizeToCard();
    }

    private void DisposeMotion()
    {
        foreach (var ticket in _morph)
            ticket.Stop();
        _morph.Clear();
        _morphing = false;
        _fader?.Dispose();
        _fader = null;
        _steps?.Dispose();
        _steps = null;
    }
}
