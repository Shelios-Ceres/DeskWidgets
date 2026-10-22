using System.Runtime.InteropServices;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Interop;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.PixelUi;

/// <summary>
/// Text Services Framework support for a window that draws its own text field (the Pixel Big Folder panel). Without it the
/// Pinyin IME never takes the keys of such a window, and the emoji panel (Win+.) and other text input UI have nowhere to put
/// text. The window gets a document whose text store is a <see cref="LineEditor"/>: the IME reads and writes the editor
/// through the store, and the window reports its own edits back with <see cref="Sync"/>. Composition text is shown in the
/// field like any other text. UI thread only.
/// </summary>
internal sealed class TextService : IDisposable
{
    private const uint TsLfSync = 0x1, TsLfReadWrite = 0x6, TsIasQueryOnly = 0x2, TsSsNoHiddenText = 0x8;
    private const int SOk = 0, EFail = unchecked((int)0x80004005), ENotImpl = unchecked((int)0x80004001), EInvalidArg = unchecked((int)0x80070057);
    private const int TsENoLock = unchecked((int)0x80040201), TsESynchronous = unchecked((int)0x80040208), TsSAsync = 0x00040300;
    private static readonly Guid TextStoreSinkIid = new("22d44c94-a419-4542-a272-ae26093ececf");

    private readonly nint _hwnd;
    private readonly Func<LineEditor> _editor;
    private readonly Func<Win32.RECT> _fieldRect;
    private readonly Action _changedByService;
    private readonly Store _store;
    private ITfThreadMgr? _threadManager;
    private ITfDocumentMgr? _document;
    private bool _activated;
    private bool _focusAssociated;

    /// <param name="editor">The field the typed text goes to now.</param>
    /// <param name="fieldRect">That field's rectangle on the screen, where the IME's candidate window belongs.</param>
    /// <param name="changedByService">The IME or the emoji panel changed the editor: redraw, and refresh whatever depends on the text.</param>
    public TextService(nint hwnd, Func<LineEditor> editor, Func<Win32.RECT> fieldRect, Action changedByService)
    {
        _hwnd = hwnd;
        _editor = editor;
        _fieldRect = fieldRect;
        _changedByService = changedByService;
        _store = new Store(hwnd, editor, fieldRect, changedByService);
        try
        {
            Start();
        }
        catch (Exception ex)
        {
            Log.Warn("Starting the text service of a Pixel window failed; the IME will not work there", ex);
            Dispose();
        }
    }

    public bool IsRunning => _document is not null;

    private void Start()
    {
        if (TF_GetThreadMgr(out var manager) < 0 || manager is null)
        {
            if (TF_CreateThreadMgr(out manager) < 0 || manager is null)
                throw new InvalidOperationException("No TSF thread manager");
        }
        _threadManager = manager;
        Check(_threadManager.Activate(out uint client), "Activate");
        _activated = true;
        Check(_threadManager.CreateDocumentMgr(out var document), "CreateDocumentMgr");
        _document = document;
        Check(document.CreateContext(client, 0, _store, out nint context, out _), "CreateContext");
        try
        {
            Check(document.Push(context), "Push");
        }
        finally
        {
            Marshal.Release(context);
        }
        Check(_threadManager.AssociateFocus(_hwnd, document, out var previous), "AssociateFocus");
        _focusAssociated = true;
        if (previous is not null)
            Marshal.ReleaseComObject(previous);
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0)
            throw new InvalidOperationException($"{what} failed: 0x{hr:X8}");
    }

    /// <summary>The window has the keyboard focus now: the document takes the text input.</summary>
    public void Focus()
    {
        if (_document is not null && _threadManager is not null)
            _threadManager.SetFocus(_document);
    }

    /// <summary>The window changed the field's text or selection (or the field changed): tell the IME, so it does not work from stale text.</summary>
    public void Sync() => _store.Sync();

    public void Dispose()
    {
        _store.Detach();
        try
        {
            if (_focusAssociated && _threadManager is not null)
            {
                if (_threadManager.AssociateFocus(_hwnd, null, out var previous) >= 0 && previous is not null)
                    Marshal.ReleaseComObject(previous);
                _focusAssociated = false;
            }
            if (_document is not null)
            {
                _document.Pop(1 /* TF_POPF_ALL */);
                Marshal.ReleaseComObject(_document);
                _document = null;
            }
            if (_activated && _threadManager is not null)
            {
                _threadManager.Deactivate();
                _activated = false;
            }
            if (_threadManager is not null)
            {
                Marshal.ReleaseComObject(_threadManager);
                _threadManager = null;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Stopping the text service of a Pixel window failed", ex);
        }
    }

    /// <summary>The clipboard's text as one line (line breaks and tabs become spaces, other control characters go), or "" when there is none.</summary>
    public static string ReadClipboardText()
    {
        const uint CfUnicodeText = 13;
        if (!OpenClipboard(0))
            return "";
        try
        {
            nint handle = GetClipboardData(CfUnicodeText);
            if (handle == 0)
                return "";
            nint pointer = GlobalLock(handle);
            if (pointer == 0)
                return "";
            try
            {
                string text = Marshal.PtrToStringUni(pointer) ?? "";
                var line = new System.Text.StringBuilder(text.Length);
                foreach (char c in text)
                {
                    if (c is '\r' or '\n' or '\t')
                        line.Append(' ');
                    else if (!char.IsControl(c))
                        line.Append(c);
                }
                return line.ToString();
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);

    // ----- the text store the IME talks to -----

    [ComVisible(true)]
    internal sealed class Store : ITextStoreACP, ITfContextOwnerCompositionSink
    {
        private readonly nint _hwnd;
        private readonly Func<LineEditor> _editor;
        private readonly Func<Win32.RECT> _fieldRect;
        private readonly Action _changedByService;
        private ITextStoreACPSink? _sink;
        private uint _lock; // the lock flags while the IME holds the lock, else 0
        private uint _pendingLock;
        private LineEditor? _shadowEditor;
        private string _shadowText = "";
        private (int Start, int End) _shadowSelection;

        public Store(nint hwnd, Func<LineEditor> editor, Func<Win32.RECT> fieldRect, Action changedByService)
        {
            _hwnd = hwnd;
            _editor = editor;
            _fieldRect = fieldRect;
            _changedByService = changedByService;
        }

        private LineEditor Editor => _editor();

        public void Detach()
        {
            _sink = null;
        }

        // The window's own edits: compare with what the IME last saw, tell it what changed.
        public void Sync()
        {
            if (_sink is null || _lock != 0)
                return;
            var editor = Editor;
            string text = editor.Text;
            var selection = editor.Selection;
            if (!ReferenceEquals(editor, _shadowEditor))
            {
                // Another field: the whole text is new.
                int old = _shadowText.Length;
                Remember(editor);
                Notify(new TS_TEXTCHANGE { acpStart = 0, acpOldEnd = old, acpNewEnd = text.Length }, selectionChanged: true);
                return;
            }
            if (!string.Equals(text, _shadowText, StringComparison.Ordinal))
            {
                int prefix = 0, limit = Math.Min(text.Length, _shadowText.Length);
                while (prefix < limit && text[prefix] == _shadowText[prefix])
                    prefix++;
                int suffix = 0;
                while (suffix < limit - prefix && text[^(suffix + 1)] == _shadowText[^(suffix + 1)])
                    suffix++;
                var change = new TS_TEXTCHANGE { acpStart = prefix, acpOldEnd = _shadowText.Length - suffix, acpNewEnd = text.Length - suffix };
                Remember(editor);
                Notify(change, selectionChanged: true);
            }
            else if (selection != _shadowSelection)
            {
                Remember(editor);
                Notify(null, selectionChanged: true);
            }
        }

        private void Remember(LineEditor editor)
        {
            _shadowEditor = editor;
            _shadowText = editor.Text;
            _shadowSelection = editor.Selection;
        }

        private void Notify(TS_TEXTCHANGE? change, bool selectionChanged)
        {
            try
            {
                if (change is { } c)
                    _sink?.OnTextChange(0, ref c);
                if (selectionChanged)
                    _sink?.OnSelectionChange();
            }
            catch (Exception ex)
            {
                Log.Warn("Telling the IME about an edit failed", ex);
            }
        }

        // ----- ITextStoreACP -----

        public int AdviseSink(ref Guid riid, object punk, uint mask)
        {
            if (riid != TextStoreSinkIid)
                return EInvalidArg;
            _sink = punk as ITextStoreACPSink;
            Remember(Editor);
            return _sink is null ? EInvalidArg : SOk;
        }

        public int UnadviseSink(object punk)
        {
            _sink = null;
            return SOk;
        }

        public int RequestLock(uint flags, out int hrSession)
        {
            hrSession = SOk;
            if (_sink is null)
            {
                hrSession = EFail;
                return SOk;
            }
            if (_lock != 0)
            {
                if ((flags & TsLfSync) != 0)
                {
                    hrSession = TsESynchronous;
                    return SOk;
                }
                _pendingLock = flags & TsLfReadWrite; // granted when the current lock ends
                hrSession = TsSAsync;
                return SOk;
            }
            _lock = flags & TsLfReadWrite;
            try
            {
                hrSession = _sink.OnLockGranted(flags);
            }
            finally
            {
                _lock = 0;
            }
            // Edits the IME made are in the shadow already; remember the selection it left.
            Remember(Editor);
            if (_pendingLock != 0 && _sink is not null)
            {
                uint pending = _pendingLock;
                _pendingLock = 0;
                _lock = pending;
                try
                {
                    _sink.OnLockGranted(pending);
                }
                finally
                {
                    _lock = 0;
                }
                Remember(Editor);
            }
            return SOk;
        }

        public int GetStatus(out TS_STATUS status)
        {
            status = new TS_STATUS { dwDynamicFlags = 0, dwStaticFlags = TsSsNoHiddenText };
            return SOk;
        }

        public int QueryInsert(int start, int end, uint cch, out int resultStart, out int resultEnd)
        {
            int length = Editor.Text.Length;
            resultStart = Math.Clamp(start, 0, length);
            resultEnd = Math.Clamp(end, resultStart, length);
            return SOk;
        }

        public int GetSelection(uint index, uint count, nint selection, out uint fetched)
        {
            fetched = 0;
            if ((_lock & 0x2) == 0)
                return TsENoLock;
            if (count == 0 || (index != 0 && index != uint.MaxValue))
                return SOk;
            var (start, end) = Editor.Selection;
            WriteSelection(selection, start, end);
            fetched = 1;
            return SOk;
        }

        private static void WriteSelection(nint target, int start, int end)
        {
            Marshal.WriteInt32(target, 0, start);
            Marshal.WriteInt32(target, 4, end);
            Marshal.WriteInt32(target, 8, 2 /* TS_AE_END */);
            Marshal.WriteInt32(target, 12, 0);
        }

        public int SetSelection(uint count, nint selection)
        {
            if ((_lock & 0x4) != 4)
                return TsENoLock;
            if (count == 0)
                return SOk;
            Editor.SelectRange(Marshal.ReadInt32(selection, 0), Marshal.ReadInt32(selection, 4));
            _changedByService();
            return SOk;
        }

        public int GetText(int start, int end, nint plain, uint cchRequested, out uint cchReturned, nint runInfo, uint runRequested, out uint runReturned, out int next)
        {
            cchReturned = 0;
            runReturned = 0;
            next = start;
            if ((_lock & 0x2) == 0)
                return TsENoLock;
            string text = Editor.Text;
            if (end < 0 || end > text.Length)
                end = text.Length;
            if (start < 0 || start > end)
                return EInvalidArg;
            int count = (int)Math.Min(end - start, cchRequested);
            if (count > 0 && plain != 0)
                Marshal.Copy(text.ToCharArray(start, count), 0, plain, count);
            cchReturned = (uint)count;
            next = start + count;
            if (runRequested > 0 && runInfo != 0 && count > 0)
            {
                Marshal.WriteInt32(runInfo, 0, count);
                Marshal.WriteInt32(runInfo, 4, 0 /* TS_RT_PLAIN */);
                runReturned = 1;
            }
            return SOk;
        }

        public int SetText(uint flags, int start, int end, nint text, uint cch, out TS_TEXTCHANGE change)
        {
            change = default;
            if ((_lock & 0x4) != 4)
                return TsENoLock;
            var editor = Editor;
            int length = editor.Text.Length;
            if (end < 0 || end > length)
                end = length;
            if (start < 0 || start > end)
                return EInvalidArg;
            string inserted = ReadText(text, cch);
            editor.Replace(start, end, inserted);
            change = new TS_TEXTCHANGE { acpStart = start, acpOldEnd = end, acpNewEnd = start + inserted.Length };
            Changed();
            return SOk;
        }

        public int GetFormattedText(int start, int end, out nint dataObject)
        {
            dataObject = 0;
            return ENotImpl;
        }

        public int GetEmbedded(int pos, ref Guid service, ref Guid riid, out nint punk)
        {
            punk = 0;
            return ENotImpl;
        }

        public int QueryInsertEmbedded(nint service, nint formatEtc, out int insertable)
        {
            insertable = 0;
            return SOk;
        }

        public int InsertEmbedded(uint flags, int start, int end, nint dataObject, out TS_TEXTCHANGE change)
        {
            change = default;
            return ENotImpl;
        }

        public int InsertTextAtSelection(uint flags, nint text, uint cch, out int start, out int end, out TS_TEXTCHANGE change)
        {
            start = end = 0;
            change = default;
            var editor = Editor;
            var (selectionStart, selectionEnd) = editor.Selection;
            if ((flags & TsIasQueryOnly) != 0)
            {
                if ((_lock & 0x2) == 0)
                    return TsENoLock;
                start = selectionStart;
                end = selectionEnd;
                return SOk;
            }
            if ((_lock & 0x4) != 4)
                return TsENoLock;
            string inserted = ReadText(text, cch);
            editor.Replace(selectionStart, selectionEnd, inserted);
            start = selectionStart;
            end = selectionStart + inserted.Length;
            change = new TS_TEXTCHANGE { acpStart = selectionStart, acpOldEnd = selectionEnd, acpNewEnd = end };
            Changed();
            return SOk;
        }

        public int InsertEmbeddedAtSelection(uint flags, nint dataObject, out int start, out int end, out TS_TEXTCHANGE change)
        {
            start = end = 0;
            change = default;
            return ENotImpl;
        }

        public int RequestSupportedAttrs(uint flags, uint count, nint attributes) => SOk;

        public int RequestAttrsAtPosition(int position, uint count, nint attributes, uint flags) => SOk;

        public int RequestAttrsTransitioningAtPosition(int position, uint count, nint attributes, uint flags) => SOk;

        public int FindNextAttrTransition(int start, int halt, uint count, nint attributes, uint flags, out int next, out int found, out int offset)
        {
            next = halt;
            found = 0;
            offset = 0;
            return SOk;
        }

        public int RetrieveRequestedAttrs(uint count, nint values, out uint fetched)
        {
            fetched = 0;
            return SOk;
        }

        public int GetEndACP(out int acp)
        {
            acp = 0;
            if ((_lock & 0x2) == 0)
                return TsENoLock;
            acp = Editor.Text.Length;
            return SOk;
        }

        public int GetActiveView(out uint view)
        {
            view = 0;
            return SOk;
        }

        public int GetACPFromPoint(uint view, ref Win32.POINT point, uint flags, out int acp)
        {
            acp = Editor.Text.Length;
            return SOk;
        }

        public int GetTextExt(uint view, int start, int end, out Win32.RECT rect, out int clipped)
        {
            rect = _fieldRect();
            clipped = 0;
            return SOk;
        }

        public int GetScreenExt(uint view, out Win32.RECT rect)
        {
            Native.GetWindowRect(_hwnd, out rect);
            return SOk;
        }

        public int GetWnd(uint view, out nint hwnd)
        {
            hwnd = _hwnd;
            return SOk;
        }

        // ----- ITfContextOwnerCompositionSink -----

        public int OnStartComposition(nint composition, out int ok)
        {
            ok = 1;
            return SOk;
        }

        public int OnUpdateComposition(nint composition, nint newRange) => SOk;

        public int OnEndComposition(nint composition) => SOk;

        // ----- helpers -----

        private static string ReadText(nint text, uint cch) => text == 0 || cch == 0 ? "" : Marshal.PtrToStringUni(text, (int)cch);

        /// <summary>The IME changed the editor inside its lock: the shadow follows at once so the window's own sync does not report it back.</summary>
        private void Changed()
        {
            Remember(Editor);
            _changedByService();
        }
    }

    // ----- interop -----

    [StructLayout(LayoutKind.Sequential)]
    internal struct TS_STATUS
    {
        public uint dwDynamicFlags;
        public uint dwStaticFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TS_TEXTCHANGE
    {
        public int acpStart;
        public int acpOldEnd;
        public int acpNewEnd;
    }

    [ComImport, Guid("28888fe3-c2a0-483a-a3ea-8cb1ce51ff3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITextStoreACP
    {
        [PreserveSig] int AdviseSink(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] object punk, uint mask);
        [PreserveSig] int UnadviseSink([MarshalAs(UnmanagedType.IUnknown)] object punk);
        [PreserveSig] int RequestLock(uint lockFlags, out int hrSession);
        [PreserveSig] int GetStatus(out TS_STATUS status);
        [PreserveSig] int QueryInsert(int start, int end, uint cch, out int resultStart, out int resultEnd);
        [PreserveSig] int GetSelection(uint index, uint count, nint selection, out uint fetched);
        [PreserveSig] int SetSelection(uint count, nint selection);
        [PreserveSig] int GetText(int start, int end, nint plain, uint cchRequested, out uint cchReturned, nint runInfo, uint runRequested, out uint runReturned, out int next);
        [PreserveSig] int SetText(uint flags, int start, int end, nint text, uint cch, out TS_TEXTCHANGE change);
        [PreserveSig] int GetFormattedText(int start, int end, out nint dataObject);
        [PreserveSig] int GetEmbedded(int position, ref Guid service, ref Guid riid, out nint punk);
        [PreserveSig] int QueryInsertEmbedded(nint service, nint formatEtc, out int insertable);
        [PreserveSig] int InsertEmbedded(uint flags, int start, int end, nint dataObject, out TS_TEXTCHANGE change);
        [PreserveSig] int InsertTextAtSelection(uint flags, nint text, uint cch, out int start, out int end, out TS_TEXTCHANGE change);
        [PreserveSig] int InsertEmbeddedAtSelection(uint flags, nint dataObject, out int start, out int end, out TS_TEXTCHANGE change);
        [PreserveSig] int RequestSupportedAttrs(uint flags, uint count, nint attributes);
        [PreserveSig] int RequestAttrsAtPosition(int position, uint count, nint attributes, uint flags);
        [PreserveSig] int RequestAttrsTransitioningAtPosition(int position, uint count, nint attributes, uint flags);
        [PreserveSig] int FindNextAttrTransition(int start, int halt, uint count, nint attributes, uint flags, out int next, out int found, out int offset);
        [PreserveSig] int RetrieveRequestedAttrs(uint count, nint values, out uint fetched);
        [PreserveSig] int GetEndACP(out int acp);
        [PreserveSig] int GetActiveView(out uint view);
        [PreserveSig] int GetACPFromPoint(uint view, ref Win32.POINT point, uint flags, out int acp);
        [PreserveSig] int GetTextExt(uint view, int start, int end, out Win32.RECT rect, out int clipped);
        [PreserveSig] int GetScreenExt(uint view, out Win32.RECT rect);
        [PreserveSig] int GetWnd(uint view, out nint hwnd);
    }

    [ComImport, Guid("22d44c94-a419-4542-a272-ae26093ececf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITextStoreACPSink
    {
        [PreserveSig] int OnTextChange(uint flags, ref TS_TEXTCHANGE change);
        [PreserveSig] int OnSelectionChange();
        [PreserveSig] int OnLayoutChange(int code, uint view);
        [PreserveSig] int OnStatusChange(uint flags);
        [PreserveSig] int OnAttrsChange(int start, int end, uint count, nint attributes);
        [PreserveSig] int OnLockGranted(uint lockFlags);
        [PreserveSig] int OnStartEditTransaction();
        [PreserveSig] int OnEndEditTransaction();
    }

    [ComImport, Guid("5F20AA40-B57A-4F34-96AB-3576F377CC79"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfContextOwnerCompositionSink
    {
        [PreserveSig] int OnStartComposition(nint composition, out int ok);
        [PreserveSig] int OnUpdateComposition(nint composition, nint newRange);
        [PreserveSig] int OnEndComposition(nint composition);
    }

    [ComImport, Guid("aa80e801-2021-11d2-93e0-0060b067b86e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfThreadMgr
    {
        [PreserveSig] int Activate(out uint clientId);
        [PreserveSig] int Deactivate();
        [PreserveSig] int CreateDocumentMgr(out ITfDocumentMgr document);
        [PreserveSig] int EnumDocumentMgrs(out nint enumerator);
        [PreserveSig] int GetFocus(out ITfDocumentMgr? document);
        [PreserveSig] int SetFocus(ITfDocumentMgr document);
        [PreserveSig] int AssociateFocus(nint hwnd, ITfDocumentMgr? document, out ITfDocumentMgr? previous);
        [PreserveSig] int IsThreadFocus(out int focused);
        [PreserveSig] int GetFunctionProvider(ref Guid clsid, out nint provider);
        [PreserveSig] int EnumFunctionProviders(out nint enumerator);
        [PreserveSig] int GetGlobalCompartment(out nint compartmentManager);
    }

    [ComImport, Guid("aa80e7f4-2021-11d2-93e0-0060b067b86e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfDocumentMgr
    {
        [PreserveSig] int CreateContext(uint client, uint flags, [MarshalAs(UnmanagedType.IUnknown)] object? punk, out nint context, out uint editCookie);
        [PreserveSig] int Push(nint context);
        [PreserveSig] int Pop(uint flags);
        [PreserveSig] int GetTop(out nint context);
        [PreserveSig] int GetBase(out nint context);
        [PreserveSig] int EnumContexts(out nint enumerator);
    }

    [DllImport("msctf.dll")]
    private static extern int TF_GetThreadMgr(out ITfThreadMgr? manager);

    [DllImport("msctf.dll")]
    private static extern int TF_CreateThreadMgr(out ITfThreadMgr? manager);
}
