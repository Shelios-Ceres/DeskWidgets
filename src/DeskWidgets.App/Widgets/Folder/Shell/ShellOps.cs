using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DeskWidgets.Widgets.Folder;

/// <summary>Records what an IFileOperation did, item by item (results, and the paths of the items it created).</summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class OpSink : IFileOperationProgressSink
{
    public readonly List<string> Events = [];

    /// <summary>The new path of each moved or copied item, keyed by the source path; and of each new item, in the order created.</summary>
    public readonly Dictionary<string, string> Placed = new(StringComparer.OrdinalIgnoreCase);

    public readonly List<string> NewItems = [];

    private void Note(string verb, int hr, IShellItem? source, IShellItem? created)
    {
        string? path = created is null ? null : Shell.FileSystemPath(created);
        Events.Add($"{verb} hr=0x{hr:X8}{(path is null ? "" : " -> " + path)}");
        if (hr < 0 || path is null)
            return;
        if (verb == "new")
            NewItems.Add(path);
        else if (source is not null && Shell.FileSystemPath(source) is { } from)
            Placed[from] = path;
    }

    public int StartOperations() => 0;
    public int FinishOperations(int hrResult) { Events.Add($"finish hr=0x{hrResult:X8}"); return 0; }
    public int PreRenameItem(uint flags, IShellItem item, string newName) => 0;
    public int PostRenameItem(uint flags, IShellItem item, string newName, int hrRename, IShellItem? newlyCreated) { Note("rename", hrRename, item, newlyCreated); return 0; }
    public int PreMoveItem(uint flags, IShellItem item, IShellItem destFolder, string newName) => 0;
    public int PostMoveItem(uint flags, IShellItem item, IShellItem destFolder, string newName, int hrMove, IShellItem? newlyCreated) { Note("move", hrMove, item, newlyCreated); return 0; }
    public int PreCopyItem(uint flags, IShellItem item, IShellItem destFolder, string newName) => 0;
    public int PostCopyItem(uint flags, IShellItem item, IShellItem destFolder, string newName, int hrCopy, IShellItem? newlyCreated) { Note("copy", hrCopy, item, newlyCreated); return 0; }
    public int PreDeleteItem(uint flags, IShellItem item) => 0;
    public int PostDeleteItem(uint flags, IShellItem item, int hrDelete, IShellItem? newlyCreated) { Note("delete", hrDelete, item, null); return 0; }
    public int PreNewItem(uint flags, IShellItem destFolder, string newName) => 0;
    public int PostNewItem(uint flags, IShellItem destFolder, string newName, string templateName, uint fileAttributes, int hrNew, IShellItem? newItem) { Note("new", hrNew, null, newItem); return 0; }
    public int UpdateProgress(uint workTotal, uint workSoFar) => 0;
    public int ResetTimer() => 0;
    public int PauseTimer() => 0;
    public int ResumeTimer() => 0;
}

/// <summary>The OLE drag source for dragging items out of a window we draw ourselves (the Pixel panel).</summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class DropSource : IDropSource
{
    private const int DRAGDROP_S_DROP = 0x40100, DRAGDROP_S_CANCEL = 0x40101, DRAGDROP_S_USEDEFAULTCURSORS = 0x40102;

    public int QueryContinueDrag(bool escapePressed, uint keyState)
    {
        if (escapePressed)
            return DRAGDROP_S_CANCEL;
        return (keyState & ShellNative.MK_LBUTTON) == 0 ? DRAGDROP_S_DROP : 0;
    }

    public int GiveFeedback(uint effect) => DRAGDROP_S_USEDEFAULTCURSORS;
}

/// <summary>The Shell operations the Big Folder needs. Every call that can raise Explorer's own dialogs runs on the UI thread.</summary>
internal static class ShellOps
{
    /// <summary>Undo-able, with Explorer's progress and conflict dialogs. Never FOF_NOCONFIRMATION: it silently overwrites.</summary>
    public const uint InteractiveFlags = ShellNative.FOF_ALLOWUNDO | ShellNative.FOFX_ADDUNDORECORD;

    public static IFileOperation CreateFileOperation()
    {
        var clsid = ShellNative.CLSID_FileOperation;
        var iid = typeof(IFileOperation).GUID;
        Shell.Check(ShellNative.CoCreateInstance(ref clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */ | 4 /* LOCAL_SERVER */, ref iid, out var obj), "CoCreateInstance(FileOperation)");
        return (IFileOperation)obj;
    }

    /// <summary>Moves or copies items into a folder (one undo step).</summary>
    public static (int Hr, OpSink Sink) Perform(DropEffect effect, IEnumerable<IShellItem> sources, IShellItem destFolder, uint flags, IntPtr owner = default)
    {
        using var busy = PanelBusy.Enter(); // Explorer's progress and conflict windows must not close the panel
        var sink = new OpSink();
        var op = CreateFileOperation();
        op.SetOperationFlags(flags);
        if (owner != 0)
            op.SetOwnerWindow(owner);
        foreach (var source in sources)
        {
            int hr = effect switch
            {
                DropEffect.Move => op.MoveItem(source, destFolder, null, sink),
                DropEffect.Copy => op.CopyItem(source, destFolder, null, sink),
                _ => throw new ArgumentException("not a file operation: " + effect),
            };
            Shell.Check(hr, $"queue {effect}");
        }
        return (op.PerformOperations(), sink);
    }

    /// <summary>Sends items to the Recycle Bin, with an undo record.</summary>
    public static (int Hr, OpSink Sink) Recycle(IEnumerable<IShellItem> items, IntPtr owner = default)
    {
        using var busy = PanelBusy.Enter();
        var sink = new OpSink();
        var op = CreateFileOperation();
        // No FOF_NOCONFIRMATION: an item that cannot be recycled (too big for the bin, a drive without one) must ask before it is destroyed.
        op.SetOperationFlags(InteractiveFlags | ShellNative.FOFX_RECYCLEONDELETE | ShellNative.FOF_WANTNUKEWARNING);
        if (owner != 0)
            op.SetOwnerWindow(owner);
        foreach (var item in items)
            Shell.Check(op.DeleteItem(item, sink), "queue delete");
        return (op.PerformOperations(), sink);
    }

    public static (int Hr, OpSink Sink) Rename(IShellItem item, string newName, IntPtr owner = default)
    {
        using var busy = PanelBusy.Enter();
        var sink = new OpSink();
        var op = CreateFileOperation();
        op.SetOperationFlags(InteractiveFlags);
        if (owner != 0)
            op.SetOwnerWindow(owner);
        Shell.Check(op.RenameItem(item, newName, sink), "queue rename");
        return (op.PerformOperations(), sink);
    }

    /// <summary>
    /// Writes a shortcut into an existing (empty) file: a path for a file or program, or the ID list of an item that has no path
    /// (a Start or AppsFolder app).
    /// </summary>
    public static void FillShortcut(string linkPath, string? target, IShellItem? virtualItem)
    {
        var link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(ShellNative.CLSID_ShellLink)!)!;
        if (virtualItem is not null)
        {
            var pidl = Shell.Pidl(virtualItem);
            try
            {
                Shell.Check(link.SetIDList(pidl), "SetIDList");
            }
            finally
            {
                ShellNative.ILFree(pidl);
            }
        }
        else
        {
            Shell.Check(link.SetPath(target!), "SetPath");
            if (Path.GetDirectoryName(target) is { } working)
                link.SetWorkingDirectory(working);
        }
        ((IPersistFile)link).Save(linkPath, true);
    }

    public static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "shortcut" : cleaned;
    }

    public static string UniquePath(string folder, string stem, string extension)
    {
        string path = Path.Combine(folder, stem + extension);
        for (int n = 2; File.Exists(path) || Directory.Exists(path); n++)
            path = Path.Combine(folder, $"{stem} ({n}){extension}");
        return path;
    }

    /// <summary>What a shortcut points to: a path, or (an ID-list shortcut) the item's parsing name, which is the AUMID for an app.</summary>
    public readonly record struct LinkInfo(string? Target, string? Arguments, string? IdListName);

    public static LinkInfo ReadShortcut(string linkPath)
    {
        var link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(ShellNative.CLSID_ShellLink)!)!;
        ((IPersistFile)link).Load(linkPath, 0);
        var target = new StringBuilder(520);
        link.GetPath(target, target.Capacity, IntPtr.Zero, 4 /* SLGP_RAWPATH */);
        var args = new StringBuilder(1024);
        link.GetArguments(args, args.Capacity);
        string? idName = null;
        if (link.GetIDList(out var pidl) == 0 && pidl != IntPtr.Zero)
        {
            try
            {
                idName = Shell.Name(Shell.FromPidl(pidl), ShellNative.SIGDN_DESKTOPABSOLUTEPARSING);
            }
            finally
            {
                ShellNative.ILFree(pidl);
            }
        }
        return new LinkInfo(target.Length == 0 ? null : Environment.ExpandEnvironmentVariables(target.ToString()), args.Length == 0 ? null : args.ToString(), idName);
    }

    /// <summary>ShellExecuteEx on a file, a shortcut or a "shell:AppsFolder\..." name; <paramref name="verb"/> null means the default.</summary>
    public static (bool Ok, int Win32Error) Open(string file, string? verb = null, string? workingDirectory = null, IntPtr owner = default)
    {
        using var busy = PanelBusy.Enter(); // a "runas" brings up the UAC prompt, an unknown type the "Open with" box
        var info = new ShellNative.SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<ShellNative.SHELLEXECUTEINFO>(),
            fMask = ShellNative.SEE_MASK_NOASYNC,
            hwnd = owner,
            lpVerb = verb,
            lpFile = file,
            lpDirectory = workingDirectory,
            nShow = 1,
        };
        bool ok = ShellNative.ShellExecuteEx(ref info);
        return (ok, ok ? 0 : Marshal.GetLastWin32Error());
    }

    /// <summary>The system's folder picker; null when cancelled.</summary>
    public static string? PickFolder(IntPtr owner, string title, string? startIn)
    {
        using var busy = PanelBusy.Enter();
        var clsid = ShellNative.CLSID_FileOpenDialog;
        var iid = typeof(IFileOpenDialog).GUID;
        if (ShellNative.CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out var obj) < 0)
            return null;
        var dialog = (IFileOpenDialog)obj;
        dialog.GetOptions(out uint options);
        dialog.SetOptions(options | ShellNative.FOS_PICKFOLDERS | ShellNative.FOS_FORCEFILESYSTEM | ShellNative.FOS_PATHMUSTEXIST);
        dialog.SetTitle(title);
        if (startIn is not null && Directory.Exists(startIn))
            dialog.SetFolder(Shell.FromPath(startIn));
        if (dialog.Show(owner) < 0 || dialog.GetResult(out var result) < 0)
            return null;
        return Shell.FileSystemPath(result);
    }
}
