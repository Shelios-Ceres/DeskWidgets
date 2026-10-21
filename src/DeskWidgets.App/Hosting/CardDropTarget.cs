using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI.Dispatching;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DeskWidgets.Hosting;

/// <summary>
/// The card window's own OLE drop target. It gets the source's real data object, so Start and AppsFolder items arrive intact
/// (WinUI's drop events would reduce them to nothing). Hover answers come from the widget's <see cref="DropPolicy"/>, the label
/// is written onto the data object as "DropDescription" through the Shell's drag helper, and the work is done after
/// <c>Drop</c> has returned. Never forwards the drop to the Shell.
/// </summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
internal sealed class CardDropTarget : IDropTarget
{
    private readonly nint _top;
    private readonly Func<IDropWidget?> _widget;
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private readonly IDropTargetHelper? _helper;
    private IDataObject? _data;
    private List<Dropped> _items = [];
    private DropEffect _allowed;
    private DropKeys _keys = DropKeys.None;
    private DropVerb? _shown;
    private bool _accepting;

    public CardDropTarget(nint topLevelWindow, Func<IDropWidget?> widget)
    {
        ShellNative.OleInitialize(IntPtr.Zero); // a no-op when the thread already has OLE
        _top = topLevelWindow;
        _widget = widget;
        try
        {
            var clsid = new Guid("4657278A-411B-11d2-839A-00C04FD918D0"); // CLSID_DragDropHelper
            var iid = typeof(IDropTargetHelper).GUID;
            if (ShellNative.CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out var helper) >= 0)
                _helper = (IDropTargetHelper)helper;
        }
        catch (Exception ex)
        {
            Log.Warn("The Shell's drag helper is not available; drops work without its drag image", ex);
        }
    }

    /// <summary>Registers this target on the window and on every window below it that has none yet.</summary>
    public void RegisterOn(nint window)
    {
        var all = new List<nint> { window };
        all.AddRange(ShellNative.Children(window));
        foreach (nint target in all)
        {
            if (ShellNative.GetProp(target, "OleDropTargetInterface") != IntPtr.Zero)
                continue;
            int hr = ShellNative.RegisterDragDrop(target, this);
            if (hr < 0)
                Log.Warn($"Registering the card's drop target on window {target:X} failed: 0x{hr:X8}");
        }
    }

    public int DragEnter(IDataObject dataObject, uint keyState, POINTL pt, ref uint effect)
    {
        _data = dataObject;
        _shown = null;
        _items = [];
        _accepting = false;
        var widget = _widget();
        if (widget is { AcceptsDrops: true })
        {
            try
            {
                _items = FolderActions.ReadDrop(dataObject);
                _accepting = _items.Count > 0;
            }
            catch (Exception ex)
            {
                Log.Warn("Reading a drag failed", ex);
            }
        }
        _allowed = (DropEffect)effect;
        _keys = DropKeys.FromOle(keyState);
        var point = pt;
        if (_accepting)
            _helper?.DragEnter(_top, dataObject, ref point, effect);
        effect = Answer(widget);
        return 0;
    }

    public int DragOver(uint keyState, POINTL pt, ref uint effect)
    {
        _keys = DropKeys.FromOle(keyState);
        var point = pt;
        effect = Answer(_widget());
        if (_accepting)
            _helper?.DragOver(ref point, effect);
        return 0;
    }

    private uint Answer(IDropWidget? widget)
    {
        if (!_accepting || widget is not { AcceptsDrops: true })
        {
            Hint(widget, null);
            return (uint)DropEffect.None;
        }
        var decisions = _items.Select(i => widget.DropPolicy.Decide(i.Entry, _keys, widget.DropFolderPath)).ToList();
        var verb = DropPolicy.Summarize(decisions);
        Hint(widget, verb == DropVerb.None ? null : verb);
        if (verb != _shown)
            _shown = verb;
        return (uint)DropPolicy.CursorEffect(decisions, _allowed);
    }

    private DropVerb? _described;

    private void Hint(IDropWidget? widget, DropVerb? verb)
    {
        widget?.ShowDropHint(verb);
        if (verb == _described || _data is null)
            return;
        _described = verb;
        if (verb is null || widget is null)
            return;
        // The label the Shell's drag image shows next to the cursor ("Move to Projects"), with the folder's name as the insert.
        var (type, message) = verb.Value switch
        {
            DropVerb.Move => (2, Strings.FolderDropMove("%1")),
            DropVerb.Copy => (1, Strings.FolderDropCopy("%1")),
            DropVerb.Shortcut => (4, Strings.FolderDropShortcut("%1")),
            _ => (6, Strings.FolderDropMixed("%1")),
        };
        SetDropDescription(_data, type, message, widget.DropFolderName);
    }

    public int DragLeave()
    {
        _helper?.DragLeave();
        _widget()?.ShowDropHint(null);
        Reset();
        return 0;
    }

    public int Drop(IDataObject dataObject, uint keyState, POINTL pt, ref uint effect)
    {
        var widget = _widget();
        var point = pt;
        if (!_accepting || widget is not { AcceptsDrops: true })
        {
            _helper?.DragLeave();
            widget?.ShowDropHint(null);
            effect = (uint)DropEffect.None;
            Reset();
            return 0;
        }
        // The key state at Drop has no mouse buttons; the modifiers of the last DragOver are what the label promised.
        var keys = _keys;
        var decisions = _items.Select(i => widget.DropPolicy.Decide(i.Entry, keys, widget.DropFolderPath)).ToList();
        var chosen = DropPolicy.CursorEffect(decisions, _allowed);
        _helper?.Drop(dataObject, ref point, (uint)chosen);
        widget.ShowDropHint(null);
        var items = _items;
        // The work (which may raise Explorer's own dialogs) starts once the source has been let go.
        _ui.TryEnqueue(() =>
        {
            try
            {
                widget.DropItems(items, keys);
            }
            catch (Exception ex)
            {
                Log.Warn("Carrying out a drop failed", ex);
            }
        });
        // Whatever moving there is, the target does it: tell the source nothing was moved, so it does not delete the original.
        effect = (uint)(chosen == DropEffect.Move ? DropEffect.None : chosen);
        Reset();
        return 0;
    }

    private void Reset()
    {
        _data = null;
        _items = [];
        _accepting = false;
        _shown = null;
        _described = null;
    }

    private static void SetDropDescription(IDataObject data, int imageType, string message, string insert)
    {
        try
        {
            const int Chars = 260;
            IntPtr handle = GlobalAlloc(0x0042 /* GMEM_MOVEABLE | GMEM_ZEROINIT */, (UIntPtr)(4 + Chars * 2 * 2));
            IntPtr ptr = ShellNative.GlobalLock(handle);
            Marshal.WriteInt32(ptr, imageType);
            WriteUnicode(ptr + 4, message, Chars);
            WriteUnicode(ptr + 4 + Chars * 2, insert, Chars);
            ShellNative.GlobalUnlock(handle);
            var etc = new FORMATETC
            {
                cfFormat = (short)ShellNative.RegisterClipboardFormat("DropDescription"),
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                ptd = IntPtr.Zero,
                tymed = TYMED.TYMED_HGLOBAL,
            };
            var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = handle, pUnkForRelease = null };
            data.SetData(ref etc, ref medium, true);
        }
        catch (Exception)
        {
            // A source that does not take a description simply shows no label next to the cursor; the card's own label remains.
        }
    }

    private static void WriteUnicode(IntPtr destination, string text, int capacity)
    {
        text = text.Length >= capacity ? text[..(capacity - 1)] : text;
        foreach (char c in text)
        {
            Marshal.WriteInt16(destination, (short)c);
            destination += 2;
        }
        Marshal.WriteInt16(destination, 0);
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
}
