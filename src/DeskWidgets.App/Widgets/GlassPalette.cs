using DeskWidgets.Core.Devices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DeskWidgets.Widgets;

/// <summary>
/// The Glass theme's colours as plain ARGB values (0xAARRGGBB), one set for the
/// light system theme and one for the dark one. The roles are those of
/// docs/THEMES.md section 8. The dial roles don't change with the system theme:
/// a face is day or night by the city's local time.
/// </summary>
internal sealed record GlassColors
{
    /// <summary>Names, values, big numbers.</summary>
    public required uint Primary { get; init; }

    /// <summary>State, time, date, time difference.</summary>
    public required uint Secondary { get; init; }

    /// <summary>Empty groove of a battery ring.</summary>
    public required uint Track { get; init; }

    /// <summary>Empty device slot.</summary>
    public required uint Placeholder { get; init; }

    public required uint Green { get; init; }

    public required uint Yellow { get; init; }

    public required uint Red { get; init; }

    /// <summary>The clock's weekday.</summary>
    public required uint Accent { get; init; }

    /// <summary>The world clock's second hand.</summary>
    public required uint Orange { get; init; }

    public required uint DayFace { get; init; }

    /// <summary>Hands, numerals and ticks on a day face.</summary>
    public required uint DayInk { get; init; }

    /// <summary>Minor ticks on a day face: the ink at 45%.</summary>
    public required uint DayFaint { get; init; }

    public required uint NightFace { get; init; }

    /// <summary>Hands, numerals and ticks on a night face.</summary>
    public required uint NightInk { get; init; }

    /// <summary>Minor ticks on a night face: the ink at 45%.</summary>
    public required uint NightFaint { get; init; }

    /// <summary>Glyph drawn on a coloured badge (the charging bolt). Not part of the shared roles of the clocks; the battery widget uses it.</summary>
    public required uint OnAccent { get; init; }

    public static readonly GlassColors Light = new()
    {
        Primary = 0xFF1C1C1E,
        Secondary = 0x993C3C43,
        Track = 0x24000000,
        Placeholder = 0x14000000,
        Green = 0xFF34C759,
        Yellow = 0xFFFFCC00,
        Red = 0xFFFF3B30,
        Accent = 0xFFFF453A,
        Orange = 0xFFFF9500,
        DayFace = 0xFFFFFFFF,
        DayInk = 0xFF1C1C1E,
        DayFaint = 0x731C1C1E,
        NightFace = 0xFF1C1C1E,
        NightInk = 0xFFFFFFFF,
        NightFaint = 0x73FFFFFF,
        OnAccent = 0xFFFFFFFF,
    };

    public static readonly GlassColors Dark = new()
    {
        Primary = 0xFFFFFFFF,
        Secondary = 0x99EBEBF5,
        Track = 0x3DFFFFFF,
        Placeholder = 0x1FFFFFFF,
        Green = 0xFF30D158,
        Yellow = 0xFFFFD60A,
        Red = 0xFFFF453A,
        Accent = 0xFFFF453A,
        Orange = 0xFFFF9500,
        DayFace = 0xFFFFFFFF,
        DayInk = 0xFF1C1C1E,
        DayFaint = 0x731C1C1E,
        NightFace = 0xFF1C1C1E,
        NightInk = 0xFFFFFFFF,
        NightFaint = 0x73FFFFFF,
        OnAccent = 0xFF000000,
    };
}

/// <summary>
/// The brushes of the Glass theme: <see cref="GlassColors"/> turned into brushes
/// once per system theme and shared by every widget. Create them on the UI thread.
/// </summary>
internal sealed class GlassPalette
{
    private static GlassPalette? s_light;
    private static GlassPalette? s_dark;

    private GlassPalette(GlassColors colors)
    {
        Primary = Brush(colors.Primary);
        Secondary = Brush(colors.Secondary);
        Track = Brush(colors.Track);
        Placeholder = Brush(colors.Placeholder);
        Green = Brush(colors.Green);
        Yellow = Brush(colors.Yellow);
        Red = Brush(colors.Red);
        Accent = Brush(colors.Accent);
        Orange = Brush(colors.Orange);
        DayFace = Brush(colors.DayFace);
        DayInk = Brush(colors.DayInk);
        DayFaint = Brush(colors.DayFaint);
        NightFace = Brush(colors.NightFace);
        NightInk = Brush(colors.NightInk);
        NightFaint = Brush(colors.NightFaint);
        OnAccent = Brush(colors.OnAccent);
    }

    public SolidColorBrush Primary { get; }

    public SolidColorBrush Secondary { get; }

    public SolidColorBrush Track { get; }

    public SolidColorBrush Placeholder { get; }

    public SolidColorBrush Green { get; }

    public SolidColorBrush Yellow { get; }

    public SolidColorBrush Red { get; }

    public SolidColorBrush Accent { get; }

    public SolidColorBrush Orange { get; }

    public SolidColorBrush DayFace { get; }

    public SolidColorBrush DayInk { get; }

    public SolidColorBrush DayFaint { get; }

    public SolidColorBrush NightFace { get; }

    public SolidColorBrush NightInk { get; }

    public SolidColorBrush NightFaint { get; }

    public SolidColorBrush OnAccent { get; }

    public static GlassPalette For(ElementTheme theme)
        => theme == ElementTheme.Dark ? (s_dark ??= new GlassPalette(GlassColors.Dark)) : (s_light ??= new GlassPalette(GlassColors.Light));

    /// <summary>Red at 20% or below (unless charging), yellow in power-saving mode, green otherwise — like iOS.</summary>
    public SolidColorBrush LevelBrush(DeviceStatus device)
    {
        if (device.BatteryPercent is int percent && percent <= 20 && !device.IsCharging)
            return Red;
        return device.IsPowerSaving ? Yellow : Green;
    }

    private static SolidColorBrush Brush(uint argb)
        => new(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
}
