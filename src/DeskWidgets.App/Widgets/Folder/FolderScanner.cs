using System.Diagnostics;
using DeskWidgets.Core.Diagnostics;
using Microsoft.Win32;

namespace DeskWidgets.Widgets.Folder;

/// <summary>Reads a Big Folder's contents into <see cref="FolderItem"/>s. Runs on an STA thread (shortcuts are read through the Shell).</summary>
internal static class FolderScanner
{
    private static readonly Dictionary<string, (long Ticks, string? Product, string? Description)> VersionCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Explorer's "Hide extensions for known file types" setting (on by default).</summary>
    public static bool ShowExtensions()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return key?.GetValue("HideFileExt") is int hide && hide == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static List<FolderItem> Scan(string folder, OriginLog origins)
    {
        bool showExtensions = ShowExtensions();
        var items = new List<FolderItem>();
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 || entry.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                items.Add(Describe(entry, origins, showExtensions));
            }
            catch (Exception ex)
            {
                Log.Warn($"Reading '{entry.Name}' in a Big Folder failed", ex);
            }
        }
        return items;
    }

    private static FolderItem Describe(FileSystemInfo entry, OriginLog origins, bool showExtensions)
    {
        string path = entry.FullName;
        bool isFolder = entry is DirectoryInfo;
        string extension = isFolder ? "" : entry.Extension.ToLowerInvariant();
        DateTime added = origins.AddedUtc(entry.Name) ?? new[] { entry.CreationTimeUtc, entry.LastWriteTimeUtc }.Max();
        string display = isFolder || extension is not (".lnk" or ".url") && showExtensions ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name);
        if (!isFolder && display.Length == 0)
            display = entry.Name;

        if (isFolder)
            return new FolderItem { Path = path, DisplayName = display, IsFolder = true, Kind = FolderItemKind.Folder, AddedUtc = added };

        if (extension == ".lnk")
            return DescribeShortcut(path, display, added);

        if (extension == ".url")
        {
            string text = File.ReadAllText(path);
            string? steam = AppIconPack.SteamId(text);
            if (steam is not null)
            {
                var identity = new AppIdentity(SteamId: steam);
                return new FolderItem { Path = path, DisplayName = display, Kind = FolderItemKind.App, AddedUtc = added, Identity = identity, CustomIcon = AppIconPack.Match(identity) };
            }
            return new FolderItem { Path = path, DisplayName = display, Kind = FolderItemKind.WebLink, AddedUtc = added, LabelName = entry.Name };
        }

        var kind = FolderKinds.OfFile(entry.Name);
        if (kind == FolderItemKind.App)
        {
            var identity = ExeIdentity(path);
            return new FolderItem { Path = path, DisplayName = display, Kind = FolderItemKind.App, AddedUtc = added, Identity = identity, CustomIcon = AppIconPack.Match(identity) };
        }
        return new FolderItem { Path = path, DisplayName = display, Kind = kind, AddedUtc = added, LabelName = entry.Name };
    }

    private static FolderItem DescribeShortcut(string path, string display, DateTime added)
    {
        ShellOps.LinkInfo info;
        string? ownAumid = null;
        try
        {
            info = ShellOps.ReadShortcut(path);
            ownAumid = Shell.Str(Shell.FromPath(path), ShellNative.PKEY_AppUserModel_ID);
        }
        catch (Exception)
        {
            // An unreadable shortcut is an app whose target is gone.
            return new FolderItem { Path = path, DisplayName = display, Kind = FolderItemKind.App, AddedUtc = added, IsShortcut = true, TargetMissing = true };
        }

        if (info.Target is { } target)
        {
            bool isDirectory = Directory.Exists(target);
            bool exists = isDirectory || File.Exists(target);
            var kind = isDirectory ? FolderItemKind.Folder : FolderKinds.OfFile(target);
            if (kind == FolderItemKind.App || (!exists && kind == FolderItemKind.Other))
                kind = FolderItemKind.App;
            AppIdentity? identity = null;
            string? icon = null;
            if (kind == FolderItemKind.App)
            {
                identity = ExeIdentity(target) with { Aumid = ownAumid };
                icon = AppIconPack.Match(identity);
            }
            return new FolderItem
            {
                Path = path, DisplayName = display, Kind = kind, AddedUtc = added, IsShortcut = true, IsFolder = false,
                TargetMissing = !exists, LabelName = Path.GetFileName(target), Identity = identity, CustomIcon = icon,
            };
        }

        // An ID-list shortcut: a Start or Store app (its parsing name is the AUMID), or a shell folder such as This PC.
        string? parsing = info.IdListName;
        if (parsing is not null && parsing.StartsWith("::{", StringComparison.Ordinal))
            return new FolderItem { Path = path, DisplayName = display, Kind = FolderItemKind.Folder, AddedUtc = added, IsShortcut = true };
        string? aumid = ownAumid ?? parsing;
        bool resolvable = aumid is not null && AppsFolderHas(aumid);
        var appIdentity = new AppIdentity(Aumid: aumid);
        return new FolderItem
        {
            Path = path, DisplayName = display, Kind = FolderItemKind.App, AddedUtc = added, IsShortcut = true,
            TargetMissing = !resolvable, Identity = appIdentity, CustomIcon = AppIconPack.Match(appIdentity),
        };
    }

    private static bool AppsFolderHas(string aumid)
    {
        try
        {
            Shell.FromPath(@"shell:AppsFolder\" + aumid);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static AppIdentity ExeIdentity(string exePath)
    {
        string? product = null, description = null;
        try
        {
            if (File.Exists(exePath))
            {
                long ticks = File.GetLastWriteTimeUtc(exePath).Ticks;
                lock (VersionCache)
                {
                    if (!VersionCache.TryGetValue(exePath, out var cached) || cached.Ticks != ticks)
                    {
                        var info = FileVersionInfo.GetVersionInfo(exePath);
                        VersionCache[exePath] = cached = (ticks, info.ProductName, info.FileDescription);
                    }
                    (product, description) = (cached.Product, cached.Description);
                }
            }
        }
        catch (Exception)
        {
        }
        return new AppIdentity(ExePath: exePath, ProductName: product, FileDescription: description);
    }
}
