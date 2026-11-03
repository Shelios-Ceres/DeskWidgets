using System.Runtime.InteropServices;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Widgets.Folder;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DeskWidgets.Dock;

[ComImport, Guid("F04061AC-1659-4A3F-A954-775AA57FC083"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAssocHandler
{
    [PreserveSig] int GetName(out IntPtr name);
    [PreserveSig] int GetUIName(out IntPtr name);
    [PreserveSig] int GetIconLocation(out IntPtr path, out int index);
    [PreserveSig] int IsRecommended();
    [PreserveSig] int MakeDefault([MarshalAs(UnmanagedType.LPWStr)] string description);
    [PreserveSig] int Invoke(IntPtr dataObject);
    [PreserveSig] int CreateInvoker([MarshalAs(UnmanagedType.Interface)] object dataObject, out IntPtr invoker);
}

[ComImport, Guid("973810AE-9599-4B88-9E4D-6EE98C9552DA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumAssocHandlers
{
    [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IAssocHandler handler, out uint fetched);
}

/// <summary>
/// One app registered for a file type (<c>SHAssocEnumHandlers</c>). <see cref="Name"/> is the exe path for classic apps and a plain name for
/// others; <see cref="IconLocation"/> is <c>path,index</c> or, for a Store app, <c>@{Package_version_arch__hash?ms-resource://...},0</c>.
/// <see cref="Native"/> is a COM object of the STA worker that enumerated it: use it (and <see cref="DockOpenWith.Release"/> it) only there.
/// </summary>
internal sealed record DockHandler(string Name, string UiName, string IconLocation, IAssocHandler? Native = null)
{
    /// <summary>What makes two handlers the same app across file types.</summary>
    public string Identity => (Name + "|" + IconLocation).ToLowerInvariant();
}

/// <summary>
/// PROPOSAL 3.6 / SPIKE 6: which dock apps can open the dragged files, and opening them. <see cref="Enumerate"/>, <see cref="Invoke"/> and
/// <see cref="Release"/> touch COM and must run on the dock's STA worker, the same thread for all three (never the UI thread).
/// The rest is pure.
/// </summary>
internal static class DockOpenWith
{
    private const int AssocFilterNone = 0; // all handlers, not only the recommended ones

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHAssocEnumHandlers(string ext, int filter, [MarshalAs(UnmanagedType.Interface)] out IEnumAssocHandlers handlers);

    /// <summary>
    /// Distinct lower-case extensions with their dot (".png"), in first-seen order. A file without one gives "" (kept, so the caller can see
    /// there was one); <see cref="Enumerate"/> ignores "".
    /// </summary>
    public static List<string> ExtensionsOf(IEnumerable<string> paths)
    {
        var list = new List<string>();
        foreach (string path in paths)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            if (!list.Contains(ext))
                list.Add(ext);
        }
        return list;
    }

    /// <summary>
    /// The full handler list for every extension, intersected by identity (a handler counts only if it handles every extension). Extensions
    /// "" are ignored; no extension left, or one extension with no handler, gives an empty list. Call once per drag, on the STA worker;
    /// hand the result to <see cref="Release"/> on that thread when the drag is over.
    /// </summary>
    public static IReadOnlyList<DockHandler> Enumerate(IReadOnlyCollection<string> extensions)
    {
        var wanted = extensions.Where(ext => ext.Length > 1 && ext[0] == '.').Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
            return [];
        var result = new List<DockHandler>();
        for (int i = 0; i < wanted.Count; i++)
        {
            var found = EnumerateOne(wanted[i]);
            if (i == 0)
            {
                result = found;
                continue;
            }
            var identities = found.Select(handler => handler.Identity).ToHashSet();
            Release(found); // the first extension's objects are the ones kept
            var drop = result.Where(kept => !identities.Contains(kept.Identity)).ToList();
            Release(drop);
            result.RemoveAll(drop.Contains);
            if (result.Count == 0)
                break;
        }
        return result;
    }

    private static List<DockHandler> EnumerateOne(string ext)
    {
        var list = new List<DockHandler>();
        int hr = SHAssocEnumHandlers(ext, AssocFilterNone, out var enumerator);
        if (hr < 0)
        {
            Log.Warn($"SHAssocEnumHandlers({ext}) failed: 0x{hr:X8}");
            return list;
        }
        try
        {
            while (enumerator.Next(1, out var native, out uint fetched) == 0 && fetched == 1)
            {
                string name = Take(native.GetName(out var namePtr), namePtr) ?? "";
                string ui = Take(native.GetUIName(out var uiPtr), uiPtr) ?? "";
                string icon = Take(native.GetIconLocation(out var iconPtr, out int index), iconPtr) ?? "";
                if (name.Length == 0 && icon.Length == 0)
                {
                    Marshal.ReleaseComObject(native);
                    continue;
                }
                list.Add(new DockHandler(name, ui, icon + "," + index, native));
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
        return list;
    }

    private static string? Take(int hr, IntPtr ptr)
    {
        if (hr < 0 || ptr == IntPtr.Zero)
            return null;
        string text = Marshal.PtrToStringUni(ptr)!;
        ShellNative.CoTaskMemFree(ptr);
        return text;
    }

    /// <summary>Releases the native handlers. Same STA worker as <see cref="Enumerate"/>; safe to call twice.</summary>
    public static void Release(IEnumerable<DockHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            if (handler.Native is { } native && Marshal.IsComObject(native))
            {
                try
                {
                    Marshal.FinalReleaseComObject(native);
                }
                catch (InvalidComObjectException)
                {
                    // Already released.
                }
            }
        }
    }

    /// <summary>
    /// Opens the files with the handler (<c>IAssocHandler.Invoke</c> on the Shell's data object of the files: works for desktop and Store apps).
    /// Returns an error text, or null. On the STA worker that enumerated the handler, before <see cref="Release"/>.
    /// </summary>
    public static string? Invoke(DockHandler handler, IReadOnlyList<string> paths)
    {
        if (handler.Native is not { } native)
            return Strings.DockOpenWithGone;
        if (paths.Count == 0)
            return Strings.DockNothingToOpen;
        var items = new List<IShellItem>();
        IntPtr unknown = IntPtr.Zero;
        try
        {
            foreach (string path in paths)
                items.Add(Shell.FromPath(path));
            var data = Shell.DataObjectOf(items);
            unknown = Marshal.GetComInterfaceForObject(data, typeof(IDataObject));
            int hr = native.Invoke(unknown);
            if (hr < 0)
            {
                Log.Warn($"IAssocHandler.Invoke ({handler.UiName}) failed: 0x{hr:X8}");
                return Strings.DockOpenWithFailed(handler.UiName, hr);
            }
            return null;
        }
        catch (COMException ex)
        {
            Log.Warn($"Opening files with {handler.UiName} failed", ex);
            return Strings.DockOpenWithFailed(handler.UiName, ex.HResult);
        }
        finally
        {
            if (unknown != IntPtr.Zero)
                Marshal.Release(unknown);
            foreach (var item in items)
                Marshal.ReleaseComObject(item);
        }
    }

    // ----- matching (pure) -----

    public static bool Matches(DockItem app, DockHandler handler) => Matches(app, handler.Name, handler.IconLocation);

    /// <summary>
    /// Whether the handler is this dock app, in SPIKE 6's order: the handler's name is the app's exe path; the icon location's exe is the app's
    /// exe path; for a Store app, the icon location's package name is the AUMID's. A group never matches, nor an app with no exe path and no AUMID.
    /// </summary>
    public static bool Matches(DockItem app, string handlerName, string iconLocation)
    {
        if (app.IsGroup || !app.SupportsRunning)
            return false;
        if (!string.IsNullOrEmpty(app.ExePath))
        {
            string? exe = Normalize(app.ExePath);
            if (exe is null)
                return false;
            if (Normalize(handlerName) is { } named && Path.IsPathFullyQualified(named) && string.Equals(named, exe, StringComparison.OrdinalIgnoreCase))
                return true;
            return IconExe(iconLocation) is { } icon && string.Equals(icon, exe, StringComparison.OrdinalIgnoreCase);
        }
        if (app.IsStoreApp && AumidPackageName(app.Aumid) is { } package && StorePackageName(iconLocation) is { } handlerPackage)
            return string.Equals(package, handlerPackage, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    /// <summary>The exe path of an icon location (<c>path,index</c>, optionally quoted, with environment variables); null for a Store or resource form or a relative path.</summary>
    public static string? IconExe(string? iconLocation)
    {
        string text = (iconLocation ?? "").Trim();
        if (text.Length == 0 || text[0] == '@')
            return null;
        if (text[0] == '"')
        {
            int close = text.IndexOf('"', 1);
            text = close < 0 ? text[1..] : text[1..close];
        }
        else
        {
            int comma = text.LastIndexOf(',');
            if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out _))
                text = text[..comma];
        }
        string? path = Normalize(text);
        return path is not null && Path.IsPathFullyQualified(path) ? path : null;
    }

    /// <summary>The package name of a Store icon location <c>@{Microsoft.Windows.Photos_2026..._x64__8wekyb3d8bbwe?ms-resource://...}</c>: what is before the first underscore.</summary>
    public static string? StorePackageName(string? iconLocation)
    {
        string text = iconLocation ?? "";
        if (!text.StartsWith("@{", StringComparison.Ordinal))
            return null;
        int underscore = text.IndexOf('_', 2);
        return underscore > 2 ? text[2..underscore] : null;
    }

    /// <summary>The package name of an AUMID "Package_hash!App": what is before the first underscore.</summary>
    public static string? AumidPackageName(string? aumid)
    {
        string text = aumid ?? "";
        int underscore = text.IndexOf('_');
        return underscore > 0 ? text[..underscore] : null;
    }

    private static string? Normalize(string? path)
    {
        string text = (path ?? "").Trim().Trim('"');
        if (text.Length == 0)
            return null;
        try
        {
            text = Environment.ExpandEnvironmentVariables(text);
            return Path.IsPathFullyQualified(text) ? Path.GetFullPath(text) : text;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
