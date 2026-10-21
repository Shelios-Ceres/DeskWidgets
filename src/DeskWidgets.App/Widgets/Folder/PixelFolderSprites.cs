using DeskWidgets.Pixel;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// The Pixel theme's Big Folder sprites (the Pixel design's sprites.txt, Phase 4): 16 × 16, drawn with the 16 fixed sprite roles of
/// <see cref="PixelPalette"/> ('#' ink, '.' transparent). The 14 file types and the 13 custom app icons are our own drawings.
/// </summary>
internal static class PixelFolderSprites
{
    /// <summary>Folder</summary>
    public static PixelSprite Folder { get; } = new(
        "................",
        ".######.........",
        "#oooooo#........",
        "#oooooo########.",
        "#oooooooooooooo#",
        "#oooooooooooooo#",
        "#ywwwwwwwwwwwwy#",
        "#yyyyyyyyyyyyyy#",
        "#yyyyyyyyyyyyyy#",
        "#yyyyyyyyyyyyyy#",
        "#yyyyyyyyyyyyyy#",
        "#yyyyyyyyyyyyyy#",
        "#yyyyyyyyyyyyyy#",
        "#yyyyyyyyyyyyyy#",
        ".##############.",
        "................");

    /// <summary>Document</summary>
    public static PixelSprite Document { get; } = new(
        "...#######......",
        "..#wwwwww##.....",
        "..#wbbbbw#g#....",
        "..#wwwwww#gg#...",
        "..#wwwwwww####..",
        "..#wwwwwwwwww#..",
        "..#wbbbbbbbbw#..",
        "..#wwwwwwwwww#..",
        "..#wbbbbbbbbw#..",
        "..#wwwwwwwwww#..",
        "..#wbbbbbbbbw#..",
        "..#wwwwwwwwww#..",
        "..#wbbbbbwwww#..",
        "..#wwwwwwwwww#..",
        "..#wwwwwwwwww#..",
        "...##########...");

    /// <summary>Spreadsheet</summary>
    public static PixelSprite Spreadsheet { get; } = new(
        "...#######......",
        "..#wwwwww##.....",
        "..#wGGGGw#g#....",
        "..#wwwwww#gg#...",
        "..#wwwwwww####..",
        "..#wwwwwwwwww#..",
        "..#wGGGGGGGGw#..",
        "..#wGGGGGGGGw#..",
        "..#wGwwGwwwGw#..",
        "..#wGGGGGGGGw#..",
        "..#wGwwGwwwGw#..",
        "..#wGGGGGGGGw#..",
        "..#wGLLGLLLGw#..",
        "..#wwwwwwwwww#..",
        "..#wwwwwwwwww#..",
        "...##########...");

    /// <summary>Presentation</summary>
    public static PixelSprite Presentation { get; } = new(
        "...#######......",
        "..#wwwwww##.....",
        "..#woooow#g#....",
        "..#wwwwww#gg#...",
        "..#wwwwwww####..",
        "..#wwwwwwwwww#..",
        "..#woooooooow#..",
        "..#wooooowoow#..",
        "..#wooowowoow#..",
        "..#wowowowoow#..",
        "..#wowowowoow#..",
        "..#woooooooow#..",
        "..#wwwwsswwww#..",
        "..#wwwwwwwwww#..",
        "..#wwwwwwwwww#..",
        "...##########...");

    /// <summary>PDF</summary>
    public static PixelSprite Pdf { get; } = new(
        "...#######......",
        "..#wwwwww##.....",
        "..#wssssw#g#....",
        "..#wwwwww#gg#...",
        "..#wsssssw####..",
        "..#wwwwwwwwww#..",
        "..#wwwwwwwwww#..",
        "..#rrrrrrrrrr#..",
        "..#rwrwwrwwrr#..",
        "..#rrwrrwrrwr#..",
        "..#rwrwwrwwrr#..",
        "..#rrrrrrrrrr#..",
        "..#wwwwwwwwww#..",
        "..#wsssssswww#..",
        "..#wwwwwwwwww#..",
        "...##########...");

    /// <summary>Text</summary>
    public static PixelSprite Text { get; } = new(
        "...#######......",
        "..#wwwwww##.....",
        "..#wssssw#g#....",
        "..#wwwwww#gg#...",
        "..#wwwwwww####..",
        "..#wwwwwwwwww#..",
        "..#wssssssssw#..",
        "..#wwwwwwwwww#..",
        "..#wsssssssww#..",
        "..#wwwwwwwwww#..",
        "..#wssssssssw#..",
        "..#wwwwwwwwww#..",
        "..#wsssssswww#..",
        "..#wwwwwwwwww#..",
        "..#wwwwwwwwww#..",
        "...##########...");

    /// <summary>Code</summary>
    public static PixelSprite Code { get; } = new(
        "...#######......",
        "..#wwwwww##.....",
        "..#wssssw#g#....",
        "..#wwwwww#gg#...",
        "..#wwwwwww####..",
        "..#wwwwwwwwww#..",
        "..#wwwtwwtwww#..",
        "..#wwtwwwtwww#..",
        "..#wtwwwtwwtw#..",
        "..#wwtwwtwtww#..",
        "..#wwwtwtwwtw#..",
        "..#wwwwtwwtww#..",
        "..#wwwwwwwwww#..",
        "..#wwwwwwwwww#..",
        "..#wwwwwwwwww#..",
        "...##########...");

    /// <summary>Archive</summary>
    public static PixelSprite Archive { get; } = new(
        "...#######......",
        "..#wwwwdg##.....",
        "..#wwwwgw#g#....",
        "..#wwwwdg#gg#...",
        "..#wwwwgww####..",
        "..#wwwwdgwwww#..",
        "..#wwwwgwwwww#..",
        "..#wwwwdgwwww#..",
        "..#wwwwgwwwww#..",
        "..#wwwdddwwww#..",
        "..#wwwdydwwww#..",
        "..#wwwdydwwww#..",
        "..#wwwdddwwww#..",
        "..#wwwwwwwwww#..",
        "..#wwwwwwwwww#..",
        "...##########...");

    /// <summary>Audio</summary>
    public static PixelSprite Audio { get; } = new(
        "...#######......",
        "..#wwwwww##.....",
        "..#wssssw#g#....",
        "..#wwwwww#gg#...",
        "..#wwwwwww####..",
        "..#wwwwwwpppw#..",
        "..#wwwwwwppww#..",
        "..#wwwwwwpwww#..",
        "..#wwwwwwpwww#..",
        "..#wwwwwwpwww#..",
        "..#wwwwpppwww#..",
        "..#wwwpppwwww#..",
        "..#wwwwpwwwww#..",
        "..#wwwwwwwwww#..",
        "..#wwwwwwwwww#..",
        "...##########...");

    /// <summary>Web link</summary>
    public static PixelSprite Weblink { get; } = new(
        "................",
        ".....######.....",
        "...##llbbll##...",
        "..#lbllbbllbl#..",
        "..#GGllbbllbl#..",
        ".#lGGGlbbllbll#.",
        ".#llGGlbbllbll#.",
        ".#bbbbbbbbbbbb#.",
        ".#bbbbbbbbbbbb#.",
        ".#llbllbbllGGl#.",
        ".#llbllbblGGGl#.",
        "..#lbllbbllGl#..",
        "..#lbllbbllbl#..",
        "...##llbbll##...",
        ".....######.....",
        "................");

    /// <summary>Image</summary>
    public static PixelSprite Image { get; } = new(
        "................",
        ".##############.",
        "#llllllllllllll#",
        "#llllllllllllll#",
        "#lllllllllyywll#",
        "#lllllllllyylll#",
        "#llllllllllllll#",
        "#llllllllllllll#",
        "#llltllllllllll#",
        "#lltttlllllGlll#",
        "#ltttttlllGGGll#",
        "#tttttttGGGGGGl#",
        "#GGGGGGGGGGGGGG#",
        "#GGGGGGGGGGGGGG#",
        ".##############.",
        "................");

    /// <summary>Video</summary>
    public static PixelSprite Video { get; } = new(
        "................",
        ".##############.",
        "#dddddddddddddd#",
        "#gdgdgdgdgdgdgd#",
        "#dddddddddddddd#",
        "#dddddwdddddddd#",
        "#dddddwwddddddd#",
        "#dddddwwwdddddd#",
        "#dddddwwwdddddd#",
        "#dddddwwddddddd#",
        "#dddddwdddddddd#",
        "#dddddddddddddd#",
        "#gdgdgdgdgdgdgd#",
        "#dddddddddddddd#",
        ".##############.",
        "................");

    /// <summary>App</summary>
    public static PixelSprite App { get; } = new(
        "................",
        "...ssssssssss...",
        "..ssssssssssss..",
        ".ssssssssssssss.",
        ".ssswwwsswwwsss.",
        ".ssswwwsswwwsss.",
        ".ssswwwsswwwsss.",
        ".ssssssssssssss.",
        ".ssssssssssssss.",
        ".ssswwwssgggsss.",
        ".ssswwwssgggsss.",
        ".ssswwwssgggsss.",
        ".ssssssssssssss.",
        "..ssssssssssss..",
        "...ssssssssss...",
        "................");

    /// <summary>Other</summary>
    public static PixelSprite Other { get; } = new(
        "...#######......",
        "..#wwwwww##.....",
        "..#wwwwww#g#....",
        "..#wwwwww#gg#...",
        "..#wwwwwww####..",
        "..#wwwwwwwwww#..",
        ".##wwwwwwwwww##.",
        "#dddddddddddddd#",
        "#dddddddddddddd#",
        "#dddddddddddddd#",
        "#dddddddddddddd#",
        "#dddddddddddddd#",
        "#dddddddddddddd#",
        "#dddddddddddddd#",
        ".##wwwwwwwwww##.",
        "...##########...");

    /// <summary>WeChat</summary>
    public static PixelSprite Wechat { get; } = new(
        "...##########...",
        "..#GGGGGGGGGG#..",
        ".#GGGGGGGGGGGG#.",
        "#GGGGGGGGGGGGGG#",
        "#GGGGwwwwwwGGGG#",
        "#GGGwwwwwwwwGGG#",
        "#GGGwGwwGwwGGGG#",
        "#GGGwwwwwwwwGGG#",
        "#GGGGwwwwwwGGGG#",
        "#GGGGwwGGGGGGGG#",
        "#GGGwGGGGGGGGGG#",
        "#GGGGGGGGGGGGGG#",
        "#GGGGGGGGGGGGGG#",
        ".#tGGGGGGGGGGt#.",
        "..#tttttttttt#..",
        "...##########...");

    /// <summary>QQ</summary>
    public static PixelSprite Qq { get; } = new(
        "...##########...",
        "..#yyyyyyyyyy#..",
        ".#yyyyyyyyyyyy#.",
        "#yyyyywwwwyyyyy#",
        "#yyyywwwwwwyyyy#",
        "#yyyww#ww#wwyyy#",
        "#yyywwwwwwwwyyy#",
        "#yyywwwwwwwwyyy#",
        "#yyyywwwwwwyyyy#",
        "#yyyrrrrrrrryyy#",
        "#yyyyrryyrryyyy#",
        "#yyyyyyyyyyyyyy#",
        "#yyyyyyyyyyyyyy#",
        ".#oyyyyyyyyyyo#.",
        "..#oooooooooo#..",
        "...##########...");

    /// <summary>DingTalk</summary>
    public static PixelSprite Dingtalk { get; } = new(
        "...##########...",
        "..#llllllllll#..",
        ".#llllllllllll#.",
        "#llllllwwllllll#",
        "#lllllwwwwlllll#",
        "#llllwwwwwwllll#",
        "#llllwwwwwwllll#",
        "#llllwwwwwwllll#",
        "#llllwwwwwwllll#",
        "#lllwwwwwwwwlll#",
        "#llllllllllllll#",
        "#llllllwwllllll#",
        "#llllllllllllll#",
        ".#bllllllllllb#.",
        "..#bbbbbbbbbb#..",
        "...##########...");

    /// <summary>Tencent Meeting</summary>
    public static PixelSprite Wemeet { get; } = new(
        "...##########...",
        "..#cccccccccc#..",
        ".#cccccccccccc#.",
        "#cccccccccccccc#",
        "#cccccccccccccc#",
        "#ccnnnnnnnccccc#",
        "#ccnnnnnnnccncc#",
        "#ccnnwwnnncnncc#",
        "#ccnnwwnnnnnncc#",
        "#ccnnnnnnncnncc#",
        "#ccnnnnnnnccncc#",
        "#cccccccccccccc#",
        "#cccccccccccccc#",
        ".#lccccccccccl#.",
        "..#llllllllll#..",
        "...##########...");

    /// <summary>Word</summary>
    public static PixelSprite Word { get; } = new(
        "...##########...",
        "..#bbbbbbbbbb#..",
        ".#bbbbbbbbbbbb#.",
        "#bbbwwwwwbbbbbb#",
        "#bbbwwwwwgbbbbb#",
        "#bbbwwwwwwgbbbb#",
        "#bbbwnnnnnwwbbb#",
        "#bbbwwwwwwwwbbb#",
        "#bbbwnnnnnnwbbb#",
        "#bbbwwwwwwwwbbb#",
        "#bbbwnnnnnnwbbb#",
        "#bbbwwwwwwwwbbb#",
        "#bbbwnnnnwwwbbb#",
        ".#nbbbbbbbbbbn#.",
        "..#nnnnnnnnnn#..",
        "...##########...");

    /// <summary>Excel</summary>
    public static PixelSprite Excel { get; } = new(
        "...##########...",
        "..#tttttttttt#..",
        ".#tttttttttttt#.",
        "#tttttttttttttt#",
        "#ttwwwwwwwwwwtt#",
        "#ttwttwttwttwtt#",
        "#ttwwwwwwwwwwtt#",
        "#ttwttwttwttwtt#",
        "#ttwwwwwwwwwwtt#",
        "#ttwttwttwttwtt#",
        "#ttwwwwwwwwwwtt#",
        "#tttttttttttttt#",
        "#tttttttttttttt#",
        ".#nttttttttttn#.",
        "..#nnnnnnnnnn#..",
        "...##########...");

    /// <summary>PowerPoint</summary>
    public static PixelSprite Powerpoint { get; } = new(
        "...##########...",
        "..#rrrrrrrrrr#..",
        ".#rrrrrrrrrrrr#.",
        "#rrrrrrrrrrrrrr#",
        "#rrrrrwwoorrrrr#",
        "#rrrrwwwooorrrr#",
        "#rrrwwwwoooorrr#",
        "#rrrwwwwoooorrr#",
        "#rrrwwwwwwwwrrr#",
        "#rrrwwwwwwwwrrr#",
        "#rrrrwwwwwwrrrr#",
        "#rrrrrwwwwrrrrr#",
        "#rrrrrrrrrrrrrr#",
        ".#prrrrrrrrrrp#.",
        "..#pppppppppp#..",
        "...##########...");

    /// <summary>Chrome</summary>
    public static PixelSprite Chrome { get; } = new(
        "...##########...",
        "..#wwwwwwwwww#..",
        ".#wwwwwwwwwwww#.",
        "#wwrrryyyyGGGww#",
        "#wwrrryyyyGGGww#",
        "#wwrrryyyyGGGww#",
        "#wwwwwwwwwwwwww#",
        "#wwggggggggggww#",
        "#wwwwwwwwwwwwww#",
        "#wwggggggggwwww#",
        "#wwwwwwwwwwwwww#",
        "#wwgggggggggwww#",
        "#wwwwwwwwwwwwww#",
        ".#gwwwwwwwwwwg#.",
        "..#gggggggggg#..",
        "...##########...");

    /// <summary>Edge</summary>
    public static PixelSprite Edge { get; } = new(
        "...##########...",
        "..#bbbbbbbbbb#..",
        ".#bbbbbbbbbbbb#.",
        "#bbbbbbbbbbbbbb#",
        "#bbbbbbbbbbbbbb#",
        "#bbbbbwwbbbbbbb#",
        "#bbbbwwwwbbbbbb#",
        "#bbbwwbbwwbbwbb#",
        "#GGwwGGGGwwwwGG#",
        "#GGGGGGGGGwwGGG#",
        "#GGGGGGGGGGGGGG#",
        "#GGGGGGGGGGGGGG#",
        "#GGGGGGGGGGGGGG#",
        "#GGttttttttttGG#",
        "#GGGGGGGGGGGGGG#",
        ".##############.");

    /// <summary>Claude</summary>
    public static PixelSprite Claude { get; } = new(
        "...##########...",
        "..#oooooooooo#..",
        ".#oooooooooooo#.",
        "#oooooowwoooooo#",
        "#oooooowwoooooo#",
        "#ooooowwwwooooo#",
        "#oooowwwwwwoooo#",
        "#oowwwwwwwwwwoo#",
        "#oowwwwwwwwwwoo#",
        "#oooowwwwwwoooo#",
        "#ooooowwwwooooo#",
        "#oooooowwoooooo#",
        "#oooooowwoooooo#",
        ".#roooooooooor#.",
        "..#rrrrrrrrrr#..",
        "...##########...");

    /// <summary>ChatGPT</summary>
    public static PixelSprite Chatgpt { get; } = new(
        "...##########...",
        "..#dddddddddd#..",
        ".#dddddddddddd#.",
        "#dddddwwwwddddd#",
        "#ddddwddddwdddd#",
        "#dddwddddddwddd#",
        "#ddwdddwwdddwdd#",
        "#ddwddwddwddwdd#",
        "#ddwddwddwddwdd#",
        "#ddwdddwwdddwdd#",
        "#dddwddddddwddd#",
        "#ddddwddddwdddd#",
        "#dddddwwwwddddd#",
        ".##dddddddddd##.",
        "...##########...",
        "................");

    /// <summary>Cursor</summary>
    public static PixelSprite Cursor { get; } = new(
        "...##########...",
        "..#ssssssssss#..",
        ".#ssssssssssss#.",
        "#sssswsssssssss#",
        "#sssswwssssssss#",
        "#sssswwwsssssss#",
        "#sssswwwwssssss#",
        "#sssswwwwwsssss#",
        "#sssswwwwwwssss#",
        "#sssswwwsssssss#",
        "#sssswswwssssss#",
        "#sssssswwssssss#",
        "#ssssssssssssss#",
        ".#dssssssssssd#.",
        "..#dddddddddd#..",
        "...##########...");

    /// <summary>Kimi</summary>
    public static PixelSprite Kimi { get; } = new(
        "...##########...",
        "..#nnnnnnnnnn#..",
        ".#nnnnnnnnnnnn#.",
        "#nnnnnnnnnnnnnn#",
        "#nnnnyynnnnwnnn#",
        "#nnnyynnnnnnnnn#",
        "#nnnyynnnnnnnnn#",
        "#nnnyynnnnnnwnn#",
        "#nnnyyynnnnnnnn#",
        "#nnnyyyynnnnnnn#",
        "#nnnyyyyyyyynnn#",
        "#nnnnyyyyyynnnn#",
        "#nnnnnnnnnnnnnn#",
        ".##nnnnnnnnnn##.",
        "...##########...",
        "................");

    public static PixelSprite For(FolderItemKind kind) => kind switch
    {
        FolderItemKind.Folder => Folder,
        FolderItemKind.Document => Document,
        FolderItemKind.Spreadsheet => Spreadsheet,
        FolderItemKind.Presentation => Presentation,
        FolderItemKind.Pdf => Pdf,
        FolderItemKind.Text => Text,
        FolderItemKind.Code => Code,
        FolderItemKind.Archive => Archive,
        FolderItemKind.Audio => Audio,
        FolderItemKind.WebLink => Weblink,
        FolderItemKind.Image => Image,
        FolderItemKind.Video => Video,
        FolderItemKind.App => App,
        FolderItemKind.Other => Other,
        _ => Other,
    };

    /// <summary>The sprite of a custom app icon (the key is the one in <see cref="AppIconRow"/>), or null.</summary>
    public static PixelSprite? ForApp(string key) => key switch
    {
        "wechat" => Wechat,
        "qq" => Qq,
        "dingtalk" => Dingtalk,
        "tencentmeeting" => Wemeet,
        "word" => Word,
        "excel" => Excel,
        "powerpoint" => Powerpoint,
        "chrome" => Chrome,
        "edge" => Edge,
        "claude" => Claude,
        "chatgpt" => Chatgpt,
        "cursor" => Cursor,
        "kimi" => Kimi,
        _ => null,
    };
}
