using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>
/// Which of <see cref="AppIconPack"/>'s custom tiles a dock program gets, the Big Folder's rules: AUMID, exe name or Steam id first, then the
/// exe's product and description. <see cref="Resolve(DockItem)"/> may read the exe's version info (a network exe can take seconds), so the
/// controller calls it on its worker and hands the key to the surfaces in <see cref="DockItemView.CustomKey"/>; <see cref="Quick"/> reads no file.
/// </summary>
internal static class DockCustomKeys
{
    /// <summary>The key from what the item itself says (AUMID, exe name, Steam id), with no file read; null if that finds none.</summary>
    public static string? Quick(DockItem item) => AppIconPack.Match(item.Identity());

    /// <summary>The key, or null for "no custom tile". Reads the exe's version info when the quick match finds nothing: not on the UI thread.</summary>
    public static string? Resolve(DockItem item) => Resolve(item, FolderScanner.ExeIdentity);

    /// <param name="readExe">Reads an exe's product name and description (tests pass their own).</param>
    public static string? Resolve(DockItem item, Func<string, AppIdentity> readExe)
    {
        if (Quick(item) is { } key)
            return key;
        if (string.IsNullOrEmpty(item.ExePath))
            return null;
        var exe = readExe(item.ExePath);
        return AppIconPack.Match(item.Identity(exe.ProductName, exe.FileDescription));
    }
}
