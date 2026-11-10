using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>
/// Phase 16.1: a classic desktop app that was saved with only its AppUserModelID (an Applications-folder item or a taskbar pin has no file of its
/// own) gets its exe path from the Start entry behind that AUMID, when that entry simply runs the app's own exe (no arguments). The AUMID stays: it is the item's identity and
/// is what Windows reports for the app's windows. The exe path is what the icon request, the window match and "Open file location" need.
/// The decision is pure (tested); reading the Start entry is the only Shell call and belongs on a worker.
/// </summary>
internal static class DockBackfill
{
    /// <summary>
    /// Whether the item should look for an exe: a program with an AUMID and no exe path. A Store app ("Package!App") never does: its exe sits in a
    /// package folder and cannot be started or matched by path. A Steam-style link has no AUMID and is left alone, and a group has no identity of its own.
    /// </summary>
    public static bool Wants(DockItem item)
        => !item.IsGroup
            && string.IsNullOrEmpty(item.ExePath)
            && string.IsNullOrEmpty(item.Url)
            && item.Aumid is { Length: > 0 } aumid
            && !aumid.Contains('!');

    /// <summary>Whether a Start entry's target can be taken as the program's exe: a program file on a fixed local drive (a network path would be asked for its icon with the user's credentials).</summary>
    public static bool IsUsableTarget(string? target)
        => DockIdentity.SafeIconPath(target) is { } path && DockIdentity.IsProgramFile(path);

    /// <summary>
    /// Puts a Start entry's target on an item that <see cref="Wants"/> it. Returns whether anything changed, so the caller saves.
    /// A Start entry that has arguments is not taken (B5-1): it runs a launcher or a host exe with them (Sticky Notes is ONENOTE.EXE
    /// "/memoryWindow start", a game is its launcher with a game id, an SDK prompt is cmd.exe), and that exe is not the program: its icon would be
    /// the host's and every window of the host would light the item. The item stays AUMID-only, as before: the Start entry's icon, started through
    /// the Applications folder, matched by AUMID.
    /// </summary>
    public static bool Apply(DockItem item, string? target, string? arguments)
    {
        if (!Wants(item) || !string.IsNullOrWhiteSpace(arguments) || !IsUsableTarget(target))
            return false;
        item.ExePath = DockIdentity.SafeIconPath(target);
        return true;
    }

    /// <summary>The programs of a dock that still need an exe path (groups opened up), in order.</summary>
    public static List<DockItem> Needing(IEnumerable<DockItem> items) => [.. DockGroups.Programs(items).Where(Wants)];

    /// <summary>
    /// Reads the Start entry's target and arguments (<c>shell:AppsFolder\&lt;aumid&gt;</c>, whose <c>System.Link.*</c> properties come from the Start-menu
    /// shortcut). Off the UI thread: the Applications folder is enumerated by the Shell. Nothing is found for a Store app or an AUMID Windows does not know.
    /// </summary>
    public static (string? Target, string? Arguments) ReadStartEntry(string aumid)
    {
        try
        {
            var entry = Shell.FromPath(@"shell:AppsFolder\" + aumid);
            return (Shell.Str(entry, ShellNative.PKEY_Link_TargetParsingPath), Shell.Str(entry, "System.Link.Arguments"));
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    /// <summary>Looks the Start entry up and applies it. Returns whether the item changed.</summary>
    public static bool FromStartEntry(DockItem item)
    {
        if (!Wants(item))
            return false;
        var (target, arguments) = ReadStartEntry(item.Aumid!);
        return Apply(item, target, arguments);
    }
}

/// <summary>
/// What the Shell is asked for a dock icon, in order (Phase 16.1): the exe, else the Start entry <c>shell:AppsFolder\&lt;aumid&gt;</c>, else a
/// link's own icon file. When the exe is asked, the Start entry is the fallback (tried when the exe gives no good icon); only when both fail
/// does the dock draw its generic tile. Glass and Pixel share this so they never disagree.
/// </summary>
internal static class DockIconSource
{
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    /// <summary>The first thing to ask for, or null when the item has nothing to ask (the generic tile).</summary>
    public static string? PathFor(DockItem item)
        => !string.IsNullOrEmpty(item.ExePath) ? item.ExePath
            : !string.IsNullOrEmpty(item.Aumid) ? AppsFolderPrefix + item.Aumid
            : DockIdentity.SafeIconPath(item.IconPath) is { } linkIcon ? linkIcon // a link's own icon file, local only (a UNC path would send credentials)
            : null;

    /// <summary>What to ask when <see cref="PathFor"/> gives no good icon: the Start entry, for an exe that also has an AUMID.</summary>
    public static string? FallbackFor(DockItem item)
        => !string.IsNullOrEmpty(item.ExePath) && !string.IsNullOrEmpty(item.Aumid) ? AppsFolderPrefix + item.Aumid : null;
}
