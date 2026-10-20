using DeskWidgets.Core.Devices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace DeskWidgets.Widgets.Battery;

/// <summary>
/// Circular battery gauge in the style of Apple's Batteries widget: a faint
/// track, a coloured arc running clockwise from 12 o'clock, the device glyph
/// in the middle and an optional charging badge. Level changes animate.
/// </summary>
internal sealed class BatteryRing : Grid
{
    private static readonly TimeSpan AnimationLength = TimeSpan.FromMilliseconds(700);

    private readonly double _diameter;
    private readonly double _thickness;
    private readonly Ellipse _track;
    private readonly Microsoft.UI.Xaml.Shapes.Path _arc;
    private readonly FontIcon _glyph;
    private readonly Grid _badge;
    private readonly FontIcon _badgeGlyph;
    private double _shown;
    private double _from;
    private double _target = double.NaN;
    private DateTime _animationStart;
    private bool _animating;

    public BatteryRing(double diameter, double thickness)
    {
        _diameter = diameter;
        _thickness = thickness;
        Width = diameter;
        Height = diameter;

        _track = new Ellipse { Width = diameter, Height = diameter, StrokeThickness = thickness };
        _arc = new Microsoft.UI.Xaml.Shapes.Path
        {
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        _glyph = new FontIcon
        {
            FontSize = Math.Round(diameter * 0.36),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        double badgeSize = Math.Round(diameter * 0.36);
        _badgeGlyph = new FontIcon
        {
            Glyph = "",
            FontSize = Math.Round(badgeSize * 0.62),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _badge = new Grid
        {
            Width = badgeSize,
            Height = badgeSize,
            CornerRadius = new CornerRadius(badgeSize / 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -badgeSize * 0.2, -badgeSize * 0.2, 0),
            Visibility = Visibility.Collapsed,
        };
        _badge.Children.Add(_badgeGlyph);

        Children.Add(_track);
        Children.Add(_arc);
        Children.Add(_glyph);
        Children.Add(_badge);
    }

    /// <summary>Shows <paramref name="device"/>, or an empty slot when it is null.</summary>
    public void Update(DeviceStatus? device, GlassPalette palette, bool chargingBadge)
    {
        if (device is null)
        {
            _track.Stroke = palette.Placeholder;
            _glyph.Glyph = string.Empty;
            _badge.Visibility = Visibility.Collapsed;
            SetTarget(0, animate: false);
            return;
        }

        bool known = device.IsConnected && device.BatteryPercent is not null;
        var level = palette.LevelBrush(device);
        _track.Stroke = palette.Track;
        _arc.Stroke = level;
        _glyph.Glyph = DeviceGlyphs.For(device);
        _glyph.Foreground = device.IsConnected ? palette.Primary : palette.Secondary;
        _badge.Background = level;
        _badgeGlyph.Foreground = palette.OnAccent;
        _badge.Visibility = chargingBadge && known && device.IsCharging ? Visibility.Visible : Visibility.Collapsed;
        SetTarget(known ? device.BatteryPercent!.Value / 100.0 : 0, animate: true);
    }

    private void SetTarget(double target, bool animate)
    {
        if (target == _target)
            return;
        bool first = double.IsNaN(_target);
        _target = target;
        if (!animate)
        {
            StopAnimation();
            _shown = target;
            Draw(target);
            return;
        }
        // A new ring sweeps in from empty; an existing one glides to the new level.
        _from = first ? 0 : _shown;
        _animationStart = DateTime.UtcNow;
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void OnRendering(object? sender, object e)
    {
        double t = Math.Clamp((DateTime.UtcNow - _animationStart) / AnimationLength, 0, 1);
        double eased = 1 - Math.Pow(1 - t, 3);
        _shown = _from + (_target - _from) * eased;
        Draw(_shown);
        if (t >= 1)
            StopAnimation();
    }

    private void StopAnimation()
    {
        if (!_animating)
            return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void Draw(double fraction)
    {
        double radius = (_diameter - _thickness) / 2;
        double center = _diameter / 2;
        if (fraction <= 0.001)
        {
            _arc.Data = null;
            return;
        }
        if (fraction >= 0.999)
        {
            _arc.Data = new EllipseGeometry { Center = new Point(center, center), RadiusX = radius, RadiusY = radius };
            return;
        }

        double angle = fraction * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle)),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = angle > Math.PI,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _arc.Data = geometry;
    }
}
