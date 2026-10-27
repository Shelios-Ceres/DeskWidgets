using DeskWidgets.Hosting;

namespace DeskWidgets.Tests;

/// <summary>Phase 13.1: the snapshot rules, recovery from a damaged settings.json, and a save that fails.</summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deskwidgets-settings-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    public SettingsStoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private SettingsStore Store() => new(_folder, () => _now);

    private string SettingsFile => Path.Combine(_folder, "settings.json");

    private string Backups => Path.Combine(_folder, "backups");

    private static void Add(SettingsStore store, string type) => store.Current.Widgets.Add(new WidgetPlacement { Type = type });

    // ----- the rules -----

    [Fact]
    public void ASnapshotsName_RoundTripsItsTime()
    {
        var time = new DateTime(2026, 10, 24, 9, 5, 7, DateTimeKind.Utc);
        string name = SettingsBackups.NameFor(time);
        Assert.Equal("settings-20261024-090507.json", name);
        Assert.True(SettingsBackups.TryParse(name, out var parsed));
        Assert.Equal(time, parsed);
        Assert.False(SettingsBackups.TryParse("settings.json", out _));
        Assert.False(SettingsBackups.TryParse("settings-bad.json", out _));
    }

    [Fact]
    public void ASnapshotIsDue_OnlyAnHourAfterTheLast()
    {
        var last = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(SettingsBackups.Due(null, last));
        Assert.False(SettingsBackups.Due(last, last.AddMinutes(59)));
        Assert.True(SettingsBackups.Due(last, last.AddMinutes(60)));
    }

    [Fact]
    public void Pruning_KeepsTheNewestTen_AndTheNewestOfEachOfTheLastSevenDays()
    {
        var now = new DateTime(2026, 10, 24, 23, 0, 0, DateTimeKind.Utc);
        var all = new List<DateTime>();
        for (int hour = 0; hour < 30; hour++)
            all.Add(now.AddHours(-hour)); // 30 hourly snapshots: today, and into yesterday
        for (int day = 2; day <= 12; day++)
        {
            all.Add(now.Date.AddDays(-day).AddHours(8));
            all.Add(now.Date.AddDays(-day).AddHours(20));
        }
        var delete = SettingsBackups.ToDelete(all, now);
        var kept = all.Except(delete).ToList();
        Assert.All(all.OrderByDescending(t => t).Take(10), t => Assert.Contains(t, kept)); // the newest ten
        for (int back = 0; back < SettingsBackups.Days; back++)
        {
            var day = now.Date.AddDays(-back);
            var newestOfDay = all.Where(t => t.Date == day).Max();
            Assert.Contains(newestOfDay, kept); // one per day for a week
        }
        Assert.DoesNotContain(now.Date.AddDays(-9).AddHours(8), kept); // older than a week: gone
        Assert.InRange(kept.Count, 10, 10 + SettingsBackups.Days);
    }

    // ----- the store -----

    [Fact]
    public void AGoodFile_IsKeptAsASnapshot_WhenTheAppStarts_ButNotTwiceInARow()
    {
        var first = Store();
        Add(first, "clock");
        Assert.True(first.Save());
        first.StartSession();
        Assert.Single(Directory.GetFiles(Backups));

        _now = _now.AddMinutes(5);
        var second = Store(); // unchanged file: the newest snapshot is the same
        second.StartSession();
        Assert.Single(Directory.GetFiles(Backups));

        Add(second, "battery");
        _now = _now.AddMinutes(5);
        Assert.True(second.Save()); // changed, but a snapshot was made 5 minutes ago
        Assert.Single(Directory.GetFiles(Backups));

        _now = _now.AddHours(2);
        Add(second, "worldclock");
        Assert.True(second.Save()); // an hour has passed: a new one, of the new state
        Assert.Equal(2, Directory.GetFiles(Backups).Length);
    }

    [Fact]
    public void ADamagedFile_LoadsTheNewestSnapshotThatParses_AndKeepsTheBadFile()
    {
        var store = Store();
        Add(store, "clock");
        Add(store, "battery");
        store.Save();
        store.StartSession();
        _now = _now.AddHours(3);
        Add(store, "worldclock");
        store.Save(); // a newer snapshot with three widgets
        File.WriteAllText(Path.Combine(Backups, SettingsBackups.NameFor(_now.AddHours(1))), "{ not json"); // the newest one is itself damaged

        File.WriteAllText(SettingsFile, "{ \"Widgets\": [ { \"Type\": \"clo"); // truncated, as a power cut leaves it
        _now = _now.AddHours(2);
        var recovered = Store();

        Assert.Equal(SettingsRecoveryKind.FromBackup, recovered.Recovery.Kind);
        Assert.Equal(3, recovered.Current.Widgets.Count);
        Assert.NotNull(recovered.Recovery.BadFile);
        Assert.Contains("clo", File.ReadAllText(Path.Combine(_folder, recovered.Recovery.BadFile!)));
        Assert.Equal(3, Store().Current.Widgets.Count); // settings.json itself is good again
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("null")]
    public void AnEmptyFile_IsRecoveredLikeADamagedOne(string content)
    {
        var store = Store();
        Add(store, "clock");
        store.Save();
        store.StartSession();
        File.WriteAllText(SettingsFile, content);
        var recovered = Store();
        Assert.Equal(SettingsRecoveryKind.FromBackup, recovered.Recovery.Kind);
        Assert.Single(recovered.Current.Widgets);
    }

    [Fact]
    public void WithNoUsableBackup_ItStartsFresh_AndSaysSo()
    {
        File.WriteAllText(SettingsFile, "garbage");
        var store = Store();
        Assert.Equal(SettingsRecoveryKind.Fresh, store.Recovery.Kind);
        Assert.Empty(store.Current.Widgets);
        Assert.False(store.Current.Initialized);
        Assert.Equal("garbage", File.ReadAllText(Path.Combine(_folder, store.Recovery.BadFile!)));
    }

    [Fact]
    public void AMissingFile_WithBackupsBesideIt_IsPutBack_AndSaysSo()
    {
        var store = Store();
        Add(store, "clock");
        Add(store, "battery");
        store.Save();
        store.StartSession(); // a snapshot with two widgets
        File.Delete(SettingsFile); // a cleaner, or a hand, removed it
        _now = _now.AddDays(1);

        var restored = Store();

        Assert.Equal(SettingsRecoveryKind.Missing, restored.Recovery.Kind);
        Assert.Equal(2, restored.Current.Widgets.Count);
        Assert.Null(restored.Recovery.BadFile);
        Assert.True(File.Exists(SettingsFile)); // put back on disk too
        Assert.Equal(SettingsRecoveryKind.None, Store().Recovery.Kind);
    }

    [Fact]
    public void Peek_ReadsTheSettings_WithoutChangingAnyFile()
    {
        var store = Store();
        Add(store, "clock");
        store.Save();
        store.StartSession(); // a snapshot with one widget
        const string Damaged = "{ \"Widgets\": [ { \"Type\": \"clo";
        File.WriteAllText(SettingsFile, Damaged); // damaged while the app runs
        var before = Directory.GetFiles(_folder, "*", SearchOption.AllDirectories).Order().ToList();

        var peeked = SettingsStore.Peek(_folder);

        Assert.Single(peeked.Widgets); // the newest snapshot that reads
        Assert.Equal(before, Directory.GetFiles(_folder, "*", SearchOption.AllDirectories).Order().ToList()); // no bad file kept, nothing put back
        Assert.Equal(Damaged, File.ReadAllText(SettingsFile));

        string nowhere = Path.Combine(_folder, "nowhere");
        Assert.Empty(SettingsStore.Peek(nowhere).Widgets);
        Assert.False(Directory.Exists(nowhere));
    }

    [Fact]
    public void ANormalStart_AndAFirstRun_NeedNoNotice()
    {
        Assert.Equal(SettingsRecoveryKind.None, Store().Recovery.Kind); // no file yet
        var store = Store();
        Add(store, "clock");
        store.Save();
        Assert.Equal(SettingsRecoveryKind.None, Store().Recovery.Kind);
    }

    [Fact]
    public void OnlyTheNewestThreeBadFiles_AreKept()
    {
        for (int i = 0; i < 5; i++)
        {
            File.WriteAllText(SettingsFile, "garbage " + i);
            _now = _now.AddMinutes(1);
            Store();
        }
        Assert.Equal(3, Directory.GetFiles(_folder, "settings.bad-*.json").Length);
    }

    [Theory]
    [InlineData(0x80070070, "the disk is full")]
    [InlineData(0x80070027, "the disk is full")]
    [InlineData(0x80070005, "access denied")]
    [InlineData(0x80070020, "the file is in use")]
    [InlineData(0x80070021, "the file is in use")]
    [InlineData(0x80070057, "error 0x80070057")]
    public void TheReasonOfAFailedSave_IsShortAndPlain(uint hresult, string expected)
        => Assert.Equal(expected, Strings.SaveFailureReason(new IOException("long message with a path", unchecked((int)hresult))));

    [Fact]
    public void AnAccessDeniedException_IsAccessDenied() => Assert.Equal("access denied", Strings.SaveFailureReason(new UnauthorizedAccessException("path")));

    [Fact]
    public void AFailedSave_IsReported_AndTheOldFileStaysWhole()
    {
        var store = Store();
        Add(store, "clock");
        Assert.True(store.Save());
        string before = File.ReadAllText(SettingsFile);

        Add(store, "battery");
        using (new FileStream(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None)) // held open: the move over it fails
        {
            Assert.False(store.Save());
            Assert.NotNull(store.LastError);
        }
        Assert.Equal(before, File.ReadAllText(SettingsFile));
        Assert.True(store.Save()); // the next try works
        Assert.Null(store.LastError);
        Assert.Equal(2, Store().Current.Widgets.Count);
    }

    // ----- a file that is locked for a while is not a damaged file -----

    private SettingsStore Store(params TimeSpan[] delays) => new(_folder, () => _now, delays);

    private static TimeSpan[] Waits(int tries, int ms) => Enumerable.Repeat(TimeSpan.FromMilliseconds(ms), tries).ToArray();

    [Fact]
    public void AFileLockedForAMoment_IsReadAgain_NotReplacedByABackup()
    {
        var first = Store();
        Add(first, "clock");
        first.Save();
        first.StartSession(); // a snapshot with one widget
        Add(first, "battery");
        Add(first, "worldclock");
        first.Save(); // the file has three
        _now = _now.AddMinutes(1);

        var gate = new FileStream(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Run(() =>
        {
            Thread.Sleep(250);
            gate.Dispose();
        });
        var loaded = Store(Waits(10, 100));

        Assert.Equal(SettingsRecoveryKind.None, loaded.Recovery.Kind);
        Assert.Equal(3, loaded.Current.Widgets.Count); // the file itself, not the one-widget snapshot
        Assert.Empty(Directory.GetFiles(_folder, "settings.bad-*.json"));
    }

    [Fact]
    public void AFileThatCannotBeReadAtAll_IsLeftAlone_AndSavesAreHeldUntilItCan()
    {
        var first = Store();
        Add(first, "clock");
        first.Save();
        first.StartSession(); // a snapshot with one widget
        Add(first, "battery");
        Add(first, "worldclock");
        first.Save(); // the file has three
        string original = File.ReadAllText(SettingsFile);
        _now = _now.AddMinutes(1);

        var gate = new FileStream(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None);
        var held = Store(Waits(2, 10));
        Assert.Equal(SettingsRecoveryKind.Held, held.Recovery.Kind);
        Assert.Single(held.Current.Widgets); // the snapshot, in memory
        Assert.Empty(Directory.GetFiles(_folder, "settings.bad-*.json")); // nothing is called damaged

        Add(held, "folder");
        Assert.False(held.Save()); // held
        Assert.NotNull(held.LastError);
        gate.Dispose();
        Assert.Equal(original, File.ReadAllText(SettingsFile)); // never written over

        Assert.True(held.Save()); // it reads again: the old content is kept aside, then ours is saved
        string kept = Assert.Single(Directory.GetFiles(_folder, "settings.kept-*.json"));
        Assert.Equal(original, File.ReadAllText(kept));
        Assert.Equal(2, Store().Current.Widgets.Count);
    }

    [Fact]
    public void AHeldSave_TriesOnce_SoTheUiThreadNeverWaits_ButTheLastSaveIsPatient()
    {
        var first = Store();
        Add(first, "clock");
        first.Save();
        first.StartSession(); // a snapshot with one widget
        _now = _now.AddMinutes(1);

        var gate = new FileStream(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None);
        var held = Store(Waits(2, 10));
        Assert.Equal(SettingsRecoveryKind.Held, held.Recovery.Kind);
        Add(held, "folder");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(held.Save()); // the save timer's tick, on the UI thread
        Assert.True(clock.ElapsedMilliseconds < 150, $"an ordinary held save waited {clock.ElapsedMilliseconds} ms"); // it used to sleep 400 ms

        // The last save (exit, sign-out) still waits a moment for a lock that is about to go.
        _ = Task.Run(() =>
        {
            Thread.Sleep(150);
            gate.Dispose();
        });
        Assert.True(held.Save(patient: true));
        Assert.Equal(2, Store().Current.Widgets.Count);
    }

    // ----- a placement -----

    [Fact]
    public void ACardThatStaysWhereItIs_HasNothingToSave()
    {
        var placement = new WidgetPlacement { X = 120, Y = 40 };
        Assert.False(placement.MoveTo(120, 40)); // a re-snap after a display change that moved nothing
        Assert.True(placement.MoveTo(240, 40));
        Assert.Equal((240, 40), (placement.X, placement.Y));
    }
}
