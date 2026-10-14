namespace DeskWidgets.Pixel;

/// <summary>
/// The pixel theme's colours (0xAARRGGBB): a day set for the light system
/// theme and a night set for dark, both built on the Sweetie 16 palette.
/// </summary>
internal sealed class PixelPalette
{
    private static PixelPalette? s_day;
    private static PixelPalette? s_night;

    // The card.
    public required uint Outline { get; init; }
    public required uint Panel { get; init; }
    public required uint Highlight { get; init; }
    public required uint Shade { get; init; }

    // Text and icons, strongest to weakest.
    public required uint Ink { get; init; }
    public required uint Soft { get; init; }
    public required uint Faint { get; init; }

    /// <summary>Inside of a recessed slot.</summary>
    public required uint Well { get; init; }

    /// <summary>A slot's shadowed top edge.</summary>
    public required uint WellEdge { get; init; }

    /// <summary>The empty part of a gauge.</summary>
    public required uint Track { get; init; }

    /// <summary>Hard drop shadow under big numbers.</summary>
    public required uint TextShadow { get; init; }

    public required uint Green { get; init; }
    public required uint Yellow { get; init; }
    public required uint Red { get; init; }

    /// <summary>The clock's weekday.</summary>
    public required uint Accent { get; init; }

    /// <summary>Second hands.</summary>
    public required uint Orange { get; init; }

    public required uint DayFace { get; init; }
    public required uint DayHands { get; init; }
    public required uint DayTicks { get; init; }
    public required uint NightFace { get; init; }
    public required uint NightHands { get; init; }
    public required uint NightTicks { get; init; }

    /// <summary>The rim around a clock face.</summary>
    public required uint Rim { get; init; }

    public static PixelPalette For(bool dark) => dark ? s_night ??= CreateNight() : s_day ??= CreateDay();

    private static PixelPalette CreateDay() => new()
    {
        Outline = Rgb(0x333C57),
        Panel = Rgb(0xF4F4F4),
        Highlight = Rgb(0xFFFFFF),
        Shade = Rgb(0xC5D3DC),
        Ink = Rgb(0x1A1C2C),
        Soft = Rgb(0x566C86),
        Faint = Rgb(0x94B0C2),
        Well = Rgb(0xE3E9ED),
        WellEdge = Rgb(0xC5D3DC),
        Track = Rgb(0xD5DFE6),
        TextShadow = Rgb(0xC5D3DC),
        Green = Rgb(0x38B764),
        Yellow = Rgb(0xFFCD75),
        Red = Rgb(0xB13E53),
        Accent = Rgb(0xB13E53),
        Orange = Rgb(0xEF7D57),
        DayFace = Rgb(0xFFFFFF),
        DayHands = Rgb(0x1A1C2C),
        DayTicks = Rgb(0x94B0C2),
        NightFace = Rgb(0x29366F),
        NightHands = Rgb(0xF4F4F4),
        NightTicks = Rgb(0x566C86),
        Rim = Rgb(0x333C57),
    };

    private static PixelPalette CreateNight() => new()
    {
        Outline = Rgb(0x0B0C14),
        Panel = Rgb(0x1A1C2C),
        Highlight = Rgb(0x333C57),
        Shade = Rgb(0x111220),
        Ink = Rgb(0xF4F4F4),
        Soft = Rgb(0x94B0C2),
        Faint = Rgb(0x566C86),
        Well = Rgb(0x131423),
        WellEdge = Rgb(0x0B0C14),
        Track = Rgb(0x333C57),
        TextShadow = Rgb(0x0B0C14),
        Green = Rgb(0xA7F070),
        Yellow = Rgb(0xFFCD75),
        Red = Rgb(0xEF5D57),
        Accent = Rgb(0xEF7D57),
        Orange = Rgb(0xEF7D57),
        DayFace = Rgb(0xF4F4F4),
        DayHands = Rgb(0x1A1C2C),
        DayTicks = Rgb(0x94B0C2),
        NightFace = Rgb(0x29366F),
        NightHands = Rgb(0xF4F4F4),
        NightTicks = Rgb(0x566C86),
        Rim = Rgb(0x566C86),
    };

    private static uint Rgb(uint rgb) => 0xFF000000 | rgb;
}
