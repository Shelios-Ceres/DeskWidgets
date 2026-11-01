using System.Numerics;
using DeskWidgets.Motion;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace DeskWidgets.Dock;

/// <summary>
/// What the Glass dock draws (HANDOFF 2, 3 and 5): the bar's tint and sheen over the frost, the icons, the gap, the empty text and the hover
/// label, all in the real window's DIPs. Every moving thing is placed by its visual's Translation and moved by a Composition key frame from
/// where it is, so a new state cuts the last one short; with "Animation effects" off every moment is one frame. Nothing runs when still.
/// </summary>
internal sealed partial class GlassDockSurface
{
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text");
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons");
    private const double LabelRadius = 13;
    private const double TileRadiusFraction = 0.225;

    /// <summary>One icon: a fixed box (placed and slid), the picture inside it (lifted, pressed, entering, leaving), and the tile (dimmed when missing).</summary>
    private sealed class Slot
    {
        public string Key = "";
        public DockItemView View = null!;
        public Canvas Root = null!;
        public Grid Lift = null!;
        public Grid TileHost = null!;
        public string? TileState;
        public bool Loading;
        public double Size;

        // Where the box is going, in window DIPs (NaN: not placed yet).
        public double X = double.NaN, Y = double.NaN;
    }

    private enum LabelState
    {
        Hidden,
        Shown,
        Hiding,
    }

    private readonly Grid _root = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Canvas _stage = new() { IsHitTestVisible = false };
    private readonly Grid _barLayer = new() { IsHitTestVisible = false };
    private readonly Border _tint = new();
    private readonly Border _sheen = new();
    private readonly Rectangle _gapPlate = new() { IsHitTestVisible = false };
    private readonly Grid _emptyHost = new() { IsHitTestVisible = false };
    private readonly FontIcon _emptyGlyph = new() { Glyph = "", FontFamily = IconFont, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _emptyText = new() { FontFamily = TextFont, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _label = new() { IsHitTestVisible = false, Height = DockGeometry.LabelHeightDip };
    private readonly Border _labelTint = new() { CornerRadius = new CornerRadius(LabelRadius) };
    private readonly Border _labelSheen = new() { CornerRadius = new CornerRadius(LabelRadius) };
    private readonly TextBlock _labelText = new()
    {
        FontFamily = TextFont, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _measure = new() { FontFamily = TextFont, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap };
    private CompositionRoundedRectangleGeometry? _barClip;
    private CompositionGeometricClip? _barClipObject;

    private readonly List<Slot> _slots = [];
    private readonly List<Slot> _leaving = [];
    private readonly HashSet<FrameClock.Ticket> _timers = [];
    private GlassDockTiles? _tiles;
    private IReadOnlyList<DockItemView> _views = [];
    private DockIconSize _size = DockIconSize.Medium;
    private int _gap = -1;
    private string? _hoverKey, _pressedKey, _dragKey;
    private DipRect _barTarget, _barRegion;
    private bool _barPlaced;
    private int _barGeneration;
    private DipRect _labelRect;
    private DipRect? _labelRegion;
    private LabelState _labelState;
    private int _labelGeneration;
    private bool _gapShown, _emptyShown;
    private bool _snapNext = true;
    private bool _repaintQueued;
    private FrameClock.Ticket? _layoutDelay;

    private double WindowWidthDip => _actual.Width / Scale;

    private double BarLeftDip => (_bar.X - _actual.X) / Scale;

    private double BarTopDip => (_bar.Y - _actual.Y) / Scale;

    private double ScreenLeftDip => (_work.X - _actual.X) / Scale;

    private double ScreenRightDip => (_work.Right - _actual.X) / Scale;

    private double IconDip => DockGeometry.IconDip(_size);

    private bool Animate => _shown && MotionSettings.Enabled;

    private GlassPalette Palette => GlassPalette.For(_root.ActualTheme);

    private bool Dark => _root.ActualTheme == ElementTheme.Dark;

    // ---------------------------------------------------------------- the scene

    private void BuildScene()
    {
        _tiles = new GlassDockTiles(RepaintSoon);
        _barLayer.Children.Add(_tint);
        _barLayer.Children.Add(_sheen);
        Canvas.SetZIndex(_barLayer, 0);
        _stage.Children.Add(_barLayer);
        // The bar's tint and sheen cover the window and are cut to the bar by a rounded clip, the frost's twin (it moves with it in B7).
        var barVisual = GlassMotion.VisualOf(_barLayer);
        _barClip = barVisual.Compositor.CreateRoundedRectangleGeometry();
        _barClipObject = barVisual.Compositor.CreateGeometricClip(_barClip);
        barVisual.Clip = _barClipObject;

        // Hidden things are hidden by their visual's opacity: an element whose Opacity property is 0 is not drawn at all, and cannot fade.
        GlassMotion.VisualOf(_gapPlate).Opacity = 0;
        Canvas.SetZIndex(_gapPlate, 1);
        _stage.Children.Add(_gapPlate);

        _emptyText.Text = Strings.DockEmpty;
        _emptyHost.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { _emptyGlyph, _emptyText },
        });
        GlassMotion.VisualOf(_emptyHost).Opacity = 0;
        Canvas.SetZIndex(_emptyHost, 1);
        _stage.Children.Add(_emptyHost);

        _label.Children.Add(_labelTint);
        _label.Children.Add(_labelSheen);
        _label.Children.Add(_labelText);
        GlassMotion.VisualOf(_label).Opacity = 0;
        Canvas.SetZIndex(_label, 4);
        _stage.Children.Add(_label);

        _root.Children.Add(_stage);
        ApplyThemeBrushes();
    }

    /// <summary>The scene and the frost take the real window's size.</summary>
    private void SizeScene()
    {
        double width = _actual.Width / Scale, height = _actual.Height / Scale;
        _stage.Width = _barLayer.Width = width;
        _stage.Height = _barLayer.Height = height;
        _glass.SetWindowSize(new Vector2(_actual.Width, _actual.Height));
    }

    /// <summary>The cards' glass (WidgetFrame): a 24 % tint and the diagonal sheen. No outline, no shadow.</summary>
    private void ApplyThemeBrushes()
    {
        bool dark = Dark;
        var blend = MotionSettings.Enabled ? GlassMotion.Blend : TimeSpan.Zero;
        foreach (var tint in new[] { _tint, _labelTint })
        {
            tint.BackgroundTransition = new BrushTransition { Duration = blend };
            tint.Background = new SolidColorBrush(dark ? Colors.Black : Colors.White) { Opacity = 0.24 };
        }
        _sheen.Background = Sheen(dark, absolute: true);
        _labelSheen.Background = Sheen(dark, absolute: false);
        if (_barPlaced)
            UpdateSheen(_barTarget);
        var palette = Palette;
        _labelText.Foreground = palette.Primary;
        _emptyText.Foreground = palette.Primary;
        _emptyGlyph.Foreground = palette.Primary;
        _gapPlate.Fill = palette.Hover;
    }

    private static LinearGradientBrush Sheen(bool dark, bool absolute) => new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0.7, 1),
        MappingMode = absolute ? BrushMappingMode.Absolute : BrushMappingMode.RelativeToBoundingBox,
        GradientStops =
        {
            new GradientStop { Color = Windows.UI.Color.FromArgb(dark ? (byte)0x1F : (byte)0x38, 255, 255, 255), Offset = 0 },
            new GradientStop { Color = Windows.UI.Color.FromArgb(0, 255, 255, 255), Offset = 0.6 },
        },
    };

    /// <summary>The bar's sheen runs across the bar (not the window): its points are the bar's, in the layer's coordinates.</summary>
    private void UpdateSheen(DipRect bar)
    {
        if (_sheen.Background is LinearGradientBrush sheen)
        {
            sheen.StartPoint = new Point(bar.X, bar.Y);
            sheen.EndPoint = new Point(bar.X + 0.7 * bar.Width, bar.Bottom);
        }
    }

    private void OnThemeChanged(FrameworkElement sender, object args)
    {
        if (_disposed)
            return;
        ApplyThemeBrushes();
        foreach (var slot in _slots)
            UpdateTile(slot, fade: false);
    }

    // ---------------------------------------------------------------- the icons

    public void SetItems(IReadOnlyList<DockItemView> items, DockIconSize size, DockChange change)
    {
        if (_disposed)
            return;
        bool delayed = _layoutDelay is not null;
        if (_layoutDelay is { } pending)
        {
            pending.Stop();
            _timers.Remove(pending);
            _layoutDelay = null;
        }
        bool animate = Animate && !_snapNext;
        bool sizeChanged = size != _size;
        _size = size;
        _views = items;
        if (_gap > items.Count)
            _gap = items.Count;
        if (change.Kind == DockChangeKind.Reset || !animate && (sizeChanged || _snapNext) || sizeChanged && change.Kind != DockChangeKind.Resized)
            Rebuild();
        else if (sizeChanged)
            Resize();
        else
            Update(change.Kind, animate, delayed);
    }

    /// <summary>Every icon made again in its place, at once (the first draw, a language or screen change, no motion).</summary>
    private void Rebuild()
    {
        _snapNext = false;
        foreach (var slot in _slots.Concat(_leaving))
            _stage.Children.Remove(slot.Root);
        _slots.Clear();
        _leaving.Clear();
        _hoverKey = _pressedKey = _dragKey = null;
        foreach (var view in _views)
            _slots.Add(NewSlot(view));
        _barPlaced = false;
        Layout(TimeSpan.Zero);
        // The controller shows the label again right after (it re-sends the hover).
        HideLabel(snap: true);
    }

    /// <summary>
    /// D13: the old icons fade out where they are (Quick); the new ones, laid out at the new size, fade in from the Move's half-way point
    /// (Enter); the bar's frost and tint go from the old size to the new one about its centre (Move), as B7.
    /// </summary>
    private void Resize()
    {
        foreach (var slot in _slots)
        {
            GlassMotion.FadeTo(slot.Lift, 0, GlassMotion.Quick, GlassEase.In);
            Leave(slot, GlassMotion.Quick);
        }
        _slots.Clear();
        _hoverKey = _pressedKey = _dragKey = null;
        foreach (var view in _views)
        {
            var slot = NewSlot(view);
            GlassMotion.Preset(slot.Lift, 0, 1, Centre(slot));
            _slots.Add(slot);
        }
        Layout(GlassMotion.Move);
        foreach (var slot in _slots)
            GlassMotion.FadeTo(slot.Lift, 1, GlassMotion.Enter, GlassEase.Out, GlassMotion.Move / 2);
    }

    /// <summary>The same icons, some added, removed or moved (D7, D10, D11), or a redraw only.</summary>
    private void Update(DockChangeKind kind, bool animate, bool delayed)
    {
        var old = new Dictionary<string, Slot>();
        foreach (var slot in _slots)
            old.TryAdd(slot.Key, slot);
        var next = new List<Slot>();
        var entering = new List<Slot>();
        foreach (var view in _views)
        {
            if (old.Remove(view.Item.Key, out var slot))
            {
                slot.View = view;
                UpdateTile(slot, fade: true);
            }
            else
            {
                slot = NewSlot(view);
                entering.Add(slot);
            }
            next.Add(slot);
        }
        _slots.Clear();
        _slots.AddRange(next);
        foreach (var gone in old.Values)
            Remove(gone, animate);
        if (Find(_hoverKey) is null)
            _hoverKey = null;
        if (Find(_pressedKey) is null)
            _pressedKey = null;
        if (Find(_dragKey) is null)
            _dragKey = null;

        bool moves = animate && (kind is DockChangeKind.Added or DockChangeKind.Removed or DockChangeKind.Moved || delayed);
        if (kind == DockChangeKind.Removed && animate && old.Count > 0)
        {
            // D11: the icon goes first (Exit), then the others close up and the bar shortens (Move).
            _layoutDelay = After((int)GlassMotion.Exit.TotalMilliseconds, () =>
            {
                _layoutDelay = null;
                Layout(GlassMotion.Move);
            });
        }
        else
        {
            Layout(moves ? GlassMotion.Move : TimeSpan.Zero);
        }
        // D10: a new icon enters in its place (opacity and 85 % to 100 %, Enter) while the others make room.
        foreach (var slot in entering)
        {
            if (animate && kind == DockChangeKind.Added)
                GlassMotion.EnterItem(slot.Lift, Centre(slot));
        }
    }

    private Slot NewSlot(DockItemView view)
    {
        double icon = IconDip;
        var tileHost = new Grid { Width = icon, Height = icon, IsHitTestVisible = false };
        var lift = new Grid { Width = icon, Height = icon, IsHitTestVisible = false, Children = { tileHost } };
        var root = new Canvas { Width = icon, Height = icon, IsHitTestVisible = false, Children = { lift } };
        Canvas.SetZIndex(root, 2);
        var slot = new Slot { Key = view.Item.Key, View = view, Root = root, Lift = lift, TileHost = tileHost, Size = icon };
        GlassMotion.VisualOf(lift).CenterPoint = new Vector3(Centre(slot), 0);
        UpdateTile(slot, fade: false);
        _stage.Children.Add(root);
        return slot;
    }

    private static Vector2 Centre(Slot slot) => new((float)slot.Size / 2, (float)slot.Size / 2);

    /// <summary>Redraws a tile when what it shows changed (a new icon, the theme); a missing program is drawn at 40 % (D14, still).</summary>
    private void UpdateTile(Slot slot, bool fade)
    {
        slot.TileHost.Opacity = slot.View.Missing ? 0.4 : 1;
        var (state, build, loading) = _tiles!.Describe(slot.View.Item, Palette, Dark, slot.Size, Scale);
        if (state == slot.TileState)
            return;
        bool wasLoading = slot.Loading;
        slot.TileState = state;
        slot.Loading = loading;
        var tile = build();
        slot.TileHost.Children.Clear();
        slot.TileHost.Children.Add(tile);
        if (fade && wasLoading && !loading && MotionSettings.Enabled)
        {
            // The picture was on its way: it fades in over the plate that stood in for it (as GlassTileProvider does).
            GlassMotion.Preset(tile, 0, 1, Vector2.Zero);
            GlassMotion.FadeTo(tile, 1, GlassMotion.Exit, GlassEase.Out);
        }
    }

    /// <summary>Icons arrive one after another: the tiles are looked at once for all of them.</summary>
    private void RepaintSoon()
    {
        if (_repaintQueued || _disposed)
            return;
        _repaintQueued = true;
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            _repaintQueued = false;
            if (_disposed)
                return;
            foreach (var slot in _slots)
                UpdateTile(slot, fade: true);
        });
    }

    /// <summary>D11: an icon that went away goes to opacity 0 and 85 % (Exit), then leaves the tree.</summary>
    private void Remove(Slot slot, bool animate)
    {
        if (!animate)
        {
            _stage.Children.Remove(slot.Root);
            return;
        }
        GlassMotion.LeaveItem(slot.Lift, Centre(slot));
        Leave(slot, GlassMotion.LeaveDuration);
    }

    private void Leave(Slot slot, TimeSpan after)
    {
        _leaving.Add(slot);
        After((int)after.TotalMilliseconds + 40, () =>
        {
            _leaving.Remove(slot);
            _stage.Children.Remove(slot.Root);
        });
    }

    private Slot? Find(string? key) => key is null ? null : _slots.FirstOrDefault(slot => slot.Key == key);

    /// <summary>Puts every icon, the bar, the gap and the empty text where the current list, size and gap say, over <paramref name="duration"/> (zero: at once).</summary>
    private void Layout(TimeSpan duration)
    {
        if (!Animate)
            duration = TimeSpan.Zero;
        var bar = GlassDockLayout.Bar(_size, _views.Count, _gap, BarLeftDip, BarTopDip, WindowWidthDip);
        double top = GlassDockLayout.IconTop(bar);
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (slot.Key != _dragKey)
                MoveSlot(slot, GlassDockLayout.IconLeft(_size, bar, i, _gap), top, duration);
        }
        PlaceBar(bar, duration);
        PlaceGap(bar, duration);
        PlaceEmpty(bar);
    }

    private void MoveSlot(Slot slot, double x, double y, TimeSpan duration)
    {
        if (slot.X == x && slot.Y == y)
            return;
        bool first = double.IsNaN(slot.X);
        slot.X = x;
        slot.Y = y;
        Translate(slot.Root, new Vector2((float)x, (float)y), first ? TimeSpan.Zero : duration, Bezier.Move);
    }

    /// <summary>B7 for the bar: the frost's clip and the tint's clip move together (offset, size and corner), from where they are.</summary>
    private void PlaceBar(DipRect bar, TimeSpan duration)
    {
        if (_barPlaced && bar == _barTarget)
            return;
        bool snap = duration <= TimeSpan.Zero || !_barPlaced;
        if (snap)
            duration = TimeSpan.Zero;
        float radius = (float)DockGeometry.CornerDip(_size);
        var offset = new Vector2((float)bar.X, (float)bar.Y);
        var size = new Vector2((float)bar.Width, (float)bar.Height);
        if (_barClip is { } clip)
        {
            AnimateValue(clip, "Offset", offset, duration, Bezier.Move, v => clip.Offset = v);
            AnimateValue(clip, "Size", size, duration, Bezier.Move, v => clip.Size = v);
            AnimateValue(clip, "CornerRadius", new Vector2(radius), duration, Bezier.Move, v => clip.CornerRadius = v);
        }
        float scale = (float)Scale;
        _glass.ShapeBar(offset * scale, size * scale, radius * scale, duration, Bezier.Move);
        UpdateSheen(bar);

        // The region holds the bar's old and new rectangles while it moves, then only the new one.
        int generation = ++_barGeneration;
        _barRegion = snap ? bar : DipRect.Union(_barRegion, bar);
        _barTarget = bar;
        _barPlaced = true;
        if (!snap)
        {
            After((int)duration.TotalMilliseconds + 40, () =>
            {
                if (generation != _barGeneration)
                    return;
                _barRegion = _barTarget;
                UpdateRegion();
            });
        }
        UpdateRegion();
    }

    /// <summary>D10: the gap a dragged program will land in, a tile-sized plate in Hover; it opens and closes with the icons (Move).</summary>
    private void PlaceGap(DipRect bar, TimeSpan duration)
    {
        if (_gap < 0)
        {
            if (_gapShown)
                GlassMotion.FadeTo(_gapPlate, 0, GlassMotion.Exit, GlassEase.In);
            _gapShown = false;
            return;
        }
        var rect = GlassDockLayout.Gap(_size, bar, _gap);
        _gapPlate.Width = rect.Width;
        _gapPlate.Height = rect.Height;
        _gapPlate.RadiusX = _gapPlate.RadiusY = rect.Width * TileRadiusFraction;
        var at = new Vector2((float)rect.X, (float)rect.Y);
        if (!_gapShown)
        {
            Translate(_gapPlate, at, TimeSpan.Zero, Bezier.Move);
            GlassMotion.FadeTo(_gapPlate, 1, GlassMotion.Enter, GlassEase.Out);
            _gapShown = true;
        }
        else
        {
            Translate(_gapPlate, at, duration, Bezier.Move);
        }
    }

    /// <summary>The empty bar: the Download glyph and "Drop apps here", centred. A gap on an empty bar takes their place.</summary>
    private void PlaceEmpty(DipRect bar)
    {
        bool show = _views.Count == 0 && _gap < 0;
        if (show)
        {
            _emptyHost.Width = bar.Width;
            _emptyHost.Height = bar.Height;
            Translate(_emptyHost, new Vector2((float)bar.X, (float)bar.Y), TimeSpan.Zero, Bezier.Move);
        }
        if (show != _emptyShown)
            GlassMotion.FadeTo(_emptyHost, show ? 1 : 0, GlassMotion.Quick, show ? GlassEase.Out : GlassEase.In);
        _emptyShown = show;
    }

    public void SetGap(int index)
    {
        if (_disposed)
            return;
        int gap = index < 0 ? -1 : Math.Min(index, _views.Count);
        if (gap == _gap)
            return;
        _gap = gap;
        Layout(GlassMotion.Move);
    }

    // ---------------------------------------------------------------- hover, press, drag

    public void SetHover(int index, string? label)
    {
        if (_disposed)
            return;
        var slot = index >= 0 && index < _slots.Count && label is not null ? _slots[index] : null;
        string? key = slot?.Key;
        if (key != _hoverKey)
        {
            // D1: the picture rises by the lift (Quick); the one left behind drops back (Quick to the next icon, Exit off the bar).
            if (Find(_hoverKey) is { } before && before.Key != _dragKey)
                LiftTo(before, 0, key is null ? GlassMotion.Exit : GlassMotion.Quick, key is null ? Bezier.In : Bezier.Out);
            _hoverKey = key;
            if (slot is not null && key != _dragKey)
                LiftTo(slot, -DockGeometry.LiftDip(_size), GlassMotion.Quick, Bezier.Out);
        }
        if (slot is null)
            HideLabel(snap: false);
        else
            ShowLabel(slot, label!);
    }

    /// <summary>The picture only: the box (and so what the controller hit-tests) stays where it is (HANDOFF 11.3).</summary>
    private void LiftTo(Slot slot, double y, TimeSpan duration, Bezier curve)
        => Translate(slot.Lift, new Vector2(0, (float)y), Animate ? duration : TimeSpan.Zero, curve);

    public void SetPressed(int index)
    {
        if (_disposed)
            return;
        string? key = index >= 0 && index < _slots.Count ? _slots[index].Key : null;
        if (key == _pressedKey)
            return;
        // D2: the tile goes down to 94 % about its centre (Press) and comes back (Release), as the Big Folder's tiles.
        if (Find(_pressedKey) is { } before)
            GlassMotion.ScaleTo(before.Lift, 1, GlassMotion.Release, GlassEase.Out, Centre(before));
        _pressedKey = key;
        if (Find(key) is { } now)
            GlassMotion.ScaleTo(now.Lift, GlassMotion.PressScale, GlassMotion.Press, GlassEase.Out, Centre(now));
    }

    public void SetDrag(int index, double x)
    {
        if (_disposed)
            return;
        if (index < 0 || index >= _slots.Count)
        {
            // D7: let go, the icon settles into its slot (Move).
            if (Find(_dragKey) is { } dropped)
            {
                _dragKey = null;
                Canvas.SetZIndex(dropped.Root, 2);
                LiftTo(dropped, dropped.Key == _hoverKey ? -DockGeometry.LiftDip(_size) : 0, GlassMotion.Move, Bezier.Move);
            }
            _dragKey = null;
            Layout(GlassMotion.Move);
            return;
        }
        var slot = _slots[index];
        if (slot.Key != _dragKey)
        {
            if (Find(_dragKey) is { } other)
            {
                Canvas.SetZIndex(other.Root, 2);
                LiftTo(other, 0, GlassMotion.Quick, Bezier.Out);
            }
            _dragKey = slot.Key;
            Canvas.SetZIndex(slot.Root, 3);
            LiftTo(slot, -DockGeometry.LiftDip(_size), GlassMotion.Quick, Bezier.Out);
        }
        // D7: the dragged icon follows the pointer, lifted, at full opacity; the others slide (their order came in SetItems).
        double left = GlassDockLayout.DragLeft(_size, _barTarget, BarLeftDip, x);
        slot.X = left;
        slot.Y = GlassDockLayout.IconTop(_barTarget);
        Translate(slot.Root, new Vector2((float)left, (float)slot.Y), TimeSpan.Zero, Bezier.Move);
    }

    // ---------------------------------------------------------------- the label

    private double Measure(string text)
    {
        _measure.Text = text;
        _measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _measure.DesiredSize.Width;
    }

    /// <summary>
    /// D1: the label fades in and rises 4 (Enter); to the next icon it slides there (Quick). Its frost is the second visual in the
    /// window, moved with it on the same curve, and the region takes in the label's rectangle (both places while it slides).
    /// </summary>
    private void ShowLabel(Slot slot, string text)
    {
        string shown = NameShortener.OneLine(text, GlassDockLayout.LabelTextMaxDip, Measure);
        var rect = GlassDockLayout.Label(slot.X + slot.Size / 2, Measure(shown), _barTarget.Y, ScreenLeftDip, ScreenRightDip);
        if (_labelState == LabelState.Shown && rect == _labelRect && _labelText.Text == shown)
            return;
        _labelText.Text = shown;
        _label.Width = rect.Width;
        float scale = (float)Scale;
        var at = new Vector2((float)rect.X, (float)rect.Y);
        var sizePx = new Vector2((float)rect.Width, (float)rect.Height) * scale;
        float radius = (float)LabelRadius * scale;
        int generation = ++_labelGeneration;
        var labelVisual = GlassMotion.VisualOf(_label);
        if (!Animate)
        {
            Translate(_label, at, TimeSpan.Zero, Bezier.Out);
            labelVisual.StopAnimation("Opacity");
            labelVisual.Opacity = 1;
            _glass.ShapeLabel(at * scale, sizePx, radius, TimeSpan.Zero, Bezier.Out);
            _glass.FadeLabel(1, TimeSpan.Zero, Bezier.Out);
            _labelRegion = rect;
        }
        else if (_labelState == LabelState.Hidden)
        {
            var from = at + new Vector2(0, (float)GlassDockLayout.LabelRiseDip);
            Translate(_label, from, TimeSpan.Zero, Bezier.Out);
            labelVisual.StopAnimation("Opacity");
            labelVisual.Opacity = 0;
            Translate(_label, at, GlassMotion.Enter, Bezier.Out);
            GlassMotion.FadeTo(_label, 1, GlassMotion.Enter, GlassEase.Out);
            _glass.ShapeLabel(from * scale, sizePx, radius, TimeSpan.Zero, Bezier.Out);
            _glass.FadeLabel(0, TimeSpan.Zero, Bezier.Out);
            _glass.ShapeLabel(at * scale, sizePx, radius, GlassMotion.Enter, Bezier.Out);
            _glass.FadeLabel(1, GlassMotion.Enter, Bezier.Out);
            _labelRegion = rect with { Height = rect.Height + GlassDockLayout.LabelRiseDip };
            TightenLabel(generation, GlassMotion.Enter);
        }
        else
        {
            // Shown, or still fading out: from where it is to the new icon, and back to full opacity.
            Translate(_label, at, GlassMotion.Quick, Bezier.Out);
            GlassMotion.FadeTo(_label, 1, GlassMotion.Quick, GlassEase.Out);
            _glass.ShapeLabel(at * scale, sizePx, radius, GlassMotion.Quick, Bezier.Out);
            _glass.FadeLabel(1, GlassMotion.Quick, Bezier.Out);
            _labelRegion = DipRect.Union(_labelRegion ?? _labelRect, rect);
            TightenLabel(generation, GlassMotion.Quick);
        }
        _labelRect = rect;
        _labelState = LabelState.Shown;
        UpdateRegion();
    }

    private void TightenLabel(int generation, TimeSpan after)
        => After((int)after.TotalMilliseconds + 40, () =>
        {
            if (generation != _labelGeneration || _labelState != LabelState.Shown)
                return;
            _labelRegion = _labelRect;
            UpdateRegion();
        });

    /// <summary>D1: leaving the bar, the label fades out (Exit); the region lets go of it when it has gone.</summary>
    private void HideLabel(bool snap)
    {
        if (_labelState == LabelState.Hidden || _labelState == LabelState.Hiding && !snap)
            return;
        int generation = ++_labelGeneration;
        if (snap || !Animate)
        {
            var visual = GlassMotion.VisualOf(_label);
            visual.StopAnimation("Opacity");
            visual.Opacity = 0;
            _glass.FadeLabel(0, TimeSpan.Zero, Bezier.In);
            _labelState = LabelState.Hidden;
            _labelRegion = null;
            UpdateRegion();
            return;
        }
        _labelState = LabelState.Hiding;
        GlassMotion.FadeTo(_label, 0, GlassMotion.Exit, GlassEase.In);
        _glass.FadeLabel(0, GlassMotion.Exit, Bezier.In);
        After((int)GlassMotion.Exit.TotalMilliseconds + 40, () =>
        {
            if (generation != _labelGeneration)
                return;
            _labelState = LabelState.Hidden;
            _labelRegion = null;
            UpdateRegion();
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Places an element by its visual's Translation (window DIPs), over <paramref name="duration"/> from where it is, or at once.</summary>
    private static void Translate(UIElement element, Vector2 to, TimeSpan duration, Bezier curve)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = GlassMotion.VisualOf(element);
        var target = new Vector3(to, 0);
        if (duration <= TimeSpan.Zero || !MotionSettings.Enabled)
        {
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", target);
            return;
        }
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1f, target, Easing(visual.Compositor, curve));
        animation.Duration = duration;
        visual.StartAnimation("Translation", animation);
    }

    private static void AnimateValue(CompositionObject target, string property, Vector2 to, TimeSpan duration, Bezier curve, Action<Vector2> set)
    {
        if (duration <= TimeSpan.Zero || !MotionSettings.Enabled)
        {
            target.StopAnimation(property);
            set(to);
            return;
        }
        var animation = target.Compositor.CreateVector2KeyFrameAnimation();
        animation.InsertKeyFrame(1f, to, Easing(target.Compositor, curve));
        animation.Duration = duration;
        target.StartAnimation(property, animation);
    }

    private static CubicBezierEasingFunction Easing(Compositor compositor, Bezier curve)
        => compositor.CreateCubicBezierEasingFunction(new Vector2((float)curve.X1, (float)curve.Y1), new Vector2((float)curve.X2, (float)curve.Y2));

    /// <summary>A one-off step on the shared clock, stopped with the surface.</summary>
    private FrameClock.Ticket After(int delayMs, Action action)
    {
        FrameClock.Ticket? ticket = null;
        ticket = FrameClock.After(delayMs, () =>
        {
            _timers.Remove(ticket!);
            if (!_disposed)
                action();
        });
        _timers.Add(ticket);
        return ticket;
    }

    private void StopTimers()
    {
        foreach (var ticket in _timers.ToList())
            ticket.Stop();
        _timers.Clear();
        _layoutDelay = null;
    }

    private void DisposeScene()
    {
        StopTimers();
        var barVisual = GlassMotion.VisualOf(_barLayer);
        barVisual.Clip = null;
        _barClipObject?.Dispose();
        _barClip?.Dispose();
        _barClipObject = null;
        _barClip = null;
        _stage.Children.Clear();
        _root.Children.Clear();
        _slots.Clear();
        _leaving.Clear();
    }
}
