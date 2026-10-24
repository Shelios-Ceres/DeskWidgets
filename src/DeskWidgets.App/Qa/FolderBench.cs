using System.Diagnostics;
using DeskWidgets.Hosting;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI.Dispatching;

namespace DeskWidgets.Qa;

/// <summary>
/// Phase 13.3: how a Big Folder card and its panel behave with a large folder (<c>--motion-qa=&lt;folder&gt; --motion-run=bench-glass</c> or
/// <c>bench-pixel</c>, in a test instance whose one Big Folder card shows a scratch folder of 1,000, 5,000 or 20,000 items). Each stage
/// reports how long the call took, the longest stretch the UI thread was blocked while it settled, the pictures asked of the Shell, and
/// the process's memory and handles.
/// </summary>
internal static partial class MotionQa
{
    /// <summary>Notes the longest gap between ticks of a fast UI-thread timer: how long the UI thread was blocked.</summary>
    private sealed class StallMeter
    {
        private readonly DispatcherQueueTimer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _last;

        public StallMeter(DispatcherQueue queue)
        {
            _timer = queue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(4);
            _timer.Tick += (_, _) =>
            {
                double now = _clock.Elapsed.TotalMilliseconds;
                Max = Math.Max(Max, now - _last);
                _last = now;
            };
            _last = _clock.Elapsed.TotalMilliseconds;
            _timer.Start();
        }

        public double Max { get; private set; }

        public void Reset()
        {
            Max = 0;
            _last = _clock.Elapsed.TotalMilliseconds;
        }

        public void Stop() => _timer.Stop();
    }

    private static string Memory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var self = Process.GetCurrentProcess();
        var (handles, gdi, user) = Counts();
        return $"working set {self.WorkingSet64 / 1048576} MB, private {self.PrivateMemorySize64 / 1048576} MB, managed {GC.GetTotalMemory(true) / 1048576} MB, handles {handles}, GDI {gdi}, USER {user}";
    }

    private static async Task Bench(WidgetHost host, WidgetTheme theme)
    {
        host.SetTheme(theme);
        var queue = DispatcherQueue.GetForCurrentThread();
        var meter = new StallMeter(queue);
        await Task.Delay(4000); // the card has scanned and drawn
        var window = host.Windows.First(w => w.FolderWidget is not null);
        var folder = window.FolderWidget!;
        string path = folder.Model.Path;
        Line($"## Large folder, {theme}: {folder.Model.Items.Count} items in {path}");
        Line($"- at rest, card up: {Memory()}");

        async Task Stage(string label, Action act, int settleMs = 1200)
        {
            meter.Reset();
            int icons = IconService.RequestsStarted;
            var watch = Stopwatch.StartNew();
            act();
            double call = watch.Elapsed.TotalMilliseconds;
            await Task.Delay(settleMs);
            Line($"- {label}: the call took {call:0.0} ms; longest UI stall {meter.Max:0} ms; pictures asked of the Shell +{IconService.RequestsStarted - icons}");
        }

        // A second model on the same folder: the scan on its worker, then Apply on the UI thread (sort, origin prune, watcher).
        meter.Reset();
        var done = new TaskCompletionSource();
        var model = new FolderModel(path, FolderSort.Newest, [], queue, () => { });
        model.Changed += () => done.TrySetResult();
        var scanWatch = Stopwatch.StartNew();
        model.Start();
        await done.Task;
        Line($"- a new model: scan and first Changed took {scanWatch.Elapsed.TotalMilliseconds:0} ms; longest UI stall {meter.Max:0} ms");
        model.Dispose();
        await Task.Delay(500);

        await Stage("sort Name (card only)", () => folder.Model.SetSort(FolderSort.Name));
        await Stage("sort Newest (card only)", () => folder.Model.SetSort(FolderSort.Newest));

        IFolderPanel? panel = null;
        await Stage("the panel opens", () => panel = folder.OpenPanel(), 2500);
        Line($"- panel up: {Memory()}");
        int count = folder.Model.Items.Count;
        foreach (var (label, index) in new[] { ("scroll to the middle", count / 2), ("scroll to the end", count - 1), ("scroll back to the top", 0) })
            await Stage(label, () => panel!.ScrollToItemForBench(index), 800);

        foreach (string text in new[] { "P", "Ph", "Pho", "Phot", "Photo", "Photo ", "Photo 1", "Photo", "Pho", "Ph", "P", "" })
            await Stage($"search \"{text}\" (the call filters three times)", () => panel!.PrepareForCapture(new FolderCapture(Search: text)), 300);
        foreach (string text in new[] { "b", "bz", "bizhi", "bqb", "" })
            await Stage($"pinyin search \"{text}\"", () => panel!.PrepareForCapture(new FolderCapture(Search: text)), 300);

        await Stage("sort Name (panel open)", () => folder.Model.SetSort(FolderSort.Name), 1500);
        await Stage("sort Type (panel open)", () => folder.Model.SetSort(FolderSort.Type), 1500);
        await Stage("sort Newest (panel open)", () => folder.Model.SetSort(FolderSort.Newest), 1500);

        await Stage("the panel closes", () => folder.ClosePanelForCapture(), 1500);
        Line($"- panel closed: {Memory()}");
        meter.Stop();
    }

    /// <summary>Phase 13.1: a change made just before the process quits is written by the flush (no 600 ms wait). The next start reads it back.</summary>
    private static Task FlushCheck(WidgetHost host)
    {
        Line("## flush on exit: adding a widget and quitting at once");
        host.AddForCapture(WidgetCatalog.Find("clock")!, 700, 700);
        Line("added; the app quits right after this line");
        return Task.CompletedTask;
    }

    /// <summary>Phase 13.1: with settings.json read-only, saves fail; after three tries Windows is told (the log says so), and a save works again once it is writable.</summary>
    private static async Task SaveFails(WidgetHost host)
    {
        string file = Path.Combine(Core.DataRoot.Path, "settings.json");
        Line("## saving fails: settings.json made read-only");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        host.AddForCapture(WidgetCatalog.Find("clock")!, 700, 700);
        await Task.Delay(14000);
        File.SetAttributes(file, FileAttributes.Normal);
        Line("read-only removed; waiting for the next retry");
        await Task.Delay(7000);
        Line("done waiting; see the log for 'Saving settings failed' and 'notice shown'");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EndMenu();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    /// <summary>Phase 13.4: Windows' own context menu for a file, three files, a folder and a shortcut: what it lists, that it shows and is dismissed, and that a command runs.</summary>
    private static async Task MoreOptions(WidgetHost host)
    {
        Line("## Show more options (Windows' own context menu)");
        string dir = Path.GetFullPath("D:/Widget/.scratch/bench/n1000");
        var files = Directory.GetFiles(dir, "*.txt").Take(3).ToList();
        string folder = Directory.GetDirectories(dir).First();
        string shortcut = Directory.GetFiles(dir, "*.lnk").First();
        var cases = new (string Label, List<string> Paths)[] { ("one file", [files[0]]), ("three files", files), ("a folder", [folder]), ("a shortcut", [shortcut]) };
        foreach (var (label, paths) in cases)
        {
            List<(uint Id, string? Verb)> verbs = [];
            var watch = Stopwatch.StartNew();
            string? error = ShellContextMenu.Show(paths, 0, 100, 100, null, () => { }, () => { }, (menu, hmenu) =>
            {
                verbs = ShellContextMenu.ListVerbs(menu, hmenu);
                if (label == "one file")
                    Line("  items: " + string.Join(" | ", verbs.Select(v => $"{v.Id}:{ShellContextMenu.TextOf(hmenu, v.Id)}:{v.Verb}")));
                return 0;
            });
            Line($"- {label}: built in {watch.ElapsedMilliseconds} ms, error {error ?? "none"}; {verbs.Count} commands: {string.Join(", ", verbs.Select(v => v.Verb ?? "(no verb)"))}");
        }

        // The menu itself, shown with TrackPopupMenuEx and dismissed from a timer.
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(900);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => EndMenu();
        timer.Start();
        var shown = Stopwatch.StartNew();
        string? shownError = ShellContextMenu.Show([files[0]], 0, 200, 200, null, null, null);
        Line($"- shown and dismissed: back after {shown.ElapsedMilliseconds} ms, error {shownError ?? "none"}");

        // A command runs: Copy puts the file on the clipboard.
        string? copyError = ShellContextMenu.Show([files[1]], 0, 100, 100, null, null, null, (menu, hmenu) =>
            ShellContextMenu.ListVerbs(menu, hmenu).FirstOrDefault(v => v.Verb == "copy").Id);
        await Task.Delay(300);
        Line($"- Copy invoked: error {copyError ?? "none"}, the clipboard holds files: {IsClipboardFormatAvailable(15)}");

        // Delete, Rename and Open in Windows' menu run our callbacks.
        var called = new List<string>();
        foreach (string verb in new[] { "delete", "rename", "open" })
        {
            string? result = ShellContextMenu.Show([files[2]], 0, 100, 100, () => called.Add("open"), () => called.Add("rename"), () => called.Add("delete"), (menu, hmenu) =>
                ShellContextMenu.ListVerbs(menu, hmenu).FirstOrDefault(v => v.Verb == verb).Id);
            Line($"- {verb} picked: error {result ?? "none"}, our callbacks run so far: {string.Join(", ", called)}");
        }

        // What our Delete does with a real item of the card: the Recycle Bin, and what it answers.
        var window = host.Windows.First(w => w.FolderWidget is not null);
        string victim = Path.Combine(dir, "zz recycle test.txt");
        File.WriteAllText(victim, "x");
        window.FolderWidget!.Model.Rescan();
        await Task.Delay(1500);
        var item = window.FolderWidget.Model.Items.FirstOrDefault(i => i.FileName == "zz recycle test.txt");
        if (item is null)
        {
            Line("- recycle test: the item did not show up");
        }
        else
        {
            string? answer = FolderActions.Recycle(window.FolderWidget.Model, [item], 0);
            await Task.Delay(500);
            Line($"- Recycle through our code: answer {(answer is null ? "none" : "'" + answer + "'")}, the file is still there: {File.Exists(victim)}");
        }
    }

    /// <summary>Phase 13.4 design QA: the Pixel item menu with its new last row, in English and in Chinese, with the first row current and with the last.</summary>
    private static async Task ItemMenuPixel(WidgetHost host)
    {
        host.SetTheme(WidgetTheme.Pixel);
        await Task.Delay(1500);
        Line("## the item menu in Pixel, with Show more options");
        var window = host.Windows.First(w => w.FolderWidget is not null);
        var work = Interop.Win32.GetWorkArea(default);
        int x = work.Left + 900, y = work.Top + 160;
        foreach (var language in new[] { AppLanguage.English, AppLanguage.Chinese })
        {
            Strings.Language = language;
            var rows = window.FolderWidget!.FirstItemMenuForCapture()!;
            PixelUi.PixelMenu? menu = null;
            await RecordedRect($"the menu, {language}", $"item-menu-{language}", x - 16, y - 16, 520, 700, () => menu = PixelUi.PixelMenu.Show(rows, x, y), afterMs: 500, sheetFrames: 4, shrink: 1, columns: 4);
            await RecordedRect($"the last row current, {language}", $"item-menu-{language}-last", x - 16, y - 16, 520, 700, () => menu!.OnKey(PixelUi.Native.VK_END), afterMs: 400, sheetFrames: 3, shrink: 1, columns: 3);
            menu!.CloseAll(restoreForeground: false);
            await Task.Delay(500);
        }
        Strings.Language = AppLanguage.English;
    }
}
