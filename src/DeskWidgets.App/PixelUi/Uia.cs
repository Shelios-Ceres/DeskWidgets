using System.Runtime.InteropServices;
using DeskWidgets.Interop;

namespace DeskWidgets.PixelUi;

[Flags]
internal enum ProviderOptions
{
    ServerSideProvider = 0x2,
    UseComThreading = 0x20,
}

[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}

// The UI Automation provider interfaces (UIAutomationCore.h), declared by hand so the app needs no WPF assembly.

[ComImport]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderSimple
{
    ProviderOptions ProviderOptions { get; }

    [return: MarshalAs(UnmanagedType.IUnknown)]
    object? GetPatternProvider(int patternId);

    [return: MarshalAs(UnmanagedType.Struct)]
    object? GetPropertyValue(int propertyId);

    IRawElementProviderSimple? HostRawElementProvider { get; }
}

[ComImport]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragment
{
    [return: MarshalAs(UnmanagedType.Interface)]
    IRawElementProviderFragment? Navigate(int direction);

    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
    int[]? GetRuntimeId();

    UiaRect BoundingRectangle { get; }

    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)]
    IRawElementProviderSimple[]? GetEmbeddedFragmentRoots();

    void SetFocus();

    IRawElementProviderFragmentRoot FragmentRoot { get; }
}

[ComImport]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragmentRoot
{
    [return: MarshalAs(UnmanagedType.Interface)]
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);

    [return: MarshalAs(UnmanagedType.Interface)]
    IRawElementProviderFragment? GetFocus();
}

[ComImport]
[Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInvokeProvider
{
    void Invoke();
}

[ComImport]
[Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToggleProvider
{
    void Toggle();

    int ToggleState { get; }
}

[ComImport]
[Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExpandCollapseProvider
{
    void Expand();

    void Collapse();

    int ExpandCollapseState { get; }
}

[ComImport]
[Guid("2acad808-b2d4-452d-a407-91ff1ad167b2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISelectionItemProvider
{
    void Select();

    void AddToSelection();

    void RemoveFromSelection();

    int IsSelected { get; }

    IRawElementProviderSimple? SelectionContainer { get; }
}

internal static class UiaNative
{
    public const int UiaRootObjectId = -25;
    public const int PatternInvoke = 10000, PatternExpandCollapse = 10005, PatternSelectionItem = 10010, PatternToggle = 10015;
    public const int EventMenuOpened = 20003, EventFocusChanged = 20005, EventMenuClosed = 20007, EventWindowOpened = 20016;
    public const int ControlTypeButton = 50000, ControlTypeMenu = 50009, ControlTypeMenuItem = 50011, ControlTypeText = 50020, ControlTypeWindow = 50032, ControlTypeSeparator = 50038;

    [DllImport("UIAutomationCore.dll")]
    public static extern nint UiaReturnRawElementProvider(nint hwnd, nint wParam, nint lParam, IRawElementProviderSimple? provider);

    [DllImport("UIAutomationCore.dll")]
    public static extern int UiaHostProviderFromHwnd(nint hwnd, [MarshalAs(UnmanagedType.Interface)] out IRawElementProviderSimple provider);

    [DllImport("UIAutomationCore.dll")]
    public static extern int UiaRaiseAutomationEvent(IRawElementProviderSimple provider, int eventId);

    [DllImport("UIAutomationCore.dll")]
    public static extern int UiaDisconnectProvider(IRawElementProviderSimple? provider);

    [DllImport("UIAutomationCore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UiaClientsAreListening();
}

/// <summary>
/// The UI Automation side of a popup, built on the same description as the MSAA one:
/// the window is the root (a menu or a dialog), numbered children are rows or buttons.
/// </summary>
internal abstract partial class PopupAccessible
{
    private Node? _root;
    private readonly Dictionary<int, Node> _nodes = [];

    /// <summary>Whether a row toggles (a check or a radio item): it then offers the Toggle pattern.</summary>
    protected virtual bool IsCheckable(int id) => false;

    /// <summary>Whether a row is a radio item: it then offers the SelectionItem pattern.</summary>
    protected virtual bool IsRadio(int id) => false;

    private Node RootNode => _root ??= new Node(this, 0);

    private Node NodeAt(int id)
    {
        if (id == 0)
            return RootNode;
        if (!_nodes.TryGetValue(id, out var node))
            _nodes[id] = node = new Node(this, id);
        return node;
    }

    /// <summary>Answers WM_GETOBJECT for UI Automation's root object id.</summary>
    public nint GetUiaObject(nint wParam, nint lParam) => UiaNative.UiaReturnRawElementProvider(HwndSource(), wParam, lParam, RootNode);

    /// <summary>Tells clients that the keyboard focus moved to a row (id 1..n) or the container (0).</summary>
    public void RaiseFocus(int id)
    {
        if (UiaNative.UiaClientsAreListening())
            UiaNative.UiaRaiseAutomationEvent(NodeAt(id), UiaNative.EventFocusChanged);
    }

    public void RaiseOpened(int eventId)
    {
        if (UiaNative.UiaClientsAreListening())
            UiaNative.UiaRaiseAutomationEvent(RootNode, eventId);
    }

    /// <summary>Called when the window is destroyed: lets UI Automation drop its references.</summary>
    public void DisconnectUia()
    {
        if (_root is null)
            return;
        UiaNative.UiaDisconnectProvider(_root);
        foreach (var node in _nodes.Values)
            UiaNative.UiaDisconnectProvider(node);
        _nodes.Clear();
        _root = null;
    }

    private int ControlTypeOf(int id)
    {
        int role = id == 0 ? SelfRole : Role(id);
        return role switch
        {
            RoleMenuPopup => UiaNative.ControlTypeMenu,
            RoleMenuItem => UiaNative.ControlTypeMenuItem,
            RoleSeparator => UiaNative.ControlTypeSeparator,
            RoleDialog => UiaNative.ControlTypeWindow,
            RolePushButton => UiaNative.ControlTypeButton,
            RoleStaticText => UiaNative.ControlTypeText,
            _ => UiaNative.ControlTypeWindow,
        };
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class Node(PopupAccessible owner, int id) : IRawElementProviderSimple, IRawElementProviderFragment, IRawElementProviderFragmentRoot,
        IInvokeProvider, IToggleProvider, IExpandCollapseProvider, ISelectionItemProvider
    {
        private bool IsRoot => id == 0;

        public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;

        public IRawElementProviderSimple? HostRawElementProvider
        {
            get
            {
                if (!IsRoot || UiaNative.UiaHostProviderFromHwnd(owner.HwndSource(), out var host) != 0)
                    return null;
                return host;
            }
        }

        public object? GetPatternProvider(int patternId)
        {
            if (IsRoot)
                return null;
            int state = owner.State(id);
            bool enabled = (state & StateUnavailable) == 0;
            int role = owner.Role(id);
            return patternId switch
            {
                UiaNative.PatternInvoke when enabled && role is RolePushButton or RoleMenuItem && (state & StateHasPopup) == 0 && !owner.IsCheckable(id) && !owner.IsRadio(id) => this,
                UiaNative.PatternSelectionItem when enabled && owner.IsRadio(id) => this,
                UiaNative.PatternToggle when enabled && owner.IsCheckable(id) => this,
                UiaNative.PatternExpandCollapse when enabled && (state & StateHasPopup) != 0 => this,
                _ => null,
            };
        }

        public object? GetPropertyValue(int propertyId)
        {
            int state = owner.State(id);
            switch (propertyId)
            {
                case 30003: // ControlType
                    return owner.ControlTypeOf(id);
                case 30005: // Name
                    return owner.Name(id);
                case 30010: // IsEnabled
                    return (state & StateUnavailable) == 0;
                case 30009: // IsKeyboardFocusable
                    return (state & StateFocusable) != 0;
                case 30008: // HasKeyboardFocus
                    return (state & StateFocused) != 0;
                case 30016: // IsControlElement
                case 30017: // IsContentElement
                    return true;
                case 30022: // IsOffscreen
                    return (state & StateOffscreen) != 0;
                case 30011: // AutomationId
                    return IsRoot ? "PixelPopup" : "Item" + id;
                case 30013: // HelpText
                    return owner.Description(id);
                case 30174: // IsDialog
                    return IsRoot && owner.SelfRole == RoleDialog;
                default:
                    return null;
            }
        }

        public IRawElementProviderFragment? Navigate(int direction)
        {
            int count = owner.ChildCount;
            int target = direction switch
            {
                0 /* Parent */ => IsRoot ? -1 : 0,
                1 /* NextSibling */ => !IsRoot && id < count ? id + 1 : -1,
                2 /* PreviousSibling */ => !IsRoot && id > 1 ? id - 1 : -1,
                3 /* FirstChild */ => IsRoot && count > 0 ? 1 : -1,
                4 /* LastChild */ => IsRoot && count > 0 ? count : -1,
                _ => -1,
            };
            return target < 0 || (IsRoot && direction == 0) ? null : owner.NodeAt(target);
        }

        public int[]? GetRuntimeId() => IsRoot ? null : [3 /* UiaAppendRuntimeId */, (int)owner.HwndSource(), id];

        public UiaRect BoundingRectangle
        {
            get
            {
                var rect = owner.Location(id);
                return new UiaRect { Left = rect.Left, Top = rect.Top, Width = rect.Width, Height = rect.Height };
            }
        }

        public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

        public void SetFocus()
        {
            if (!IsRoot)
                owner.Select(id);
        }

        public IRawElementProviderFragmentRoot FragmentRoot => owner.RootNode;

        public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y)
        {
            for (int child = 1; child <= owner.ChildCount; child++)
            {
                var rect = owner.Location(child);
                if (x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom)
                    return owner.NodeAt(child);
            }
            return owner.RootNode;
        }

        public IRawElementProviderFragment? GetFocus()
        {
            int focused = owner.FocusedId;
            return focused > 0 ? owner.NodeAt(focused) : null;
        }

        public void Invoke() => owner.DoDefault(id);

        public void Toggle() => owner.DoDefault(id);

        public int ToggleState => (owner.State(id) & StateChecked) != 0 ? 1 : 0;

        public void Expand() => owner.DoDefault(id);

        public void Collapse()
        {
        }

        public void Select() => owner.DoDefault(id);

        public void AddToSelection() => owner.DoDefault(id);

        public void RemoveFromSelection()
        {
        }

        public int IsSelected => (owner.State(id) & StateChecked) != 0 ? 1 : 0;

        public IRawElementProviderSimple? SelectionContainer => owner.RootNode;

        public int ExpandCollapseState => 0; // collapsed: the submenu is a separate window that opens on demand
    }
}
