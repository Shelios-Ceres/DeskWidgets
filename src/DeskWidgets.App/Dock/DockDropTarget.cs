using System.Runtime.InteropServices;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Widgets.Folder;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DeskWidgets.Dock;

/// <summary>
/// The dock's own OLE drop target (PROPOSAL 3.5, 3.6). A program dropped on the bar is added (on a group: it joins it), never opened with: the
/// gap opens where it will land and the drop puts a reference to it there. Files that are not programs are opened with the app under the pointer
/// (the apps that registered for them light up). Nothing is moved, copied or made: the effect answered at the drop is "none", so the source
/// keeps what was dragged. The same class serves the group's panel (<c>panel: true</c>), where the drop goes to the member under the pointer.
/// </summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class DockDropTarget : IDropTarget
{
    private const uint EffectCopy = 1, EffectLink = 4;

    private readonly DockController _dock;
    private readonly bool _panel;
    private List<Dropped> _items = [];
    private List<string> _files = [];
    private bool _accepting;
    private bool _openWith;
    private DockItem? _preview; // the first program of the drag as the bar would show it (for a group's mini)

    public DockDropTarget(DockController dock, bool panel = false)
    {
        _dock = dock;
        _panel = panel;
        ShellNative.OleInitialize(IntPtr.Zero);
    }

    public int DragEnter(IDataObject dataObject, uint keyState, POINTL pt, ref uint effect)
    {
        _items = [];
        _files = [];
        _accepting = false;
        _openWith = false;
        try
        {
            _items = FolderActions.ReadDrop(dataObject);
            _accepting = !_panel && _items.Any(item => DockIdentity.Classify(item.Entry) == DockDropKind.Candidate);
            _preview = _accepting ? _items.Where(item => DockIdentity.Classify(item.Entry) == DockDropKind.Candidate).Select(DockIdentity.FromDropped).FirstOrDefault(program => program is not null) : null;
            if (!_accepting)
            {
                // Not programs: real files (no folders, no virtual items) can be opened with an app.
                _files = [.. _items.Where(item => !item.Entry.IsVirtual && !item.Entry.IsFolder && item.Entry.Path is not null).Select(item => item.Entry.Path!)];
                _openWith = _files.Count > 0;
                if (_openWith)
                    _dock.BeginOpenWith(_files);
            }
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
        if (_openWith)
        {
            bool accepts;
            if (_panel)
            {
                _dock.OpenWithOverPanel(pt.x, pt.y);
                accepts = _dock.PanelOpenWithAccepts(pt.x, pt.y);
            }
            else
            {
                _dock.OpenWithOver(pt.x, pt.y);
                accepts = _dock.OpenWithAccepts(pt.x, pt.y);
            }
            _dock.ShowGap(-1);
            effect = accepts && (effect & EffectCopy) != 0 ? EffectCopy : 0;
            return 0;
        }
        if (!_accepting)
        {
            _dock.ShowGap(-1);
            effect = 0;
            return 0;
        }
        _dock.ShowProgramDrop(pt.x, pt.y, _preview);
        uint allowed = effect;
        effect = (allowed & EffectLink) != 0 ? EffectLink : (allowed & EffectCopy) != 0 ? EffectCopy : 0;
        return 0;
    }

    public int DragLeave()
    {
        bool openWith = _openWith;
        _items = [];
        _files = [];
        _accepting = false;
        _openWith = false;
        _preview = null;
        if (openWith)
            _dock.OpenWithLeft(fromPanel: _panel);
        _dock.DropEnded();
        return 0;
    }

    public int Drop(IDataObject dataObject, uint keyState, POINTL pt, ref uint effect)
    {
        var items = _items;
        bool openWith = _openWith;
        _items = [];
        _files = [];
        _openWith = false;
        _dock.DropEnded();
        effect = 0; // never "move": the source keeps the file
        if (openWith)
        {
            if (_panel)
                _dock.DropFilesOnPanel(pt.x, pt.y);
            else
                _dock.DropFiles(pt.x, pt.y);
            return 0;
        }
        if (!_accepting)
        {
            _dock.Warn(Strings.DockOnlyPrograms);
            return 0;
        }
        _accepting = false;
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
        _dock.DropPrograms(programs, pt.x, pt.y);
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
