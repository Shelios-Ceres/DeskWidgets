using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// The Glass tiles of the Big Folder as XAML: a designed tile (the art in the 64 × 64 box, scaled to the tile's size), a real app
/// icon on its plate, a photo or video frame, or the plate shown while a picture is on its way. Nothing here takes pointer input.
/// </summary>
internal static class GlassTiles
{
    private const double Box = 64;
    private const double RadiusFraction = 0.225;

    private static readonly FontFamily DisplayFont = new("Segoe UI Variable Display");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text");

    public static Color ToColor(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>A WinUI geometry for SVG path data.</summary>
    public static PathGeometry Geometry(string data)
    {
        var geometry = new PathGeometry();
        PathFigure? figure = null;
        Point current = default, start = default;
        foreach (var op in SvgPath.Parse(data))
        {
            switch (op.Kind)
            {
                case PathOpKind.Move:
                    if (figure is not null)
                        geometry.Figures.Add(figure);
                    start = current = new Point(op.X, op.Y);
                    figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = true };
                    break;
                case PathOpKind.Close:
                    if (figure is not null)
                    {
                        figure.IsClosed = true;
                        geometry.Figures.Add(figure);
                        // A following command without a move continues from the start of the closed figure.
                        figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = true };
                        current = start;
                    }
                    break;
                default:
                    figure ??= new PathFigure { StartPoint = current, IsFilled = true };
                    figure.Segments.Add(Segment(op));
                    current = new Point(op.X, op.Y);
                    break;
            }
        }
        if (figure is not null && figure.Segments.Count > 0)
            geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathSegment Segment(PathOp op) => op.Kind switch
    {
        PathOpKind.Line => new LineSegment { Point = new Point(op.X, op.Y) },
        PathOpKind.Cubic => new BezierSegment { Point1 = new Point(op.X1, op.Y1), Point2 = new Point(op.X2, op.Y2), Point3 = new Point(op.X, op.Y) },
        PathOpKind.Quad => new QuadraticBezierSegment { Point1 = new Point(op.X1, op.Y1), Point2 = new Point(op.X, op.Y) },
        PathOpKind.Arc => new ArcSegment
        {
            Point = new Point(op.X, op.Y),
            Size = new Size(op.Rx, op.Ry),
            RotationAngle = op.Rotation,
            IsLargeArc = op.LargeArc,
            SweepDirection = op.Sweep ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
        },
        _ => throw new InvalidOperationException("not a segment: " + op.Kind),
    };

    private static Brush Paint(uint argb) => new SolidColorBrush(ToColor(argb));

    /// <summary>
    /// A designed tile. <paramref name="label"/> is the extension on a Document, Spreadsheet or Presentation tile;
    /// <paramref name="symbol"/> is the extension that is the whole symbol of an Other tile. Tiles under 32 DIP carry no text.
    /// </summary>
    public static FrameworkElement Designed(TileArt art, bool dark, double size, string? label = null, string? symbol = null)
    {
        var canvas = new Canvas { Width = Box, Height = Box, RenderTransform = new ScaleTransform { ScaleX = size / Box, ScaleY = size / Box } };
        var plate = new Rectangle { Width = Box, Height = Box, RadiusX = Box * RadiusFraction, RadiusY = Box * RadiusFraction };
        if (art.GradientEnd is { } end)
        {
            plate.Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = ToColor(art.ColorFor(dark)), Offset = 0 },
                    new GradientStop { Color = ToColor(end), Offset = 1 },
                },
            };
        }
        else
        {
            plate.Fill = Paint(art.ColorFor(dark));
        }
        canvas.Children.Add(plate);
        // The sheen: white at 16 % at the top, none at the bottom, over the tile colour and under the symbol.
        canvas.Children.Add(new Rectangle
        {
            Width = Box, Height = Box, RadiusX = Box * RadiusFraction, RadiusY = Box * RadiusFraction,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(0x29, 255, 255, 255), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(0, 255, 255, 255), Offset = 1 },
                },
            },
        });
        foreach (var layer in art.Layers)
        {
            var path = new Path { Data = Geometry(layer.Path), Opacity = layer.Opacity };
            if (layer.Kind == TileLayerKind.Fill)
            {
                path.Fill = Paint(layer.Paint);
            }
            else
            {
                path.Stroke = Paint(layer.Paint);
                path.StrokeThickness = layer.StrokeWidth;
                path.StrokeStartLineCap = PenLineCap.Round;
                path.StrokeEndLineCap = PenLineCap.Round;
                path.StrokeLineJoin = PenLineJoin.Round;
            }
            canvas.Children.Add(path);
        }
        if (size >= 32)
        {
            if (!string.IsNullOrEmpty(label))
            {
                // Bold white, 11.5 / 64 of the tile, letter spacing 0.3 / 64, centred, baseline at 52.5.
                var text = new TextBlock
                {
                    Text = label, Width = Box, TextAlignment = TextAlignment.Center, FontFamily = TextFont, FontWeight = FontWeights.Bold,
                    FontSize = 11.5, CharacterSpacing = 26, Foreground = Paint(0xFFFFFFFF), LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                };
                Canvas.SetTop(text, 52.5 - 11.5);
                canvas.Children.Add(text);
            }
            else if (!string.IsNullOrEmpty(symbol))
            {
                double fontSize = FolderKinds.OtherSymbolSize(symbol);
                var text = new TextBlock
                {
                    Text = symbol, Width = Box, TextAlignment = TextAlignment.Center, FontFamily = DisplayFont, FontWeight = FontWeights.Bold,
                    FontSize = fontSize, Foreground = Paint(0xFFFFFFFF), LineHeight = fontSize, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                };
                Canvas.SetTop(text, 32 + 0.36 * fontSize - fontSize);
                canvas.Children.Add(text);
            }
        }
        return Host(canvas, size);
    }

    private static Grid Host(UIElement content, double size)
    {
        var host = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        host.Children.Add(content);
        return host;
    }

    /// <summary>The plate shown while an icon or thumbnail is being fetched.</summary>
    public static FrameworkElement Loading(GlassPalette palette, double size)
    {
        var host = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        host.Children.Add(new Rectangle { Width = size, Height = size, RadiusX = size * RadiusFraction, RadiusY = size * RadiusFraction, Fill = palette.Hover });
        return host;
    }

    /// <summary>A real app icon: on the plate at 0.68 of the tile, or edge to edge when the icon is already a filled square.</summary>
    public static FrameworkElement RealIcon(GlassPalette palette, BitmapSource icon, bool fullBleed, double size, double scale)
    {
        var host = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        if (fullBleed)
        {
            host.Children.Add(new Rectangle { Width = size, Height = size, RadiusX = size * RadiusFraction, RadiusY = size * RadiusFraction, Fill = new ImageBrush { ImageSource = icon, Stretch = Stretch.Fill } });
            return host;
        }
        host.Children.Add(new Rectangle { Width = size, Height = size, RadiusX = size * RadiusFraction, RadiusY = size * RadiusFraction, Fill = palette.TilePlate });
        double inner = IconSize(size, scale);
        host.Children.Add(new Image { Source = icon, Width = inner / scale, Height = inner / scale, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return host;
    }

    /// <summary>Physical pixels of a real icon on its plate: 0.68 of the tile.</summary>
    public static int IconPixels(double size, double scale) => Math.Max(1, (int)Math.Round(size * 0.68 * scale));

    private static double IconSize(double size, double scale) => IconPixels(size, scale);

    /// <summary>A photo or a video frame, cover-cropped and clipped to the tile shape; a video also gets the play mark.</summary>
    public static FrameworkElement Frame(BitmapSource picture, double size, bool play)
    {
        var host = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        host.Children.Add(new Rectangle { Width = size, Height = size, RadiusX = size * RadiusFraction, RadiusY = size * RadiusFraction, Fill = new ImageBrush { ImageSource = picture, Stretch = Stretch.Fill } });
        if (play)
        {
            var canvas = new Canvas { Width = Box, Height = Box, RenderTransform = new ScaleTransform { ScaleX = size / Box, ScaleY = size / Box } };
            canvas.Children.Add(new Path { Data = Geometry(GlassTileArt.PlayDisc), Fill = Paint(0x73000000) });
            canvas.Children.Add(new Path { Data = Geometry(GlassTileArt.PlayTriangle), Fill = Paint(0xFFFFFFFF) });
            host.Children.Add(canvas);
        }
        return host;
    }

    /// <summary>Premultiplied BGRA bytes as a bitmap that WinUI shows one pixel per pixel.</summary>
    public static WriteableBitmap ToBitmap(byte[] premultipliedBgra, int width, int height)
    {
        var bitmap = new WriteableBitmap(width, height);
        premultipliedBgra.CopyTo(bitmap.PixelBuffer);
        bitmap.Invalidate();
        return bitmap;
    }
}
