using System.ComponentModel;
using System.Runtime.InteropServices;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>Starts a dock program (PROPOSAL 3.7): <c>ShellExecute</c> for an exe, a Start entry or a URL; <c>ActivateApplication</c> for a Store app.</summary>
internal static class DockLauncher
{
    /// <summary>Starts the program. Returns an error text, or null when it was started (or the user declined the UAC prompt).</summary>
    public static string? Start(DockItem item, nint owner, bool asAdministrator = false)
    {
        var plan = item.PlanStart();
        switch (plan.Kind)
        {
            case DockStartKind.None:
                return new Win32Exception(2).Message;
            case DockStartKind.Activate:
                return Activate(plan.Target);
            default:
            {
                var (ok, error) = ShellOps.Open(plan.Target, asAdministrator && plan.Kind == DockStartKind.Exe ? "runas" : null, plan.WorkingDir, owner, plan.Arguments);
                // 1223: the user declined the UAC prompt. That is an answer, not a failure.
                return ok || error == 1223 ? null : new Win32Exception(error).Message;
            }
        }
    }

    private static string? Activate(string aumid)
    {
        try
        {
            var manager = (IApplicationActivationManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"))!)!;
            int hr = manager.ActivateApplication(aumid, null, 0, out _);
            return hr >= 0 ? null : Marshal.GetExceptionForHR(hr)?.Message ?? $"0x{hr:X8}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Opens the folder that holds the program's exe, with the exe selected. False when the item has no exe path (a Store app).</summary>
    public static bool OpenFileLocation(DockItem item)
    {
        if (string.IsNullOrEmpty(item.ExePath) || !File.Exists(item.ExePath))
            return false;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{item.ExePath}\"") { UseShellExecute = true });
        return true;
    }

    /// <summary>Whether the program can be found now. Reads the disk and the Shell: never call it on the UI thread (a dock froze on a missing target: RetroBar #1391).</summary>
    public static bool Exists(DockItem item)
    {
        if (!string.IsNullOrEmpty(item.ExePath))
            return File.Exists(item.ExePath);
        if (!string.IsNullOrEmpty(item.Aumid))
        {
            try
            {
                Shell.FromPath(@"shell:AppsFolder\" + item.Aumid);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        return !string.IsNullOrEmpty(item.Url); // a protocol address: nothing to look up
    }

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string aumid, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, int options, out uint processId);

        [PreserveSig]
        int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string aumid, nint items, [MarshalAs(UnmanagedType.LPWStr)] string? verb, out uint processId);

        [PreserveSig]
        int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string aumid, nint items, out uint processId);
    }
}
