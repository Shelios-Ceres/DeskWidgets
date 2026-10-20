using System.Runtime.InteropServices;
using DeskWidgets.Interop;

namespace DeskWidgets.PixelUi;

/// <summary>MSAA's IAccessible (oleacc), which UI Automation, and so Narrator, reads through its MSAA proxy.</summary>
[ComImport]
[Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IAccessible
{
    [DispId(-5000)]
    [return: MarshalAs(UnmanagedType.IDispatch)]
    object? get_accParent();

    [DispId(-5001)]
    int get_accChildCount();

    [DispId(-5002)]
    [return: MarshalAs(UnmanagedType.IDispatch)]
    object? get_accChild([In, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5003)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string? get_accName([In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5004)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string? get_accValue([In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5005)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string? get_accDescription([In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5006)]
    [return: MarshalAs(UnmanagedType.Struct)]
    object get_accRole([In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5007)]
    [return: MarshalAs(UnmanagedType.Struct)]
    object get_accState([In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5008)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string? get_accHelp([In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5009)]
    int get_accHelpTopic([MarshalAs(UnmanagedType.BStr)] out string? helpFile, [In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5010)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string? get_accKeyboardShortcut([In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5011)]
    [return: MarshalAs(UnmanagedType.Struct)]
    object? get_accFocus();

    [DispId(-5012)]
    [return: MarshalAs(UnmanagedType.Struct)]
    object? get_accSelection();

    [DispId(-5013)]
    [return: MarshalAs(UnmanagedType.BStr)]
    string? get_accDefaultAction([In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5014)]
    void accSelect(int flagsSelect, [In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5015)]
    void accLocation(out int left, out int top, out int width, out int height, [In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5016)]
    [return: MarshalAs(UnmanagedType.Struct)]
    object? accNavigate(int navDir, [In, Optional, MarshalAs(UnmanagedType.Struct)] object varStart);

    [DispId(-5017)]
    [return: MarshalAs(UnmanagedType.Struct)]
    object? accHitTest(int left, int top);

    [DispId(-5018)]
    void accDoDefaultAction([In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild);

    [DispId(-5003)]
    void put_accName([In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild, [In, MarshalAs(UnmanagedType.BStr)] string name);

    [DispId(-5004)]
    void put_accValue([In, Optional, MarshalAs(UnmanagedType.Struct)] object varChild, [In, MarshalAs(UnmanagedType.BStr)] string value);
}

/// <summary>
/// A popup window's accessibility tree: the client area is the container
/// (<see cref="SelfRole"/>), numbered children 1..n are "simple elements"
/// (rows or buttons) described by the subclass.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal abstract partial class PopupAccessible : IAccessible
{
    // Roles and states (oleacc.h).
    public const int RoleMenuPopup = 0x0B, RoleMenuItem = 0x0C, RoleDialog = 0x12, RoleStaticText = 0x29, RolePushButton = 0x2B, RoleSeparator = 0x15;
    public const int StateUnavailable = 0x1, StateSelected = 0x2, StateFocused = 0x4, StateChecked = 0x10, StateDefault = 0x100,
        StateOffscreen = 0x10000, StateFocusable = 0x100000, StateSelectable = 0x200000, StateHasPopup = 0x40000000;

    private const int ChildIdSelf = 0;
    private static readonly COMException InvalidArgument = new("Invalid argument", unchecked((int)0x80070057));
    private static readonly COMException NotImplemented = new("Not implemented", unchecked((int)0x80004001));

    protected PopupAccessible(Func<nint> hwnd) => HwndSource = hwnd;

    protected Func<nint> HwndSource { get; }

    protected abstract int ChildCount { get; }

    protected abstract int SelfRole { get; }

    protected abstract string? Name(int id);

    protected virtual string? Description(int id) => null;

    protected abstract int Role(int id);

    protected abstract int State(int id);

    protected abstract Win32.RECT Location(int id);

    protected abstract int FocusedId { get; }

    protected abstract void Select(int id);

    protected abstract void DoDefault(int id);

    protected virtual string? DefaultActionText(int id) => id == ChildIdSelf ? null : "Press";

    /// <summary>Answers WM_GETOBJECT for the client area.</summary>
    public nint GetObject(nint wParam)
    {
        nint unknown = Marshal.GetComInterfaceForObject(this, typeof(IAccessible));
        try
        {
            return Native.LresultFromObject(ref Native.IidIAccessible, wParam, unknown);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    private int Id(object varChild)
    {
        if (varChild is int id && id >= 0 && id <= ChildCount)
            return id;
        throw InvalidArgument;
    }

    public object? get_accParent()
    {
        // The client area's parent is the window itself.
        nint hwnd = HwndSource();
        if (hwnd == 0 || Native.CreateStdAccessibleObject(hwnd, Native.OBJID_WINDOW, ref Native.IidIAccessible, out nint pointer) != 0 || pointer == 0)
            return null;
        try
        {
            return Marshal.GetObjectForIUnknown(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    public int get_accChildCount() => ChildCount;

    public object? get_accChild(object varChild)
    {
        Id(varChild);
        return null; // simple elements have no object of their own
    }

    public string? get_accName(object varChild) => Name(Id(varChild));

    public string? get_accValue(object varChild) => null;

    public string? get_accDescription(object varChild) => Description(Id(varChild));

    public object get_accRole(object varChild)
    {
        int id = Id(varChild);
        return id == ChildIdSelf ? SelfRole : Role(id);
    }

    public object get_accState(object varChild)
    {
        int id = Id(varChild);
        return State(id);
    }

    public string? get_accHelp(object varChild) => null;

    public int get_accHelpTopic(out string? helpFile, object varChild)
    {
        helpFile = null;
        throw NotImplemented;
    }

    public string? get_accKeyboardShortcut(object varChild) => null;

    public object? get_accFocus() => FocusedId >= 0 ? FocusedId : null;

    public object? get_accSelection() => FocusedId > 0 ? FocusedId : null;

    public string? get_accDefaultAction(object varChild) => DefaultActionText(Id(varChild));

    public void accSelect(int flagsSelect, object varChild)
    {
        int id = Id(varChild);
        if (id > 0 && (flagsSelect & 0x3) != 0) // SELFLAG_TAKEFOCUS | SELFLAG_TAKESELECTION
            Select(id);
    }

    public void accLocation(out int left, out int top, out int width, out int height, object varChild)
    {
        var rect = Location(Id(varChild));
        left = rect.Left;
        top = rect.Top;
        width = rect.Width;
        height = rect.Height;
    }

    public object? accNavigate(int navDir, object varStart)
    {
        int id = Id(varStart);
        const int First = 7, Last = 8, Next = 5, Previous = 6; // NAVDIR_FIRSTCHILD / LASTCHILD / NEXT / PREVIOUS
        int count = ChildCount;
        int target = navDir switch
        {
            First when id == ChildIdSelf => 1,
            Last when id == ChildIdSelf => count,
            Next when id > 0 => id + 1,
            Previous when id > 0 => id - 1,
            _ => -1,
        };
        return target >= 1 && target <= count ? target : null;
    }

    public object? accHitTest(int left, int top)
    {
        for (int id = 1; id <= ChildCount; id++)
        {
            var rect = Location(id);
            if (left >= rect.Left && left < rect.Right && top >= rect.Top && top < rect.Bottom)
                return id;
        }
        var self = Location(ChildIdSelf);
        return left >= self.Left && left < self.Right && top >= self.Top && top < self.Bottom ? ChildIdSelf : null;
    }

    public void accDoDefaultAction(object varChild)
    {
        int id = Id(varChild);
        if (id > 0)
            DoDefault(id);
    }

    public void put_accName(object varChild, string name) => throw NotImplemented;

    public void put_accValue(object varChild, string value) => throw NotImplemented;
}
