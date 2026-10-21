namespace DeskWidgets.Widgets.Folder;

/// <summary>What identifies a program: gathered from a shortcut, an AppsFolder item or an exe, matched against <see cref="AppIconPack"/>.</summary>
internal sealed record AppIdentity(string? Aumid = null, string? ExePath = null, string? ProductName = null, string? FileDescription = null, string? SteamId = null)
{
    public string? ExeFileName => ExePath is null ? null : Path.GetFileName(ExePath);
}

/// <summary>One row of the custom-icon table. Adding an app takes the art and one row here, no code.</summary>
/// <param name="Key">The art's name (Glass <c>icons/app-&lt;key&gt;.svg</c>, Pixel's sprite of the same name).</param>
/// <param name="Aumids">AppUserModelIDs, exact match.</param>
/// <param name="ExeNames">Exe file names, case-insensitive.</param>
/// <param name="Products">ProductName plus FileDescription pairs, for exes whose own name is generic.</param>
/// <param name="SteamIds">The game id of a <c>steam://rungameid/&lt;id&gt;</c> link.</param>
internal sealed record AppIconRow(string Key, string[] Aumids, string[] ExeNames, (string Product, string Description)[] Products, string[] SteamIds);

/// <summary>
/// The tier-1 app icons. Matching never uses a display name: first the AppUserModelID (exact), then the exe's file name,
/// then ProductName with FileDescription, then the Steam game id.
/// </summary>
internal static class AppIconPack
{
    private static readonly string[] None = [];
    private static readonly (string, string)[] NoProducts = [];

    /// <summary>The first batch of 13, with the ids seen on the user's PC (notes: Phase 4 spike report, section 6).</summary>
    public static IReadOnlyList<AppIconRow> Rows { get; } =
    [
        new("wechat", [@"{6D809377-6AF0-444B-8957-A3773F02200E}\Tencent\Weixin\Weixin.exe"], ["Weixin.exe", "WeChat.exe"], [("Weixin", "Weixin"), ("WeChat", "WeChat")], None),
        new("qq", ["QQ"], ["QQ.exe"], [("QQ", "QQ")], None),
        // DingTalk's start-menu id is auto-generated, so its launcher's file name is the key.
        new("dingtalk", None, ["DingtalkLauncher.exe", "DingTalk.exe"], NoProducts, None),
        new("tencentmeeting", [@"{6D809377-6AF0-444B-8957-A3773F02200E}\Tencent\WeMeet\WeMeetApp.exe"], ["WeMeetApp.exe", "wemeetapp.exe"], [("腾讯会议", "腾讯会议")], None),
        new("word", ["Microsoft.Office.WINWORD.EXE.15"], ["WINWORD.EXE"], [("Microsoft Office", "Microsoft Word")], None),
        new("excel", ["Microsoft.Office.EXCEL.EXE.15"], ["EXCEL.EXE"], [("Microsoft Office", "Microsoft Excel")], None),
        new("powerpoint", ["Microsoft.Office.POWERPNT.EXE.15"], ["POWERPNT.EXE"], [("Microsoft Office", "Microsoft PowerPoint")], None),
        new("chrome", ["Chrome"], ["chrome.exe"], [("Google Chrome", "Google Chrome")], None),
        new("edge", ["MSEdge"], ["msedge.exe"], [("Microsoft Edge", "Microsoft Edge")], None),
        new("claude", ["Claude_pzs8sxrjxfjjc!Claude"], None, NoProducts, None),
        new("chatgpt", ["OpenAI.Codex_2p2nqsd0c76g0!App"], None, NoProducts, None),
        new("cursor", ["Anysphere.Cursor"], ["Cursor.exe"], [("Cursor", "Cursor")], None),
        new("kimi", ["com.moonshot.kimichat"], ["Kimi.exe"], [("Kimi", "Kimi")], None),
    ];

    /// <summary>The art key for a program, or null (it then gets its real icon on a plate).</summary>
    public static string? Match(AppIdentity identity) => Match(identity, Rows);

    public static string? Match(AppIdentity identity, IReadOnlyList<AppIconRow> rows)
    {
        if (!string.IsNullOrEmpty(identity.Aumid))
        {
            foreach (var row in rows)
            {
                if (row.Aumids.Contains(identity.Aumid, StringComparer.OrdinalIgnoreCase))
                    return row.Key;
            }
        }
        if (identity.ExeFileName is { Length: > 0 } exe)
        {
            foreach (var row in rows)
            {
                if (row.ExeNames.Contains(exe, StringComparer.OrdinalIgnoreCase))
                    return row.Key;
            }
        }
        if (!string.IsNullOrEmpty(identity.ProductName) && !string.IsNullOrEmpty(identity.FileDescription))
        {
            foreach (var row in rows)
            {
                if (row.Products.Any(p => string.Equals(p.Product, identity.ProductName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Description, identity.FileDescription, StringComparison.OrdinalIgnoreCase)))
                    return row.Key;
            }
        }
        if (!string.IsNullOrEmpty(identity.SteamId))
        {
            foreach (var row in rows)
            {
                if (row.SteamIds.Contains(identity.SteamId, StringComparer.Ordinal))
                    return row.Key;
            }
        }
        return null;
    }

    /// <summary>The Steam game id of a <c>.url</c> file's text (<c>URL=steam://rungameid/431960</c>), or null.</summary>
    public static string? SteamId(string urlFileText)
    {
        foreach (string line in urlFileText.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                continue;
            string url = trimmed[4..];
            const string Run = "steam://rungameid/";
            const string Plain = "steam://run/";
            string? rest = url.StartsWith(Run, StringComparison.OrdinalIgnoreCase) ? url[Run.Length..]
                : url.StartsWith(Plain, StringComparison.OrdinalIgnoreCase) ? url[Plain.Length..] : null;
            if (rest is null)
                return null;
            int end = 0;
            while (end < rest.Length && char.IsAsciiDigit(rest[end]))
                end++;
            return end == 0 ? null : rest[..end];
        }
        return null;
    }
}
