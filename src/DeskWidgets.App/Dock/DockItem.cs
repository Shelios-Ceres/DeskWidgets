using System.Text.Json.Serialization;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>How a saved program is started (PROPOSAL 3.7).</summary>
internal enum DockStartKind
{
    /// <summary>Nothing to start: the item has no usable identity.</summary>
    None,

    /// <summary><c>ShellExecute</c> of an exe with its arguments and working folder.</summary>
    Exe,

    /// <summary><c>IApplicationActivationManager.ActivateApplication</c>: a Store app (an AUMID with a "!" and no exe path).</summary>
    Activate,

    /// <summary><c>ShellExecute</c> of <c>shell:AppsFolder\&lt;AUMID&gt;</c>: a classic Start entry that has no path of its own.</summary>
    AppsFolder,

    /// <summary><c>ShellExecute</c> of a URL such as <c>steam://rungameid/N</c>.</summary>
    Url,
}

internal readonly record struct DockStart(DockStartKind Kind, string Target, string? Arguments = null, string? WorkingDir = null);

/// <summary>
/// One program on a dock, as saved in settings.json: its most stable identity (its AppUserModelID when Windows knows one, else the exe
/// path with the shortcut's arguments and working folder). A shortcut is read once, when it is dropped; after that the dock needs no file.
/// </summary>
internal sealed class DockItem
{
    /// <summary>What the label shows. Taken from the shortcut or the Start entry when the program was added.</summary>
    public string Name { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Aumid { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExePath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDir { get; set; }

    /// <summary>A protocol link that starts a program: <c>steam://rungameid/N</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    /// <summary>
    /// A picture file the program's icon is taken from when there is no exe to ask (a Steam link's <c>IconFile=</c>). The icon service shows it as
    /// a real icon; without it a link gets the generic program tile.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconPath { get; set; }

    /// <summary>
    /// A group (PROPOSAL 3.3): its programs, in order. Null for a program. A group holds programs only (no group in a group), at least two:
    /// <see cref="DockGroups.Normalize"/> turns a group of one back into its program and drops an empty one.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DockItem>? Children { get; set; }

    /// <summary>A group's identity (<see cref="Key"/>), made when it is made, so a rename does not change it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupId { get; set; }

    [JsonIgnore]
    public bool IsGroup => Children is not null;

    /// <summary>Whether the dock can tell if it is running: it needs an AUMID or an exe to match a window with. A Steam link has neither (its window is the game's own exe).</summary>
    [JsonIgnore]
    public bool SupportsRunning => !string.IsNullOrEmpty(ExePath) || !string.IsNullOrEmpty(Aumid);

    /// <summary>A Store app: it has a package AUMID ("Package!App") and no exe path of its own. It gets no "Run as administrator".</summary>
    [JsonIgnore]
    public bool IsStoreApp => string.IsNullOrEmpty(ExePath) && Aumid is { Length: > 0 } aumid && aumid.Contains('!');

    /// <summary>Whether two items are the same program, so a program is not added twice: by AUMID, else exe path, else URL.</summary>
    public string Key
        => IsGroup ? "group:" + GroupId
            : !string.IsNullOrEmpty(Aumid) ? "aumid:" + Aumid.ToLowerInvariant()
            : !string.IsNullOrEmpty(ExePath) ? "exe:" + ExePath.ToLowerInvariant()
            : !string.IsNullOrEmpty(Url) ? "url:" + Url.ToLowerInvariant()
            : "name:" + Name.ToLowerInvariant();

    /// <summary>What the icon table matches (AUMID, then exe name, then product, then a Steam id), as the Big Folder does.</summary>
    public AppIdentity Identity(string? productName = null, string? fileDescription = null)
        => new(Aumid, ExePath, productName, fileDescription, Url is null ? null : AppIconPack.SteamId("URL=" + Url));

    /// <summary>How a click starts it. An exe path wins over an AUMID (it keeps the arguments and the working folder the shortcut had).</summary>
    public DockStart PlanStart()
    {
        if (!string.IsNullOrEmpty(ExePath))
            return new DockStart(DockStartKind.Exe, ExePath, string.IsNullOrEmpty(Arguments) ? null : Arguments, string.IsNullOrEmpty(WorkingDir) ? null : WorkingDir);
        if (IsStoreApp)
            return new DockStart(DockStartKind.Activate, Aumid!);
        if (!string.IsNullOrEmpty(Aumid))
            return new DockStart(DockStartKind.AppsFolder, @"shell:AppsFolder\" + Aumid);
        if (!string.IsNullOrEmpty(Url))
            return new DockStart(DockStartKind.Url, Url);
        return new DockStart(DockStartKind.None, "");
    }

    public DockItem Copy() => new()
    {
        Name = Name, Aumid = Aumid, ExePath = ExePath, Arguments = Arguments, WorkingDir = WorkingDir, Url = Url, IconPath = IconPath,
        GroupId = GroupId, Children = Children?.Select(child => child.Copy()).ToList(),
    };
}

/// <summary>One dock: the screen it is on, where it sits along the bottom edge, its icon size and its programs. Saved in settings.json beside the widgets.</summary>
internal sealed class DockSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The screen's device name (<c>\\.\DISPLAY1</c>); null or unknown means the primary screen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Monitor { get; set; }

    /// <summary>How far the bar's centre is from the work area's centre, in DIPs (negative is to the left).</summary>
    public double OffsetDip { get; set; }

    public DockIconSize IconSize { get; set; } = DockIconSize.Medium;

    public List<DockItem> Items { get; set; } = [];
}
