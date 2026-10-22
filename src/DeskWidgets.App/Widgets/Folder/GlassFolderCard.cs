using System.Numerics;
using DeskWidgets.Motion;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI.Text;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// The Big Folder card in the Glass theme (the Glass design's HANDOFF §1 and §4): a header, a grid of tiles with one-line
/// names, a stack when there are more items than cells, and the empty, missing and drop-hover states. It only draws; the widget
/// feeds it the model and the pointer, and asks it what the pointer is over (<see cref="HitTest"/>).
/// Each item has one cell for as long as it is shown (keyed by its file name, the stack by its own key), so that a cell can slide to
/// a new place, enter or leave, and its tile can scale on a press (Phase 12, A2 to A4).
/// </summary>
internal sealed class GlassFolderCard : Canvas, IFolderCardView
{
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text");
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons");
    private const string StackKey = "\0stack";

    /// <summary>One cell of the grid: its tile (or the stack's plate and minis) and its name, at the cell's place.</summary>
    private sealed class CardCell
    {
        public Canvas Root = null!;
        public Canvas TileHost = null!;
        public string Key = "";
        public int Index;
        public double X, Y, TileSize;
        public GlassMotion.SlideState Slide = new();
    }

    private readonly IWidgetSite _site;
    private readonly List<(Rect Rect, double Radius, FolderHit Hit)> _regions = [];
    private readonly GlassTileProvider _tiles;
    // One plate per region (the header, each cell, the stack), so that crossing from one to the next fades the old one out (Exit) while the new
    // one fades in (Quick), instead of one plate jumping between them. A plate is shown by a fade on its visual.
    private readonly Dictionary<FolderHit, (Rectangle Plate, bool On)> _plates = [];
    private readonly Border _tint = new() { IsHitTestVisible = false, CornerRadius = new CornerRadius(WidgetLayout.CornerRadius) };
    private readonly Border _label = new() { IsHitTestVisible = false, Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 0, 12, 0) };
    private readonly TextBlock _labelText = new() { FontFamily = TextFont, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon _labelGlyph = new() { FontSize = 12, FontFamily = IconFont, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, CardCell> _cells = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, CardCell> _byIndex = [];
    private readonly List<UIElement> _loose = [];
    private readonly List<(CardCell Cell, DateTime Until)> _leaving = [];
    private HashSet<string>? _shownKeys;
    private WidgetSize? _builtSize;
    private FrameClock.Ticket? _sweeper;
    private FolderModel? _model;
    private WidgetSize _size = WidgetSize.Medium;
    private FolderHit _hover = FolderHit.None;
    private FolderHit _pressed = FolderHit.None;
    private DropVerb? _dropVerb;
    private bool _hintShown, _labelShown;
    private bool _rebuildQueued;
    private bool _fixedParts;

    public GlassFolderCard(IWidgetSite site)
    {
        _site = site;
        _tiles = new GlassTileProvider(() => _site.Scale, () => Dark, RebuildSoon);
        _label.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _labelGlyph, _labelText } };
        // The drop tint and its label come and go by a fade on their visuals (an element whose Opacity property is 0 is not drawn at all).
        GlassMotion.VisualOf(_tint).Opacity = 0;
        GlassMotion.VisualOf(_label).Opacity = 0;
        Background = null;
        ActualThemeChanged += (_, _) => Rebuild();
        Unloaded += (_, _) => _sweeper?.Stop();
    }

    public FrameworkElement Element => this;

    public void Show(FolderModel model, WidgetSize size)
    {
        _model = model;
        _size = size;
        Rebuild();
    }

    /// <summary>Rebuilds soon; icons that arrive one after another repaint once.</summary>
    private void RebuildSoon()
    {
        if (_rebuildQueued)
            return;
        _rebuildQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        });
    }

    public FolderHit HitTest(Point point)
    {
        foreach (var (rect, _, hit) in _regions)
        {
            if (point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height)
                return hit;
        }
        return FolderHit.None;
    }

    public void SetHover(FolderHit hit)
    {
        if (hit == _hover)
            return;
        _hover = hit;
        PlacePlate();
    }

    public void SetPressed(FolderHit hit)
    {
        var before = CellAt(_pressed);
        _pressed = hit;
        var after = CellAt(hit);
        if (before != after)
        {
            // A3: the tile goes down to 94 % (Press) and comes back (Release) when the press ends, or turns into a drag of the card.
            if (before is not null)
                GlassMotion.ScaleTo(before.TileHost, 1, GlassMotion.Release, GlassEase.Out, TileCentre(before));
            if (after is not null)
                GlassMotion.ScaleTo(after.TileHost, GlassMotion.PressScale, GlassMotion.Press, GlassEase.Out, TileCentre(after));
        }
        PlacePlate();
    }

    private static Vector2 TileCentre(CardCell cell) => new((float)cell.TileSize / 2, (float)cell.TileSize / 2);

    private CardCell? CellAt(FolderHit hit) => hit.Kind switch
    {
        FolderHitKind.Item => _byIndex.GetValueOrDefault(hit.Index),
        FolderHitKind.Stack => _cells.GetValueOrDefault(StackKey),
        _ => null,
    };

    /// <summary>One plate for each region, made once and moved with its region; they are shown and hidden by <see cref="PlacePlate"/>.</summary>
    private void SyncPlates()
    {
        foreach (var hit in _plates.Keys.Where(hit => !_regions.Any(r => r.Hit == hit)).ToList())
        {
            Children.Remove(_plates[hit].Plate);
            _plates.Remove(hit);
        }
        foreach (var (rect, radius, hit) in _regions)
        {
            if (!_plates.TryGetValue(hit, out var entry))
            {
                var plate = new Rectangle { IsHitTestVisible = false };
                GlassMotion.VisualOf(plate).Opacity = 0; // hidden by its visual: an element whose Opacity property is 0 is not drawn at all
                SetZIndex(plate, 0);
                Children.Add(plate);
                entry = (plate, false);
                _plates[hit] = entry;
            }
            entry.Plate.RadiusX = entry.Plate.RadiusY = radius;
            entry.Plate.Width = rect.Width;
            entry.Plate.Height = rect.Height;
            SetLeft(entry.Plate, rect.X);
            SetTop(entry.Plate, rect.Y);
        }
    }

    /// <summary>A3: the plate under the pointer fades in (Quick) and the one it left fades out (Exit); pressed turns it to the pressed colour at once. With "Animation effects" off they snap.</summary>
    private void PlacePlate()
    {
        var target = _pressed.Kind != FolderHitKind.None ? _pressed : _hover;
        var palette = Palette;
        foreach (var hit in _plates.Keys.ToList())
        {
            var (plate, wasOn) = _plates[hit];
            bool on = target.Kind != FolderHitKind.None && hit == target;
            plate.Fill = _pressed.Kind != FolderHitKind.None && on ? palette.Pressed : palette.Hover;
            if (on != wasOn)
            {
                GlassMotion.FadeTo(plate, on ? 1 : 0, on ? GlassMotion.Quick : GlassMotion.Exit, on ? GlassEase.Out : GlassEase.In);
                _plates[hit] = (plate, on);
            }
        }
    }

    private GlassPalette Palette => GlassPalette.For(ActualTheme);

    private bool Dark => ActualTheme == ElementTheme.Dark;

    // ----- drop hint -----

    /// <summary>A3: on Glass the tile's spring back on release is the confirm; nothing more.</summary>
    public void Confirm(FolderHit hit)
    {
    }

    public void ShowDropHint(DropVerb? verb, bool snap = false)
    {
        if (verb == _dropVerb)
            return;
        _dropVerb = verb;
        ApplyDropHint(animate: true);
    }

    /// <summary>
    /// A2a. A drag over the card: the tint fades in (Quick), the label fades in and rises from 6 DIP below (Enter); a modifier key
    /// that changes the verb swaps the glyph and the words at once; leaving, or a drop, takes both out (Exit).
    /// </summary>
    private void ApplyDropHint(bool animate)
    {
        if (_dropVerb is not { } verb || _model is null)
        {
            if (_hintShown || _labelShown)
            {
                GlassMotion.FadeTo(_tint, 0, GlassMotion.Exit, GlassEase.In);
                GlassMotion.FadeTo(_label, 0, GlassMotion.Exit, GlassEase.In);
            }
            _hintShown = _labelShown = false;
            return;
        }
        string folder = _model.Name;
        string text = verb switch
        {
            DropVerb.Move => Strings.FolderDropMove(folder),
            DropVerb.Copy => Strings.FolderDropCopy(folder),
            DropVerb.Shortcut => Strings.FolderDropShortcut(folder),
            _ => Strings.FolderDropMixed(folder),
        };
        _labelGlyph.Glyph = verb switch
        {
            DropVerb.Move => "",
            DropVerb.Copy => "",
            DropVerb.Shortcut => "",
            _ => "", // Add
        };
        var measure = Measurer(12, FontWeights.SemiBold);
        double width = WidgetLayout.GetCardSize(_size).Width;
        _labelText.Text = NameShortener.OneLine(text, width - 64, measure);
        _label.UpdateLayout();
        PlaceLabel();
        if (!animate)
        {
            if (!_hintShown)
            {
                GlassMotion.VisualOf(_tint).Opacity = 1;
                GlassMotion.VisualOf(_label).Opacity = 1;
            }
            _hintShown = _labelShown = true;
            return;
        }
        if (!_hintShown)
            GlassMotion.FadeTo(_tint, 1, GlassMotion.Quick, GlassEase.Out);
        if (!_labelShown)
        {
            GlassMotion.Preset(_label, 0, 1, Vector2.Zero);
            GlassMotion.FadeTo(_label, 1, GlassMotion.Enter, GlassEase.Out);
            GlassMotion.SlideFrom(_label, new Vector2(0, 6), GlassMotion.Enter, GlassEase.Out);
        }
        _hintShown = _labelShown = true;
    }

    private void PlaceLabel()
    {
        var card = WidgetLayout.GetCardSize(_size);
        _label.Measure(new Size(card.Width, 26));
        SetLeft(_label, (card.Width - _label.DesiredSize.Width) / 2);
        SetTop(_label, card.Height - 12 - 26);
    }

    // ----- drawing -----

    private Func<string, double> Measurer(double fontSize, FontWeight weight)
    {
        var block = new TextBlock { FontFamily = TextFont, FontSize = fontSize, FontWeight = weight };
        return text =>
        {
            block.Text = text;
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return block.DesiredSize.Width;
        };
    }

    private void AddLoose(UIElement element, int z = 2)
    {
        SetZIndex(element, z);
        Children.Add(element);
        _loose.Add(element);
    }

    private void Rebuild()
    {
        if (_model is null)
            return;
        var palette = Palette;
        var card = WidgetLayout.GetCardSize(_size);
        Width = card.Width;
        Height = card.Height;
        // The plate, the cells, the tint and the label stay in the tree (their motion goes on across a rebuild); the rest is made again.
        foreach (var element in _loose)
            Children.Remove(element);
        _loose.Clear();
        _regions.Clear();
        if (!_fixedParts)
        {
            _fixedParts = true;
            SetZIndex(_tint, 5);
            Children.Add(_tint);
            SetZIndex(_label, 6);
            Children.Add(_label);
        }

        var items = _model.Items;
        var wanted = new List<(string Key, int Cell, FolderItem? Item)>();
        if (!_model.Exists)
        {
            Header(palette, card, showCount: false);
            Missing(palette, card);
        }
        else
        {
            Header(palette, card, showCount: true);
            if (items.Count == 0)
                Empty(palette, card);
            else
                Cells(palette, card, items, wanted);
        }
        // A change of what the card shows animates; the first look, a new size and a new theme do not.
        bool animate = MotionSettings.Enabled && _shownKeys is not null && _builtSize == _size;
        _builtSize = _size;
        SyncCells(wanted, animate);
        SyncPlates();

        // On top of everything: the drop tint and its label.
        _tint.Width = card.Width;
        _tint.Height = card.Height;
        _tint.Background = palette.DropTint;
        _label.Background = palette.Select;
        _labelText.Foreground = palette.OnSelect;
        _labelGlyph.Foreground = palette.OnSelect;
        ApplyDropHint(animate: false);
        PlacePlate();
    }

    private void Header(GlassPalette palette, Size card, bool showCount)
    {
        var model = _model!;
        double right = card.Width;
        double countWidth = 0;
        var count = new TextBlock { FontFamily = TextFont, FontSize = 12, Foreground = palette.Secondary, IsHitTestVisible = false, Text = model.Items.Count.ToString() };
        if (showCount)
        {
            count.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            countWidth = count.DesiredSize.Width;
            SetLeft(count, right - 24 - countWidth);
            SetTop(count, 12 + (18 - count.DesiredSize.Height) / 2);
            AddLoose(count);
            var chevron = new FontIcon { Glyph = "", FontSize = 9, FontFamily = IconFont, Foreground = palette.Secondary, IsHitTestVisible = false };
            SetLeft(chevron, right - 12 - 9);
            SetTop(chevron, 12 + (18 - 9) / 2.0);
            AddLoose(chevron);
        }
        double available = card.Width - 24 - (showCount ? countWidth + 22 : 0);
        string name = NameShortener.OneLine(model.Name, available, Measurer(13, FontWeights.SemiBold));
        var title = new TextBlock { Text = name, FontFamily = TextFont, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = palette.Primary, IsHitTestVisible = false };
        title.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(title, 12);
        SetTop(title, 12 + (18 - title.DesiredSize.Height) / 2);
        AddLoose(title);
        AutomationProperties.SetName(title, model.Name);
        if (showCount)
            _regions.Add((new Rect(6, 9, card.Width - 12, 24), 9, new FolderHit(FolderHitKind.Header)));
    }

    private void Empty(GlassPalette palette, Size card)
    {
        bool large = _size == WidgetSize.Large;
        double disc = large ? 56 : 44;
        var block = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        var circle = new Grid { Width = disc, Height = disc, HorizontalAlignment = HorizontalAlignment.Center };
        circle.Children.Add(new Ellipse { Fill = palette.Hover });
        circle.Children.Add(new FontIcon { Glyph = "", FontSize = large ? 24 : 20, FontFamily = IconFont, Foreground = palette.Secondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        block.Children.Add(circle);
        block.Children.Add(new TextBlock { Text = Strings.FolderEmpty, FontFamily = TextFont, FontSize = large ? 13 : 12, Foreground = palette.Secondary, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        CenterBlock(block, card);
    }

    private void Missing(GlassPalette palette, Size card)
    {
        var block = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        block.Children.Add(new Grid
        {
            Height = 26, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { new FontIcon { Glyph = "", FontSize = 22, FontFamily = IconFont, Foreground = palette.Secondary, VerticalAlignment = VerticalAlignment.Center } },
        });
        block.Children.Add(new TextBlock { Text = Strings.FolderMissing, FontFamily = TextFont, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = palette.Primary, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0), Height = 18 });
        string path = NameShortener.Path(_model!.Path, card.Width - 28, Measurer(11, FontWeights.Normal));
        block.Children.Add(new TextBlock { Text = path, FontFamily = TextFont, FontSize = 11, Foreground = palette.Secondary, TextAlignment = TextAlignment.Center, Height = 15 });
        if (_size != WidgetSize.Small)
            block.Children.Add(new TextBlock { Text = Strings.FolderMissingHint, FontFamily = TextFont, FontSize = 11, Foreground = palette.Secondary, TextAlignment = TextAlignment.Center, Height = 15 });
        CenterBlock(block, card);
    }

    /// <summary>Puts a block in the middle of the area under the header (y 34 to the bottom minus 10).</summary>
    private void CenterBlock(FrameworkElement block, Size card)
    {
        double top = 34, bottom = card.Height - 10;
        var host = new Grid { Width = card.Width, Height = bottom - top, IsHitTestVisible = false };
        block.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(block);
        SetTop(host, top);
        AddLoose(host);
    }

    /// <summary>
    /// The grid per size (Glass HANDOFF 1.1, Phase 8 option B): names are Text 10 (10.5 on Large) with a 13 line height; Small and Medium
    /// keep one line, Large takes two. The name box is the column minus 2, at row + 46 (row + 52 on Large, where the rows are 80 and the tile is 44 at row + 4).
    /// </summary>
    private (double ColumnWidth, double RowHeight, double Tile, double TileTop, double NameTop, double NameHeight, double NameFont, double NameWidth, int Lines) Metrics()
        => _size switch
        {
            WidgetSize.Small => (70, 60, 40, 2, 46, 13, 10, 68, 1),
            WidgetSize.Medium => (80, 60, 40, 2, 46, 13, 10, 78, 1),
            _ => (80, 80, 44, 4, 52, 26, 10.5, 78, 2),
        };

    /// <summary>
    /// Lays out the cells: the regions, and for each cell what it shows. The cells themselves are made or reused by <see cref="SyncCells"/>
    /// (this fills in <paramref name="wanted"/> and the cell's content through <see cref="FillCell"/> once it has one).
    /// </summary>
    private void Cells(GlassPalette palette, Size card, IReadOnlyList<FolderItem> items, List<(string Key, int Cell, FolderItem? Item)> wanted)
    {
        var layout = FolderCards.Layout(_size, items.Count);
        int cells = layout.HasStack ? layout.Shown + 1 : layout.Shown;
        var (columnWidth, rowHeight, _, _, _, _, _, _, _) = Metrics();
        double gridLeft = (card.Width - layout.Columns * columnWidth) / 2, gridTop = 34;
        for (int cell = 0; cell < cells; cell++)
        {
            double x0 = gridLeft + cell % layout.Columns * columnWidth, y0 = gridTop + cell / layout.Columns * rowHeight;
            bool isStack = layout.HasStack && cell == layout.Shown;
            var hit = isStack ? new FolderHit(FolderHitKind.Stack) : new FolderHit(FolderHitKind.Item, cell);
            _regions.Add((new Rect(x0 + 2, y0, columnWidth - 4, rowHeight - 1), 12, hit));
            wanted.Add((isStack ? StackKey : items[cell].FileName, cell, isStack ? null : items[cell]));
        }
        _layout = layout;
        _gridLeft = gridLeft;
        _gridTop = gridTop;
        _palette = palette;
        _items = items;
    }

    private CardCells _layout;
    private double _gridLeft, _gridTop;
    private GlassPalette? _palette;
    private IReadOnlyList<FolderItem> _items = [];

    /// <summary>
    /// Makes the cells the card needs, keyed by item: an existing cell takes its new place (a slide), a new one enters, one that is
    /// no longer wanted leaves where it stands (Exit) and goes when it has gone. Without <paramref name="animate"/> all of that snaps.
    /// </summary>
    private void SyncCells(List<(string Key, int Cell, FolderItem? Item)> wanted, bool animate)
    {
        var keys = new HashSet<string>(wanted.Select(w => w.Key), StringComparer.OrdinalIgnoreCase);
        var previous = _shownKeys;
        _shownKeys = keys;
        var leavers = new List<CardCell>();
        foreach (var (key, cell) in _cells.ToList())
        {
            if (keys.Contains(key))
                continue;
            _cells.Remove(key);
            if (animate)
                leavers.Add(cell);
            else
                Children.Remove(cell.Root);
        }
        _byIndex.Clear();
        var (columnWidth, rowHeight, tile, tileTop, nameTop, nameHeight, nameFont, nameWidth, lines) = Metrics();
        var enterDelay = leavers.Count > 0 ? TimeSpan.FromMilliseconds(80) : TimeSpan.Zero;
        foreach (var (key, index, item) in wanted)
        {
            double x0 = _gridLeft + index % _layout.Columns * columnWidth, y0 = _gridTop + index / _layout.Columns * rowHeight;
            bool fresh = !_cells.TryGetValue(key, out var cell);
            if (fresh)
            {
                cell = new CardCell { Key = key, Root = new Canvas(), TileHost = new Canvas(), X = x0, Y = y0 };
                SetZIndex(cell.Root, 1);
                Children.Add(cell.Root);
                _cells[key] = cell;
            }
            cell!.Index = index;
            _byIndex[index] = cell;
            bool moved = !fresh && (cell.X != x0 || cell.Y != y0);
            var delta = new Vector2((float)(cell.X - x0), (float)(cell.Y - y0));
            cell.X = x0;
            cell.Y = y0;
            SetLeft(cell.Root, x0);
            SetTop(cell.Root, y0);
            if (moved)
                GlassMotion.Slide(cell.Root, cell.Slide, delta, animate);
            FillCell(cell, item, columnWidth, rowHeight, tile, tileTop, nameTop, nameHeight, nameFont, nameWidth, lines);
            if (fresh && animate && previous is not null && !previous.Contains(key))
                GlassMotion.EnterItem(cell.Root, new Vector2((float)columnWidth / 2, (float)(tileTop + tile / 2)), enterDelay);
        }
        foreach (var cell in leavers)
        {
            GlassMotion.LeaveItem(cell.Root, new Vector2((float)columnWidth / 2, (float)(tileTop + tile / 2)));
            _leaving.Add((cell, DateTime.UtcNow + GlassMotion.LeaveDuration));
        }
        if (_leaving.Count > 0)
            StartSweeper();
    }

    private void StartSweeper()
    {
        if (_sweeper is { Active: true })
            return;
        _sweeper = FrameClock.Every(60, _ =>
        {
            var now = DateTime.UtcNow;
            for (int i = _leaving.Count - 1; i >= 0; i--)
            {
                if (_leaving[i].Until > now)
                    continue;
                Children.Remove(_leaving[i].Cell.Root);
                _leaving.RemoveAt(i);
            }
            if (_leaving.Count == 0)
                _sweeper?.Stop();
        });
    }

    /// <summary>Fills a cell with its tile (or the stack) and its name; the cell's own visual, and its tile host's, are not touched.</summary>
    private void FillCell(CardCell cell, FolderItem? item, double columnWidth, double rowHeight, double tile, double tileTop, double nameTop, double nameHeight, double nameFont, double nameWidth, int lines)
    {
        var palette = _palette ?? Palette;
        var measure = Measurer(nameFont, FontWeights.Normal);
        cell.Root.Children.Clear();
        cell.TileHost.Children.Clear();
        cell.TileSize = tile;
        SetLeft(cell.TileHost, (columnWidth - tile) / 2);
        SetTop(cell.TileHost, tileTop);
        cell.TileHost.Width = tile;
        cell.TileHost.Height = tile;
        cell.Root.Children.Add(cell.TileHost);
        var name = new TextBlock
        {
            FontFamily = TextFont, FontSize = nameFont, Width = nameWidth, Height = nameHeight, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap, MaxLines = lines, LineHeight = 13, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            IsHitTestVisible = false,
        };
        if (item is null)
        {
            AddStack(palette, cell.TileHost, _items, _layout, tile);
            name.Text = "+" + _layout.StackMore;
            name.FontWeight = FontWeights.SemiBold;
            name.Foreground = palette.Secondary;
            AutomationProperties.SetName(name, "+" + _layout.StackMore);
        }
        else
        {
            var visual = _tiles.Tile(palette, item, tile);
            if (item.TargetMissing)
                visual.Opacity = 0.4;
            SetLeft(visual, 0);
            SetTop(visual, 0);
            cell.TileHost.Children.Add(visual);
            bool shortened;
            if (lines == 2)
            {
                var (first, second) = NameShortener.TwoLines(item.DisplayName, nameWidth, measure);
                name.Text = second.Length == 0 ? first : first + "\n" + second;
                // A break at a space drops the space; anything else missing was cut.
                shortened = (first + second).Replace(" ", "") != item.DisplayName.Replace(" ", "");
            }
            else
            {
                string shown = NameShortener.OneLine(item.DisplayName, nameWidth, measure);
                name.Text = shown;
                shortened = shown != item.DisplayName;
            }
            name.Foreground = item.TargetMissing ? palette.Secondary : palette.Primary;
            AutomationProperties.SetName(name, item.DisplayName);
            if (shortened)
            {
                name.IsHitTestVisible = true;
                ToolTipService.SetToolTip(name, item.DisplayName);
            }
        }
        SetLeft(name, 1); // the name box is the column minus 2, centred in it
        SetTop(name, nameTop);
        cell.Root.Children.Add(name);
    }

    private void AddStack(GlassPalette palette, Canvas host, IReadOnlyList<FolderItem> items, CardCells layout, double tile)
    {
        var plate = new Rectangle { Width = tile, Height = tile, RadiusX = tile * 0.225, RadiusY = tile * 0.225, Fill = palette.Hover, IsHitTestVisible = false };
        SetLeft(plate, 0);
        SetTop(plate, 0);
        host.Children.Add(plate);
        double mini = _size == WidgetSize.Large ? 17 : 16, inset = _size == WidgetSize.Large ? 4 : 3; // minis + gap 2 + insets fill the tile: 4 + 17 + 2 + 17 + 4 = 44
        for (int i = 0; i < layout.Minis.Length; i++)
        {
            var visual = _tiles.Tile(palette, items[layout.Minis[i]], mini);
            SetLeft(visual, inset + i % 2 * (mini + 2));
            SetTop(visual, inset + i / 2 * (mini + 2));
            host.Children.Add(visual);
        }
    }
}
