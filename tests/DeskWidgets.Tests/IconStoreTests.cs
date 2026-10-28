using DeskWidgets.Desktop;

namespace DeskWidgets.Tests;

/// <summary>The icon arranger's file: a damaged one is kept aside before a new order replaces it, and one locked for a moment is read again.</summary>
public class IconStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(TestSetup.DataFolder, "icon-store-" + Guid.NewGuid().ToString("N"));

    public IconStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string LayoutFile => Path.Combine(_root, "desktop-icons.json");

    private string[] BadFiles => Directory.GetFiles(_root, "desktop-icons.bad-*.json");

    private IconLayoutSettings Load() => IconStore.LoadLayout(LayoutFile, Path.Combine(_root, "legacy"), takeOver: false, save: true);

    [Fact]
    public void ADamagedLayout_IsKeptAside_BeforeANewOrderReplacesIt()
    {
        const string Damaged = """{"Order":["a.lnk","b.l""";
        File.WriteAllText(LayoutFile, Damaged);

        Assert.Empty(Load().Order); // the arranger starts a new order, and its first save replaces the file

        Assert.Equal(Damaged, File.ReadAllText(Assert.Single(BadFiles))); // but the old order can still be got back
    }

    [Fact]
    public void OnlyTheNewestThreeDamagedLayouts_AreKept()
    {
        for (int i = 0; i < 4; i++)
            File.WriteAllText(Path.Combine(_root, $"desktop-icons.bad-20261020-00000{i}.json"), "older");
        File.WriteAllText(LayoutFile, "garbage");

        Load();

        Assert.Equal(3, BadFiles.Length);
        Assert.Contains(BadFiles, f => File.ReadAllText(f) == "garbage");
    }

    [Fact]
    public void AtStart_ALockedLayout_IsNotWaitedFor_AndNothingMaySaveOverIt()
    {
        File.WriteAllText(LayoutFile, """{"Order":["a.lnk","b.lnk"]}""");
        using (new FileStream(LayoutFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var layout = IconStore.TryLoadLayout(LayoutFile, Path.Combine(_root, "legacy"), takeOver: false, save: true, waits: []);
            Assert.Null(layout); // the arranger reads it later on its own thread, and saves nothing until then
            Assert.True(clock.ElapsedMilliseconds < 150, $"the UI thread waited {clock.ElapsedMilliseconds} ms"); // it used to sleep up to 850 ms
        }
        Assert.Empty(BadFiles); // locked is not damaged
        Assert.Equal(["a.lnk", "b.lnk"], IconStore.TryLoadLayout(LayoutFile, Path.Combine(_root, "legacy"), false, true, [])!.Order);
    }

    [Fact]
    public void ALayoutLockedForAMoment_IsReadAgain_NotStartedAfresh()
    {
        File.WriteAllText(LayoutFile, """{"Order":["a.lnk","b.lnk"]}""");
        var gate = new FileStream(LayoutFile, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Run(() =>
        {
            Thread.Sleep(150);
            gate.Dispose();
        });

        Assert.Equal(["a.lnk", "b.lnk"], Load().Order);
        Assert.Empty(BadFiles);
    }
}
