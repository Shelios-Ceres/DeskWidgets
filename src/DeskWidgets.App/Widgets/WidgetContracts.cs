using DeskWidgets.Core.Devices;
using DeskWidgets.Pixel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DeskWidgets.Widgets;

/// <summary>Apple-style widget sizes: small square, medium (2×1), large (2×2).</summary>
public enum WidgetSize
{
    Small,
    Medium,
    Large,
}

/// <summary>The look of every widget, picked in the tray or widget menu.</summary>
public enum WidgetTheme
{
    /// <summary>Apple-style cards of thin live glass.</summary>
    Glass,

    /// <summary>Opaque pixel-art cards drawn at low resolution and enlarged to whole screen pixels.</summary>
    Pixel,
}

public static class WidgetLayout
{
    public const double CornerRadius = 22;

    /// <summary>Card size in DIPs. Medium/large are two smalls plus the 16-DIP gap.</summary>
    public static Windows.Foundation.Size GetCardSize(WidgetSize size) => size switch
    {
        WidgetSize.Small => new(164, 164),
        WidgetSize.Medium => new(344, 164),
        _ => new(344, 344),
    };

    /// <summary>
    /// Card size in physical pixels at a DPI scale. A pixel card is its art
    /// size times a whole number of pixels, so at scales such as 125 % it
    /// comes out a little larger than the glass card.
    /// </summary>
    public static SizeInt32 GetCardPixels(WidgetSize size, WidgetTheme theme, double scale)
    {
        if (theme == WidgetTheme.Pixel)
        {
            var (width, height) = PixelCard.ArtSize(size);
            int factor = PixelCard.Factor(scale);
            return new SizeInt32(width * factor, height * factor);
        }
        var card = GetCardSize(size);
        return new SizeInt32((int)Math.Round(card.Width * scale), (int)Math.Round(card.Height * scale));
    }
}

/// <summary>
/// A widget instance. The host owns the window, glass card, dragging, sizing
/// and persistence; a widget only provides its content.
/// </summary>
public interface IWidget : IDisposable
{
    /// <summary>The content placed on the glass card (transparent background).</summary>
    FrameworkElement View { get; }

    /// <summary>Called once before showing and whenever the user picks another size.</summary>
    void ApplySize(WidgetSize size);

    /// <summary>Widget-specific entries for the right-click menu (may be empty).</summary>
    IEnumerable<MenuFlyoutItemBase> CreateMenuItems();

    /// <summary>Called after the user switches the app language: rebuild any text taken from <see cref="Strings"/>.</summary>
    void ApplyLanguage();
}

/// <summary>Describes a widget type and creates instances of it. Register factories in <see cref="WidgetCatalog"/>.</summary>
public interface IWidgetFactory
{
    /// <summary>Stable id stored in settings.json — never rename.</summary>
    string TypeId { get; }

    string DisplayName { get; }

    /// <summary>Segoe Fluent Icons glyph for menus.</summary>
    string Glyph { get; }

    IReadOnlyList<WidgetSize> SupportedSizes { get; }

    WidgetSize DefaultSize { get; }

    IWidget Create(WidgetContext context);
}

/// <summary>Shared services available to every widget.</summary>
public sealed class AppServices
{
    public required DeviceHub Devices { get; init; }

    /// <summary>The UI thread; device events arrive on background threads.</summary>
    public required DispatcherQueue Dispatcher { get; init; }
}

public sealed class WidgetContext
{
    public required AppServices Services { get; init; }

    /// <summary>Per-instance options, persisted with the widget.</summary>
    public required WidgetOptions Options { get; init; }

    /// <summary>
    /// The look to draw in. The host recreates every widget when the user
    /// switches theme, so this never changes for an instance. A widget without
    /// a pixel look still works: its content sits on the pixel card.
    /// </summary>
    public WidgetTheme Theme { get; init; }
}

/// <summary>String key/value options saved in settings.json with the widget instance.</summary>
public sealed class WidgetOptions
{
    private readonly Dictionary<string, string> _values;
    private readonly Action _changed;

    public WidgetOptions(Dictionary<string, string> values, Action changed)
    {
        _values = values;
        _changed = changed;
    }

    public bool GetBool(string key, bool fallback = false)
        => _values.TryGetValue(key, out var value) && bool.TryParse(value, out bool parsed) ? parsed : fallback;

    public void SetBool(string key, bool value)
    {
        _values[key] = value ? "true" : "false";
        _changed();
    }

    public string? GetString(string key) => _values.GetValueOrDefault(key);

    public void SetString(string key, string? value)
    {
        if (value is null)
            _values.Remove(key);
        else
            _values[key] = value;
        _changed();
    }
}
