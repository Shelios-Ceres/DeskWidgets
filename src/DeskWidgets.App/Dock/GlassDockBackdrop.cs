using System.Numerics;
using System.Runtime.InteropServices;
using DeskWidgets.Hosting;
using DeskWidgets.Motion;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT;
using SystemComposition = Windows.UI.Composition;

namespace DeskWidgets.Dock;

/// <summary>
/// The Glass dock's live frost: as <see cref="GlassBackdrop"/>, a host-backdrop visual on the top-level window under the XAML, but two of
/// them (HANDOFF 11.1): one for the bar, one for the hover label, each clipped to its own rounded rectangle. Both visuals cover the whole
/// window and only their clips move, so a shape can be animated (B7's morph, D1's label) without the visual ever being re-placed mid-way.
/// Positions are window pixels. Everything here animates from the value it has now; a zero duration sets the value at once.
/// </summary>
internal sealed class GlassDockBackdrop : SystemBackdrop, IDisposable
{
    private SystemComposition.Desktop.DesktopWindowTarget? _target;
    private SystemComposition.ContainerVisual? _root;
    private SystemComposition.CompositionBackdropBrush? _brush;
    private SystemComposition.SpriteVisual? _bar, _label;
    private SystemComposition.CompositionRoundedRectangleGeometry? _barShape, _labelShape;

    public void Attach(nint hwnd)
    {
        var compositor = GlassBackdrop.Compositor;
        compositor.As<ICompositorDesktopInterop>().CreateDesktopWindowTarget(hwnd, false, out nint raw);
        _target = SystemComposition.Desktop.DesktopWindowTarget.FromAbi(raw);
        Marshal.Release(raw);

        _root = compositor.CreateContainerVisual();
        _brush = compositor.CreateHostBackdropBrush();
        (_bar, _barShape) = Frost(compositor);
        (_label, _labelShape) = Frost(compositor);
        _label.Opacity = 0;
        _root.Children.InsertAtTop(_bar);
        _root.Children.InsertAtTop(_label);
        _target.Root = _root;
    }

    private (SystemComposition.SpriteVisual, SystemComposition.CompositionRoundedRectangleGeometry) Frost(SystemComposition.Compositor compositor)
    {
        var shape = compositor.CreateRoundedRectangleGeometry();
        var visual = compositor.CreateSpriteVisual();
        visual.Brush = _brush;
        visual.Clip = compositor.CreateGeometricClip(shape);
        return (visual, shape);
    }

    /// <summary>The window's size in pixels: both frost visuals cover all of it (their clips cut out the shapes).</summary>
    public void SetWindowSize(Vector2 size)
    {
        if (_bar is null || _label is null)
            return;
        _bar.Size = size;
        _label.Size = size;
    }

    /// <summary>The bar's rounded rectangle (window pixels), moved there over <paramref name="duration"/> from where it is now.</summary>
    public void ShapeBar(Vector2 offset, Vector2 size, float radius, TimeSpan duration, Bezier curve)
    {
        if (_barShape is not { } shape)
            return;
        Animate(shape, "Offset", offset, duration, curve, v => shape.Offset = v);
        Animate(shape, "Size", size, duration, curve, v => shape.Size = v);
        Animate(shape, "CornerRadius", new Vector2(radius), duration, curve, v => shape.CornerRadius = v);
    }

    /// <summary>The label's pill: its size and radius at once, its place over <paramref name="duration"/> from where it is now.</summary>
    public void ShapeLabel(Vector2 offset, Vector2 size, float radius, TimeSpan duration, Bezier curve)
    {
        if (_labelShape is not { } shape)
            return;
        Animate(shape, "Size", size, TimeSpan.Zero, curve, v => shape.Size = v);
        Animate(shape, "CornerRadius", new Vector2(radius), TimeSpan.Zero, curve, v => shape.CornerRadius = v);
        Animate(shape, "Offset", offset, duration, curve, v => shape.Offset = v);
    }

    public void FadeLabel(float to, TimeSpan duration, Bezier curve)
    {
        if (_label is { } label)
            Animate(label, "Opacity", to, duration, curve, v => label.Opacity = v);
    }

    /// <summary>D15: a new Glass dock fades in (opacity only).</summary>
    public void FadeIn(TimeSpan duration)
    {
        if (_root is not { } root)
            return;
        root.StopAnimation("Opacity");
        root.Opacity = 0;
        Animate(root, "Opacity", 1f, duration, Bezier.Out, v => root.Opacity = v);
    }

    private static void Animate(SystemComposition.CompositionObject target, string property, Vector2 to, TimeSpan duration, Bezier curve, Action<Vector2> set)
    {
        if (duration <= TimeSpan.Zero)
        {
            target.StopAnimation(property);
            set(to);
            return;
        }
        var compositor = target.Compositor;
        var animation = compositor.CreateVector2KeyFrameAnimation();
        animation.InsertKeyFrame(1f, to, Easing(compositor, curve));
        animation.Duration = duration;
        target.StartAnimation(property, animation);
    }

    private static void Animate(SystemComposition.CompositionObject target, string property, float to, TimeSpan duration, Bezier curve, Action<float> set)
    {
        if (duration <= TimeSpan.Zero)
        {
            target.StopAnimation(property);
            set(to);
            return;
        }
        var compositor = target.Compositor;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1f, to, Easing(compositor, curve));
        animation.Duration = duration;
        target.StartAnimation(property, animation);
    }

    private static SystemComposition.CubicBezierEasingFunction Easing(SystemComposition.Compositor compositor, Bezier curve)
        => compositor.CreateCubicBezierEasingFunction(new Vector2((float)curve.X1, (float)curve.Y1), new Vector2((float)curve.X2, (float)curve.Y2));

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        connectedTarget.SystemBackdrop = GlassBackdrop.Compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
    }

    /// <summary>As <see cref="GlassBackdrop"/>: the base version throws for this kind of backdrop, so it is deliberately not called.</summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
    }

    public void Dispose()
    {
        // Every Composition object holds a kernel handle until it is disposed: all of them, the shared brush once.
        if (_target is not null)
            _target.Root = null;
        foreach (var visual in new[] { _bar, _label })
        {
            if (visual is null)
                continue;
            visual.Brush = null;
            visual.Clip?.Dispose();
            visual.Clip = null;
        }
        _root?.Children.RemoveAll();
        _barShape?.Dispose();
        _labelShape?.Dispose();
        _bar?.Dispose();
        _label?.Dispose();
        _brush?.Dispose();
        _root?.Dispose();
        _target?.Dispose();
        _barShape = _labelShape = null;
        _bar = _label = null;
        _brush = null;
        _root = null;
        _target = null;
    }

    [ComImport]
    [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(nint hwndTarget, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out nint result);
    }
}
