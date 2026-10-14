using DeskWidgets.Pixel;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskWidgets.Widgets.Clock;

/// <summary>
/// Minimal date-and-time widget. Also the reference for writing a new widget:
/// a factory describing it, and a UserControl implementing <see cref="IWidget"/>.
/// </summary>
internal sealed class ClockWidgetFactory : IWidgetFactory
{
    public string TypeId => "clock";

    public string DisplayName => Strings.ClockWidget;

    public string Glyph => "";

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public WidgetSize DefaultSize => WidgetSize.Small;

    public IWidget Create(WidgetContext context) => new ClockWidget(context);
}

internal sealed class ClockWidget : UserControl, IWidget
{
    private readonly DispatcherQueueTimer _timer;
    private readonly TextBlock _weekday = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _time = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _date = new();
    private readonly StackPanel _panel = new();
    private readonly PixelSurface? _pixel;
    private WidgetSize _size;
    private DateTime _shownMinute;

    public ClockWidget(WidgetContext context)
    {
        var display = new FontFamily("Segoe UI Variable Display");
        _weekday.FontFamily = display;
        _time.FontFamily = display;
        _date.FontFamily = new FontFamily("Segoe UI Variable Text");
        _weekday.Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xFF, 0x45, 0x3A)); // Apple calendar red
        _panel.Children.Add(_weekday);
        _panel.Children.Add(_time);
        _panel.Children.Add(_date);
        if (context.Theme == WidgetTheme.Pixel)
            Content = _pixel = new PixelSurface((canvas, palette) => ClockPixelScene.Draw(canvas, palette, _size, DateTime.Now));
        else
            Content = _panel;

        _timer = context.Services.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        Refresh();
    }

    public FrameworkElement View => this;

    public void ApplySize(WidgetSize size)
    {
        _size = size;
        var (width, height) = PixelCard.ArtSize(size);
        _pixel?.SetArtSize(width, height);
        bool medium = size != WidgetSize.Small;
        _panel.Padding = medium ? new Thickness(22, 18, 22, 18) : new Thickness(18, 16, 18, 16);
        _panel.VerticalAlignment = VerticalAlignment.Center;
        _weekday.FontSize = medium ? 16 : 14;
        _time.FontSize = medium ? 64 : 46;
        _time.Margin = new Thickness(-2, medium ? -6 : -4, 0, medium ? -6 : -4);
        _date.FontSize = medium ? 16 : 14;
    }

    public IEnumerable<MenuFlyoutItemBase> CreateMenuItems() => [];

    public void ApplyLanguage()
    {
        _pixel?.Invalidate();
        Refresh();
    }

    private void ApplyTheme()
    {
        bool dark = ActualTheme == ElementTheme.Dark;
        _time.Foreground = new SolidColorBrush(dark ? Colors.White : ColorHelper.FromArgb(0xFF, 0x1C, 0x1C, 0x1E));
        _date.Foreground = new SolidColorBrush(dark ? ColorHelper.FromArgb(0x99, 0xEB, 0xEB, 0xF5) : ColorHelper.FromArgb(0x99, 0x3C, 0x3C, 0x43));
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        if (_pixel is not null)
        {
            // The pixel clock shows no seconds: repaint only when the minute turns.
            var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            if (minute != _shownMinute)
            {
                _shownMinute = minute;
                _pixel.Invalidate();
            }
            return;
        }
        var culture = Strings.Culture;
        _weekday.Text = now.ToString("dddd", culture);
        _time.Text = now.ToString("HH:mm", culture);
        _date.Text = now.ToString(culture.DateTimeFormat.MonthDayPattern, culture);
    }

    public void Dispose() => _timer.Stop();
}
