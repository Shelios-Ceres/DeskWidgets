using System.Runtime.InteropServices;
using DeskWidgets.Core.Diagnostics;

namespace DeskWidgets.Widgets.Folder;

[ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu2 : IContextMenu
{
    [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
}

[ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu3 : IContextMenu2
{
    [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
}

/// <summary>
/// Windows' own context menu for one or several files (Phase 13.4, "Show more options"): <c>IContextMenu</c> from the Shell fills a Win32
/// popup menu, which is shown with <c>TrackPopupMenuEx</c>; the owner-drawn parts (Send to, Open with) get their messages through
/// <c>IContextMenu2/3</c>; the chosen command is run with <c>InvokeCommand</c>. The look is Windows' in both themes.
/// Open, Rename and Delete are answered by our own code (the caller's callbacks) instead, so the Recycle Bin, the origin log and the
/// in-place rename work as in our own menu; Paste is left out (there is nowhere to paste in a Big Folder card).
/// <para>
/// <b>Risk:</b> shell extensions (the "Open with", archive, antivirus, cloud-storage entries) are other people's code that runs in this
/// process, as thumbnail handlers already do. A managed exception from one is caught and reported; a crash inside native code is not
/// catchable. The call is made on the UI thread and a slow extension blocks it for that long.
/// </para>
/// </summary>
internal static class ShellContextMenu
{
    private const uint FirstId = 1, LastId = 0x7FFF;
    private const uint CmfExtendedVerbs = 0x100;
    private const uint GcsVerbW = 4, GcsVerbA = 0;
    private const uint MiimId = 0x2, MiimSubmenu = 0x4, MiimFtype = 0x100;
    private const uint MftSeparator = 0x800;
    private const uint MfByPosition = 0x400;
    private const uint TpmReturnCmd = 0x100, TpmRightButton = 0x2;
    private const uint CmicMaskUnicode = 0x4000, CmicMaskPtInvoke = 0x20000000;
    private const string WindowClass = "DeskWidgets.ShellMenuHost";

    /// <summary>The verbs left out of the menu: nothing to paste into.</summary>
    private static readonly HashSet<string> LeftOut = new(StringComparer.OrdinalIgnoreCase) { "paste", "pastelink", "pastespecial" };

    private static readonly WndProc s_procedure = HostProc; // must outlive the window class
    private static bool s_registered;
    private static IContextMenu2? s_current2;
    private static IContextMenu3? s_current3;

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    /// <param name="paths">The files, all in one folder.</param>
    /// <param name="owner">The window that owns the dialogs the commands bring up (the panel, or the card).</param>
    /// <param name="onOpen">Runs instead of the Shell's Open; null leaves Windows' own.</param>
    /// <param name="onRename">Runs instead of the Shell's Rename; null leaves Rename out of the menu.</param>
    /// <param name="onDelete">Runs instead of the Shell's Delete; null leaves Delete out of the menu.</param>
    /// <returns>A message for the user when something failed, else null.</returns>
    /// <param name="choose">Design QA and tests: picks the command (its id, or 0 for none) instead of showing the menu.</param>
    public static string? Show(IReadOnlyList<string> paths, IntPtr owner, int x, int y, Action? onOpen, Action? onRename, Action? onDelete,
        Func<IContextMenu, IntPtr, uint>? choose = null)
    {
        using var busy = PanelBusy.Enter(); // the menu and what it starts: the panel stays up through all of it
        IContextMenu? menu = null;
        IntPtr hmenu = 0, host = 0;
        try
        {
            var items = paths.Select(Shell.FromPath).ToList();
            var array = Shell.ArrayOf(items);
            menu = Shell.Bind<IContextMenu>(array, ShellNative.BHID_SFUIObject, ShellNative.IID_IContextMenu);
            hmenu = CreatePopupMenu();
            uint flags = (GetAsyncKeyState(0x10 /* VK_SHIFT */) & 0x8000) != 0 ? CmfExtendedVerbs : 0;
            int hr = menu.QueryContextMenu(hmenu, 0, FirstId, LastId, flags);
            if (hr < 0)
                return null; // nothing to show
            s_current2 = menu as IContextMenu2;
            s_current3 = menu as IContextMenu3;
            Trim(menu, hmenu, keepRename: onRename is not null, keepDelete: onDelete is not null);
            if (GetMenuItemCount(hmenu) <= 0)
                return null;
            // The verbs are read now, while the menu is built: after the menu has been shown and closed some handlers no longer give them.
            var verbs = new Dictionary<uint, string>();
            foreach (var (id, name) in ListVerbs(menu, hmenu))
            {
                if (id != 0 && name is not null)
                    verbs[id] = name;
            }

            host = CreateHost(owner);
            SetForegroundWindow(host); // or the menu would not close when the user clicks elsewhere
            uint command = choose is not null ? choose(menu, hmenu) : TrackPopupMenuEx(hmenu, TpmReturnCmd | TpmRightButton, x, y, host, 0);
            PostMessage(host, 0 /* WM_NULL */, 0, 0);
            if (command < FirstId)
                return null;

            string? verb = verbs.GetValueOrDefault(command);
            Log.Info($"Windows' menu: command {command - FirstId} \"{TextOf(hmenu, command)}\", verb {verb ?? "(none)"}");
            if (verb is not null)
            {
                if (verb.Equals("open", StringComparison.OrdinalIgnoreCase) && onOpen is not null)
                {
                    onOpen();
                    return null;
                }
                if (verb.Equals("rename", StringComparison.OrdinalIgnoreCase) && onRename is not null)
                {
                    onRename();
                    return null;
                }
                if (verb.Equals("delete", StringComparison.OrdinalIgnoreCase) && onDelete is not null)
                {
                    onDelete();
                    return null;
                }
            }
            return Invoke(menu, command - FirstId, owner, x, y);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            Log.Warn("Windows' context menu failed", ex);
            return ex.Message;
        }
        finally
        {
            s_current2 = null;
            s_current3 = null;
            if (hmenu != 0)
                DestroyMenu(hmenu);
            if (host != 0)
                DestroyWindow(host);
            if (menu is not null)
                Marshal.ReleaseComObject(menu);
        }
    }

    /// <summary>The commands in a menu with their verbs, submenus included (design QA and tests).</summary>
    internal static List<(uint Id, string? Verb)> ListVerbs(IContextMenu menu, IntPtr hmenu)
    {
        var list = new List<(uint, string?)>();
        for (int position = 0; position < GetMenuItemCount(hmenu); position++)
        {
            var info = new MenuItemInfo { Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = MiimId | MiimSubmenu | MiimFtype };
            if (!GetMenuItemInfo(hmenu, (uint)position, true, ref info) || (info.Type & MftSeparator) != 0)
                continue;
            if (info.Submenu != 0)
            {
                list.Add((0, "<submenu>"));
                continue;
            }
            if (info.Id >= FirstId && info.Id <= LastId)
                list.Add((info.Id, VerbOf(menu, info.Id - FirstId)));
        }
        return list;
    }

    /// <summary>The text of a menu item by its command id (for the log and the QA listing).</summary>
    internal static string TextOf(IntPtr hmenu, uint command)
    {
        var buffer = new System.Text.StringBuilder(256);
        return GetMenuString(hmenu, command, buffer, buffer.Capacity, 0 /* MF_BYCOMMAND */) > 0 ? buffer.ToString() : "";
    }

    /// <summary>The verb of a command ("open", "delete", "properties"), or null for one that has none. Some handlers answer only in ANSI.</summary>
    private static string? VerbOf(IContextMenu menu, uint offset)
    {
        IntPtr buffer = Marshal.AllocHGlobal(512 * 2);
        try
        {
            foreach (uint type in new[] { GcsVerbW, GcsVerbA })
            {
                Marshal.WriteInt16(buffer, 0, 0);
                if (menu.GetCommandString((UIntPtr)offset, type, IntPtr.Zero, buffer, 256) < 0)
                    continue;
                string text = (type == GcsVerbW ? Marshal.PtrToStringUni(buffer) : Marshal.PtrToStringAnsi(buffer)) ?? "";
                if (text.Length > 0)
                    return text;
            }
            return null;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Leaves out the verbs that cannot work here, and the separators that would be left doubled or at an end.</summary>
    private static void Trim(IContextMenu menu, IntPtr hmenu, bool keepRename, bool keepDelete)
    {
        for (int position = GetMenuItemCount(hmenu) - 1; position >= 0; position--)
        {
            var info = new MenuItemInfo { Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = MiimId | MiimSubmenu | MiimFtype };
            if (!GetMenuItemInfo(hmenu, (uint)position, true, ref info) || info.Submenu != 0 || (info.Type & MftSeparator) != 0)
                continue;
            if (info.Id < FirstId || info.Id > LastId)
                continue;
            string? verb = VerbOf(menu, info.Id - FirstId);
            if (verb is null)
                continue;
            bool leave = LeftOut.Contains(verb)
                || (!keepRename && verb.Equals("rename", StringComparison.OrdinalIgnoreCase))
                || (!keepDelete && verb.Equals("delete", StringComparison.OrdinalIgnoreCase));
            if (leave)
                DeleteMenu(hmenu, (uint)position, MfByPosition);
        }
        // A separator at the top or the bottom, or two in a row, goes.
        bool lastWasSeparator = true;
        for (int position = 0; position < GetMenuItemCount(hmenu); position++)
        {
            var info = new MenuItemInfo { Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = MiimFtype };
            GetMenuItemInfo(hmenu, (uint)position, true, ref info);
            bool separator = (info.Type & MftSeparator) != 0;
            if (separator && lastWasSeparator)
            {
                DeleteMenu(hmenu, (uint)position, MfByPosition);
                position--;
                continue;
            }
            lastWasSeparator = separator;
        }
        int count = GetMenuItemCount(hmenu);
        if (count > 0)
        {
            var info = new MenuItemInfo { Size = (uint)Marshal.SizeOf<MenuItemInfo>(), Mask = MiimFtype };
            GetMenuItemInfo(hmenu, (uint)(count - 1), true, ref info);
            if ((info.Type & MftSeparator) != 0)
                DeleteMenu(hmenu, (uint)(count - 1), MfByPosition);
        }
    }

    private static string? Invoke(IContextMenu menu, uint offset, IntPtr owner, int x, int y)
    {
        var info = new CmInvokeCommandInfoEx
        {
            Size = (uint)Marshal.SizeOf<CmInvokeCommandInfoEx>(),
            Mask = CmicMaskUnicode | CmicMaskPtInvoke,
            Hwnd = owner,
            Verb = (IntPtr)offset, // MAKEINTRESOURCE(offset): the command's number, not a name
            VerbW = (IntPtr)offset,
            Show = 1, // SW_SHOWNORMAL
            Point = new Point { X = x, Y = y },
        };
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<CmInvokeCommandInfoEx>());
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            int hr = menu.InvokeCommand(pointer);
            // Like Explorer, a command that returns a failure is only logged: it has shown its own message if it had one (a cancelled dialog,
            // "no printer", an app that refused), and a second, empty one from us would only be noise.
            if (hr < 0)
                Log.Warn($"Windows' menu command {VerbOf(menu, offset) ?? offset.ToString()} returned 0x{hr:X8}");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    // ----- the host window: the menu's owner, which passes the owner-drawn parts' messages on -----

    private static IntPtr CreateHost(IntPtr owner)
    {
        if (!s_registered)
        {
            var windowClass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(s_procedure),
                Instance = GetModuleHandle(null),
                ClassName = WindowClass,
            };
            RegisterClassEx(ref windowClass);
            s_registered = true;
        }
        const int WsPopup = unchecked((int)0x80000000), WsExToolWindow = 0x80;
        return CreateWindowEx(WsExToolWindow, WindowClass, "", WsPopup, 0, 0, 0, 0, owner, 0, GetModuleHandle(null), 0);
    }

    private static nint HostProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        const uint WmInitMenuPopup = 0x117, WmDrawItem = 0x2B, WmMeasureItem = 0x2C, WmMenuChar = 0x120;
        if (msg is WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar)
        {
            try
            {
                if (s_current3 is not null)
                {
                    int hr = s_current3.HandleMenuMsg2(msg, wParam, lParam, out var result);
                    if (hr >= 0)
                        return msg == WmMenuChar ? result : (msg == WmInitMenuPopup ? 0 : 1);
                }
                else if (s_current2 is not null && s_current2.HandleMenuMsg(msg, wParam, lParam) >= 0)
                {
                    return msg == WmInitMenuPopup ? 0 : 1;
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                Log.Warn("A shell extension's menu message failed", ex);
            }
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // ----- native -----

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CmInvokeCommandInfoEx
    {
        public uint Size;
        public uint Mask;
        public IntPtr Hwnd;
        public IntPtr Verb;
        public IntPtr Parameters;
        public IntPtr Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon;
        public IntPtr Title;
        public IntPtr VerbW;
        public IntPtr ParametersW;
        public IntPtr DirectoryW;
        public IntPtr TitleW;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuItemInfo
    {
        public uint Size;
        public uint Mask;
        public uint Type;
        public uint State;
        public uint Id;
        public IntPtr Submenu;
        public IntPtr CheckedBitmap;
        public IntPtr UncheckedBitmap;
        public IntPtr ItemData;
        public IntPtr TypeData;
        public uint Cch;
        public IntPtr Item;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WndProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSm;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("user32.dll", EntryPoint = "GetMenuItemInfoW")]
    private static extern bool GetMenuItemInfo(IntPtr menu, uint item, bool byPosition, ref MenuItemInfo info);

    [DllImport("user32.dll", EntryPoint = "GetMenuStringW", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(IntPtr menu, uint item, System.Text.StringBuilder text, int max, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DeleteMenu(IntPtr menu, uint position, uint flags);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
