using System.Runtime.InteropServices;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Widgets.Folder;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DeskWidgets.Dock;

/// <summary>
/// The dock's own OLE drop target (PROPOSAL 3.5). A program dropped anywhere on the bar is added, never opened with: the gap opens where it will
/// land and the drop puts a reference to it there. Anything that is not a program is refused in Build A (Open with comes in Build C). Nothing
/// is moved, copied or made: the effect answered at the drop is "none", so the source keeps what was dragged.
/// </summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class DockDropTarget : IDropTarget
{
    private const uint EffectCopy = 1, EffectLink = 4;

    private readonly DockController _dock;
    private List<Dropped> _items = [];
    private bool _accepting;

    public DockDropTarget(DockController dock)
    {
        _dock = dock;
        ShellNative.OleInitialize(IntPtr.Zero);
    }

    public int DragEnter(IDataObject dataObject, uint keyState, POINTL pt, ref uint effect)
    {
        _items = [];
        _accepting = false;
        try
        {
            _items = FolderActions.ReadDrop(dataObject);
            _accepting = _items.Any(item => DockIdentity.Classify(item.Entry) == DockDropKind.Candidate);
        }
        catch (Exception ex)
        {
            Log.Warn("Reading a drag over the dock failed", ex);
        }
        return Answer(pt, ref effect);
    }

    public int DragOver(uint keyState, POINTL pt, ref uint effect) => Answer(pt, ref effect);

    private int Answer(POINTL pt, ref uint effect)
    {
        if (!_accepting)
        {
            _dock.ShowGap(-1);
            effect = 0;
            return 0;
        }
        _dock.ShowGap(_dock.SlotAtScreenX(pt.x));
        uint allowed = effect;
        effect = (allowed & EffectLink) != 0 ? EffectLink : (allowed & EffectCopy) != 0 ? EffectCopy : 0;
        return 0;
    }

    public int DragLeave()
    {
        _items = [];
        _accepting = false;
        _dock.DropEnded();
        return 0;
    }

    public int Drop(IDataObject dataObject, uint keyState, POINTL pt, ref uint effect)
    {
        int slot = _dock.SlotAtScreenX(pt.x);
        var items = _items;
        _items = [];
        _dock.DropEnded();
        effect = 0; // never "move": the source keeps the file
        if (!_accepting)
        {
            _dock.Warn(Strings.DockOnlyPrograms);
            return 0;
        }
        // Read now: the shell items belong to the drag. The identity is saved; nothing of the drag is kept.
        var programs = new List<DockItem>();
        foreach (var dropped in items)
        {
            if (DockIdentity.FromDropped(dropped) is { } program)
                programs.Add(program);
        }
        if (programs.Count == 0)
        {
            _dock.Warn(Strings.DockOnlyPrograms);
            return 0;
        }
        _dock.Add(programs, slot);
        return 0;
    }
}

/// <summary>Registers one drop target on a window and on every window below it (OLE asks only the window under the pointer), and takes it off again.</summary>
internal sealed class DropTargetRegistration
{
    private readonly List<nint> _registered = [];

    public void RegisterOn(nint window, IDropTarget target)
    {
        var all = new List<nint> { window };
        all.AddRange(ShellNative.Children(window));
        foreach (nint candidate in all)
        {
            if (ShellNative.GetProp(candidate, "OleDropTargetInterface") != IntPtr.Zero)
                continue;
            int hr = ShellNative.RegisterDragDrop(candidate, target);
            if (hr < 0)
                Log.Warn($"Registering the dock's drop target on window {candidate:X} failed: 0x{hr:X8}");
            else
                _registered.Add(candidate);
        }
    }

    public void Revoke()
    {
        foreach (nint window in _registered)
        {
            if (ShellNative.IsWindow(window))
                ShellNative.RevokeDragDrop(window);
        }
        _registered.Clear();
    }
}
