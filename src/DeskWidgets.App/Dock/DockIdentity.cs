using System.Diagnostics;
using System.Text.RegularExpressions;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>What a dragged thing is to the dock (PROPOSAL 3.5): a candidate program, or something the dock refuses in Build A.</summary>
internal enum DockDropKind
{
    /// <summary>A folder, a document, a picture... Not added (opening it with an app comes with Build C).</summary>
    NotAProgram,

    /// <summary>An exe, a shortcut, a Steam link or a Start or Applications-folder item: read it to see whether it is one.</summary>
    Candidate,
}

/// <summary>
/// Turns what the user dropped, or what the taskbar pins, into a saved <see cref="DockItem"/>: the AppUserModelID when there is one, else the
/// exe path with its arguments and working folder (SPIKE section 5.1). Reading is done once; the dock keeps no file afterwards.
/// </summary>
internal static partial class DockIdentity
{
    private static readonly string[] ProgramExtensions = [".exe", ".bat", ".cmd", ".com"];

    /// <summary>The first look at a drag item, from what the drag says about it (no file is opened).</summary>
    public static DockDropKind Classify(DropEntry entry)
    {
        if (entry.IsVirtual)
            return DockDropKind.Candidate; // a Start or Applications-folder item
        if (entry.IsFolder || entry.Path is null)
            return DockDropKind.NotAProgram;
        string extension = Path.GetExtension(entry.Path).ToLowerInvariant();
        return extension is ".lnk" or ".url" || ProgramExtensions.Contains(extension) ? DockDropKind.Candidate : DockDropKind.NotAProgram;
    }

    /// <summary>
    /// A link's icon file is asked of the Shell, which opens it: a UNC path or a URL there would make Windows connect to a remote host and send
    /// the user's NTLM credentials. Only a rooted path on a fixed local drive is accepted; anything else is no icon (the generic tile).
    /// </summary>
    public static string? SafeIconPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        string trimmed = path.Trim().Trim('"');
        // "C:\..." only: no "\\server", no "\\?\UNC", no "scheme://", no relative path, no drive-relative "C:foo".
        if (trimmed.Length < 4 || !char.IsAsciiLetter(trimmed[0]) || trimmed[1] != ':' || (trimmed[2] != '\\' && trimmed[2] != '/') || trimmed.Contains("://", StringComparison.Ordinal))
            return null;
        try
        {
            return new DriveInfo(trimmed[..1]).DriveType == DriveType.Fixed ? trimmed : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool IsProgramFile(string path) => ProgramExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>A parsing name of the form <c>{known-folder GUID}\rest</c> (WeChat's and Tencent Meeting's "AUMID"): the GUID and the rest.</summary>
    public static (Guid Folder, string Remainder)? SplitKnownFolderName(string name)
    {
        var match = KnownFolderName().Match(name);
        return match.Success && Guid.TryParse(match.Groups[1].Value, out var folder) ? (folder, match.Groups[2].Value) : null;
    }

    [GeneratedRegex(@"^\{([0-9A-Fa-f\-]{36})\}\\(.+)$")]
    private static partial Regex KnownFolderName();

    /// <summary>The steam id of a <c>steam://rungameid/N</c> style address, else null.</summary>
    public static string? SteamUrl(string text) => AppIconPack.SteamId("URL=" + text) is null ? null : text;

    // ---- reading (COM and files; call on an STA thread or the UI thread, one item at a time) ----

    /// <summary>The program a drag item stands for, or null when it is not one (a shortcut to a document, a plain web link, an unreadable item).</summary>
    public static DockItem? FromDropped(Dropped dropped)
    {
        try
        {
            if (Classify(dropped.Entry) == DockDropKind.NotAProgram)
                return null;
            if (dropped.Entry.IsVirtual)
                return FromShellItem(dropped.Item);
            return FromPath(dropped.Entry.Path!);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static DockItem? FromPath(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".lnk")
            return FromShortcut(path);
        if (extension == ".url")
            return FromUrlFile(path);
        return IsProgramFile(path) ? FromExe(path) : null;
    }

    public static DockItem FromExe(string exePath, string? arguments = null, string? workingDir = null, string? aumid = null, string? name = null)
    {
        string label = !string.IsNullOrWhiteSpace(name) ? name : DescribeExe(exePath);
        return new DockItem
        {
            Name = label,
            ExePath = exePath,
            Arguments = string.IsNullOrEmpty(arguments) ? null : arguments,
            WorkingDir = string.IsNullOrEmpty(workingDir) ? null : workingDir,
            Aumid = string.IsNullOrEmpty(aumid) ? null : aumid,
        };
    }

    private static string DescribeExe(string exePath)
    {
        try
        {
            if (File.Exists(exePath) && FileVersionInfo.GetVersionInfo(exePath).FileDescription is { Length: > 0 } description)
                return description;
        }
        catch (Exception)
        {
        }
        return Path.GetFileNameWithoutExtension(exePath);
    }

    public static DockItem? FromShortcut(string lnkPath)
    {
        var info = ShellOps.ReadShortcut(lnkPath);
        string? ownAumid = null;
        try
        {
            ownAumid = Shell.Str(Shell.FromPath(lnkPath), ShellNative.PKEY_AppUserModel_ID);
        }
        catch (Exception)
        {
        }
        string name = Path.GetFileNameWithoutExtension(lnkPath);

        if (info.Target is { } target)
        {
            // A shortcut to a document or a folder is not a program. A missing exe still is (the dock then shows it as missing).
            if (Directory.Exists(target) || !IsProgramFile(target))
                return null;
            return FromExe(target, info.Arguments, info.WorkingDir, ownAumid, name);
        }

        // An ID-list shortcut: a Start or Store app (its parsing name is the AUMID), a steam link, or a shell folder such as This PC.
        string? parsing = info.IdListName;
        if (parsing is null || parsing.StartsWith("::{", StringComparison.Ordinal))
            return null;
        return FromParsingName(ownAumid ?? parsing, name);
    }

    public static DockItem? FromUrlFile(string urlPath)
    {
        string text = File.ReadAllText(urlPath);
        string? steam = AppIconPack.SteamId(text);
        if (steam is null)
            return null; // an ordinary web link is not a program
        var lines = text.Split('\n').Select(l => l.Trim()).ToList();
        string? url = lines.FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..];
        // The link's own icon file (Steam keeps one per game); the dock keeps the path so the link needs no file afterwards.
        string? icon = lines.FirstOrDefault(l => l.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))?[9..];
        return url is null ? null : new DockItem { Name = Path.GetFileNameWithoutExtension(urlPath), Url = url, IconPath = SafeIconPath(icon) };
    }

    /// <summary>A Start or Applications-folder item (it has no path): its parsing name is the AUMID, a known-folder path or a steam address.</summary>
    public static DockItem? FromShellItem(IShellItem item)
    {
        string? parsing = Shell.Str(item, ShellNative.PKEY_AppUserModel_ID) ?? Shell.Name(item, ShellNative.SIGDN_DESKTOPABSOLUTEPARSING);
        string name = Shell.Name(item, ShellNative.SIGDN_NORMALDISPLAY) ?? "";
        if (string.IsNullOrEmpty(parsing) || parsing.StartsWith("::{", StringComparison.Ordinal))
            return null;
        // A real path (a desktop item with a file behind it) is read as that file.
        if (Shell.FileSystemPath(item) is { } file && File.Exists(file))
            return FromPath(file);
        return FromParsingName(parsing, name);
    }

    /// <summary>What an Applications-folder entry's name means: a steam address, an exe path relative to a known folder, or an AUMID.</summary>
    public static DockItem FromParsingName(string parsing, string name)
    {
        if (SteamUrl(parsing) is { } url)
            return new DockItem { Name = name, Url = url };
        if (SplitKnownFolderName(parsing) is { } known && Shell.KnownFolder(known.Folder) is { } folder)
        {
            // WeChat's and Tencent Meeting's "AUMID" is a path relative to Program Files: expand it so the exe can start it and match its windows.
            string exe = Path.Combine(folder, known.Remainder);
            if (File.Exists(exe))
                return FromExe(exe, aumid: parsing, name: name);
        }
        if (Path.IsPathRooted(parsing) && IsProgramFile(parsing))
            return FromExe(parsing, aumid: parsing, name: name);
        return new DockItem { Name = name, Aumid = parsing };
    }

    /// <summary>A Store or Start app by its AUMID (the taskbar's Store pins): the name comes from the Applications folder.</summary>
    public static DockItem FromAumid(string aumid)
    {
        string name = aumid;
        try
        {
            name = Shell.Name(Shell.FromPath(@"shell:AppsFolder\" + aumid), ShellNative.SIGDN_NORMALDISPLAY) ?? aumid;
        }
        catch (Exception)
        {
        }
        return FromParsingName(aumid, name);
    }
}
