using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using DeskWidgets.Motion;

namespace DeskWidgets.Pixel;

/// <summary>
/// Shows pixel art painted on a <see cref="PixelCanvas"/>: enlarged by a
/// whole number of screen pixels per art pixel (<see cref="PixelCard.Factor"/>)
/// and displayed one bitmap pixel per screen pixel, so nothing ever filters
/// an edge soft. Repaints on <see cref="Invalidate"/>, when the system theme
/// flips between light and dark, and when the DPI changes.
///
/// It also holds the one DISSOLVE (Phase 12, B7): when the system theme flips, or when the surface is new or has a new size inside a
/// <see cref="DissolveIn"/> scope, the new picture comes on over 4 frames, wherever the 4×4 Bayer value is below 4 × the frame, with the
/// old picture (or an empty card) elsewhere. Every widget gets it by drawing on a PixelSurface.
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

    // DISSOLVE: the picture it starts from, which frame it is at (1 to 4, 0 at rest), the picture shown while it runs, and what asked for it.
    private static int s_dissolveScope;
    private PixelCanvas? _dissolveFrom;
    private PixelCanvas? _shown;
    private StepAnimator? _steps;
    private int _dissolveFrame;
    private bool _dissolveNext, _dissolveEmpty;

    /// <summary>Surfaces made, or given a new size, while <paramref name="action"/> runs come on with a DISSOLVE from an empty card (a size change, a theme switch).</summary>
    public static void DissolveIn(Action action)
    {
        s_dissolveScope++;
        try
        {
            action();
        }
        finally
        {
            s_dissolveScope--;
        }
    }

    /// <param name="paint">Draws one frame on a cleared canvas of the art size.</param>
    public PixelSurface(Action<PixelCanvas, PixelPalette> paint)
    {
        _paint = paint;
        _dissolveEmpty = s_dissolveScope > 0 && MotionSettings.Enabled;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_image);
        Loaded += (_, _) =>
        {
            Attach(XamlRoot);
            Invalidate();
        };
        Unloaded += (_, _) =>
        {
            Attach(null);
            EndDissolve();
        };
        ActualThemeChanged += (_, _) =>
        {
            BeginThemeDissolve();
            Invalidate();
        };
    }

    /// <summary>What was painted last (valid until the next repaint clears it): a motion that has to show a cell as it was takes its picture from here.</summary>
    internal PixelCanvas? LastCanvas => _canvas;

    /// <summary>The drawing's size in art pixels.</summary>
    public void SetArtSize(int width, int height)
    {
        if (_artSize == (width, height))
            return;
        _artSize = (width, height);
        if (s_dissolveScope > 0 && MotionSettings.Enabled)
            _dissolveEmpty = true;
        Invalidate();
    }

    /// <summary>Repaints now, without waiting for the dispatcher (a stepped motion paints once per frame).</summary>
    public void RenderNow() => Render();

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

        bool start = false;
        if (_dissolveEmpty && MotionSettings.Enabled)
        {
            _dissolveFrom = new PixelCanvas(width, height);
            start = true;
        }
        else if (_dissolveNext && _dissolveFrom is { } from && from.Width == width && from.Height == height && MotionSettings.Enabled)
        {
            start = true;
        }
        _dissolveNext = _dissolveEmpty = false;
        if (start)
        {
            StartDissolve();
            return;
        }
        if (_dissolveFrame > 0 && _dissolveFrom is { } running && running.Width == width && running.Height == height)
        {
            PresentDissolve(factor); // repainted while it runs (a clock ticked): keep the frame it is at
            return;
        }
        EndDissolve();
        Present(_canvas, factor);
    }

    /// <summary>A system theme flip: the picture on screen now (the old theme's) is where the dissolve starts.</summary>
    private void BeginThemeDissolve()
    {
        if (!MotionSettings.Enabled || _canvas is null || _dissolveEmpty)
            return;
        var shown = _dissolveFrame > 0 ? _shown : _canvas;
        if (shown is null)
            return;
        var copy = new PixelCanvas(shown.Width, shown.Height);
        Array.Copy(shown.Pixels, copy.Pixels, shown.Pixels.Length);
        _dissolveFrom = copy;
        _dissolveNext = true;
    }

    private void StartDissolve()
    {
        _steps ??= new StepAnimator();
        _dissolveFrame = 1;
        _steps.Start(4, frame =>
        {
            _dissolveFrame = frame + 1;
            if (_canvas is not null && XamlRoot is not null)
                PresentDissolve(PixelCard.Factor(_scale));
        }, EndDissolve);
    }

    private void EndDissolve()
    {
        _steps?.Cancel();
        _dissolveFrame = 0;
        _dissolveFrom = null;
        _shown = null;
    }

    /// <summary>Frame k of the dissolve: the new picture where the Bayer value is below 4k, the old one elsewhere.</summary>
    private void PresentDissolve(int factor)
    {
        var from = _dissolveFrom;
        var to = _canvas;
        if (from is null || to is null)
            return;
        if (_shown is null || _shown.Width != to.Width || _shown.Height != to.Height)
            _shown = new PixelCanvas(to.Width, to.Height);
        int limit = _dissolveFrame * 4;
        for (int y = 0; y < to.Height; y++)
        {
            for (int x = 0; x < to.Width; x++)
                _shown.Pixels[y * to.Width + x] = PixelMotion.BayerAt(x, y) < limit ? to.Pixels[y * to.Width + x] : from.Pixels[y * to.Width + x];
        }
        Present(_shown, factor);
    }

    private void Present(PixelCanvas canvas, int factor)
    {
        int width = canvas.Width, height = canvas.Height;
        int pixelWidth = width * factor, pixelHeight = height * factor;
        if (_bitmap is null || _bitmap.PixelWidth != pixelWidth || _bitmap.PixelHeight != pixelHeight)
        {
            _bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
            _pixels = new byte[pixelWidth * pixelHeight * 4];
            _image.Source = _bitmap;
        }
        canvas.CopyScaled(_pixels, factor);
        _pixels.CopyTo(_bitmap.PixelBuffer);
        _bitmap.Invalidate();
        // Sized in DIPs so that one bitmap pixel lands on exactly one screen pixel.
        _image.Width = pixelWidth / _scale;
        _image.Height = pixelHeight / _scale;
    }
}
