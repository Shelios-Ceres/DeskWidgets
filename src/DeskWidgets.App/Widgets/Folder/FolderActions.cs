using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskWidgets.Core;
using DeskWidgets.Core.Diagnostics;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DeskWidgets.Widgets.Folder;

/// <summary>An item being dropped: the Shell item (to hand to the file operation) and its plain description (for <see cref="DropPolicy"/>).</summary>
internal readonly record struct Dropped(IShellItem Item, DropEntry Entry);

/// <summary>
/// What the Big Folder does to the Shell: take a drop, open, rename, recycle and so on. Everything runs on the UI thread
/// (Explorer's own dialogs may appear). Methods that can fail return the error text, or null on success.
/// </summary>
internal static class FolderActions
{
    private const int ErrorCancelled = unchecked((int)0x800704C7); // HRESULT_FROM_WIN32(ERROR_CANCELLED)

    /// <summary>Reads what a drag carries into shell items, whatever their source: Explorer, the desktop, Start, AppsFolder.</summary>
    public static List<Dropped> ReadDrop(IDataObject data)
    {
        var array = Shell.ArrayFromDataObject(data);
        if (array is null)
            return [];
        return Shell.Items(array).Select(item =>
        {
            string? path = Shell.FileSystemPath(item);
            bool folder = path is not null && Directory.Exists(path);
            return new Dropped(item, new DropEntry(path, folder, Shell.Name(item, ShellNative.SIGDN_NORMALDISPLAY) ?? path ?? "?"));
        }).ToList();
    }

    /// <summary>
    /// Carries out a drop as one Explorer operation (one Ctrl+Z): moves and copies through IFileOperation with its own progress and
    /// conflict dialogs, shortcuts as new items that are then filled in. Never forwards the drop to the Shell.
    /// </summary>
    public static string? ExecuteDrop(FolderModel model, DropPolicy policy, IReadOnlyList<Dropped> items, DropKeys keys, IntPtr owner)
    {
        using var busy = PanelBusy.Enter(); // the operation, the conflict windows and the shortcut writes: the panel stays up through all of it
        try
        {
            var decisions = items.Select(d => (d, decision: policy.Decide(d.Entry, keys, model.Path))).ToList();
            if (decisions.All(x => x.decision.Effect == DropEffect.None))
                return null;

            var destination = Shell.FromPath(model.Path);
            var sink = new OpSink();
            var op = ShellOps.CreateFileOperation();
            op.SetOperationFlags(ShellOps.InteractiveFlags);
            if (owner != 0)
                op.SetOwnerWindow(owner);

            var moved = new List<string>();
            var copied = new List<string>();
            var links = new List<(string Path, Dropped Item, Decision Decision)>();
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (dropped, decision) in decisions)
            {
                switch (decision.Effect)
                {
                    case DropEffect.Move:
                        Shell.Check(op.MoveItem(dropped.Item, destination, null, sink), "queue move");
                        moved.Add(dropped.Entry.Path!);
                        break;
                    case DropEffect.Copy:
                        Shell.Check(op.CopyItem(dropped.Item, destination, null, sink), "queue copy");
                        copied.Add(dropped.Entry.Path!);
                        break;
                    case DropEffect.Link:
                    {
                        string stem = ShortcutStem(dropped, decision);
                        string path = ShellOps.UniquePath(model.Path, stem, ".lnk");
                        for (int n = 2; !claimed.Add(path); n++)
                            path = ShellOps.UniquePath(model.Path, $"{stem} ({n})", ".lnk");
                        Shell.Check(op.NewItem(destination, 0x80 /* FILE_ATTRIBUTE_NORMAL */, Path.GetFileName(path), null, sink), "queue shortcut");
                        links.Add((path, dropped, decision));
                        break;
                    }
                }
            }

            int hr = op.PerformOperations();
            string? error = null;
            if (hr < 0 && hr != ErrorCancelled)
                error = $"0x{hr:X8}";
            op.GetAnyOperationsAborted(out bool aborted);

            var now = DateTime.UtcNow;
            // The shortcuts exist as empty files now; write them in the order they were queued.
            for (int i = 0; i < sink.NewItems.Count && i < links.Count; i++)
            {
                var (_, dropped, decision) = links[i];
                string created = sink.NewItems[i];
                try
                {
                    ShellOps.FillShortcut(created, decision.Target ?? dropped.Entry.Path, dropped.Entry.IsVirtual ? dropped.Item : null);
                    model.Origins.Record(Path.GetFileName(created), null, now);
                }
                catch (Exception ex)
                {
                    Log.Warn("Writing a Big Folder shortcut failed", ex);
                    error ??= ex.Message;
                }
            }
            foreach (var source in moved)
            {
                if (sink.Placed.TryGetValue(source, out string? placed))
                    model.Origins.Record(Path.GetFileName(placed), source, now);
            }
            foreach (var source in copied)
            {
                if (sink.Placed.TryGetValue(source, out string? placed))
                    model.Origins.Record(Path.GetFileName(placed), null, now);
            }
            model.SaveOrigins();
            model.Rescan();
            Log.Info($"Big Folder drop: {string.Join("; ", sink.Events)}{(aborted ? " (cancelled)" : "")}");
            return error;
        }
        catch (Exception ex)
        {
            Log.Warn("A Big Folder drop failed", ex);
            return ex.Message;
        }
    }

    /// <summary>The name a new shortcut gets: the item's name without its extension.</summary>
    private static string ShortcutStem(Dropped dropped, Decision decision)
    {
        string name = decision.Target is { } target ? Path.GetFileNameWithoutExtension(target)
            : dropped.Entry.IsVirtual || dropped.Entry.IsFolder ? dropped.Entry.Name
            : Path.GetFileNameWithoutExtension(dropped.Entry.Path!);
        return ShellOps.SafeFileName(name);
    }

    // ----- opening -----

    public static string? Open(FolderItem item, IntPtr owner)
    {
        var (ok, error) = ShellOps.Open(item.Path, null, null, owner);
        return ok ? null : new System.ComponentModel.Win32Exception(error).Message;
    }

    public static string? RunAsAdministrator(FolderItem item, IntPtr owner)
    {
        var (ok, error) = ShellOps.Open(item.Path, "runas", null, owner);
        return ok || error == 1223 /* ERROR_CANCELLED: the user declined the prompt */ ? null : new System.ComponentModel.Win32Exception(error).Message;
    }

    /// <summary>The folder a shortcut's program lives in, selected there; null when the shortcut has no path (a Store app).</summary>
    public static bool CanOpenFileLocation(FolderItem item) => ShortcutTarget(item) is not null;

    public static string? ShortcutTarget(FolderItem item)
    {
        if (!item.IsShortcut)
            return null;
        try
        {
            string? target = ShellOps.ReadShortcut(item.Path).Target;
            return target is not null && (File.Exists(target) || Directory.Exists(target)) ? target : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? OpenFileLocation(FolderItem item)
    {
        string? target = ShortcutTarget(item);
        return target is null ? null : SelectInExplorer(target);
    }

    public static string? ShowInExplorer(FolderItem item) => SelectInExplorer(item.Path);

    private static string? SelectInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static string? OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Opens Windows' Applications folder, where apps can be dragged from onto the card.</summary>
    public static string? AddApps()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder") { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static void Properties(FolderItem item, IntPtr owner) => ShellNative.SHObjectProperties(owner, 2 /* SHOP_FILEPATH */, item.Path, null);

    // ----- changing the folder's contents -----

    /// <summary>
    /// Renames an item to what the user typed. A name typed without the extension keeps it (shortcuts are always .lnk), so the
    /// extension cannot be lost by editing the name Explorer would show.
    /// </summary>
    public static string? Rename(FolderModel model, FolderItem item, string typed, IntPtr owner, out string? newFileName)
    {
        newFileName = null;
        string name = typed.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Strings.FolderOperationFailed(name);
        string extension = item.IsFolder ? "" : Path.GetExtension(item.FileName);
        string newName = name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && item.DisplayName == item.FileName ? name : name + extension;
        if (string.Equals(newName, item.FileName, StringComparison.Ordinal))
            return null;
        try
        {
            var (hr, _) = ShellOps.Rename(Shell.FromPath(item.Path), newName, owner);
            if (hr < 0)
                return hr == ErrorCancelled ? null : $"0x{hr:X8}";
            newFileName = newName;
            model.Origins.Rename(item.FileName, newName);
            model.SaveOrigins();
            model.Rescan();
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn("Renaming a Big Folder item failed", ex);
            return ex.Message;
        }
    }

    /// <summary>Move to Recycle Bin (files) or Remove from folder (shortcuts: only the shortcut goes). Undo-able in Explorer.</summary>
    public static string? Recycle(FolderModel model, IEnumerable<FolderItem> items, IntPtr owner)
    {
        try
        {
            var list = items.ToList();
            var (hr, _) = ShellOps.Recycle(list.Select(i => Shell.FromPath(i.Path)), owner);
            foreach (var item in list)
                model.Origins.Remove(item.FileName);
            model.SaveOrigins();
            model.Rescan();
            return hr < 0 && hr != ErrorCancelled ? $"0x{hr:X8}" : null;
        }
        catch (Exception ex)
        {
            Log.Warn("Recycling Big Folder items failed", ex);
            return ex.Message;
        }
    }

    public static bool CanPutBack(FolderModel model, FolderItem item)
        => model.Origins.OriginOf(item.FileName) is { } origin && Path.GetDirectoryName(origin) is { } dir && Directory.Exists(dir);

    /// <summary>Moves an item back to the folder it came from.</summary>
    public static string? PutBack(FolderModel model, FolderItem item, IntPtr owner)
    {
        if (model.Origins.OriginOf(item.FileName) is not { } origin || Path.GetDirectoryName(origin) is not { } directory || !Directory.Exists(directory))
            return Strings.FolderOperationFailed(item.DisplayName);
        try
        {
            var (hr, _) = ShellOps.Perform(DropEffect.Move, [Shell.FromPath(item.Path)], Shell.FromPath(directory), ShellOps.InteractiveFlags, owner);
            if (hr >= 0)
            {
                model.Origins.Remove(item.FileName);
                model.SaveOrigins();
                model.Rescan();
            }
            return hr < 0 && hr != ErrorCancelled ? $"0x{hr:X8}" : null;
        }
        catch (Exception ex)
        {
            Log.Warn("Putting a Big Folder item back failed", ex);
            return ex.Message;
        }
    }

    // ----- the folder itself -----

    /// <summary>A new, empty folder for a new card: under the user profile, on the Desktop's drive ("Big Folder", then "Big Folder (2)", ...). A test instance makes it under its own data folder instead.</summary>
    public static string CreateDefaultFolder()
    {
        string desktop = Shell.KnownFolder(ShellNative.FOLDERID_Desktop) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string profile = Shell.KnownFolder(ShellNative.FOLDERID_Profile) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string parent = DefaultFolderParent(profile, desktop, DataRoot.IsTestInstance ? DataRoot.BigFolders : null);
        string path = ShellOps.UniquePath(parent, ShellOps.SafeFileName(Strings.FolderDefaultName), "");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>The folder a new card's folder is made in: <paramref name="testFolder"/> for a test instance, else the profile if it is on the Desktop's drive, else "Big Folders" on that drive.</summary>
    internal static string DefaultFolderParent(string profile, string desktop, string? testFolder)
        => testFolder ?? (DropPolicy.SameVolume(profile, desktop) ? profile : Path.Combine(Path.GetPathRoot(desktop)!, "Big Folders"));

    /// <summary>Renames the folder itself. Returns the new path, or null with <paramref name="error"/> set.</summary>
    public static string? RenameFolder(string folder, string typed, IntPtr owner, out string? error)
    {
        error = null;
        string name = typed.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = Strings.FolderOperationFailed(name);
            return null;
        }
        if (string.Equals(name, Path.GetFileName(folder.TrimEnd('\\')), StringComparison.Ordinal))
            return folder;
        try
        {
            var (hr, _) = ShellOps.Rename(Shell.FromPath(folder), name, owner);
            if (hr < 0)
            {
                error = hr == ErrorCancelled ? null : $"0x{hr:X8}";
                return null;
            }
            return Path.Combine(Path.GetDirectoryName(folder.TrimEnd('\\'))!, name);
        }
        catch (Exception ex)
        {
            Log.Warn("Renaming a Big Folder's folder failed", ex);
            error = ex.Message;
            return null;
        }
    }

    public static string? ChooseFolder(IntPtr owner, string? startIn) => ShellOps.PickFolder(owner, Strings.FolderPickerTitle, startIn);
}
