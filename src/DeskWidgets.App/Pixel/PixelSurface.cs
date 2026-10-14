using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeskWidgets.Pixel;

/// <summary>
/// Shows pixel art painted on a <see cref="PixelCanvas"/>: enlarged by a
/// whole number of screen pixels per art pixel (<see cref="PixelCard.Factor"/>)
/// and displayed one bitmap pixel per screen pixel, so nothing ever filters
/// an edge soft. Repaints on <see cref="Invalidate"/>, when the system theme
/// flips between light and dark, and when the DPI changes.
/// </summary>
internal sealed partial class PixelSurface : Grid
{
    private readonly Image _image = new()
    {
        Stretch = Stretch.Fill,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private readonly Action<PixelCanvas, PixelPalette> _paint;
    private (int Width, int Height) _artSize = (1, 1);
    private PixelCanvas? _canvas;
    private WriteableBitmap? _bitmap;
    private byte[] _pixels = [];
    private XamlRoot? _root;
    private double _scale;
    private bool _queued;

    /// <param name="paint">Draws one frame on a cleared canvas of the art size.</param>
    public PixelSurface(Action<PixelCanvas, PixelPalette> paint)
    {
        _paint = paint;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_image);
        Loaded += (_, _) =>
        {
            Attach(XamlRoot);
            Invalidate();
        };
        Unloaded += (_, _) => Attach(null);
        ActualThemeChanged += (_, _) => Invalidate();
    }

    /// <summary>The drawing's size in art pixels.</summary>
    public void SetArtSize(int width, int height)
    {
        if (_artSize == (width, height))
            return;
        _artSize = (width, height);
        Invalidate();
    }

    /// <summary>Repaints soon; several requests in a row paint once.</summary>
    public void Invalidate()
    {
        if (_queued)
            return;
        _queued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _queued = false;
            Render();
        });
    }

    private void Attach(XamlRoot? root)
    {
        if (_root is not null)
            _root.Changed -= OnRootChanged;
        _root = root;
        if (_root is not null)
            _root.Changed += OnRootChanged;
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (sender.RasterizationScale != _scale)
            Invalidate();
    }

    private void Render()
    {
        if (XamlRoot is not { } root)
            return; // not in a window yet; Loaded paints
        _scale = root.RasterizationScale;
        int factor = PixelCard.Factor(_scale);
        var (width, height) = _artSize;
        if (_canvas is null || _canvas.Width != width || _canvas.Height != height)
            _canvas = new PixelCanvas(width, height);
        else
            _canvas.Clear();
        _paint(_canvas, PixelPalette.For(ActualTheme == ElementTheme.Dark));

        int pixelWidth = width * factor, pixelHeight = height * factor;
        if (_bitmap is null || _bitmap.PixelWidth != pixelWidth || _bitmap.PixelHeight != pixelHeight)
        {
            _bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
            _pixels = new byte[pixelWidth * pixelHeight * 4];
            _image.Source = _bitmap;
        }
        _canvas.CopyScaled(_pixels, factor);
        _pixels.CopyTo(_bitmap.PixelBuffer);
        _bitmap.Invalidate();
        // Sized in DIPs so that one bitmap pixel lands on exactly one screen pixel.
        _image.Width = pixelWidth / _scale;
        _image.Height = pixelHeight / _scale;
    }
}
