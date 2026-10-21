namespace DeskWidgets.Widgets.Folder;

internal enum TileLayerKind
{
    Fill,
    Stroke,
}

/// <summary>One layer of a tile's art, in the 64 × 64 box: a filled path, or an unfilled path with round caps and joins.</summary>
internal sealed record TileLayer(TileLayerKind Kind, uint Paint, double Opacity, double StrokeWidth, string Path);

/// <summary>A designed tile: its colour in light and dark (one colour for a custom app icon), its layers, and optionally a gradient end.</summary>
internal sealed record TileArt(uint Light, uint Dark, TileLayer[] Layers, uint? GradientEnd = null)
{
    public uint ColorFor(bool dark) => dark ? Dark : Light;
}

/// <summary>
/// The Glass tile art of the Big Folder: the 14 file types and the 13 custom app icons, from the Glass design's ICONS.md
/// (Phase 4). Generated from that file; to add an app, add the art here and one row to <see cref="AppIconPack"/>.
/// </summary>
internal static class GlassTileArt
{
    public static IReadOnlyDictionary<string, TileArt> All { get; } = new Dictionary<string, TileArt>
    {
        ["folder"] = new(0xFF34AADC, 0xFF40B8E8, [new(TileLayerKind.Fill, 0xFFFFFFFF, 0.72, 0.0, "M14 22a4 4 0 0 1 4-4h9.2a3 3 0 0 1 2.2.96l2.9 3.08a3 3 0 0 0 2.2.96H46a4 4 0 0 1 4 4V44a4 4 0 0 1-4 4H18a4 4 0 0 1-4-4z"), new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M14 30a3 3 0 0 1 3-3h30a3 3 0 0 1 3 3v14a4 4 0 0 1-4 4H18a4 4 0 0 1-4-4z")]),
        ["document"] = new(0xFF007AFF, 0xFF0A84FF, [new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 4.0, "M20 16H44M20 24H44M20 32H36")]),
        ["spreadsheet"] = new(0xFF34C759, 0xFF30D158, [new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 3.5, "M21 12h22a3 3 0 0 1 3 3v19a3 3 0 0 1-3 3H21a3 3 0 0 1-3-3V15a3 3 0 0 1 3-3zM18 20.5H46M18 28.5H46M28.5 12V37")]),
        ["presentation"] = new(0xFFFF9500, 0xFFFF9F0A, [new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 3.5, "M19 12h26a3 3 0 0 1 3 3v15a3 3 0 0 1-3 3H19a3 3 0 0 1-3-3V15a3 3 0 0 1 3-3zM32 33v5"), new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 3.5, "M25 27v-4M32 27v-8M39 27v-6")]),
        ["pdf"] = new(0xFFFF3B30, 0xFFFF453A, [new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 3.6, "M14 42V22h5.5a5 5 0 0 1 0 10H14M27 42V22h4a8 10 0 0 1 0 20h-4zM42 42V22h9M42 31.5h7.5")]),
        ["text"] = new(0xFF8E8E93, 0xFF98989D, [new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 4.2, "M17 19H35M26 19V45"), new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 3.2, "M40 31H48M40 39H48M40 45H46")]),
        ["code"] = new(0xFF5856D6, 0xFF5E5CE6, [new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 4.2, "M23 22L14 32l9 10M41 22l9 10-9 10M35.5 18l-7 28")]),
        ["archive"] = new(0xFFA2845E, 0xFFAC8E68, [new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M26.5 6h5.5v5h-5.5zM32 11h5.5v5H32zM26.5 16h5.5v5h-5.5zM32 21h5.5v5H32z"), new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 3.4, "M27 30.5h10a2.5 2.5 0 0 1 2.5 2.5v10.5a2.5 2.5 0 0 1-2.5 2.5H27a2.5 2.5 0 0 1-2.5-2.5V33a2.5 2.5 0 0 1 2.5-2.5zM32 36v4.5")]),
        ["audio"] = new(0xFFFF2D55, 0xFFFF375F, [new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 4.0, "M28 42V19l16-4v21"), new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M23 47.5a5.5 5 0 1 0 0-10a5.5 5 0 1 0 0 10zM39 41.5a5.5 5 0 1 0 0-10a5.5 5 0 1 0 0 10z")]),
        ["weblink"] = new(0xFF30B0C7, 0xFF40C8E0, [new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 3.2, "M32 17a15 15 0 1 0 0 30a15 15 0 1 0 0-30zM32 17c-4.5 4-7 9.3-7 15s2.5 11 7 15M32 17c4.5 4 7 9.3 7 15s-2.5 11-7 15M17.5 28h29M17.5 36h29")]),
        ["image"] = new(0xFF00C7BE, 0xFF00C7BE, [new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M13 45l12-16 8 10 6-7 12 13z"), new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M44 25.5a4.5 4.5 0 1 0 0-9a4.5 4.5 0 1 0 0 9z")]),
        ["video"] = new(0xFFAF52DE, 0xFFBF5AF2, [new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M26 19.6v24.8a2 2 0 0 0 3 1.7l19.6-12.4a2 2 0 0 0 0-3.4L29 17.9a2 2 0 0 0-3 1.7z")]),
        ["app"] = new(0xFF636366, 0xFF7C7C80, [new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M21 18h7a3 3 0 0 1 3 3v7a3 3 0 0 1-3 3h-7a3 3 0 0 1-3-3v-7a3 3 0 0 1 3-3zM36 18h7a3 3 0 0 1 3 3v7a3 3 0 0 1-3 3h-7a3 3 0 0 1-3-3v-7a3 3 0 0 1 3-3zM21 33h7a3 3 0 0 1 3 3v7a3 3 0 0 1-3 3h-7a3 3 0 0 1-3-3v-7a3 3 0 0 1 3-3zM36 33h7a3 3 0 0 1 3 3v7a3 3 0 0 1-3 3h-7a3 3 0 0 1-3-3v-7a3 3 0 0 1 3-3z")]),
        ["other"] = new(0xFFAEAEB2, 0xFF8E8E93, []),
        ["app-wechat"] = new(0xFF07C160, 0xFF07C160, [new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M32 14c-10.5 0-18 6.9-18 15.2 0 4.7 2.4 8.8 6.3 11.6L18.6 48l7.6-3.8c1.8.5 3.8.7 5.8.7 10.5 0 18-6.9 18-15.7S42.5 14 32 14z"), new(TileLayerKind.Fill, 0xFF07C160, 1.0, 0.0, "M24 32a2.7 2.7 0 1 0 0-5.4a2.7 2.7 0 1 0 0 5.4zM32 32a2.7 2.7 0 1 0 0-5.4a2.7 2.7 0 1 0 0 5.4zM40 32a2.7 2.7 0 1 0 0-5.4a2.7 2.7 0 1 0 0 5.4z")]),
        ["app-qq"] = new(0xFF12B7F5, 0xFF12B7F5, [new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M32 12c-8.8 0-14 8.4-14 18.5C18 41.6 24.3 50 32 50s14-8.4 14-19.5C46 20.4 40.8 12 32 12z"), new(TileLayerKind.Fill, 0xFF1D2B4F, 1.0, 0.0, "M32 12c-8.8 0-14 8.4-14 18.5 0 1 .05 2 .15 3 3.3-3.4 8.2-5 13.85-5s10.55 1.6 13.85 5c.1-1 .15-2 .15-3C46 20.4 40.8 12 32 12z"), new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M27 25.6a2.2 2.2 0 1 0 0-4.4a2.2 2.2 0 1 0 0 4.4zM37 25.6a2.2 2.2 0 1 0 0-4.4a2.2 2.2 0 1 0 0 4.4z"), new(TileLayerKind.Fill, 0xFFFFB020, 1.0, 0.0, "M28.6 30.4h6.8L32 34.6z")]),
        ["app-dingtalk"] = new(0xFF1677FF, 0xFF1677FF, [new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M24.5 13h15a1.5 1.5 0 0 1 1.4 2l-2.4 8.4 6.1 5.6a1.5 1.5 0 0 1 .4 1.1V33H19v-2.9a1.5 1.5 0 0 1 .4-1.1l6.1-5.6-2.4-8.4a1.5 1.5 0 0 1 1.4-2z"), new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 3.4, "M32 33v16")]),
        ["app-tencentmeeting"] = new(0xFFFFFFFF, 0xFFFFFFFF, [new(TileLayerKind.Fill, 0xFF2F6BEA, 1.0, 0.0, "M18 21h18a4 4 0 0 1 4 4v14a4 4 0 0 1-4 4H18a4 4 0 0 1-4-4V25a4 4 0 0 1 4-4z"), new(TileLayerKind.Fill, 0xFF1DC6DC, 1.0, 0.0, "M42.5 29.2l5.6-4.1A1.2 1.2 0 0 1 50 26v12a1.2 1.2 0 0 1-1.9 1l-5.6-4.1z")]),
        ["app-word"] = new(0xFF185ABD, 0xFF185ABD, [new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 3.6, "M15 22H31M15 30H27M15 38H23M15 46H33"), new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M45.2 13.6l5.2 5.2a1.6 1.6 0 0 1 0 2.3L35 36.5l-8.4 2.3 2.3-8.4 15.4-15.4a1.6 1.6 0 0 1 2.3 0z")]),
        ["app-excel"] = new(0xFF107C41, 0xFF107C41, [new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 4.6, "M43 17H21l11.5 15L21 47h22")]),
        ["app-powerpoint"] = new(0xFFC43E1C, 0xFFC43E1C, [new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M30 20v15h15a15 15 0 1 1-15-15z"), new(TileLayerKind.Fill, 0xFFFFFFFF, 0.75, 0.0, "M34 16a15 15 0 0 1 15 15H34z")]),
        ["app-chrome"] = new(0xFFFFFFFF, 0xFFFFFFFF, [new(TileLayerKind.Fill, 0xFFEA4335, 1.0, 0.0, "M14 18a3 3 0 0 1 3-3h9v7H14z"), new(TileLayerKind.Fill, 0xFFFBBC04, 1.0, 0.0, "M27.5 15h9v7h-9z"), new(TileLayerKind.Fill, 0xFF34A853, 1.0, 0.0, "M38 15h9a3 3 0 0 1 3 3v4H38z"), new(TileLayerKind.Fill, 0xFFE8EEF8, 1.0, 0.0, "M14 24h36v20a3 3 0 0 1-3 3H17a3 3 0 0 1-3-3z"), new(TileLayerKind.Fill, 0xFF4285F4, 1.0, 0.0, "M32 41.5a6 6 0 1 0 0-12a6 6 0 1 0 0 12z")]),
        ["app-edge"] = new(0xFF0F5BB5, 0xFF0F5BB5, [new(TileLayerKind.Stroke, 0xFFFFFFFF, 1.0, 4.6, "M13 36c6-8 13-10 19-6s13 3 19-5"), new(TileLayerKind.Stroke, 0xFFFFFFFF, 0.7, 3.6, "M13 45c6-6 13-7 19-4s13 2 19-4")], 0xFF35C46E),
        ["app-claude"] = new(0xFFD97757, 0xFFD97757, [new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M32 13c1.4 11.6 6 16.6 19 19-13 2.4-17.6 7.4-19 19-1.4-11.6-6-16.6-19-19 13-2.4 17.6-7.4 19-19z")]),
        ["app-chatgpt"] = new(0xFFFFFFFF, 0xFFFFFFFF, [new(TileLayerKind.Stroke, 0xFF111111, 1.0, 4.4, "M32 16.5l13.4 7.75v15.5L32 47.5l-13.4-7.75v-15.5z"), new(TileLayerKind.Fill, 0xFF111111, 1.0, 0.0, "M32 36a4 4 0 1 0 0-8a4 4 0 1 0 0 8z")]),
        ["app-cursor"] = new(0xFF1F1F22, 0xFF1F1F22, [new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M22 13.5v31.2a1 1 0 0 0 1.7.7l7.2-7.3 5.3 11.6a1 1 0 0 0 1.3.5l4.1-1.9a1 1 0 0 0 .5-1.3l-5.3-11.4h10.1a1 1 0 0 0 .7-1.7L23.7 12.8a1 1 0 0 0-1.7.7z")]),
        ["app-kimi"] = new(0xFF101733, 0xFF101733, [new(TileLayerKind.Fill, 0xFFFFFFFF, 1.0, 0.0, "M35 14.5A17.5 17.5 0 1 0 49.5 41 14 14 0 0 1 35 14.5z"), new(TileLayerKind.Fill, 0xFF3B82FF, 1.0, 0.0, "M45 23.5a4.5 4.5 0 1 0 0-9a4.5 4.5 0 1 0 0 9z")]),
    };

    /// <summary>The art for a file type.</summary>
    public static TileArt For(FolderItemKind kind) => All[kind switch
    {
        FolderItemKind.Folder => "folder",
        FolderItemKind.Document => "document",
        FolderItemKind.Spreadsheet => "spreadsheet",
        FolderItemKind.Presentation => "presentation",
        FolderItemKind.Pdf => "pdf",
        FolderItemKind.Text => "text",
        FolderItemKind.Code => "code",
        FolderItemKind.Archive => "archive",
        FolderItemKind.Audio => "audio",
        FolderItemKind.WebLink => "weblink",
        FolderItemKind.Image => "image",
        FolderItemKind.Video => "video",
        FolderItemKind.App => "app",
        _ => "other",
    }];

    /// <summary>The art of a custom app icon (the key is the one in <see cref="AppIconRow"/>), or null.</summary>
    public static TileArt? ForApp(string key) => All.GetValueOrDefault("app-" + key);

    /// <summary>The play mark drawn over a video frame: a disc at 45 % black and a white triangle (the Glass design's ICONS.md).</summary>
    public const string PlayDisc = "M32 20a12 12 0 1 0 0 24a12 12 0 1 0 0-24z";

    public const string PlayTriangle = "M28.6 26.4v11.2a.9.9 0 0 0 1.4.75l8.4-5.6a.9.9 0 0 0 0-1.5L30 25.65a.9.9 0 0 0-1.4.75z";
}