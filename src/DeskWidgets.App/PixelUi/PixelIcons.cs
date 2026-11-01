using DeskWidgets.Pixel;

namespace DeskWidgets.PixelUi;

/// <summary>Sprites for the Pixel menus and dialogs: marks, arrows, 7×7 row icons and the dialog badges.</summary>
internal static class PixelIcons
{
    public static readonly PixelSprite Check = new(
        "......#",
        ".....##",
        "#...##.",
        "##.##..",
        ".###...",
        "..#....");

    public static readonly PixelSprite Radio = new(
        "..#..",
        ".###.",
        "#####",
        ".###.",
        "..#..");

    public static readonly PixelSprite Arrow = new(
        "#..",
        "##.",
        "###",
        "##.",
        "#..");

    public static readonly PixelSprite Up = new(
        "..#..",
        ".###.",
        "#####");

    public static readonly PixelSprite Down = new(
        "#####",
        ".###.",
        "..#..");

    public static readonly PixelSprite Size = new(
        "###.###",
        "#.....#",
        "#.....#",
        ".......",
        "#.....#",
        "#.....#",
        "###.###");

    public static readonly PixelSprite Globe = new(
        "..###..",
        ".#.#.#.",
        "#..#..#",
        "#######",
        "#..#..#",
        ".#.#.#.",
        "..###..");

    public static readonly PixelSprite Theme = new(
        "..###..",
        ".#..##.",
        "#...###",
        "#...###",
        "#...###",
        ".#..##.",
        "..###..");

    public static readonly PixelSprite Add = new(
        "#######",
        "#.....#",
        "#..#..#",
        "#.###.#",
        "#..#..#",
        "#.....#",
        "#######");

    public static readonly PixelSprite Remove = new(
        "..###..",
        "#######",
        ".#...#.",
        ".#.#.#.",
        ".#.#.#.",
        ".#...#.",
        ".#####.");

    public static readonly PixelSprite Refresh = new(
        "..###.#",
        ".#...##",
        "#...###",
        "#......",
        "#.....#",
        ".#...#.",
        "..###..");

    public static readonly PixelSprite Battery = new(
        ".......",
        "######.",
        "#.##.##",
        "#.##.##",
        "######.",
        ".......",
        ".......");

    public static readonly PixelSprite Clock = new(
        "..###..",
        ".#.#.#.",
        "#..#..#",
        "#..##.#",
        "#.....#",
        ".#...#.",
        "..###..");

    /// <summary>The app's mark in a dialog's title strip: 'g' Faint, 'o' Ink, anything else Green.</summary>
    public static readonly PixelSprite AppMark = new(
        "..g##..",
        ".g...#.",
        "g.....#",
        "#..o..#",
        "#.....#",
        ".#...#.",
        "..###..");

    /// <summary>The warning badge: '+' Yellow, 'x' the exclamation mark, anything else the outline.</summary>
    public static readonly PixelSprite Warning = new(
        "......#......",
        ".....#+#.....",
        ".....#+#.....",
        "....#+++#....",
        "....#+x+#....",
        "...#++x++#...",
        "...#++x++#...",
        "..#+++x+++#..",
        "..#+++++++#..",
        ".#++++x++++#.",
        ".#+++++++++#.",
        "#############");

    // The Big Folder's menu icons (the Pixel design's 7×7 sprites).

    public static readonly PixelSprite Folder = new(".##....", "#..####", "#.....#", "#.....#", "#.....#", "#######", ".......");

    public static readonly PixelSprite Pencil = new(".....##", "....#.#", "...#.#.", "..#.#..", ".#.#...", "#.#....", "##.....");

    public static readonly PixelSprite Shield = new(".#####.", "#.....#", "#..#..#", "#.###.#", "#..#..#", ".#...#.", "..###..");

    public static readonly PixelSprite PutBack = new("..#....", ".##....", "#######", ".##...#", "..#...#", "......#", "..#####");

    public static readonly PixelSprite Sort = new(".#.###.", ".#.....", ".#.##..", ".#.....", "###.#..", ".#.....", ".......");

    /// <summary>Two apps over a bar: the "Dock" row of the Add widget menus.</summary>
    public static readonly PixelSprite Dock = new(".......", ".##.##.", ".##.##.", ".......", "#######", ".......", ".......");

    /// <summary>A screen with a taskbar along its bottom: "Add taskbar apps" (Dock).</summary>
    public static readonly PixelSprite TaskbarApps = new("#######", "#.....#", "#.....#", "#.....#", "#.###.#", "#######", ".......");

    /// <summary>The 7×7 icon of a widget type in the "Add widget" submenu.</summary>
    public static PixelSprite? ForWidget(string typeId) => typeId switch
    {
        "battery" => Battery,
        "clock" => Clock,
        "worldclock" => Globe,
        "folder" => Folder,
        _ => null,
    };
}
