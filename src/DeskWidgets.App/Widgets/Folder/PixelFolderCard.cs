using DeskWidgets.Pixel;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace DeskWidgets.Widgets.Folder;

/// <summary>What the widget needs from a card's drawing, in either theme.</summary>
internal interface IFolderCardView
{
    FrameworkElement Element { get; }

    /// <summary>Draws the folder's current state at this size.</summary>
    void Show(FolderModel model, WidgetSize size);

    /// <summary>What is under a point, in DIPs from the card's top-left.</summary>
    FolderHit HitTest(Point point);

    void SetHover(FolderHit hit);

    void SetPressed(FolderHit hit);

    /// <summary>The drop tint and label for what a hovering drag would do; null hides them.</summary>
    void ShowDropHint(DropVerb? verb);
}

/// <summary>The Big Folder card in the Pixel theme: a <see cref="PixelSurface"/> painted by <see cref="FolderPixelScene"/>.</summary>
internal sealed class PixelFolderCard : IFolderCardView
{
    private readonly IWidgetSite _site;
    private readonly PixelSurface _surface;
    private readonly PixelFolderIcons _icons;
    private FolderModel? _model;
    private WidgetSize _size = WidgetSize.Medium;
    private FolderHit _hover = FolderHit.None, _pressed = FolderHit.None;
    private DropVerb? _drop;
    private FolderPixelScene.CardPlan? _plan;
    private FolderPixelView? _view;

    public PixelFolderCard(IWidgetSite site)
    {
        _site = site;
        _icons = new PixelFolderIcons(() => _surface!.Invalidate());
        _surface = new PixelSurface(Paint);
    }

    public FrameworkElement Element => _surface;

    public void Show(FolderModel model, WidgetSize size)
    {
        _model = model;
        _size = size;
        var (width, height) = PixelCard.ArtSize(size);
        _surface.SetArtSize(width, height);
        Replan();
        _surface.Invalidate();
    }

    private void Replan()
    {
        if (_model is null)
            return;
        _view = new FolderPixelView(_model.Name, _model.Path, _model.Items, _model.Exists, _size, _hover, _pressed, _drop, _icons);
        _plan = FolderPixelScene.Plan(_view);
    }

    private void Paint(PixelCanvas canvas, PixelPalette palette)
    {
        if (_plan is null || _view is null)
            return;
        FolderPixelScene.Draw(canvas, palette, _view, _plan);
    }

    public FolderHit HitTest(Point point)
    {
        if (_plan is null)
            return FolderHit.None;
        // Art pixels are whole screen pixels per art pixel: DIPs to screen pixels to art pixels.
        double scale = _site.Scale;
        int factor = PixelCard.Factor(scale);
        int x = (int)Math.Floor(point.X * scale / factor), y = (int)Math.Floor(point.Y * scale / factor);
        return FolderPixelScene.HitTest(_plan, x, y);
    }

    public void SetHover(FolderHit hit)
    {
        if (hit == _hover)
            return;
        _hover = hit;
        Redraw();
    }

    public void SetPressed(FolderHit hit)
    {
        if (hit == _pressed)
            return;
        _pressed = hit;
        Redraw();
    }

    public void ShowDropHint(DropVerb? verb)
    {
        if (verb == _drop)
            return;
        _drop = verb;
        Redraw();
    }

    private void Redraw()
    {
        Replan();
        _surface.Invalidate();
    }
}
