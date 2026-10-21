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

    // Menus and dialogs.

    /// <summary>The current menu row (pointer or keyboard), the default button's face, the question badge.</summary>
    public required uint Select { get; init; }

    /// <summary>A pressed menu row; the default button's hover, pressed and bottom row.</summary>
    public required uint SelectPressed { get; init; }

    /// <summary>The default button's top row (decoration only, never under text).</summary>
    public required uint SelectLight { get; init; }

    /// <summary>Text and marks on <see cref="Select"/> and <see cref="SelectPressed"/>.</summary>
    public required uint OnSelect { get; init; }

    /// <summary>The parent row of an open submenu whose focus is inside it.</summary>
    public required uint OpenFill { get; init; }

    public required uint Button { get; init; }
    public required uint ButtonTop { get; init; }
    public required uint ButtonHover { get; init; }
    public required uint ButtonPressed { get; init; }

    /// <summary>The hard shadow under menus and dialogs (an alpha colour, two art pixels down and right).</summary>
    public required uint Shadow { get; init; }

    // The Big Folder's sprites: the 16 Sweetie colours as fixed roles, the same by day and by night (like real app icons).
    // The sprites' symbols map to them in this order: # p r o y L G t n b l c w g s d.

    public required uint SpriteInk { get; init; }
    public required uint SpritePurple { get; init; }
    public required uint SpriteRed { get; init; }
    public required uint SpriteOrange { get; init; }
    public required uint SpriteYellow { get; init; }
    public required uint SpriteLime { get; init; }
    public required uint SpriteGreen { get; init; }
    public required uint SpriteTeal { get; init; }
    public required uint SpriteNavy { get; init; }
    public required uint SpriteBlue { get; init; }
    public required uint SpriteSky { get; init; }
    public required uint SpriteCyan { get; init; }
    public required uint SpriteWhite { get; init; }
    public required uint SpriteSilver { get; init; }
    public required uint SpriteSlate { get; init; }
    public required uint SpriteDusk { get; init; }

    /// <summary>The colour of a sprite symbol: '#' ink, p purple, r red, o orange, y yellow, L lime, G green, t teal, n navy, b blue, l sky, c cyan, w white, g silver, s slate, d dusk; 0 for anything else ('.' is transparent).</summary>
    public uint Sprite(char symbol) => symbol switch
    {
        '#' => SpriteInk,
        'p' => SpritePurple,
        'r' => SpriteRed,
        'o' => SpriteOrange,
        'y' => SpriteYellow,
        'L' => SpriteLime,
        'G' => SpriteGreen,
        't' => SpriteTeal,
        'n' => SpriteNavy,
        'b' => SpriteBlue,
        'l' => SpriteSky,
        'c' => SpriteCyan,
        'w' => SpriteWhite,
        'g' => SpriteSilver,
        's' => SpriteSlate,
        'd' => SpriteDusk,
        _ => 0,
    };

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
        Select = Rgb(0x3B5DC9),
        SelectPressed = Rgb(0x29366F),
        SelectLight = Rgb(0x41A6F6),
        OnSelect = Rgb(0xF4F4F4),
        OpenFill = Rgb(0xD5DFE6),
        Button = Rgb(0xFFFFFF),
        ButtonTop = Rgb(0xFFFFFF),
        ButtonHover = Rgb(0xE3E9ED),
        ButtonPressed = Rgb(0xC5D3DC),
        Shadow = 0x661A1C2C,
        SpriteInk = 0xFF1A1C2C,
        SpritePurple = 0xFF5D275D,
        SpriteRed = 0xFFB13E53,
        SpriteOrange = 0xFFEF7D57,
        SpriteYellow = 0xFFFFCD75,
        SpriteLime = 0xFFA7F070,
        SpriteGreen = 0xFF38B764,
        SpriteTeal = 0xFF257179,
        SpriteNavy = 0xFF29366F,
        SpriteBlue = 0xFF3B5DC9,
        SpriteSky = 0xFF41A6F6,
        SpriteCyan = 0xFF73EFF7,
        SpriteWhite = 0xFFF4F4F4,
        SpriteSilver = 0xFF94B0C2,
        SpriteSlate = 0xFF566C86,
        SpriteDusk = 0xFF333C57,
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
        Select = Rgb(0x3B5DC9),
        SelectPressed = Rgb(0x29366F),
        SelectLight = Rgb(0x41A6F6),
        OnSelect = Rgb(0xF4F4F4),
        OpenFill = Rgb(0x333C57),
        Button = Rgb(0x333C57),
        ButtonTop = Rgb(0x566C86),
        ButtonHover = Rgb(0x566C86),
        ButtonPressed = Rgb(0x1A1C2C),
        Shadow = 0x99000000,
        SpriteInk = 0xFF1A1C2C,
        SpritePurple = 0xFF5D275D,
        SpriteRed = 0xFFB13E53,
        SpriteOrange = 0xFFEF7D57,
        SpriteYellow = 0xFFFFCD75,
        SpriteLime = 0xFFA7F070,
        SpriteGreen = 0xFF38B764,
        SpriteTeal = 0xFF257179,
        SpriteNavy = 0xFF29366F,
        SpriteBlue = 0xFF3B5DC9,
        SpriteSky = 0xFF41A6F6,
        SpriteCyan = 0xFF73EFF7,
        SpriteWhite = 0xFFF4F4F4,
        SpriteSilver = 0xFF94B0C2,
        SpriteSlate = 0xFF566C86,
        SpriteDusk = 0xFF333C57,
    };

    private static uint Rgb(uint rgb) => 0xFF000000 | rgb;
}
