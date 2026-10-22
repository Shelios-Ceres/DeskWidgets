using DeskWidgets.Interop;
using DeskWidgets.PixelUi;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

/// <summary>The text store the IME and the emoji panel talk to, driven the way they drive it: take a lock, read, write.</summary>
public class TextServiceTests
{
    private const uint ReadWrite = 0x6, Read = 0x2;
    private const int NoLock = unchecked((int)0x80040201);
    private static readonly Guid SinkIid = new("22d44c94-a419-4542-a272-ae26093ececf");

    private sealed class Sink : TextService.ITextStoreACPSink
    {
        public Action<uint>? InLock;
        public readonly List<(int Start, int OldEnd, int NewEnd)> TextChanges = [];
        public int SelectionChanges;

        public int OnTextChange(uint flags, ref TextService.TS_TEXTCHANGE change)
        {
            TextChanges.Add((change.acpStart, change.acpOldEnd, change.acpNewEnd));
            return 0;
        }

        public int OnSelectionChange()
        {
            SelectionChanges++;
            return 0;
        }

        public int OnLayoutChange(int code, uint view) => 0;

        public int OnStatusChange(uint flags) => 0;

        public int OnAttrsChange(int start, int end, uint count, nint attributes) => 0;

        public int OnLockGranted(uint lockFlags)
        {
            InLock?.Invoke(lockFlags);
            return 0;
        }

        public int OnStartEditTransaction() => 0;

        public int OnEndEditTransaction() => 0;
    }

    private sealed class Fixture
    {
        public LineEditor Editor;
        public int Changed;
        public readonly Sink Sink = new();
        public readonly TextService.Store Store;

        public Fixture(string text, bool selectAll = false)
        {
            Editor = new LineEditor(text, selectAll);
            Store = new TextService.Store(0, () => Editor, () => default, () => Changed++);
            var iid = SinkIid;
            Assert.Equal(0, Store.AdviseSink(ref iid, Sink, 0x7));
        }

        /// <summary>Runs <paramref name="work"/> inside a lock the way the IME does.</summary>
        public void Locked(Action work, uint flags = ReadWrite)
        {
            Sink.InLock = _ => work();
            Assert.Equal(0, Store.RequestLock(flags, out int hr));
            Assert.Equal(0, hr);
            Sink.InLock = null;
        }

        public static nint Pin(string text, out System.Runtime.InteropServices.GCHandle handle)
        {
            handle = System.Runtime.InteropServices.GCHandle.Alloc(text.ToCharArray(), System.Runtime.InteropServices.GCHandleType.Pinned);
            return handle.AddrOfPinnedObject();
        }
    }

    [Fact]
    public void InsertTextAtSelection_ReplacesTheSelection_AndTheEditorFollows()
    {
        var f = new Fixture("report", selectAll: true);
        f.Locked(() =>
        {
            nint text = Fixture.Pin("你好", out var handle);
            try
            {
                Assert.Equal(0, f.Store.InsertTextAtSelection(0, text, 2, out int start, out int end, out var change));
                Assert.Equal((0, 2), (start, end));
                Assert.Equal((0, 6, 2), (change.acpStart, change.acpOldEnd, change.acpNewEnd));
            }
            finally
            {
                handle.Free();
            }
        });
        Assert.Equal("你好", f.Editor.Text);
        Assert.Equal(2, f.Editor.Caret);
        Assert.False(f.Editor.AllSelected);
        Assert.Equal(1, f.Changed);
        Assert.Empty(f.Sink.TextChanges); // what the IME did itself is not reported back
    }

    [Fact]
    public void AnEmoji_GoesInIntact_AndTheCaretStaysAfterThePair()
    {
        var f = new Fixture("ab");
        f.Locked(() =>
        {
            nint text = Fixture.Pin("😀", out var handle);
            try
            {
                Assert.Equal(0, f.Store.InsertTextAtSelection(0, text, 2, out _, out int end, out _));
                Assert.Equal(4, end);
            }
            finally
            {
                handle.Free();
            }
        });
        Assert.Equal("ab😀", f.Editor.Text);
        f.Editor.Left();
        Assert.Equal(2, f.Editor.Caret);
    }

    [Fact]
    public void SetText_ReplacesARange_AndReportsTheChange()
    {
        var f = new Fixture("ni'hao");
        f.Locked(() =>
        {
            nint text = Fixture.Pin("你好", out var handle);
            try
            {
                Assert.Equal(0, f.Store.SetText(0, 0, 6, text, 2, out var change));
                Assert.Equal((0, 6, 2), (change.acpStart, change.acpOldEnd, change.acpNewEnd));
            }
            finally
            {
                handle.Free();
            }
        });
        Assert.Equal("你好", f.Editor.Text);
    }

    [Fact]
    public void GetText_ReadsASliceOfTheField()
    {
        var f = new Fixture("a😀bc");
        string? read = null;
        f.Locked(() =>
        {
            var buffer = new char[8];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                Assert.Equal(0, f.Store.GetText(1, -1, handle.AddrOfPinnedObject(), 8, out uint returned, 0, 0, out _, out int next));
                read = new string(buffer, 0, (int)returned);
                Assert.Equal(5, next);
            }
            finally
            {
                handle.Free();
            }
        }, Read);
        Assert.Equal("😀bc", read);
    }

    [Fact]
    public void WithoutALock_ReadingAndWritingAreRefused()
    {
        var f = new Fixture("abc");
        Assert.Equal(NoLock, f.Store.GetEndACP(out _));
        Assert.Equal(NoLock, f.Store.GetSelection(0, 1, 0, out _));
        Assert.Equal(NoLock, f.Store.InsertTextAtSelection(0, 0, 0, out _, out _, out _));
        f.Locked(() => Assert.Equal(NoLock, f.Store.InsertTextAtSelection(0, 0, 0, out _, out _, out _)), Read); // a read lock cannot write
    }

    [Fact]
    public void TheWindowsOwnEdit_IsReportedToTheIme_AsTheRangeThatChanged()
    {
        var f = new Fixture("abc");
        f.Editor.Left();
        f.Editor.Insert("X");
        f.Store.Sync();
        Assert.Equal([(2, 2, 3)], f.Sink.TextChanges);
        Assert.Equal(1, f.Sink.SelectionChanges);
        f.Store.Sync();
        Assert.Single(f.Sink.TextChanges); // nothing new, nothing reported
    }

    [Fact]
    public void ADifferentField_IsReportedAsAllNewText()
    {
        var f = new Fixture("abc");
        f.Editor = new LineEditor("hello world");
        f.Store.Sync();
        Assert.Equal([(0, 3, 11)], f.Sink.TextChanges);
    }

    [Fact]
    public void TheWindowsEdit_IsNotReportedWhileTheImeHoldsTheLock()
    {
        var f = new Fixture("abc");
        f.Locked(() =>
        {
            f.Editor.Insert("Z");
            f.Store.Sync();
        });
        Assert.Empty(f.Sink.TextChanges);
    }

    [Fact]
    public void ASelectionOfTheWholeText_ShowsAsSelectAll()
    {
        var f = new Fixture("abc");
        f.Locked(() =>
        {
            nint selection = System.Runtime.InteropServices.Marshal.AllocHGlobal(16);
            try
            {
                System.Runtime.InteropServices.Marshal.WriteInt32(selection, 0, 0);
                System.Runtime.InteropServices.Marshal.WriteInt32(selection, 4, 3);
                Assert.Equal(0, f.Store.SetSelection(1, selection));
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(selection);
            }
        });
        Assert.True(f.Editor.AllSelected);
    }
}
