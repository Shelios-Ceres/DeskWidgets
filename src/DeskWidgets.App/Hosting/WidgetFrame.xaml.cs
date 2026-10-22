using DeskWidgets.Pixel;
using DeskWidgets.Widgets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DeskWidgets.Motion;

namespace DeskWidgets.Hosting;

/// <summary>
/// The card every widget sits in. Glass: a thin theme tint and a diagonal
/// sheen, drawn over the live blur that <see cref="GlassBackdrop"/> provides
/// behind the window. Pixel: an opaque pixel-art panel (<see cref="PixelCard"/>).
/// </summary>
public sealed partial class WidgetFrame : UserControl
{
    private PixelSurface? _pixelPanel;
    private double _pixelShare = 1; // GROW and SHRINK (B8): how much of the card is drawn, centred; the content waits for the whole card

    public WidgetFrame()
    {
        InitializeComponent();
        // B7, light to dark: the tint and the sheen blend to the new colours (Blend, 250 ms); text, tiles and plates change at once.
        GlassTint.BackgroundTransition = new BrushTransition { Duration = GlassMotion.Blend };
        GlassSheen.BackgroundTransition = new BrushTransition { Duration = GlassMotion.Blend };
        RefreshBlend();
        ActualThemeChanged += (_, _) => RefreshBlend();
    }

    /// <summary>The blend follows "Animation effects": read again for the next light/dark change.</summary>
    public void RefreshBlend()
    {
        var duration = MotionSettings.Enabled ? GlassMotion.Blend : TimeSpan.Zero;
        GlassTint.BackgroundTransition.Duration = duration;
        GlassSheen.BackgroundTransition.Duration = duration;
    }

    /// <summary>The widget's content, for the motion that fades it.</summary>
    internal FrameworkElement ContentElement => ContentHost;

    /// <summary>B7, a size change: the tint and the sheen laid out at the new card size from the first frame, top-left (the clip shows how much of them); null gives them back to the card.</summary>
    internal void PinLayers(System.Numerics.Vector2? size)
    {
        foreach (var layer in new FrameworkElement[] { GlassTint, GlassSheen })
        {
            if (size is { } pinned)
            {
                layer.HorizontalAlignment = HorizontalAlignment.Left;
                layer.VerticalAlignment = VerticalAlignment.Top;
                layer.Width = pinned.X;
                layer.Height = pinned.Y;
            }
            else
            {
                layer.ClearValue(WidthProperty);
                layer.ClearValue(HeightProperty);
                layer.HorizontalAlignment = HorizontalAlignment.Stretch;
                layer.VerticalAlignment = VerticalAlignment.Stretch;
            }
        }
    }

    /// <summary>
    /// B7, a size change: lays the content out for the new size (in DIPs) while the card is still at the union of both, anchored top-left;
    /// null gives the layout back to the card.
    /// </summary>
    internal void PinContent(System.Numerics.Vector2? size)
    {
        if (size is { } pinned)
        {
            ContentHost.HorizontalAlignment = HorizontalAlignment.Left;
            ContentHost.VerticalAlignment = VerticalAlignment.Top;
            ContentHost.Width = pinned.X;
            ContentHost.Height = pinned.Y;
        }
        else
        {
            ContentHost.ClearValue(WidthProperty);
            ContentHost.ClearValue(HeightProperty);
            ContentHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            ContentHost.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    public void SetContent(UIElement content) => ContentHost.Content = content;

    /// <summary>
    /// Pixel B8: draws the empty card at <paramref name="share"/> of its size (1 is the whole card with its content) and repaints now.
    /// The content is hidden until the whole card is there.
    /// </summary>
    public void SetPixelShare(double share)
    {
        if (share == _pixelShare)
            return;
        _pixelShare = share;
        ContentHost.Opacity = share >= 1 ? 1 : 0;
        _pixelPanel?.RenderNow();
    }

    /// <summary>Shows the card for <paramref name="theme"/> at <paramref name="size"/>.</summary>
    public void SetCard(WidgetTheme theme, WidgetSize size)
    {
        bool pixel = theme == WidgetTheme.Pixel;
        GlassTint.Visibility = pixel ? Visibility.Collapsed : Visibility.Visible;
        GlassSheen.Visibility = GlassTint.Visibility;
        if (pixel && _pixelPanel is null)
        {
            _pixelPanel = new PixelSurface((canvas, palette) => PixelCard.DrawShare(canvas, palette, _pixelShare)) { IsHitTestVisible = false };
            Root.Children.Insert(0, _pixelPanel);
        }
        if (_pixelPanel is null)
            return;
        _pixelPanel.Visibility = pixel ? Visibility.Visible : Visibility.Collapsed;
        var (width, height) = PixelCard.ArtSize(size);
        _pixelPanel.SetArtSize(width, height);
    }
}
