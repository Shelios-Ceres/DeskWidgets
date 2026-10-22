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
/// </summary>
internal sealed class GlassFolderCard : Canvas, IFolderCardView
{
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text");
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons");
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(150);

    private readonly IWidgetSite _site;
    private readonly List<(Rect Rect, double Radius, FolderHit Hit)> _regions = [];
    private readonly GlassTileProvider _tiles;
    private readonly Rectangle _plate = new() { Opacity = 0, IsHitTestVisible = false, OpacityTransition = new ScalarTransition { Duration = FadeIn } };
    private readonly Border _tint = new() { Opacity = 0, IsHitTestVisible = false, CornerRadius = new CornerRadius(WidgetLayout.CornerRadius), OpacityTransition = new ScalarTransition { Duration = FadeIn } };
    private readonly Border _label = new() { Opacity = 0, IsHitTestVisible = false, Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 0, 12, 0), OpacityTransition = new ScalarTransition { Duration = FadeIn } };
    private readonly TextBlock _labelText = new() { FontFamily = TextFont, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon _labelGlyph = new() { FontSize = 12, FontFamily = IconFont, VerticalAlignment = VerticalAlignment.Center };
    private FolderModel? _model;
    private WidgetSize _size = WidgetSize.Medium;
    private FolderHit _hover = FolderHit.None;
    private FolderHit _pressed = FolderHit.None;
    private DropVerb? _dropVerb;
    private bool _rebuildQueued;

    public GlassFolderCard(IWidgetSite site)
    {
        _site = site;
        _tiles = new GlassTileProvider(() => _site.Scale, () => Dark, RebuildSoon);
        _label.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _labelGlyph, _labelText } };
        Background = null;
        ActualThemeChanged += (_, _) => Rebuild();
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
        _pressed = hit;
        PlacePlate();
    }

    private void PlacePlate()
    {
        var target = _pressed.Kind != FolderHitKind.None ? _pressed : _hover;
        var region = _regions.FirstOrDefault(r => r.Hit == target);
        var palette = Palette;
        if (target.Kind == FolderHitKind.None || region.Rect.Width == 0)
        {
            _plate.OpacityTransition = new ScalarTransition { Duration = FadeOut };
            _plate.Opacity = 0;
            return;
        }
        _plate.OpacityTransition = new ScalarTransition { Duration = FadeIn };
        _plate.Fill = _pressed.Kind != FolderHitKind.None ? palette.Pressed : palette.Hover;
        _plate.RadiusX = _plate.RadiusY = region.Radius;
        _plate.Width = region.Rect.Width;
        _plate.Height = region.Rect.Height;
        SetLeft(_plate, region.Rect.X);
        SetTop(_plate, region.Rect.Y);
        _plate.Opacity = 1;
    }

    private GlassPalette Palette => GlassPalette.For(ActualTheme);

    private bool Dark => ActualTheme == ElementTheme.Dark;

    // ----- drop hint -----

    public void ShowDropHint(DropVerb? verb)
    {
        if (verb == _dropVerb)
            return;
        _dropVerb = verb;
        ApplyDropHint();
    }

    private void ApplyDropHint()
    {
        if (_dropVerb is not { } verb || _model is null)
        {
            _tint.Opacity = 0;
            _label.Opacity = 0;
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
            DropVerb.Move => "",
            DropVerb.Copy => "",
            DropVerb.Shortcut => "",
            _ => "", // Add
        };
        var measure = Measurer(12, FontWeights.SemiBold);
        double width = WidgetLayout.GetCardSize(_size).Width;
        _labelText.Text = NameShortener.OneLine(text, width - 64, measure);
        _tint.Opacity = 1;
        _label.Opacity = 1;
        _label.UpdateLayout();
        PlaceLabel();
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

    private void Rebuild()
    {
        if (_model is null)
            return;
        var palette = Palette;
        var card = WidgetLayout.GetCardSize(_size);
        Width = card.Width;
        Height = card.Height;
        Children.Clear();
        _regions.Clear();
        Children.Add(_plate);

        var items = _model.Items;
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
                Cells(palette, card, items);
        }

        // On top of everything: the drop tint and its label.
        _tint.Width = card.Width;
        _tint.Height = card.Height;
        _tint.Background = palette.DropTint;
        Children.Add(_tint);
        _label.Background = palette.Select;
        _labelText.Foreground = palette.OnSelect;
        _labelGlyph.Foreground = palette.OnSelect;
        Children.Add(_label);
        ApplyDropHint();
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
            Children.Add(count);
            var chevron = new FontIcon { Glyph = "", FontSize = 9, FontFamily = IconFont, Foreground = palette.Secondary, IsHitTestVisible = false };
            SetLeft(chevron, right - 12 - 9);
            SetTop(chevron, 12 + (18 - 9) / 2.0);
            Children.Add(chevron);
        }
        double available = card.Width - 24 - (showCount ? countWidth + 22 : 0);
        string name = NameShortener.OneLine(model.Name, available, Measurer(13, FontWeights.SemiBold));
        var title = new TextBlock { Text = name, FontFamily = TextFont, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = palette.Primary, IsHitTestVisible = false };
        title.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(title, 12);
        SetTop(title, 12 + (18 - title.DesiredSize.Height) / 2);
        Children.Add(title);
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
        circle.Children.Add(new FontIcon { Glyph = "", FontSize = large ? 24 : 20, FontFamily = IconFont, Foreground = palette.Secondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
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
            Children = { new FontIcon { Glyph = "", FontSize = 22, FontFamily = IconFont, Foreground = palette.Secondary, VerticalAlignment = VerticalAlignment.Center } },
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
        Children.Add(host);
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

    private void Cells(GlassPalette palette, Size card, IReadOnlyList<FolderItem> items)
    {
        var (columnWidth, rowHeight, tile, tileTop, nameTop, nameHeight, nameFont, nameWidth, lines) = Metrics();
        var layout = FolderCards.Layout(_size, items.Count);
        double gridLeft = (card.Width - layout.Columns * columnWidth) / 2, gridTop = 34;
        var measure = Measurer(nameFont, FontWeights.Normal);
        int cells = layout.HasStack ? layout.Shown + 1 : layout.Shown;

        for (int cell = 0; cell < cells; cell++)
        {
            double x0 = gridLeft + cell % layout.Columns * columnWidth, y0 = gridTop + cell / layout.Columns * rowHeight;
            bool isStack = layout.HasStack && cell == layout.Shown;
            var hit = isStack ? new FolderHit(FolderHitKind.Stack) : new FolderHit(FolderHitKind.Item, cell);
            _regions.Add((new Rect(x0 + 2, y0, columnWidth - 4, rowHeight - 1), 12, hit));

            double tileX = x0 + (columnWidth - tile) / 2;
            var name = new TextBlock
            {
                FontFamily = TextFont, FontSize = nameFont, Width = nameWidth, Height = nameHeight, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap, MaxLines = lines, LineHeight = 13, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                IsHitTestVisible = false,
            };
            if (isStack)
            {
                AddStack(palette, items, layout, tileX, y0 + tileTop, tile);
                name.Text = "+" + layout.StackMore;
                name.FontWeight = FontWeights.SemiBold;
                name.Foreground = palette.Secondary;
                AutomationProperties.SetName(name, "+" + layout.StackMore);
            }
            else
            {
                var item = items[cell];
                var visual = _tiles.Tile(palette, item, tile);
                if (item.TargetMissing)
                    visual.Opacity = 0.4;
                SetLeft(visual, tileX);
                SetTop(visual, y0 + tileTop);
                Children.Add(visual);
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
            SetLeft(name, x0 + 1); // the name box is the column minus 2, centred in it
            SetTop(name, y0 + nameTop);
            Children.Add(name);
        }
    }

    private void AddStack(GlassPalette palette, IReadOnlyList<FolderItem> items, CardCells layout, double x, double y, double tile)
    {
        var plate = new Rectangle { Width = tile, Height = tile, RadiusX = tile * 0.225, RadiusY = tile * 0.225, Fill = palette.Hover, IsHitTestVisible = false };
        SetLeft(plate, x);
        SetTop(plate, y);
        Children.Add(plate);
        double mini = _size == WidgetSize.Large ? 17 : 16, inset = _size == WidgetSize.Large ? 4 : 3; // minis + gap 2 + insets fill the tile: 4 + 17 + 2 + 17 + 4 = 44
        for (int i = 0; i < layout.Minis.Length; i++)
        {
            var visual = _tiles.Tile(palette, items[layout.Minis[i]], mini);
            SetLeft(visual, x + inset + i % 2 * (mini + 2));
            SetTop(visual, y + inset + i / 2 * (mini + 2));
            Children.Add(visual);
        }
    }
}
