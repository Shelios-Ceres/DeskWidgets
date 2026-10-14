using DeskWidgets.Pixel;
using DeskWidgets.Widgets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskWidgets.Hosting;

/// <summary>
/// The card every widget sits in. Glass: a thin theme tint and a diagonal
/// sheen, drawn over the live blur that <see cref="GlassBackdrop"/> provides
/// behind the window. Pixel: an opaque pixel-art panel (<see cref="PixelCard"/>).
/// </summary>
public sealed partial class WidgetFrame : UserControl
{
    private PixelSurface? _pixelPanel;

    public WidgetFrame()
    {
        InitializeComponent();
    }

    public void SetContent(UIElement content) => ContentHost.Content = content;

    /// <summary>Shows the card for <paramref name="theme"/> at <paramref name="size"/>.</summary>
    public void SetCard(WidgetTheme theme, WidgetSize size)
    {
        bool pixel = theme == WidgetTheme.Pixel;
        GlassTint.Visibility = pixel ? Visibility.Collapsed : Visibility.Visible;
        GlassSheen.Visibility = GlassTint.Visibility;
        if (pixel && _pixelPanel is null)
        {
            _pixelPanel = new PixelSurface(PixelCard.Draw) { IsHitTestVisible = false };
            Root.Children.Insert(0, _pixelPanel);
        }
        if (_pixelPanel is null)
            return;
        _pixelPanel.Visibility = pixel ? Visibility.Visible : Visibility.Collapsed;
        var (width, height) = PixelCard.ArtSize(size);
        _pixelPanel.SetArtSize(width, height);
    }
}
