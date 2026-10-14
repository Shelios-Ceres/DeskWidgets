using System.ComponentModel;
using System.Runtime.InteropServices;
using DeskWidgets.Interop;

namespace DeskWidgets.Desktop;

/// <summary>A desktop item (its full parsing path) and where it sits in the desktop view.</summary>
internal sealed record IconPosition(string Key, int X, int Y);

/// <summary>A monitor's work area in the desktop view's coordinates.</summary>
internal sealed record DisplayArea(string Device, bool Primary, int Left, int Top, int Width, int Height);

/// <summary>
/// One reading of the desktop: Explorer's layout flags and grid spacing, the
/// monitors and every icon. The JSON shape matches the DesktopRightAlign tool's
/// backups, so its original-layout.json can be taken over as is.
/// </summary>
internal sealed record DesktopSnapshot(DateTimeOffset CapturedAt, uint Flags, int SpacingX, int SpacingY,
    int IconSize, long Window, List<DisplayArea> Displays, List<IconPosition> Items);

/// <summary>
/// The desktop's icon view through documented Shell interfaces only
/// (ShellWindows → IShellBrowser → IFolderView2): no injection into Explorer,
/// no cross-process memory, no ListView messages. Ported from the standalone
/// DesktopRightAlign tool. Open one per pass, so a restarted Explorer never
/// leaves stale proxies behind.
/// </summary>
internal sealed class ShellDesktop : IDisposable
{
    /// <summary>FWF_AUTOARRANGE | FWF_SNAPTOGRID: Explorer's own arranging, which would undo ours.</summary>
    public const uint LayoutFlags = 0x1 | 0x4;

    private readonly List<object> _references = [];
    private readonly List<nint> _pidls = [];
    private readonly Dictionary<string, nint> _identities = new(StringComparer.OrdinalIgnoreCase);
    private readonly IFolderView2 _view = null!;
    private readonly nint _window;

    public ShellDesktop()
    {
        try
        {
            object windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!)!; // ShellWindows
            _references.Add(windows);
            object location = 0, root = null!;
            int desktopWindow;
            object? found = ((dynamic)windows).FindWindowSW(ref location, ref root, 8 /* SWC_DESKTOP */, out desktopWindow, 1 /* SWFO_NEEDDISPATCH */);
            object desktop = found ?? throw new InvalidOperationException(Strings.IconsDesktopNotReady);
            _references.Add(desktop);
            var topLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
            var browserId = typeof(IShellBrowser).GUID;
            Check(((IServiceProvider)desktop).QueryService(ref topLevelBrowser, ref browserId, out object browser));
            _references.Add(browser);
            Check(((IShellBrowser)browser).QueryActiveShellView(out object shellView));
            _references.Add(shellView);
            _view = (IFolderView2)shellView;
            Check(((IShellView)shellView).GetWindow(out _window));
            if (_window == 0)
                throw new InvalidOperationException(Strings.IconsDesktopNotReady);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public DesktopSnapshot Capture()
    {
        foreach (nint pidl in _pidls)
            Marshal.FreeCoTaskMem(pidl);
        _pidls.Clear();
        _identities.Clear();
        Check(_view.GetCurrentFolderFlags(out uint flags));
        Check(_view.GetSpacing(out var spacing));
        Check(_view.GetViewModeAndIconSize(out uint mode, out int iconSize));
        if (mode is not (1 or 2)) // FVM_ICON or FVM_SMALLICON
            throw new InvalidOperationException(Strings.IconsNotIconView);
        if (spacing.X <= 0 || spacing.Y <= 0)
            throw new InvalidOperationException(Strings.IconsDesktopNotReady);

        var folderId = new Guid("000214E6-0000-0000-C000-000000000046"); // IShellFolder
        Check(_view.GetFolder(ref folderId, out nint folder));
        var items = new List<IconPosition>();
        try
        {
            Check(_view.ItemCount(2 /* SVGIO_ALLVIEW */, out int count));
            for (int i = 0; i < count; i++)
            {
                Check(_view.Item(i, out nint pidl));
                _pidls.Add(pidl);
                Check(_view.GetItemPosition(pidl, out var point));
                string key = ParsingName(folder, pidl);
                if (!_identities.TryAdd(key, pidl))
                    throw new InvalidOperationException(Strings.IconsDuplicate);
                items.Add(new IconPosition(key, point.X, point.Y));
            }
        }
        finally
        {
            Marshal.Release(folder);
        }

        var displays = Win32.GetMonitors()
            .Select(monitor =>
            {
                var corner = new Win32.POINT { X = monitor.Work.Left, Y = monitor.Work.Top };
                if (!Win32.ScreenToClient(_window, ref corner))
                    throw new Win32Exception();
                return new DisplayArea(monitor.Device, monitor.Primary, corner.X, corner.Y, monitor.Work.Width, monitor.Work.Height);
            })
            .OrderByDescending(d => d.Primary).ThenBy(d => d.Left).ThenBy(d => d.Top)
            .ToList();
        return new DesktopSnapshot(DateTimeOffset.Now, flags, spacing.X, spacing.Y, iconSize, _window, displays, items);
    }

    /// <summary>A screen rectangle in the desktop view's coordinates.</summary>
    public Win32.RECT ToClient(Win32.RECT screen)
    {
        var topLeft = new Win32.POINT { X = screen.Left, Y = screen.Top };
        var bottomRight = new Win32.POINT { X = screen.Right, Y = screen.Bottom };
        Win32.ScreenToClient(_window, ref topLeft);
        Win32.ScreenToClient(_window, ref bottomRight);
        return new Win32.RECT { Left = topLeft.X, Top = topLeft.Y, Right = bottomRight.X, Bottom = bottomRight.Y };
    }

    /// <summary>Sets Explorer's auto-arrange and align-to-grid flags (only those two).</summary>
    public void SetLayoutFlags(uint flags) => Check(_view.SetCurrentFolderFlags(LayoutFlags, flags & LayoutFlags));

    /// <summary>Moves icons seen by the last <see cref="Capture"/> without changing the selection.</summary>
    public int Position(IEnumerable<IconPosition> positions)
    {
        var targets = positions.Where(p => _identities.ContainsKey(p.Key)).ToList();
        if (targets.Count == 0)
            return 0;
        var pidls = targets.Select(p => _identities[p.Key]).ToArray();
        var points = targets.Select(p => new Win32.POINT { X = p.X, Y = p.Y }).ToArray();
        Check(_view.SelectAndPositionItems((uint)targets.Count, pidls, points, 0x80 /* SVSI_POSITIONITEM */));
        return targets.Count;
    }

    public void Dispose()
    {
        foreach (nint pidl in _pidls)
            Marshal.FreeCoTaskMem(pidl);
        _pidls.Clear();
        // Casts to other interfaces share one runtime wrapper: release each wrapper once.
        var released = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (object reference in Enumerable.Reverse(_references))
        {
            if (released.Add(reference) && Marshal.IsComObject(reference))
                Marshal.ReleaseComObject(reference);
        }
        _references.Clear();
    }

    private static string ParsingName(nint folder, nint pidl)
    {
        var itemId = typeof(IShellItem).GUID;
        Check(SHCreateItemWithParent(0, folder, pidl, ref itemId, out IShellItem item));
        try
        {
            Check(item.GetDisplayName(0x80028000 /* SIGDN_DESKTOPABSOLUTEPARSING */, out nint name));
            try
            {
                return Marshal.PtrToStringUni(name) ?? throw new InvalidOperationException(Strings.IconsDesktopNotReady);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [DllImport("shell32.dll")]
    private static extern int SHCreateItemWithParent(nint parentPidl, nint folder, nint pidl, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    /// <summary>
    /// IShellView, declared only up to the window methods it inherits from
    /// IOleWindow. Explorer's cross-process proxy answers to IShellView but not
    /// to IOleWindow itself (E_NOINTERFACE).
    /// </summary>
    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        [PreserveSig] int GetWindow(out nint hwnd);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out nint hwnd);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
        [PreserveSig] int InsertMenusSB(nint menu, nint widths);
        [PreserveSig] int SetMenuSB(nint menu, nint oleMenu, nint hwnd);
        [PreserveSig] int RemoveMenusSB(nint menu);
        [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool enable);
        [PreserveSig] int TranslateAcceleratorSB(nint message, ushort id);
        [PreserveSig] int BrowseObject(nint pidl, uint flags);
        [PreserveSig] int GetViewStateStream(uint mode, out nint stream);
        [PreserveSig] int GetControlWindow(uint id, out nint window);
        [PreserveSig] int SendControlMsg(uint id, uint message, nint wparam, nint lparam, out nint result);
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint context, ref Guid handler, ref Guid riid, out nint result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint kind, out nint name);
    }

    [ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView2
    {
        [PreserveSig] int GetCurrentViewMode(out uint mode);
        [PreserveSig] int SetCurrentViewMode(uint mode);
        [PreserveSig] int GetFolder(ref Guid riid, out nint folder);
        [PreserveSig] int Item(int index, out nint pidl);
        [PreserveSig] int ItemCount(uint flags, out int count);
        [PreserveSig] int Items(uint flags, ref Guid riid, out nint items);
        [PreserveSig] int GetSelectionMarkedItem(out int index);
        [PreserveSig] int GetFocusedItem(out int index);
        [PreserveSig] int GetItemPosition(nint pidl, out Win32.POINT point);
        [PreserveSig] int GetSpacing(out Win32.POINT point);
        [PreserveSig] int GetDefaultSpacing(out Win32.POINT point);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int index, uint flags);
        [PreserveSig] int SelectAndPositionItems(uint count,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] pidls,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Win32.POINT[] points, uint flags);
        [PreserveSig] int SetGroupBy(nint key, [MarshalAs(UnmanagedType.Bool)] bool ascending);
        [PreserveSig] int GetGroupBy(nint key, nint ascending);
        [PreserveSig] int SetViewProperty(nint pidl, nint key, nint value);
        [PreserveSig] int GetViewProperty(nint pidl, nint key, nint value);
        [PreserveSig] int SetTileViewProperties(nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string properties);
        [PreserveSig] int SetExtendedTileViewProperties(nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string properties);
        [PreserveSig] int SetText(int type, [MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int SetCurrentFolderFlags(uint mask, uint flags);
        [PreserveSig] int GetCurrentFolderFlags(out uint flags);
        [PreserveSig] int GetSortColumnCount(out int count);
        [PreserveSig] int SetSortColumns(nint columns, int count);
        [PreserveSig] int GetSortColumns(nint columns, int count);
        [PreserveSig] int GetItem(int index, ref Guid riid, out nint item);
        [PreserveSig] int GetVisibleItem(int start, [MarshalAs(UnmanagedType.Bool)] bool previous, out int index);
        [PreserveSig] int GetSelectedItem(int start, out int index);
        [PreserveSig] int GetSelection([MarshalAs(UnmanagedType.Bool)] bool noneImpliesFolder, out nint selection);
        [PreserveSig] int GetSelectionState(nint pidl, out uint flags);
        [PreserveSig] int InvokeVerbOnSelection([MarshalAs(UnmanagedType.LPStr)] string verb);
        [PreserveSig] int SetViewModeAndIconSize(uint mode, int size);
        [PreserveSig] int GetViewModeAndIconSize(out uint mode, out int size);
    }
}
