using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace DeskWidgets.Dock;

/// <summary>
/// The Glass dock's running look (HANDOFF 4): the tile's light and colour swap. A custom icon on a coloured tile becomes a white tile (white to
/// #F2F2F7, top to bottom) with the symbol in the tile's colour; one on a white tile becomes a coloured tile with a white symbol; the sheen stays.
/// A real icon keeps its picture on an accent plate, the hue of its coloured pixels, instead of the white tile plate.
/// </summary>
internal static class GlassDockRunning
{
    private const double Box = 64;
    private const double RadiusFraction = 0.225;

    /// <summary>The plate of a tile with no saturated pixels to take a hue from.</summary>
    public const uint NeutralPlate = 0xFF3A3A3C;

    /// <summary>
    /// The running art of the 13 custom apps, from HANDOFF 4's paint table and <c>icons/app-&lt;key&gt;-running.svg</c>: the plate (null: the
    /// white gradient), and each layer's new paint (null: unchanged). The paths are the idle art's.
    /// </summary>
    private static readonly Dictionary<string, (uint? Plate, uint?[] Paints)> Table = new()
    {
        ["wechat"] = (null, [0xFF07C160, 0xFFFFFFFF]),
        ["qq"] = (null, [0xFF12B7F5, null, null, null]), // the body only; the eyes stay white
        ["dingtalk"] = (null, [0xFF1677FF, 0xFF1677FF]),
        ["tencentmeeting"] = (0xFF2F6BEA, [0xFFFFFFFF, null]),
        ["word"] = (null, [0xFF185ABD, 0xFF185ABD]),
        ["excel"] = (null, [0xFF107C41]),
        ["powerpoint"] = (null, [0xFFC43E1C, 0xFFC43E1C]),
        ["chrome"] = (0xFF4285F4, [null, null, null, 0xFFFFFFFF, null]),
        ["edge"] = (null, [0xFF0F5BB5, 0xFF35C46E]),
        ["claude"] = (null, [0xFFD97757]),
        ["chatgpt"] = (0xFF111111, [0xFFFFFFFF, 0xFFFFFFFF]),
        ["cursor"] = (null, [0xFF1F1F22]),
        ["kimi"] = (null, [0xFF101733, null]),
    };

    /// <summary>A designed tile's running art: the table for a custom app, else the rule (a coloured tile goes white, its white paint takes the colour).</summary>
    public static FrameworkElement Designed(string? appKey, TileArt art, bool dark, double size)
    {
        uint? plate;
        uint[] paints;
        if (appKey is not null && Table.TryGetValue(appKey, out var row))
        {
            plate = row.Plate;
            paints = [.. art.Layers.Select((layer, i) => i < row.Paints.Length && row.Paints[i] is { } paint ? paint : layer.Paint)];
        }
        else
        {
            // The rule, for a tile the table does not list (the generic app tile).
            uint colour = art.ColorFor(dark);
            if ((colour & 0xFFFFFF) == 0xFFFFFF)
            {
                // A white tile: the symbol's colour becomes the tile, the symbol white.
                plate = art.Layers.Length > 0 ? art.Layers[0].Paint : colour;
                paints = [.. art.Layers.Select(_ => 0xFFFFFFFFu)];
            }
            else
            {
                plate = null;
                paints = [.. art.Layers.Select(layer => (layer.Paint & 0xFFFFFF) == 0xFFFFFF ? colour : layer.Paint)];
            }
        }

        var canvas = new Canvas { Width = Box, Height = Box, RenderTransform = new ScaleTransform { ScaleX = size / Box, ScaleY = size / Box } };
        canvas.Children.Add(new Rectangle
        {
            Width = Box, Height = Box, RadiusX = Box * RadiusFraction, RadiusY = Box * RadiusFraction,
            Fill = plate is { } solid ? new SolidColorBrush(GlassTiles.ToColor(solid)) : WhiteTile(),
        });
        // The tile sheen (white 16 % to 0 %) stays on both looks.
        canvas.Children.Add(new Rectangle
        {
            Width = Box, Height = Box, RadiusX = Box * RadiusFraction, RadiusY = Box * RadiusFraction,
            Fill = Vertical(Color.FromArgb(0x29, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)),
        });
        for (int i = 0; i < art.Layers.Length; i++)
        {
            var layer = art.Layers[i];
            var brush = new SolidColorBrush(GlassTiles.ToColor(paints[i]));
            var path = new Path { Data = GlassTiles.Geometry(layer.Path), Opacity = layer.Opacity };
            if (layer.Kind == TileLayerKind.Fill)
            {
                path.Fill = brush;
            }
            else
            {
                path.Stroke = brush;
                path.StrokeThickness = layer.StrokeWidth;
                path.StrokeStartLineCap = PenLineCap.Round;
                path.StrokeEndLineCap = PenLineCap.Round;
                path.StrokeLineJoin = PenLineJoin.Round;
            }
            canvas.Children.Add(path);
        }
        return Host(canvas, size);
    }

    /// <summary>A real icon running: its picture at 0.68 of the tile on the accent plate (a full-bleed icon shrinks onto it too).</summary>
    public static FrameworkElement RealIcon(BitmapSource icon, uint accent, double size, double scale)
    {
        var host = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        host.Children.Add(new Rectangle { Width = size, Height = size, RadiusX = size * RadiusFraction, RadiusY = size * RadiusFraction, Fill = new SolidColorBrush(GlassTiles.ToColor(accent)) });
        double inner = GlassTiles.IconPixels(size, scale) / scale;
        host.Children.Add(new Image { Source = icon, Width = inner, Height = inner, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return host;
    }

    private static LinearGradientBrush WhiteTile() => Vertical(Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 0xF2, 0xF2, 0xF7));

    private static LinearGradientBrush Vertical(Color top, Color bottom) => new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops = { new GradientStop { Color = top, Offset = 0 }, new GradientStop { Color = bottom, Offset = 1 } },
    };

    private static Grid Host(UIElement content, double size)
    {
        var host = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        host.Children.Add(content);
        return host;
    }

    // ---------------------------------------------------------------- the accent plate

    // Computed once per picture (the icon service's image is keyed by path, size and the file's write time), for light and dark.
    private static readonly Dictionary<IconImage, (uint Light, uint Dark)> s_accents = [];

    public static uint AccentFor(IconImage image, bool dark)
    {
        if (!s_accents.TryGetValue(image, out var both))
        {
            both = (AccentPlate(image.Pixels.Bgra, dark: false), AccentPlate(image.Pixels.Bgra, dark: true));
            BoundedCache.Put(s_accents, image, both, max: 128);
        }
        return dark ? both.Dark : both.Light;
    }

    /// <summary>
    /// HANDOFF 4 and 11.5: the accent plate of a real icon. Of the pixels with alpha ≥ 200, those with HSL saturation ≥ 0.35 vote for their
    /// hue, weighted by saturation (a circular mean, so reds either side of 0° agree). The plate is that hue at saturation max(S, 0.55), S being
    /// the voters' mean saturation, and lightness 0.38 (light) or 0.42 (dark). With no voters, or voters under 2 % of the opaque pixels (an icon
    /// that is mostly transparent, white or grey, with a few coloured specks), it is <see cref="NeutralPlate"/>.
    /// </summary>
    /// <param name="bgra">Straight-alpha BGRA, four bytes a pixel (the Shell's <see cref="Pixels.Bgra"/>).</param>
    /// <returns>Opaque ARGB (0xFFRRGGBB).</returns>
    internal static uint AccentPlate(ReadOnlySpan<byte> bgra, bool dark)
    {
        double x = 0, y = 0, saturation = 0;
        int opaque = 0, voters = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 200)
                continue;
            opaque++;
            var (hue, s, _) = ToHsl(bgra[i + 2], bgra[i + 1], bgra[i]);
            if (s < 0.35)
                continue;
            voters++;
            saturation += s;
            double radians = hue * Math.PI / 180;
            x += s * Math.Cos(radians);
            y += s * Math.Sin(radians);
        }
        if (voters == 0 || voters < opaque * 0.02 || (x == 0 && y == 0))
            return NeutralPlate;
        double mean = Math.Atan2(y, x) * 180 / Math.PI;
        if (mean < 0)
            mean += 360;
        return FromHsl(mean, Math.Max(saturation / voters, 0.55), dark ? 0.42 : 0.38);
    }

    /// <summary>HSL of an sRGB colour: hue in degrees (0 to 360), saturation and lightness 0 to 1.</summary>
    internal static (double Hue, double Saturation, double Lightness) ToHsl(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
        double l = (max + min) / 2, d = max - min;
        if (d == 0)
            return (0, 0, l);
        double s = d / (1 - Math.Abs(2 * l - 1));
        double h = max == rf ? (gf - bf) / d % 6 : max == gf ? (bf - rf) / d + 2 : (rf - gf) / d + 4;
        h *= 60;
        if (h < 0)
            h += 360;
        return (h, Math.Min(1, s), l);
    }

    /// <summary>An opaque ARGB colour from HSL (hue in degrees).</summary>
    internal static uint FromHsl(double hue, double saturation, double lightness)
    {
        double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double hp = (hue % 360 + 360) % 360 / 60;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        (double r, double g, double b) = hp switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        double m = lightness - c / 2;
        static uint Byte(double v) => (uint)Math.Clamp((int)Math.Round(v * 255), 0, 255);
        return 0xFF000000 | Byte(r + m) << 16 | Byte(g + m) << 8 | Byte(b + m);
    }
}
