using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace DeskWidgets.Widgets.WorldClock;

/// <summary>
/// Apple-style analog clock: a white face by day (6:00–18:00) and a dark one by
/// night, numerals on larger faces and ticks on small ones, rounded hour and
/// minute hands and an orange second hand.
/// </summary>
internal sealed class AnalogClock : Canvas
{
    /// <summary>Faces this size (DIPs) and up get numerals; smaller ones get ticks.</summary>
    private const double NumeralsFrom = 60;

    private readonly GlassPalette _palette;
    private readonly Ellipse _face;
    private readonly List<TextBlock> _numerals = [];
    private readonly List<(Line Line, bool Major)> _ticks = [];
    private readonly List<Shape> _ink = []; // hands and their cap, drawn in the face's ink colour
    private readonly RotateTransform _hour;
    private readonly RotateTransform _minute;
    private readonly RotateTransform _second;
    private readonly Canvas _secondHand;
    private bool? _isDay;

    public AnalogClock(double diameter, GlassPalette palette)
    {
        _palette = palette;
        Width = diameter;
        Height = diameter;
        double center = diameter / 2;

        _face = new Ellipse { Width = diameter, Height = diameter };
        Children.Add(_face);
        if (diameter >= NumeralsFrom)
            AddNumerals(diameter);
        else
            AddTicks(diameter);

        double bar = Math.Max(2, diameter * 0.055);
        double stem = Math.Max(1, diameter * 0.022);
        _hour = AddHand(center, barFrom: center * 0.16, to: center * 0.52, stem, bar);
        _minute = AddHand(center, barFrom: center * 0.16, to: center * 0.8, stem, bar);
        var cap = Dot(center, Math.Max(4, diameter * 0.09));
        _ink.Add(cap);
        Children.Add(cap);

        _second = new RotateTransform { CenterX = center, CenterY = center };
        _secondHand = new Canvas { Width = diameter, Height = diameter, RenderTransform = _second };
        _secondHand.Children.Add(new Line
        {
            X1 = center,
            Y1 = center + center * 0.2,
            X2 = center,
            Y2 = center - center * 0.88,
            Stroke = _palette.Orange,
            StrokeThickness = Math.Max(1, diameter * 0.018),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
        var secondCap = Dot(center, Math.Max(2.5, diameter * 0.05));
        secondCap.Fill = _palette.Orange;
        _secondHand.Children.Add(secondCap);
        Children.Add(_secondHand);
    }

    /// <summary>Shows <paramref name="time"/> (the city's wall-clock time).</summary>
    public void SetTime(DateTime time, bool showSeconds)
    {
        bool day = time.Hour is >= 6 and < 18;
        if (day != _isDay)
            ApplyFace(day);
        double minutes = time.Minute + time.Second / 60.0;
        _hour.Angle = (time.Hour % 12 + minutes / 60) * 30;
        _minute.Angle = minutes * 6;
        _second.Angle = time.Second * 6;
        _secondHand.Visibility = showSeconds ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyFace(bool day)
    {
        _isDay = day;
        _face.Fill = day ? _palette.DayFace : _palette.NightFace;
        var ink = day ? _palette.DayInk : _palette.NightInk;
        var faint = day ? _palette.DayFaint : _palette.NightFaint;
        foreach (var numeral in _numerals)
            numeral.Foreground = ink;
        foreach (var (line, major) in _ticks)
            line.Stroke = major ? ink : faint;
        foreach (var shape in _ink)
        {
            if (shape is Ellipse)
                shape.Fill = ink;
            else
                shape.Stroke = ink;
        }
    }

    private void AddNumerals(double diameter)
    {
        double center = diameter / 2;
        double fontSize = Math.Round(diameter * 0.14 * 2) / 2;
        double ring = center - fontSize * 0.95;
        double box = fontSize * 2;
        var font = new FontFamily("Segoe UI Variable Display");
        for (int hour = 1; hour <= 12; hour++)
        {
            double angle = hour * Math.PI / 6;
            var numeral = new TextBlock
            {
                Text = hour.ToString(CultureInfo.InvariantCulture),
                FontFamily = font,
                FontSize = fontSize,
                FontWeight = FontWeights.Medium,
                TextLineBounds = TextLineBounds.Tight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var cell = new Grid { Width = box, Height = box };
            cell.Children.Add(numeral);
            SetLeft(cell, center + ring * Math.Sin(angle) - box / 2);
            SetTop(cell, center - ring * Math.Cos(angle) - box / 2);
            _numerals.Add(numeral);
            Children.Add(cell);
        }
    }

    private void AddTicks(double diameter)
    {
        double center = diameter / 2;
        for (int hour = 0; hour < 12; hour++)
        {
            bool major = hour % 3 == 0;
            double angle = hour * Math.PI / 6;
            double inner = center * (major ? 0.72 : 0.78);
            double outer = center * 0.9;
            var line = new Line
            {
                X1 = center + inner * Math.Sin(angle),
                Y1 = center - inner * Math.Cos(angle),
                X2 = center + outer * Math.Sin(angle),
                Y2 = center - outer * Math.Cos(angle),
                StrokeThickness = major ? Math.Max(1.5, diameter * 0.035) : Math.Max(1, diameter * 0.022),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            _ticks.Add((line, major));
            Children.Add(line);
        }
    }

    /// <summary>A hand pointing at 12: a thin stem from the centre, then a thicker rounded bar.</summary>
    private RotateTransform AddHand(double center, double barFrom, double to, double stemThickness, double barThickness)
    {
        var rotation = new RotateTransform { CenterX = center, CenterY = center };
        var hand = new Canvas { Width = Width, Height = Height, RenderTransform = rotation };
        foreach (var line in new[] { HandLine(center, 0, barFrom, stemThickness), HandLine(center, barFrom, to, barThickness) })
        {
            _ink.Add(line);
            hand.Children.Add(line);
        }
        Children.Add(hand);
        return rotation;
    }

    private static Line HandLine(double center, double from, double to, double thickness) => new()
    {
        X1 = center,
        Y1 = center - from,
        X2 = center,
        Y2 = center - to,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private static Ellipse Dot(double center, double size)
    {
        var dot = new Ellipse { Width = size, Height = size };
        SetLeft(dot, center - size / 2);
        SetTop(dot, center - size / 2);
        return dot;
    }
}
