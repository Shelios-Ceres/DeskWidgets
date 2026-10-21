using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DeskWidgets.Widgets.Folder;

// Phase 4 spike (task 4.3): hand-written COM and Win32 surface for the Big Folder plumbing.
// Throwaway: Phase 5 decides what to keep.

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;

    public PROPERTYKEY(string fmtid, uint pid)
    {
        this.fmtid = new Guid(fmtid);
        this.pid = pid;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINTL
{
    public int x;
    public int y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx;
    public int cy;
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetParent(out IShellItem ppsi);
    [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
}

[ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem2
{
    [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetParent(out IShellItem ppsi);
    [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    [PreserveSig] int GetPropertyStore(uint flags, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetPropertyStoreWithCreateObject(uint flags, IntPtr punkCreateObject, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetPropertyStoreForKeys(IntPtr rgKeys, uint cKeys, uint flags, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetPropertyDescriptionList(ref PROPERTYKEY key, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int Update(IntPtr pbc);
    [PreserveSig] int GetProperty(ref PROPERTYKEY key, IntPtr ppropvar);
    [PreserveSig] int GetCLSID(ref PROPERTYKEY key, out Guid clsid);
    [PreserveSig] int GetFileTime(ref PROPERTYKEY key, out FILETIME ft);
    [PreserveSig] int GetInt32(ref PROPERTYKEY key, out int value);
    [PreserveSig] int GetString(ref PROPERTYKEY key, out IntPtr psz);
    [PreserveSig] int GetUInt32(ref PROPERTYKEY key, out uint value);
    [PreserveSig] int GetUInt64(ref PROPERTYKEY key, out ulong value);
    [PreserveSig] int GetBool(ref PROPERTYKEY key, out int value);
}

[ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray
{
    [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetPropertyStore(uint flags, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetPropertyDescriptionList(ref PROPERTYKEY key, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetAttributes(uint attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemAt(uint index, out IShellItem item);
    [PreserveSig] int EnumItems(out IntPtr ppenumShellItems);
}

[ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    [PreserveSig] int Next(uint celt, out IShellItem item, out uint fetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumShellItems ppenum);
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(SIZE size, uint flags, out IntPtr phbm);
}

[ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperation
{
    [PreserveSig] int Advise(IFileOperationProgressSink sink, out uint cookie);
    [PreserveSig] int Unadvise(uint cookie);
    [PreserveSig] int SetOperationFlags(uint flags);
    [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
    [PreserveSig] int SetProgressDialog(IntPtr dialog);
    [PreserveSig] int SetProperties(IntPtr properties);
    [PreserveSig] int SetOwnerWindow(IntPtr hwnd);
    [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
    [PreserveSig] int ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
    [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IFileOperationProgressSink? sink);
    [PreserveSig] int RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    [PreserveSig] int MoveItem(IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IFileOperationProgressSink? sink);
    [PreserveSig] int MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destFolder);
    [PreserveSig] int CopyItem(IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string? copyName, IFileOperationProgressSink? sink);
    [PreserveSig] int CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destFolder);
    [PreserveSig] int DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
    [PreserveSig] int DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
    [PreserveSig] int NewItem(IShellItem destFolder, uint fileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, IFileOperationProgressSink? sink);
    [PreserveSig] int PerformOperations();
    [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}

[ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperationProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int hrResult);
    [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, int hrRename, IShellItem? newlyCreated);
    [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName, int hrMove, IShellItem? newlyCreated);
    [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName, int hrCopy, IShellItem? newlyCreated);
    [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
    [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int hrDelete, IShellItem? newlyCreated);
    [PreserveSig] int PreNewItem(uint flags, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    [PreserveSig] int PostNewItem(uint flags, IShellItem destFolder, [MarshalAs(UnmanagedType.LPWStr)] string newName, [MarshalAs(UnmanagedType.LPWStr)] string templateName, uint fileAttributes, int hrNew, IShellItem? newItem);
    [PreserveSig] int UpdateProgress(uint workTotal, uint workSoFar);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    [PreserveSig] int GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
    [PreserveSig] int GetIDList(out IntPtr pidl);
    [PreserveSig] int SetIDList(IntPtr pidl);
    [PreserveSig] int GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
    [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
    [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
    [PreserveSig] int GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
    [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
    [PreserveSig] int GetHotkey(out ushort hotkey);
    [PreserveSig] int SetHotkey(ushort hotkey);
    [PreserveSig] int GetShowCmd(out int showCmd);
    [PreserveSig] int SetShowCmd(int showCmd);
    [PreserveSig] int GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxIconPath, out int icon);
    [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
    [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relPath, uint reserved);
    [PreserveSig] int Resolve(IntPtr hwnd, uint flags);
    [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}

[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTarget
{
    [PreserveSig] int DragEnter(IDataObject dataObject, uint keyState, POINTL pt, ref uint effect);
    [PreserveSig] int DragOver(uint keyState, POINTL pt, ref uint effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(IDataObject dataObject, uint keyState, POINTL pt, ref uint effect);
}

[ComImport, Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropSource
{
    [PreserveSig] int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool escapePressed, uint keyState);
    [PreserveSig] int GiveFeedback(uint effect);
}

/// <summary>The system's open dialog (IFileOpenDialog, the methods of IFileDialog first); used with FOS_PICKFOLDERS to choose a folder.</summary>
[ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOpenDialog
{
    [PreserveSig] int Show(IntPtr owner);
    [PreserveSig] int SetFileTypes(uint count, IntPtr filters);
    [PreserveSig] int SetFileTypeIndex(uint index);
    [PreserveSig] int GetFileTypeIndex(out uint index);
    [PreserveSig] int Advise(IntPtr events, out uint cookie);
    [PreserveSig] int Unadvise(uint cookie);
    [PreserveSig] int SetOptions(uint options);
    [PreserveSig] int GetOptions(out uint options);
    [PreserveSig] int SetDefaultFolder(IShellItem folder);
    [PreserveSig] int SetFolder(IShellItem folder);
    [PreserveSig] int GetFolder(out IShellItem folder);
    [PreserveSig] int GetCurrentSelection(out IShellItem item);
    [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int GetFileName(out IntPtr name);
    [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
    [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
    [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
    [PreserveSig] int GetResult(out IShellItem item);
    [PreserveSig] int AddPlace(IShellItem item, uint placement);
    [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
    [PreserveSig] int Close(int hr);
    [PreserveSig] int SetClientGuid(ref Guid guid);
    [PreserveSig] int ClearClientData();
    [PreserveSig] int SetFilter(IntPtr filter);
    [PreserveSig] int GetResults(out IntPtr items);
    [PreserveSig] int GetSelectedItems(out IntPtr items);
}

[ComImport, Guid("4657278B-411B-11d2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTargetHelper
{
    [PreserveSig] int DragEnter(IntPtr hwndTarget, IDataObject dataObject, ref POINTL pt, uint effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int DragOver(ref POINTL pt, uint effect);
    [PreserveSig] int Drop(IDataObject dataObject, ref POINTL pt, uint effect);
    [PreserveSig] int Show([MarshalAs(UnmanagedType.Bool)] bool show);
}

[ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
    [PreserveSig] int InvokeCommand(IntPtr pici);
    [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
}

/// <summary>Constants and P/Invokes for the shell, OLE and GDI calls the spike makes.</summary>
internal static class ShellNative
{
    public static readonly Guid BHID_DataObject = new("b8c0bd9f-ed24-455c-83e6-d5390c4fe8c4");
    public static readonly Guid BHID_SFUIObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");
    public static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    public static readonly Guid IID_IDataObject = new("0000010e-0000-0000-C000-000000000046");
    public static readonly Guid IID_IDropTarget = new("00000122-0000-0000-C000-000000000046");
    public static readonly Guid IID_IContextMenu = new("000214e4-0000-0000-c000-000000000046");
    public static readonly Guid IID_IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    public static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    public static readonly Guid IID_IShellItemArray = new("b63ea76d-1f85-456f-a19c-48159efa858b");
    public static readonly Guid CLSID_FileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    public static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");

    public static readonly Guid FOLDERID_Desktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    public static readonly Guid FOLDERID_PublicDesktop = new("C4AA340D-F20F-4863-AFEF-F87EF2E6BA25");
    public static readonly Guid FOLDERID_ProgramFiles = new("905e63b6-c1bf-494e-b29c-65b732d3d21a");
    public static readonly Guid FOLDERID_ProgramFilesX86 = new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E");
    public static readonly Guid FOLDERID_Windows = new("F38BF404-1D43-42F2-9305-67DE0B28FC23");
    public static readonly Guid FOLDERID_ProgramData = new("62AB5D82-FDC1-4DC3-A9DD-070D1D495D97");
    public static readonly Guid FOLDERID_RoamingAppData = new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");
    public static readonly Guid FOLDERID_LocalAppData = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
    public static readonly Guid FOLDERID_Profile = new("5E6C858F-0E22-4760-9AFE-EA3317B67173");
    public static readonly Guid FOLDERID_StartMenu = new("625B53C3-AB48-4EC1-BA1F-A1EF4146FC19");
    public static readonly Guid FOLDERID_CommonStartMenu = new("A4115719-D62E-491D-AA7C-E74B8BE3B067");

    public static readonly PROPERTYKEY PKEY_AppUserModel_ID = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 5);
    public static readonly PROPERTYKEY PKEY_Link_TargetParsingPath = new("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25", 2);
    public static readonly PROPERTYKEY PKEY_ParsingPath = new("28636AA6-953D-11D2-B5D6-00C04FD918D0", 30);
    public static readonly PROPERTYKEY PKEY_ItemTypeText = new("B725F130-47EF-101A-A5F1-02608C9EEBAC", 4);
    public static readonly PROPERTYKEY PKEY_FileExtension = new("E4F10A3C-49E6-405D-8288-A23BD4EEAA6C", 100);
    public static readonly PROPERTYKEY PKEY_Recycle_DeletedFrom = new("9B174B33-40FF-11D2-A27E-00C04FC30871", 2);

    public const uint SIGDN_NORMALDISPLAY = 0x00000000;
    public const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;
    public const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
    public const uint SIGDN_FILESYSPATH = 0x80058000;

    public const uint SFGAO_FILESYSTEM = 0x40000000;
    public const uint SFGAO_FOLDER = 0x20000000;
    public const uint SFGAO_STREAM = 0x00400000;
    public const uint SFGAO_LINK = 0x00010000;
    public const uint SFGAO_STORAGE = 0x00000008;

    public const uint DROPEFFECT_NONE = 0, DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, DROPEFFECT_LINK = 4;
    public const uint MK_LBUTTON = 1, MK_RBUTTON = 2, MK_SHIFT = 4, MK_CONTROL = 8, MK_ALT = 0x20;

    public const uint SIIGBF_RESIZETOFIT = 0x00, SIIGBF_BIGGERSIZEOK = 0x01, SIIGBF_MEMORYONLY = 0x02, SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08, SIIGBF_INCACHEONLY = 0x10;

    public const uint FOF_SILENT = 0x4, FOF_RENAMEONCOLLISION = 0x8, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_WANTNUKEWARNING = 0x4000, FOF_NOCONFIRMMKDIR = 0x200,
        FOF_NOERRORUI = 0x400, FOFX_NOCOPYHOOKS = 0x00800000, FOFX_RECYCLEONDELETE = 0x00080000, FOFX_ADDUNDORECORD = 0x20000000;

    public const int E_PENDING = unchecked((int)0x8000000A);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    public static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

    [DllImport("shell32.dll", PreserveSig = true)]
    public static extern int SHCreateItemFromIDList(IntPtr pidl, ref Guid riid, out IShellItem item);

    [DllImport("shell32.dll", PreserveSig = true)]
    public static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, out IntPtr pidl);

    [DllImport("shell32.dll", PreserveSig = true)]
    public static extern int SHCreateShellItemArrayFromIDLists(uint cidl, IntPtr[] pidls, out IShellItemArray array);

    [DllImport("shell32.dll", PreserveSig = true)]
    public static extern int SHCreateShellItemArrayFromDataObject(IDataObject dataObject, ref Guid riid, out IShellItemArray array);

    [DllImport("shell32.dll", PreserveSig = true)]
    public static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    public static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");

    public const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;

    /// <summary>The Shell's own Properties window for a file-system path (SHOP_FILEPATH).</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SHObjectProperties(IntPtr hwnd, uint type, string item, string? page);

    [DllImport("shell32.dll")]
    public static extern IntPtr ILCombine(IntPtr first, IntPtr second);

    [DllImport("shell32.dll")]
    public static extern void ILFree(IntPtr pidl);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr ptr);

    [DllImport("ole32.dll")]
    public static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);

    [DllImport("ole32.dll")]
    public static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll")]
    public static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("ole32.dll")]
    public static extern int DoDragDrop(IDataObject dataObject, IDropSource dropSource, uint okEffects, out uint effect);

    [DllImport("ole32.dll")]
    public static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget target);

    [DllImport("ole32.dll")]
    public static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetProp(IntPtr hwnd, string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterClipboardFormat(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClipboardFormatName(uint format, StringBuilder name, int max);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    public static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("user32.dll")]
    public static extern uint GetMenuItemID(IntPtr menu, int pos);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetMenuString(IntPtr menu, uint id, StringBuilder text, int max, uint flags);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern UIntPtr GlobalSize(IntPtr hMem);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [DllImport("gdi32.dll")]
    public static extern int GetObject(IntPtr handle, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[]? bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    public const uint SEE_MASK_NOCLOSEPROCESS = 0x40, SEE_MASK_INVOKEIDLIST = 0x0C, SEE_MASK_FLAG_NO_UI = 0x400, SEE_MASK_NOASYNC = 0x100;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out DeskWidgets.Interop.Win32.RECT rect);

    public static string ClassOf(IntPtr hwnd)
    {
        var cls = new StringBuilder(128);
        GetClassNameW(hwnd, cls, cls.Capacity);
        return cls.ToString();
    }

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);

    public static List<IntPtr> Children(IntPtr parent)
    {
        var list = new List<IntPtr>();
        EnumChildWindows(parent, (hwnd, _) =>
        {
            list.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Top-level windows of this process (class, title, visible): what a blocked call may be waiting behind.</summary>
    public static List<string> OwnWindows()
    {
        var list = new List<string>();
        uint mine = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != mine)
                return true;
            var cls = new StringBuilder(128);
            var title = new StringBuilder(128);
            GetClassNameW(hwnd, cls, cls.Capacity);
            GetWindowTextW(hwnd, title, title.Capacity);
            list.Add($"{hwnd:X} class={cls} title=\"{title}\" visible={IsWindowVisible(hwnd)}");
            return true;
        }, IntPtr.Zero);
        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX, ptY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[]? handles, uint milliseconds, uint wakeMask, uint flags);

    /// <summary>Pumps this thread's messages until <paramref name="done"/> says so or the time is up; returns whether it was done.</summary>
    public static bool Pump(int timeoutMs, Func<bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            if (done())
                return true;
            MsgWaitForMultipleObjectsEx(0, null, 20, 0x04FF /* QS_ALLINPUT */, 4 /* MWMO_INPUTAVAILABLE */);
        }
        return done();
    }

    [DllImport("kernel32.dll")]
    public static extern int GetProcessId(IntPtr process);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);
}

/// <summary>Small helpers over the shell COM surface. All of them swallow nothing: failures surface as COMException with the HRESULT.</summary>
internal static class Shell
{
    public static IShellItem FromPath(string pathOrParsingName)
    {
        var iid = ShellNative.IID_IShellItem;
        Check(ShellNative.SHCreateItemFromParsingName(pathOrParsingName, IntPtr.Zero, ref iid, out var item), pathOrParsingName);
        return item;
    }

    public static IShellItem FromPidl(IntPtr pidl)
    {
        var iid = ShellNative.IID_IShellItem;
        Check(ShellNative.SHCreateItemFromIDList(pidl, ref iid, out var item), "SHCreateItemFromIDList");
        return item;
    }

    public static void Check(int hr, string what)
    {
        if (hr < 0)
            throw new COMException($"{what}: 0x{hr:X8}", hr);
    }

    public static string? Name(IShellItem item, uint sigdn)
    {
        if (item.GetDisplayName(sigdn, out var ptr) < 0 || ptr == IntPtr.Zero)
            return null;
        string text = Marshal.PtrToStringUni(ptr)!;
        ShellNative.CoTaskMemFree(ptr);
        return text;
    }

    public static string? FileSystemPath(IShellItem item) => Name(item, ShellNative.SIGDN_FILESYSPATH);

    public static uint Attributes(IShellItem item, uint mask)
        => item.GetAttributes(mask, out uint attributes) < 0 ? 0 : attributes;

    public static string? Str(IShellItem item, PROPERTYKEY key)
    {
        if (item is not IShellItem2 item2 || item2.GetString(ref key, out var ptr) < 0 || ptr == IntPtr.Zero)
            return null;
        string text = Marshal.PtrToStringUni(ptr)!;
        ShellNative.CoTaskMemFree(ptr);
        return text.Length == 0 ? null : text;
    }

    public static string? KnownFolder(Guid id)
    {
        if (ShellNative.SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var ptr) < 0)
            return null;
        string path = Marshal.PtrToStringUni(ptr)!;
        ShellNative.CoTaskMemFree(ptr);
        return path;
    }

    public static IntPtr Pidl(IShellItem item)
    {
        Check(ShellNative.SHGetIDListFromObject(item, out var pidl), "SHGetIDListFromObject");
        return pidl;
    }

    public static IShellItemArray ArrayOf(IEnumerable<IShellItem> items)
    {
        var pidls = items.Select(Pidl).ToArray();
        try
        {
            Check(ShellNative.SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out var array), "SHCreateShellItemArrayFromIDLists");
            return array;
        }
        finally
        {
            foreach (var pidl in pidls)
                ShellNative.ILFree(pidl);
        }
    }

    public static T Bind<T>(object source, Guid handler, Guid iid) where T : class
    {
        IntPtr ppv;
        int hr = source switch
        {
            IShellItem item => item.BindToHandler(IntPtr.Zero, ref handler, ref iid, out ppv),
            IShellItemArray array => array.BindToHandler(IntPtr.Zero, ref handler, ref iid, out ppv),
            _ => throw new ArgumentException("not a shell item or array"),
        };
        Check(hr, $"BindToHandler {handler}");
        try
        {
            return (T)Marshal.GetObjectForIUnknown(ppv);
        }
        finally
        {
            Marshal.Release(ppv);
        }
    }

    /// <summary>The Shell's own data object for these items: what Explorer offers while the items are dragged.</summary>
    public static IDataObject DataObjectOf(IEnumerable<IShellItem> items)
        => Bind<IDataObject>(ArrayOf(items), ShellNative.BHID_DataObject, ShellNative.IID_IDataObject);

    /// <summary>The folder's own drop target: Explorer's rules for what a drop onto this folder does.</summary>
    public static IDropTarget FolderDropTarget(IShellItem folder)
        => Bind<IDropTarget>(folder, ShellNative.BHID_SFUIObject, ShellNative.IID_IDropTarget);

    public static List<IShellItem> Children(IShellItem folder)
    {
        var list = new List<IShellItem>();
        var enumerator = Bind<IEnumShellItems>(folder, ShellNative.BHID_EnumItems, ShellNative.IID_IEnumShellItems);
        while (enumerator.Next(1, out var item, out uint fetched) == 0 && fetched == 1)
            list.Add(item);
        return list;
    }

    public static List<IShellItem> Items(IShellItemArray array)
    {
        array.GetCount(out uint count);
        var list = new List<IShellItem>();
        for (uint i = 0; i < count; i++)
        {
            array.GetItemAt(i, out var item);
            list.Add(item);
        }
        return list;
    }

    public static IShellItemArray? ArrayFromDataObject(IDataObject data)
    {
        var iid = ShellNative.IID_IShellItemArray;
        return ShellNative.SHCreateShellItemArrayFromDataObject(data, ref iid, out var array) >= 0 ? array : null;
    }

    public static List<string> Formats(IDataObject data)
    {
        var names = new List<string>();
        var enumerator = data.EnumFormatEtc(DATADIR.DATADIR_GET);
        var etc = new FORMATETC[1];
        var fetched = new int[1];
        while (enumerator.Next(1, etc, fetched) == 0)
            names.Add(FormatName(etc[0].cfFormat));
        return names;
    }

    public static string FormatName(int format)
    {
        switch (format)
        {
            case 1: return "CF_TEXT";
            case 2: return "CF_BITMAP";
            case 7: return "CF_OEMTEXT";
            case 13: return "CF_UNICODETEXT";
            case 15: return "CF_HDROP";
            case 16: return "CF_LOCALE";
        }
        var name = new StringBuilder(128);
        return ShellNative.GetClipboardFormatName((uint)format, name, name.Capacity) > 0 ? name.ToString() : $"#{format}";
    }

    public static byte[]? GetBytes(IDataObject data, string format)
    {
        var etc = new FORMATETC { cfFormat = (short)ShellNative.RegisterClipboardFormat(format), dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, ptd = IntPtr.Zero, tymed = TYMED.TYMED_HGLOBAL };
        try
        {
            data.GetData(ref etc, out var medium);
            try
            {
                if (medium.tymed != TYMED.TYMED_HGLOBAL)
                    return null;
                var size = (int)ShellNative.GlobalSize(medium.unionmember);
                var bytes = new byte[size];
                var ptr = ShellNative.GlobalLock(medium.unionmember);
                Marshal.Copy(ptr, bytes, 0, size);
                ShellNative.GlobalUnlock(medium.unionmember);
                return bytes;
            }
            finally
            {
                ShellNative.ReleaseStgMedium(ref medium);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rebuilds the items from a "Shell IDList Array" (CIDA) blob: the same thing a DataPackageView hands over
    /// as raw bytes. Item i is the parent folder's pidl followed by child i.
    /// </summary>
    public static List<IShellItem> ItemsFromCida(byte[] cida)
    {
        var items = new List<IShellItem>();
        var buffer = Marshal.AllocCoTaskMem(cida.Length);
        try
        {
            Marshal.Copy(cida, 0, buffer, cida.Length);
            uint count = (uint)Marshal.ReadInt32(buffer, 0);
            var parent = buffer + (int)(uint)Marshal.ReadInt32(buffer, 4);
            for (int i = 1; i <= count; i++)
            {
                var child = buffer + (int)(uint)Marshal.ReadInt32(buffer, 4 + 4 * i);
                var absolute = ShellNative.ILCombine(parent, child);
                try
                {
                    items.Add(FromPidl(absolute));
                }
                finally
                {
                    ShellNative.ILFree(absolute);
                }
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
        return items;
    }
}
