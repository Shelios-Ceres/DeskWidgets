using DeskWidgets.Pixel;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// Renders the Big Folder's Pixel look with sample data to PNG sheets (<c>DeskWidgets.exe --pixel-folder-preview &lt;folder&gt;</c>), light
/// and dark, English and Chinese: the cards at all sizes, their states and the icon set. Nothing is read from disk, so it works
/// beside the running app, and design QA can compare it with the design's own previews.
/// </summary>
internal static class FolderPixelPreview
{
    private const int Factor = 3;
    private const int Margin = 10, Gap = 8, LabelHeight = 9;

    public static void Render(string folder)
    {
        Directory.CreateDirectory(folder);
        var language = Strings.Language;
        try
        {
            foreach (var current in new[] { AppLanguage.English, AppLanguage.Chinese })
            {
                Strings.Language = current;
                foreach (bool dark in new[] { false, true })
                {
                    string tag = $"{(current == AppLanguage.Chinese ? "zh" : "en")}-{(dark ? "night" : "day")}";
                    var palette = PixelPalette.For(dark);
                    PixelPreview.SavePng(Path.Combine(folder, $"folder-1-sizes-{tag}.png"), Sizes(palette, dark), Factor);
                    PixelPreview.SavePng(Path.Combine(folder, $"folder-2-states-{tag}.png"), States(palette, dark), Factor);
                    PixelPreview.SavePng(Path.Combine(folder, $"folder-3-icons-{tag}.png"), Icons(palette, dark), Factor);
                    PixelPreview.SavePng(Path.Combine(folder, $"folder-4-panel-{tag}.png"), Panels(palette, dark), Factor);
                    PixelPreview.SavePng(Path.Combine(folder, $"folder-5-emoji-{tag}.png"), Emoji(palette, dark), Factor);
                }
            }
        }
        finally
        {
            Strings.Language = language;
        }
    }

    // ----- sample data -----

    private static FolderItem Sample(string name, FolderItemKind kind, string? custom = null, bool missing = false, string? label = null) => new()
    {
        Path = @"D:\Sample\" + name + (kind == FolderItemKind.App ? ".lnk" : ""),
        DisplayName = name,
        Kind = kind,
        CustomIcon = custom,
        TargetMissing = missing,
        IsShortcut = kind is FolderItemKind.App or FolderItemKind.Image or FolderItemKind.Video, // keeps the preview off the Shell
        LabelName = label ?? name,
        AddedUtc = new DateTime(2026, 10, 21),
    };

    public static List<FolderItem> LatinItems() =>
    [
        Sample("Quarterly report 2026", FolderItemKind.Document, label: "report.docx"),
        Sample("Budget", FolderItemKind.Spreadsheet, label: "budget.xlsx"),
        Sample("Pitch deck V3", FolderItemKind.Presentation, label: "pitch.pptx"),
        Sample("Manual", FolderItemKind.Pdf),
        Sample("notes", FolderItemKind.Text),
        Sample("build", FolderItemKind.Code),
        Sample("Archive", FolderItemKind.Archive),
        Sample("Song", FolderItemKind.Audio),
        Sample("Photo", FolderItemKind.Image),
        Sample("Clip", FolderItemKind.Video),
        Sample("Projects", FolderItemKind.Folder),
        Sample("README", FolderItemKind.Other, label: "README"),
        Sample("data", FolderItemKind.Other, label: "data.abc"),
        Sample("Chrome", FolderItemKind.App, "chrome"),
        Sample("Word", FolderItemKind.App, "word"),
        Sample("Cursor", FolderItemKind.App, "cursor"),
        Sample("Excel", FolderItemKind.App, "excel"),
        Sample("Edge", FolderItemKind.App, "edge"),
        Sample("Claude", FolderItemKind.App, "claude"),
        Sample("Old tool", FolderItemKind.App, missing: true),
        Sample("Link", FolderItemKind.WebLink),
        Sample("Misc", FolderItemKind.Folder),
        Sample("Last", FolderItemKind.Text),
    ];

    public static List<FolderItem> ChineseItems() =>
    [
        Sample("季度报告", FolderItemKind.Document, label: "a.docx"),
        Sample("预算表", FolderItemKind.Spreadsheet, label: "a.xlsx"),
        Sample("2026 年第三季度财务报告", FolderItemKind.Document, label: "a.docx"),
        Sample("微信", FolderItemKind.App, "wechat"),
        Sample("QQ", FolderItemKind.App, "qq"),
        Sample("PortableApp (2)", FolderItemKind.Other, label: "PortableApp (2)"), // a two-line pixel-font name inside a SimSun panel
        Sample("钉钉", FolderItemKind.App, "dingtalk"),
        Sample("腾讯会议", FolderItemKind.App, "tencentmeeting"),
        Sample("Kimi", FolderItemKind.App, "kimi"),
        Sample("项目资料", FolderItemKind.Folder),
        Sample("照片", FolderItemKind.Image),
        Sample("音乐", FolderItemKind.Audio),
        Sample("说明", FolderItemKind.Text),
        Sample("ChatGPT", FolderItemKind.App, "chatgpt"),
        Sample("PowerPoint", FolderItemKind.App, "powerpoint"),
        Sample("已卸载的应用", FolderItemKind.App, missing: true),
        Sample("压缩包", FolderItemKind.Archive),
        Sample("代码", FolderItemKind.Code),
        Sample("网页", FolderItemKind.WebLink),
        Sample("其他", FolderItemKind.Folder),
        Sample("最后", FolderItemKind.Text),
    ];

    private static List<FolderItem> Sampled() => Strings.IsChinese ? ChineseItems() : LatinItems();

    private static string FolderName => Strings.IsChinese ? "项目资料" : "Projects";

    private static FolderPixelView View(WidgetSize size, IReadOnlyList<FolderItem> items, PixelFolderIcons icons, bool exists = true, FolderHit? hover = null, FolderHit? pressed = null, DropVerb? drop = null, string? name = null)
        => new(name ?? FolderName, @"D:\Work\Projects", items, exists, size, hover ?? FolderHit.None, pressed ?? FolderHit.None, drop, icons);

    // ----- sheets -----

    private sealed record Cell(string Label, PixelCanvas Canvas);

    private static Cell Card(string label, PixelPalette palette, FolderPixelView view)
    {
        var plan = FolderPixelScene.Plan(view);
        var canvas = new PixelCanvas(plan.Width, plan.Height);
        FolderPixelScene.Draw(canvas, palette, view, plan);
        return new Cell(label, canvas);
    }

    private static PixelCanvas Compose(List<List<Cell>> rows, bool dark)
    {
        int width = rows.Max(r => r.Sum(c => c.Canvas.Width + Gap) - Gap) + 2 * Margin;
        int height = rows.Sum(r => r.Max(c => c.Canvas.Height) + LabelHeight + Gap) - Gap + 2 * Margin;
        var sheet = new PixelCanvas(width, height);
        PixelPreview.PaintWallpaper(sheet, dark);
        int y = Margin;
        foreach (var row in rows)
        {
            int x = Margin;
            foreach (var cell in row)
            {
                PixelText.Layout(cell.Label, PixelFonts.Small).Draw(sheet, x, y, dark ? 0xFF94B0C2 : 0xFF333C57);
                for (int py = 0; py < cell.Canvas.Height; py++)
                {
                    for (int px = 0; px < cell.Canvas.Width; px++)
                    {
                        uint color = cell.Canvas.Get(px, py);
                        if (color != 0)
                            sheet.Set(x + px, y + LabelHeight + py, color);
                    }
                }
                x += cell.Canvas.Width + Gap;
            }
            y += row.Max(c => c.Canvas.Height) + LabelHeight + Gap;
        }
        return sheet;
    }

    private static PixelCanvas Sizes(PixelPalette palette, bool dark)
    {
        var icons = new PixelFolderIcons(() => { });
        var items = Sampled();
        return Compose(
        [
            [
                Card("SMALL", palette, View(WidgetSize.Small, items.Take(3).ToList(), icons)),
                Card("SMALL 4", palette, View(WidgetSize.Small, items.Take(4).ToList(), icons)),
                Card("SMALL +K", palette, View(WidgetSize.Small, items.Take(9).ToList(), icons)),
                Card("MEDIUM", palette, View(WidgetSize.Medium, items.Take(5).ToList(), icons)),
            ],
            [
                Card("MEDIUM +K", palette, View(WidgetSize.Medium, items, icons)),
                Card("LARGE", palette, View(WidgetSize.Large, items, icons)),
            ],
        ], dark);
    }

    private static PixelCanvas States(PixelPalette palette, bool dark)
    {
        var icons = new PixelFolderIcons(() => { });
        var items = Sampled();
        var some = items.Take(9).ToList();
        return Compose(
        [
            [
                Card("HOVER ITEM", palette, View(WidgetSize.Medium, some, icons, hover: new FolderHit(FolderHitKind.Item, 1))),
                Card("PRESSED", palette, View(WidgetSize.Medium, some, icons, hover: new FolderHit(FolderHitKind.Item, 1), pressed: new FolderHit(FolderHitKind.Item, 1))),
                Card("HOVER HEADER", palette, View(WidgetSize.Medium, some, icons, hover: new FolderHit(FolderHitKind.Header))),
                Card("HOVER STACK", palette, View(WidgetSize.Medium, items, icons, hover: new FolderHit(FolderHitKind.Stack))),
            ],
            [
                Card("EMPTY", palette, View(WidgetSize.Medium, [], icons)),
                Card("MISSING", palette, View(WidgetSize.Medium, [], icons, exists: false)),
                Card("MISSING L", palette, View(WidgetSize.Large, [], icons, exists: false)),
                Card("DROP MOVE", palette, View(WidgetSize.Medium, some, icons, drop: DropVerb.Move)),
            ],
            [
                Card("DROP COPY", palette, View(WidgetSize.Small, some, icons, drop: DropVerb.Copy)),
                Card("DROP SHORTCUT", palette, View(WidgetSize.Medium, some, icons, drop: DropVerb.Shortcut)),
                Card("DROP MIXED", palette, View(WidgetSize.Medium, some, icons, drop: DropVerb.Mixed)),
            ],
        ], dark);
    }

    private static Cell PanelCell(string label, PixelPalette palette, IReadOnlyList<FolderItem> all, string search = "", int[]? selected = null, int hover = -1, int focus = -1,
        int firstRow = 0, bool sortHover = false, PanelEdit? edit = null, FolderSort sort = FolderSort.Newest, string? name = null)
    {
        name ??= FolderName;
        var icons = new PixelFolderIcons(() => { });
        var items = search.Length == 0 ? all : all.Where(i => Search.NameSearch.Matches(i.DisplayName, new Search.NameQuery(search))).ToList();
        var selection = new PanelSelection();
        selection.SetView(items);
        foreach (int index in selected ?? [])
            selection.Toggle(index);
        if (focus >= 0)
        {
            selection.Focus = focus;
            selection.KeyboardFocus = true;
        }
        var view = new FolderPanelView(name, @"D:\Work\Projects", items, search, sort, all.Count, selection, hover, focus, firstRow, sortHover, false, edit, icons);
        var plan = FolderPixelPanelScene.Plan(view, FolderPixelPanelScene.MetricRank(name, search, sort, items));
        var canvas = FolderPixelPanelScene.Draw(palette, view, plan);
        return new Cell(label, canvas);
    }

    /// <summary>The panel in its states: resting, selected, hovered, searched, scrolled, renaming, and with nothing matching.</summary>
    private static PixelCanvas Panels(PixelPalette palette, bool dark)
    {
        var items = Sampled();
        var many = Enumerable.Range(0, 3).SelectMany(round => items.Select(i => Sample(i.DisplayName + (round == 0 ? "" : " " + round), i.Kind, i.CustomIcon, i.TargetMissing, i.LabelName))).ToList();
        string search = Strings.IsChinese ? "项目" : "pro";
        return Compose(
        [
            [
                PanelCell("PANEL", palette, items),
                PanelCell("SELECTED", palette, items, selected: [1, 2, 6], hover: 4),
                PanelCell("KEY FOCUS", palette, items, selected: [3], focus: 3, sortHover: true),
            ],
            [
                PanelCell("SEARCH", palette, items, search: search),
                PanelCell("SCROLLED", palette, many, selected: [12], firstRow: 3),
                PanelCell("NO MATCHES", palette, items, search: "qzx"),
            ],
            [
                PanelCell("RENAME ITEM", palette, items, selected: [0], edit: new PanelEdit(PanelEditTarget.Item, new LineEditor(items[0].DisplayName, selectAll: true), 0)),
                PanelCell("RENAME FOLDER", palette, items, edit: new PanelEdit(PanelEditTarget.Folder, new LineEditor(FolderName), -1)),
                PanelCell("EMPTY", palette, []),
            ],
        ], dark);
    }

    /// <summary>Emoji in names and in the folder's title (Phase 8): the cards at three sizes and the panel, and the tiles at 8×.</summary>
    private static List<FolderItem> EmojiItems() =>
    [
        Sample("📁 Archive", FolderItemKind.Folder),
        Sample("旅行🎉", FolderItemKind.Folder),
        Sample("✅ Done list", FolderItemKind.Text),
        Sample("❤️ 家人", FolderItemKind.Folder),
        Sample("🚀 Launch plan", FolderItemKind.Presentation, label: "launch.pptx"),
        Sample("微信", FolderItemKind.App, "wechat"),
        Sample("⭐ 收藏", FolderItemKind.Folder),
        Sample("🔥 Hot 2026", FolderItemKind.Spreadsheet, label: "hot.xlsx"),
        Sample("Budget", FolderItemKind.Spreadsheet, label: "budget.xlsx"),
        Sample("😀 Fun", FolderItemKind.Folder),
        Sample("腾讯会议", FolderItemKind.App, "tencentmeeting"),
        Sample("Kimi", FolderItemKind.App, "kimi"),
    ];

    private static PixelCanvas Emoji(PixelPalette palette, bool dark)
    {
        var icons = new PixelFolderIcons(() => { });
        var items = EmojiItems();
        string name = Strings.IsChinese ? "🔥 项目" : "🔥 Projects";
        var tiles = new PixelCanvas(8 * 9 + 2, 9);
        int tx = 1;
        foreach (string emoji in new[] { "📁", "😀", "🎉", "❤️", "⭐", "🔥", "✅", "🚀" })
        {
            string encoded = PixelEmoji.Encode(emoji);
            for (int k = 0; k < encoded.Length; k++)
                PixelEmoji.Draw(tiles, encoded[k], 7, tx, 1, palette.Outline);
            tx += 9;
        }
        return Compose(
        [
            [
                Card("SMALL", palette, View(WidgetSize.Small, items.Take(4).ToList(), icons, name: name)),
                Card("MEDIUM", palette, View(WidgetSize.Medium, items.Take(8).ToList(), icons, name: name)),
            ],
            [
                Card("LARGE", palette, View(WidgetSize.Large, items, icons, name: name)),
                PanelCell("PANEL", palette, items, selected: [4], name: name),
            ],
            [
                new Cell("EMOJI TILES 7X7", tiles),
                PanelCell("RENAME", palette, items, selected: [0], name: name, edit: new PanelEdit(PanelEditTarget.Item, new LineEditor(items[0].DisplayName, selectAll: false), 0)),
            ],
        ], dark);
    }

    /// <summary>The 14 file types and the 13 custom apps, at card size.</summary>
    private static PixelCanvas Icons(PixelPalette palette, bool dark)
    {
        var icons = new PixelFolderIcons(() => { });
        var types = Enum.GetValues<FolderItemKind>().Select(kind => Sample(kind.ToString(), kind, label: kind == FolderItemKind.Other ? "file.xyz" : kind.ToString())).ToList();
        var apps = AppIconPack.Rows.Select(row => Sample(row.Key, FolderItemKind.App, row.Key)).ToList();
        PixelCanvas Strip(IReadOnlyList<FolderItem> list)
        {
            var strip = new PixelCanvas(list.Count * 20 + 4, 22);
            for (int i = 0; i < list.Count; i++)
                FolderPixelScene.Blit(icons.Icon(palette, list[i]), strip, 2 + i * 20, 3);
            return strip;
        }
        return Compose([[new Cell("FILE TYPES", Strip(types))], [new Cell("APPS", Strip(apps))]], dark);
    }
}
