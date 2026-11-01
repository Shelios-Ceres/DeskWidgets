using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Widgets.Folder;
using Microsoft.Win32;

namespace DeskWidgets.Dock;

/// <summary>
/// The apps pinned to the Windows taskbar, in the taskbar's order (PROPOSAL 3.5, SPIKE 5.3). They are in
/// <c>HKCU\...\Explorer\Taskband\FavoritesResolve</c>, not in the <c>User Pinned\TaskBar</c> folder, which has no Store pins and keeps stale
/// files. The value is a sequence of entries, each a <c>uint32</c> size and a shortcut stream. This only reads: the taskbar is never changed.
/// </summary>
internal static class TaskbandPins
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";
    private const string ValueName = "FavoritesResolve";

    /// <summary>The folder the taskbar's classic pins live in; a pin entry's target is a file there.</summary>
    public const string PinnedFolderMarker = @"\User Pinned\TaskBar\";

    /// <summary>
    /// Splits the value into its shortcut streams. A size that does not fit the rest of the value ends the list (a damaged or newer format
    /// gives what was read so far, never an exception). Pure: tested on synthetic bytes.
    /// </summary>
    public static IReadOnlyList<byte[]> Split(ReadOnlySpan<byte> blob)
    {
        var entries = new List<byte[]>();
        int offset = 0;
        while (offset + 4 <= blob.Length)
        {
            uint size = BitConverter.ToUInt32(blob.Slice(offset, 4));
            // The smallest shortcut stream is the 76-byte header.
            if (size < 76 || size > (uint)(blob.Length - offset - 4))
                break;
            entries.Add(blob.Slice(offset + 4, (int)size).ToArray());
            offset += 4 + (int)size;
        }
        return entries;
    }

    /// <summary>Whether a pin entry's target path is a file in the taskbar's own folder (so that file holds the real shortcut).</summary>
    public static bool IsPinnedFolderFile(string? path) => path is not null && path.Contains(PinnedFolderMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>The taskbar's pins as dock programs, in order. Empty when the key is missing or unreadable; reading failures are logged.</summary>
    public static List<DockItem> Read()
    {
        var items = new List<DockItem>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key?.GetValue(ValueName) is not byte[] blob)
                return items;
            foreach (var entry in Split(blob))
            {
                try
                {
                    if (Resolve(entry) is { } item)
                        items.Add(item);
                }
                catch (Exception ex)
                {
                    Log.Warn("A taskbar pin could not be read; skipped", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("The taskbar's pins could not be read", ex);
        }
        return items;
    }

    private static DockItem? Resolve(byte[] stream)
    {
        var link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(ShellNative.CLSID_ShellLink)!)!;
        nint raw = SHCreateMemStream(stream, (uint)stream.Length);
        if (raw == 0)
            return null;
        try
        {
            var memory = (IStream)Marshal.GetObjectForIUnknown(raw);
            ((IPersistStream)link).Load(memory);

            var target = new StringBuilder(520);
            link.GetPath(target, target.Capacity, 0, 4 /* SLGP_RAWPATH */);
            string path = Environment.ExpandEnvironmentVariables(target.ToString());
            if (path.Length > 0)
            {
                // A classic pin: its target is the shortcut in the taskbar's folder.
                if (IsPinnedFolderFile(path) && File.Exists(path))
                    return DockIdentity.FromPath(path);
                return DockIdentity.FromPath(path);
            }

            // A Store pin: an ID list whose parsing name is the AUMID.
            if (link.GetIDList(out nint pidl) == 0 && pidl != 0)
            {
                try
                {
                    string? parsing = Shell.Name(Shell.FromPidl(pidl), ShellNative.SIGDN_DESKTOPABSOLUTEPARSING);
                    if (!string.IsNullOrEmpty(parsing) && !parsing.StartsWith("::{", StringComparison.Ordinal))
                        return DockIdentity.FromAumid(parsing);
                }
                finally
                {
                    ShellNative.ILFree(pidl);
                }
            }
            return null;
        }
        finally
        {
            Marshal.Release(raw);
        }
    }

    [DllImport("shlwapi.dll")]
    private static extern nint SHCreateMemStream(byte[] init, uint size);
}

/// <summary>The part of IPersistStream the pin reader calls (the framework only ships IPersistFile).</summary>
[ComImport, Guid("00000109-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistStream
{
    void GetClassID(out Guid classId);

    [PreserveSig]
    int IsDirty();

    void Load(IStream stream);

    void Save(IStream stream, [MarshalAs(UnmanagedType.Bool)] bool clearDirty);

    void GetSizeMax(out long size);
}
