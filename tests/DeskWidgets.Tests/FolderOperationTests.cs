using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Tests;

/// <summary>What a Big Folder remembers about its items (the origin log, the manual order) through failed reads, missing folders and removals, and the shortcuts a drop fills in.</summary>
public class FolderOperationTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(TestSetup.DataFolder, "folder-ops-" + Guid.NewGuid().ToString("N"));

    public FolderOperationTests() => Directory.CreateDirectory(Folder);

    public void Dispose()
    {
        try
        {
            File.Delete(OriginLog.PathFor(Folder));
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Folder => Path.Combine(_root, "Projects");

    private FolderItem Item(string fileName) => new() { Path = Path.Combine(Folder, fileName), DisplayName = fileName, Kind = FolderItemKind.Other, AddedUtc = Day };

    private static string[] Names(IEnumerable<FolderItem> items) => items.Select(i => i.FileName).ToArray();

    /// <summary>An origin log on disk for the folder, as earlier drops from the desktop left it.</summary>
    private void SeedOrigins(params string[] names)
    {
        var log = new OriginLog();
        foreach (string name in names)
            log.Record(name, @"C:\Users\asus\Desktop\" + name, Day);
        log.Save(OriginLog.PathFor(Folder));
    }

    /// <summary>A model whose reads come from <paramref name="scan"/>; its results are applied at once, on the scanner's thread.</summary>
    private FolderModel Model(Func<List<FolderItem>> scan, Action persist, FolderSort sort = FolderSort.Newest, params string[] order)
        => new(Folder, sort, order, action => action(), persist, (_, _) => scan());

    /// <summary>Runs <paramref name="act"/> and waits for the model's next result.</summary>
    private static void AndWait(FolderModel model, Action act, int seconds = 5)
    {
        using var changed = new ManualResetEventSlim();
        void OnChanged() => changed.Set();
        model.Changed += OnChanged;
        try
        {
            act();
            Assert.True(changed.Wait(TimeSpan.FromSeconds(seconds)), "the model never showed a result");
        }
        finally
        {
            model.Changed -= OnChanged;
        }
    }

    // ----- a read that fails, a folder that goes missing -----

    [Fact]
    public void AFailedRead_KeepsTheItemsTheOriginsAndTheOrder_AndReadsAgainByItself()
    {
        SeedOrigins("a.txt", "b.txt");
        bool fail = false;
        int persisted = 0;
        using var model = Model(() => fail ? throw new IOException("the network blinked") : [Item("a.txt"), Item("b.txt")], () => persisted++, FolderSort.Manual, "b.txt", "a.txt");
        AndWait(model, model.Start);
        Assert.Equal(["b.txt", "a.txt"], Names(model.Items));

        fail = true;
        AndWait(model, model.Rescan);
        Assert.True(model.Exists);
        Assert.Equal(["b.txt", "a.txt"], Names(model.Items)); // a failed read is not an empty folder
        Assert.Equal(["b.txt", "a.txt"], model.Order);
        Assert.Equal(2, model.Origins.Count);
        Assert.Equal(2, OriginLog.Load(OriginLog.PathFor(Folder)).Count); // nothing empty was saved over the log
        Assert.Equal(0, persisted);

        // The watcher only hears of changes, so the model looks again by itself a few seconds later.
        fail = false;
        AndWait(model, () => { }, seconds: 10);
        Assert.Equal(["b.txt", "a.txt"], Names(model.Items));
    }

    [Fact]
    public void AMissingFolder_KeepsItsOriginsAndManualOrder_ForWhenItComesBack()
    {
        SeedOrigins("a.txt", "b.txt");
        int persisted = 0;
        using var model = Model(() => [Item("a.txt"), Item("b.txt")], () => persisted++, FolderSort.Manual, "b.txt", "a.txt");
        AndWait(model, model.Start);

        string away = Folder + " (away)";
        Directory.Move(Folder, away); // renamed elsewhere, or its drive unplugged
        AndWait(model, model.Rescan);
        Assert.False(model.Exists);
        Assert.Empty(model.Items);
        Assert.Equal(["b.txt", "a.txt"], model.Order);
        Assert.Equal(2, model.Origins.Count);
        Assert.Equal(2, OriginLog.Load(OriginLog.PathFor(Folder)).Count);

        Assert.True(SpinWait.SpinUntil(() => TryMove(away, Folder), TimeSpan.FromSeconds(5))); // and back
        AndWait(model, model.Rescan);
        Assert.True(model.Exists);
        Assert.Equal(["b.txt", "a.txt"], Names(model.Items)); // in the user's order, which was never rewritten
        Assert.Equal(0, persisted);
    }

    private static bool TryMove(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
            return true;
        }
        catch (IOException)
        {
            return false; // the watcher that was on it may still be letting go
        }
    }

    // ----- removals: only what is gone is forgotten -----

    [Fact]
    public void ARemovalTheUserPartlyCancelled_ForgetsOnlyTheItemsThatAreGone()
    {
        var log = new OriginLog();
        foreach (string name in new[] { "a.txt", "b.txt", "c.txt" })
            log.Record(name, @"C:\Users\asus\Desktop\" + name, Day);

        // Windows asked about b.txt (too big for the Recycle Bin) and the user said no: a.txt and c.txt went, b.txt stayed.
        Assert.True(FolderActions.ForgetGone(log, [Item("a.txt"), Item("b.txt"), Item("c.txt")], path => path.EndsWith("b.txt", StringComparison.Ordinal)));
        Assert.Null(log.OriginOf("a.txt"));
        Assert.Equal(@"C:\Users\asus\Desktop\b.txt", log.OriginOf("b.txt")); // "Put back" still works for it
        Assert.Null(log.OriginOf("c.txt"));

        // Cancelled outright (or a Put back whose conflict was skipped): everything is still there, and nothing is forgotten.
        Assert.False(FolderActions.ForgetGone(log, [Item("b.txt")], _ => true));
        Assert.Equal(1, log.Count);
    }

    // ----- the origin log is shared by the scanner's thread and the UI thread -----

    [Fact]
    public async Task TheOriginLog_CanBeReadAndSavedWhileItIsWritten()
    {
        var log = new OriginLog();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var writer = Task.Run(() =>
        {
            for (int i = 0; !stop.IsCancellationRequested; i++)
            {
                log.Record($"item {i % 2000}.txt", @"C:\Users\asus\Desktop\x.txt", Day);
                if (i % 2 == 0)
                    log.Remove($"item {(i + 1000) % 2000}.txt");
            }
        });
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                _ = log.AddedUtc("item 7.txt"); // what the scanner asks
                _ = log.ToJson(); // what a save does
            }
        });
        await Task.WhenAll(writer, reader); // an unguarded dictionary throws here ("Collection was modified") within a few milliseconds
    }

    // ----- the shortcuts a drop makes -----

    private static Dropped Drop(string path) => new(null!, new DropEntry(path, false, Path.GetFileName(path)));

    [Fact]
    public void AShortcutThatWasNotMade_DoesNotShiftTheOthersOntoTheWrongTargets()
    {
        var link = new Decision(DropEffect.Link, "program: new shortcut, the original stays");
        var links = new List<(string Path, Dropped Item, Decision Decision)>
        {
            (Path.Combine(Folder, "A.lnk"), Drop(@"D:\Apps\A.exe"), link),
            (Path.Combine(Folder, "B.lnk"), Drop(@"D:\Apps\B.exe"), link),
            (Path.Combine(Folder, "C.lnk"), Drop(@"D:\Apps\C.exe"), link),
        };
        // A.lnk was not made (a conflict the user skipped): only B and C exist.
        var created = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["B.lnk"] = Path.Combine(Folder, "B.lnk"),
            ["C.lnk"] = Path.Combine(Folder, "C.lnk"),
        };

        var matched = FolderActions.MatchCreated(links, created);

        Assert.Equal(new[] { ("B.lnk", "B.exe"), ("C.lnk", "C.exe") }, matched.Select(m => (Path.GetFileName(m.Created), Path.GetFileName(m.Item.Entry.Path!))));
    }

    [Fact]
    public void TheOperationsRecord_KeysEachNewItemByItsQueuedName_AndLeavesOutTheOnesThatFailed()
    {
        string made = Path.Combine(Folder, "B.lnk");
        File.WriteAllBytes(made, []);
        var sink = new OpSink();

        sink.PostNewItem(0, null!, "A.lnk", null!, 0x80, unchecked((int)0x80070050) /* the file exists */, null);
        sink.PostNewItem(0, null!, "B.lnk", null!, 0x80, 0, Shell.FromPath(made));

        Assert.False(sink.NewItems.ContainsKey("A.lnk"));
        Assert.Equal(made, sink.NewItems["B.lnk"], ignoreCase: true);
    }
}
