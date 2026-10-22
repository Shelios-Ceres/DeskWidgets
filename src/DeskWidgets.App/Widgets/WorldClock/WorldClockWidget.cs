using DeskWidgets.Pixel;
using DeskWidgets.PixelUi;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskWidgets.Widgets.WorldClock;

internal sealed class WorldClockWidgetFactory : IWidgetFactory
{
    public string TypeId => "worldclock";

    public string DisplayName => Strings.WorldClockWidget;

    public string Glyph => "";

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium, WidgetSize.Large];

    public WidgetSize DefaultSize => WidgetSize.Medium;

    public IWidget Create(WidgetContext context) => new WorldClockWidget(context);
}

/// <summary>
/// Four cities at a glance, after Apple's World Clock widget: an analog face
/// per city (white by day, dark by night) under its English name. Small = 2×2
/// clocks, medium = a row of four with each city's time and date, large = a
/// list with each city's offset from local time, time and date. The pixel theme
/// draws the same layouts as pixel art (<see cref="WorldClockPixelScene"/>).
/// </summary>
internal sealed class WorldClockWidget : UserControl, IWidget
{
    private const string CitiesKey = "cities";
    private const string ShowSecondsKey = "showSeconds";
    private const int CityCount = 4;

    private readonly WidgetOptions _options;
    private readonly DispatcherQueueTimer _timer;
    private readonly Grid _root = new();
    private readonly List<CityView> _views = [];
    private readonly List<WorldCity> _cities;
    private readonly FontFamily _displayFont = new("Segoe UI Variable Display");
    private readonly FontFamily _textFont = new("Segoe UI Variable Text");
    private readonly PixelSurface? _pixel;
    private GlassPalette _glass = null!; // set by Render before any clock is made
    private WidgetSize _size = WidgetSize.Medium;
    private bool _disposed;

    public WorldClockWidget(WidgetContext context)
    {
        _options = context.Options;
        _cities = LoadCities();
        if (context.Theme == WidgetTheme.Pixel)
            Content = _pixel = new PixelSurface((canvas, palette) => WorldClockPixelScene.Draw(canvas, palette, _size, _cities, DateTimeOffset.UtcNow, ShowSeconds));
        else
            Content = _root;
        _timer = context.Services.Dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Tick();
        ActualThemeChanged += (_, _) => Render();
    }

    public FrameworkElement View => this;

    private bool ShowSeconds => _options.GetBool(ShowSecondsKey, fallback: true);

    public void ApplySize(WidgetSize size)
    {
        _size = size;
        var (width, height) = PixelCard.ArtSize(size);
        _pixel?.SetArtSize(width, height);
        Render();
    }

    public IEnumerable<MenuEntry> CreateMenuItems()
    {
        var slots = new List<MenuEntry>();
        for (int i = 0; i < CityCount; i++)
        {
            int index = i;
            var cities = WorldCities.Catalog
                .Select(city => MenuEntry.Radio(city.Name, city.Id == _cities[index].Id, () => SetCity(index, city), group: $"city{index}"))
                .ToList();
            slots.Add(MenuEntry.Sub(Strings.CitySlot(index + 1, _cities[index].Name), cities));
        }
        return
        [
            MenuEntry.Sub(Strings.ChangeCities, slots, glyph: "", icon: PixelIcons.Globe),
            MenuEntry.Toggle(Strings.ShowSecondHand, ShowSeconds, () =>
            {
                _options.SetBool(ShowSecondsKey, !ShowSeconds);
                Tick();
            }),
        ];
    }

    public void ApplyLanguage() => Render();

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }

    private List<WorldCity> LoadCities()
    {
        string[] saved = _options.GetString(CitiesKey)?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        return Enumerable.Range(0, CityCount)
            .Select(i => (i < saved.Length ? WorldCities.Resolve(saved[i]) : null) ?? WorldCities.Resolve(WorldCities.DefaultIds[i])!)
            .ToList();
    }

    private void SetCity(int index, WorldCity city)
    {
        _cities[index] = city;
        _options.SetString(CitiesKey, string.Join(';', _cities.Select(c => c.Id)));
        Render();
    }

    private void Render()
    {
        if (_pixel is not null)
        {
            Tick(); // repaints and keeps the timer going
            return;
        }
        _glass = GlassPalette.For(ActualTheme);
        var primary = _glass.Primary;
        var secondary = _glass.Secondary;

        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _root.ColumnDefinitions.Clear();
        _views.Clear();
        if (_size == WidgetSize.Small)
            RenderGrid(primary);
        else if (_size == WidgetSize.Medium)
            RenderRow(primary, secondary);
        else
            RenderList(primary, secondary);
        Tick();
    }

    /// <summary>Small: 2×2 clocks, each with the city's name.</summary>
    private void RenderGrid(Brush primary)
    {
        _root.Padding = new Thickness(12);
        for (int i = 0; i < 2; i++)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (int i = 0; i < CityCount; i++)
        {
            var city = _cities[i];
            var clock = new AnalogClock(46, _glass) { HorizontalAlignment = HorizontalAlignment.Center };
            var cell = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            cell.Children.Add(clock);
            cell.Children.Add(Label(city.Name, 11, FontWeights.SemiBold, primary));
            AddCell(_root, cell, row: i / 2, column: i % 2);
            _views.Add(new CityView(city, clock, Offset: null, Time: null, Date: null));
        }
    }

    /// <summary>
    /// Medium: four clocks in a row with each city's name, time and date below.
    /// One shared table keeps the rows aligned across columns.
    /// </summary>
    private void RenderRow(Brush primary, Brush secondary)
    {
        _root.Padding = new Thickness(10, 0, 10, 0);
        var table = new Grid { VerticalAlignment = VerticalAlignment.Center };
        for (int row = 0; row < 4; row++)
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < CityCount; i++)
        {
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var city = _cities[i];
            var clock = new AnalogClock(60, _glass) { HorizontalAlignment = HorizontalAlignment.Center };
            var name = Label(city.Name, 12, FontWeights.SemiBold, primary);
            name.Margin = new Thickness(0, 7, 0, 0);
            var time = Label(string.Empty, 12, FontWeights.Normal, secondary);
            time.Margin = new Thickness(0, 1, 0, 0);
            var date = Label(string.Empty, 11, FontWeights.Normal, secondary);
            AddCell(table, clock, row: 0, column: i);
            AddCell(table, name, row: 1, column: i);
            AddCell(table, time, row: 2, column: i);
            AddCell(table, date, row: 3, column: i);
            _views.Add(new CityView(city, clock, Offset: null, time, date));
        }
        _root.Children.Add(table);
    }

    /// <summary>Large: one row per city with its clock, name and offset, and its time over its date.</summary>
    private void RenderList(Brush primary, Brush secondary)
    {
        _root.Padding = new Thickness(18, 14, 18, 14); // four 84 rows in 364
        for (int i = 0; i < CityCount; i++)
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < CityCount; i++)
        {
            var city = _cities[i];
            var row = new Grid { ColumnSpacing = 14, VerticalAlignment = VerticalAlignment.Center };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var clock = new AnalogClock(60, _glass);
            var labels = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(Label(city.Name, 15, FontWeights.SemiBold, primary, centered: false));
            var offset = Label(string.Empty, 12, FontWeights.Normal, secondary, centered: false);
            labels.Children.Add(offset);
            var time = new TextBlock
            {
                FontFamily = _displayFont,
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                Foreground = primary,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var date = Label(string.Empty, 12, FontWeights.Normal, secondary, centered: false);
            date.HorizontalAlignment = HorizontalAlignment.Right;
            date.Margin = new Thickness(0, -3, 0, 0); // tuck it under the big digits
            var timeAndDate = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            timeAndDate.Children.Add(time);
            timeAndDate.Children.Add(date);
            AddCell(row, clock, row: 0, column: 0);
            AddCell(row, labels, row: 0, column: 1);
            AddCell(row, timeAndDate, row: 0, column: 2);
            AddCell(_root, row, row: i, column: 0);
            _views.Add(new CityView(city, clock, offset, time, date));
        }
    }

    private void Tick()
    {
        if (_disposed)
            return;
        var now = DateTimeOffset.UtcNow;
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        bool seconds = ShowSeconds;
        _pixel?.Invalidate();
        foreach (var view in _views)
        {
            var time = TimeZoneInfo.ConvertTime(now, view.City.Zone);
            view.Clock.SetTime(time.DateTime, seconds);
            if (view.Offset is not null)
                view.Offset.Text = WorldCities.DescribeOffset(time, local);
            if (view.Time is not null)
                view.Time.Text = WorldCities.FormatTime(time);
            if (view.Date is not null)
                view.Date.Text = WorldCities.FormatDate(time);
        }

        // Wake just after the next whole second (or minute) so the hands move on the beat.
        var wallClock = DateTime.Now;
        double wait = seconds ? 1000 - wallClock.Millisecond : (60 - wallClock.Second) * 1000 - wallClock.Millisecond;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(wait + 15);
        _timer.Start();
    }

    private TextBlock Label(string text, double fontSize, Windows.UI.Text.FontWeight weight, Brush foreground, bool centered = true) => new()
    {
        Text = text,
        FontFamily = _textFont,
        FontSize = fontSize,
        FontWeight = weight,
        Foreground = foreground,
        MaxLines = 1,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
        HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
    };

    private static void AddCell(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private sealed record CityView(WorldCity City, AnalogClock Clock, TextBlock? Offset, TextBlock? Time, TextBlock? Date);
}
