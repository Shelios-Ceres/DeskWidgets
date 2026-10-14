using DeskWidgets.Core.Devices;

namespace DeskWidgets.Pixel;

/// <summary>
/// Hand-drawn sprites for the pixel theme. '#' is ink, '+' a softer second
/// tone, '.' transparent; the caller picks the actual colours.
/// </summary>
internal static class PixelSprites
{
    public static PixelSprite Mouse { get; } = new(
        "..#####..",
        ".#..#..#.",
        "#...#...#",
        "#..+#+..#",
        "#...#...#",
        "#########",
        "#.......#",
        "#.......#",
        "#.......#",
        "#.......#",
        ".#.....#.",
        "..#####..");

    public static PixelSprite Keyboard { get; } = new(
        ".###########.",
        "#...........#",
        "#.#.#.#.#.#.#",
        "#...........#",
        "#..#.#.#.#..#",
        "#...........#",
        "#..#######..#",
        ".###########.");

    public static PixelSprite Headphones { get; } = new(
        "....#####....",
        "..##.....##..",
        ".#.........#.",
        "#...........#",
        "#...........#",
        "#...........#",
        "###.......###",
        "####.....####",
        "####.....####",
        "####.....####",
        ".##.......##.");

    public static PixelSprite Headset { get; } = new(
        "....#####....",
        "..##.....##..",
        ".#.........#.",
        "#...........#",
        "#...........#",
        "#...........#",
        "###.......###",
        "####.....####",
        "####.....####",
        "####.....####",
        ".##.......##.",
        "..#..........",
        "...###.......");

    public static PixelSprite Earbuds { get; } = new(
        ".###...###.",
        "#+++#.#+++#",
        "#+++#.#+++#",
        ".####.####.",
        "..##...##..",
        "..##...##..",
        "..##...##..",
        "..##...##..",
        "..##...##..");

    public static PixelSprite Speaker { get; } = new(
        "#########",
        "#.......#",
        "#...#...#",
        "#..#.#..#",
        "#...#...#",
        "#.......#",
        "#..###..#",
        "#.#+++#.#",
        "#.#+#+#.#",
        "#.#+++#.#",
        "#..###..#",
        "#########");

    public static PixelSprite GameController { get; } = new(
        ".###.......###.",
        "#...#######...#",
        "#..#.......#..#",
        "#.###.....#.#.#",
        "#..#.......#..#",
        "#.............#",
        "#....#####....#",
        "#...#.....#...#",
        ".###.......###.");

    public static PixelSprite Phone { get; } = new(
        ".#####.",
        "#..#..#",
        "#+++++#",
        "#+++++#",
        "#+++++#",
        "#+++++#",
        "#+++++#",
        "#+++++#",
        "#.....#",
        "#.###.#",
        ".#####.");

    public static PixelSprite Tablet { get; } = new(
        ".#########.",
        "#.........#",
        "#.+++++++.#",
        "#.+++++++.#",
        "#.+++++++.#",
        "#.+++++++.#",
        "#.+++++++.#",
        "#.+++++++.#",
        "#.+++++++.#",
        "#....#....#",
        ".#########.");

    public static PixelSprite Pen { get; } = new(
        "........##",
        ".......#+#",
        "......#+#.",
        ".....#+#..",
        "....#+#...",
        "...#+#....",
        "..#+#.....",
        ".##.......",
        ".#........",
        "#.........");

    public static PixelSprite Watch { get; } = new(
        "..###..",
        "..###..",
        ".#####.",
        "#.....#",
        "#..#..#",
        "#..##.#",
        "#.....#",
        ".#####.",
        "..###..",
        "..###..");

    public static PixelSprite Laptop { get; } = new(
        "..###########..",
        "..#+++++++++#..",
        "..#+++++++++#..",
        "..#+++++++++#..",
        "..#+++++++++#..",
        "..#+++++++++#..",
        "..###########..",
        ".#############.",
        "###############");

    public static PixelSprite Desktop { get; } = new(
        "#############",
        "#+++++++++++#",
        "#+++++++++++#",
        "#+++++++++++#",
        "#+++++++++++#",
        "#+++++++++++#",
        "#############",
        ".....###.....",
        ".....###.....",
        "...#######...");

    public static PixelSprite Bluetooth { get; } = new(
        "..#..",
        "..##.",
        "#.#.#",
        ".###.",
        "..#..",
        ".###.",
        "#.#.#",
        "..##.",
        "..#..");

    /// <summary>Charging bolt; draw it outlined so it reads over a gauge.</summary>
    public static PixelSprite Bolt { get; } = new(
        "...##",
        "..##.",
        ".##..",
        "#####",
        "..##.",
        ".##..",
        "##...");

    public static PixelSprite SmallBolt { get; } = new(
        "..#",
        ".##",
        "###",
        "##.",
        "#..");

    public static PixelSprite Sun { get; } = new(
        "....#....",
        ".#.....#.",
        "...###...",
        "..#####..",
        "#.#####.#",
        "..#####..",
        "...###...",
        ".#.....#.",
        "....#....");

    public static PixelSprite Moon { get; } = new(
        "..###.",
        ".###..",
        "###...",
        "###...",
        "###...",
        ".###..",
        "..###.");

    public static PixelSprite For(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => Mouse,
        DeviceKind.Keyboard => Keyboard,
        DeviceKind.Headphones => Headphones,
        DeviceKind.Headset => Headset,
        DeviceKind.Earbuds => Earbuds,
        DeviceKind.Speaker => Speaker,
        DeviceKind.GameController => GameController,
        DeviceKind.Phone => Phone,
        DeviceKind.Tablet => Tablet,
        DeviceKind.Pen => Pen,
        DeviceKind.Watch => Watch,
        DeviceKind.Laptop => Laptop,
        DeviceKind.Desktop => Desktop,
        _ => Bluetooth,
    };
}
