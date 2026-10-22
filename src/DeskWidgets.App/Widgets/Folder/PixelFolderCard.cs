using DeskWidgets.Motion;
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

    /// <summary>The drop tint and label for what a hovering drag would do; null hides them (with <paramref name="snap"/>: at once, for a drop).</summary>
    void ShowDropHint(DropVerb? verb, bool snap = false);

    /// <summary>A click completed on <paramref name="hit"/> (it is about to open): the Pixel card flashes it once.</summary>
    void Confirm(FolderHit hit);
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
    private readonly StepAnimator _listSteps = new();
    private readonly StepAnimator _dropSteps = new();
    private readonly StepAnimator _flashSteps = new();
    private ListMotion? _motion;
    private DropVerb? _dropDrawn; // the verb the tint still shows (it outlives _drop for the frame the tint fades out in)
    private int _dropPhase;
    private FolderHit _flash;
    private WidgetSize? _builtSize;

    public PixelFolderCard(IWidgetSite site)
    {
        _site = site;
        _icons = new PixelFolderIcons(() => _surface!.Invalidate());
        _surface = new PixelSurface(Paint);
        _surface.Unloaded += (_, _) =>
        {
            _listSteps.Cancel();
            _dropSteps.Cancel();
            _flashSteps.Cancel();
        };
    }

    public FrameworkElement Element => _surface;

    public void Show(FolderModel model, WidgetSize size)
    {
        _model = model;
        _size = size;
        var (width, height) = PixelCard.ArtSize(size);
        _surface.SetArtSize(width, height);
        var oldView = _view;
        var oldPlan = _plan;
        _listSteps.Cancel();
        _motion = null;
        Replan();
        // A2 and A4: a change of what the card shows (same size) moves, pops and leaves for three frames; a new size or a first look does not.
        bool sameSize = _builtSize == size;
        _builtSize = size;
        if (sameSize && oldView is not null && oldPlan is not null && MotionSettings.Enabled)
            StartListMotion(oldView, oldPlan);
        _surface.Invalidate();
    }

    private void StartListMotion(FolderPixelView oldView, FolderPixelScene.CardPlan oldPlan)
    {
        if (_view is null || _plan is null || !oldView.Exists || !_view.Exists || _surface.LastCanvas is not { } canvas)
            return;
        var oldKeys = oldView.Items.Take(oldPlan.Cells.Shown).Select(i => i.FileName).ToList();
        var newKeys = _view.Items.Take(_plan.Cells.Shown).Select(i => i.FileName).ToList();
        if (oldKeys.SequenceEqual(newKeys, StringComparer.OrdinalIgnoreCase))
            return;
        var palette = PixelPalette.For(_surface.ActualTheme == ElementTheme.Dark);
        (int, int)? PlaceIn(FolderPixelScene.CardPlan plan, int cell) => (plan.Columns[cell % plan.Cells.Columns], plan.Rows[cell / plan.Cells.Columns]);
        LeavingCell? Picture(int cell)
        {
            int x = oldPlan.Columns[cell % oldPlan.Cells.Columns] - 1, y = oldPlan.Rows[cell / oldPlan.Cells.Columns] - 2;
            var picture = PixelMotion.Snapshot(canvas, x, y, FolderPixelScene.CellWidth + 2, oldPlan.CellHeight + 4);
            for (int i = 0; i < picture.Pixels.Length; i++)
                if (picture.Pixels[i] == palette.Panel)
                    picture.Pixels[i] = 0; // only the cell's own pixels leave, not the card behind them
            return new LeavingCell(picture, x, y);
        }
        var motion = ListMotion.Plan(oldKeys, newKeys, cell => PlaceIn(oldPlan, cell), cell => PlaceIn(_plan, cell), Picture);
        if (motion is null)
            return;
        _motion = motion;
        _view = _view with { Motion = motion };
        _listSteps.Start(ListMotion.Frames, frame =>
        {
            motion.Frame = frame + 1;
            if (frame > 0)
                Redraw();
        }, () => _motion = null);
    }

    private void Replan()
    {
        if (_model is null)
            return;
        _view = new FolderPixelView(_model.Name, _model.Path, _model.Items, _model.Exists, _size, _hover, _pressed, _dropDrawn, _icons, _dropPhase, _motion, _flash);
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

    /// <summary>
    /// A2a: a drag over the card shows the sparse tint (1 dot in 16, no label), then the tint as it is with the label (two frames); leaving
    /// shows the sparse tint, then none. A modifier key that changes the verb changes the label at once; a drop snaps it all off.
    /// </summary>
    public void ShowDropHint(DropVerb? verb, bool snap = false)
    {
        if (verb == _drop)
            return;
        bool entering = _drop is null && verb is not null, leaving = _drop is not null && verb is null;
        _drop = verb;
        _dropSteps.Cancel();
        if (!MotionSettings.Enabled || snap || (!entering && !leaving))
        {
            _dropDrawn = verb;
            _dropPhase = verb is null ? 0 : 2;
            Redraw();
            return;
        }
        if (entering)
        {
            _dropDrawn = verb;
            _dropSteps.Start(2, frame =>
            {
                _dropPhase = frame + 1;
                Redraw();
            });
        }
        else
        {
            _dropSteps.Start(2, frame =>
            {
                _dropPhase = frame == 0 ? 1 : 0;
                if (frame > 0)
                    _dropDrawn = null;
                Redraw();
            });
        }
    }

    /// <summary>A3: the FLASH frame of a completed click, then the hover look.</summary>
    public void Confirm(FolderHit hit)
    {
        if (hit.Kind == FolderHitKind.None)
            return;
        _flashSteps.Start(2, frame =>
        {
            _flash = frame == 0 ? hit : FolderHit.None;
            Redraw();
        });
    }

    private void Redraw()
    {
        Replan();
        _surface.Invalidate();
    }
}
