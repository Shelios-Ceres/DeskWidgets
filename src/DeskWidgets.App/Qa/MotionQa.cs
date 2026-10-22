using System.Diagnostics;
using System.Text;
using DeskWidgets.Hosting;
using DeskWidgets.Interop;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DeskWidgets.Qa;

/// <summary>
/// Design QA for the Phase 12 motion (<c>--motion-qa=&lt;folder&gt; --motion-run=a,b</c>, in a test instance whose widgets are always on top
/// over the noise stage). Each moment is triggered the way the app triggers it, recorded from the screen (contact sheets and frame
/// times), and then repeated for a CPU and handle count. <c>--motion-off</c> runs the same moments with "Animation effects" off.
/// </summary>
internal static partial class MotionQa
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly StringBuilder Report = new();
    private static string s_out = "";

    private static void Line(string text = "")
    {
        Report.AppendLine(text);
        File.WriteAllText(Path.Combine(s_out, "report.txt"), Report.ToString());
    }

    /// <summary>Makes the stage the test widgets sit on (before the host starts, so the widgets come up above it).</summary>
    public static QaStage MakeStage()
    {
        var work = Win32.GetWorkArea(default);
        double scale = Win32.GetScaleAt(new Win32.POINT { X = work.Left + 1, Y = work.Top + 1 });
        return new QaStage(new RectInt32(work.Left + 60, work.Top + 60, 1800, 950), scale);
    }

    public static async void Run(WidgetHost host, string folder, string moments, QaStage stage)
    {
        s_out = folder;
        Directory.CreateDirectory(folder);
        try
        {
            await Task.Delay(3000);
            Line($"# Motion QA {DateTime.Now:HH:mm:ss}; {host.Windows.Count} widgets; Animation effects {(Motion.MotionSettings.Enabled ? "on" : "OFF")}");
            foreach (string name in moments.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    await Moment(host, name.Trim());
                }
                catch (Exception ex)
                {
                    Line($"!! {name} failed: {ex}");
                }
            }
            Line("done");
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--motion-hold", StringComparison.OrdinalIgnoreCase)))
                await Task.Delay(120000); // stay up, so that handles.ps1 can count the handles by type
        }
        catch (Exception ex)
        {
            Line("!! motion QA failed: " + ex);
        }
        stage.Close();
        Application.Current.Exit();
    }

    private static Task Moment(WidgetHost host, string name) => name switch
    {
        "a1-glass" => PanelOpenClose(host, WidgetTheme.Glass),
        "a1-pixel" => PanelOpenClose(host, WidgetTheme.Pixel),
        "a2-glass" => PanelItems(host, WidgetTheme.Glass),
        "a2-pixel" => PanelItems(host, WidgetTheme.Pixel),
        "a3-glass" => PanelHover(host, WidgetTheme.Glass),
        "search-timing" => SearchTiming(host, WidgetTheme.Glass),
        "a3-pixel" => PanelHover(host, WidgetTheme.Pixel),
        "card-glass" => CardMoments(host, WidgetTheme.Glass),
        "b5-pixel" => MenusAndDialogs(host),
        "b6-glass" => BatteryChanges(host, WidgetTheme.Glass),
        "b6-pixel" => BatteryChanges(host, WidgetTheme.Pixel),
        "b7-glass" => SizeAndShade(host, WidgetTheme.Glass),
        "b7-pixel" => SizeAndShade(host, WidgetTheme.Pixel),
        "b7-switch" => ThemeSwitch(host),
        "b8-glass" => WidgetAddRemove(host, WidgetTheme.Glass),
        "b8-pixel" => WidgetAddRemove(host, WidgetTheme.Pixel),
        "leak-widgets" => LeakWidgets(host),
        "leak-bare" => LeakBare(),
        "leak-clock" => LeakOne(host, "clock"),
        "leak-folder" => LeakOne(host, "folder"),
        "leak-theme" => LeakTheme(host),
        "card-hover-glass" => CardHover(host, WidgetTheme.Glass),
        "card-hover-pixel" => CardHover(host, WidgetTheme.Pixel),
        "card-pixel" => CardMoments(host, WidgetTheme.Pixel),
        "a4-glass" => PanelSortSearch(host, WidgetTheme.Glass),
        "a4-pixel" => PanelSortSearch(host, WidgetTheme.Pixel),
        _ => Task.Run(() => Line($"unknown moment {name}")),
    };

    // ----- helpers -----

    private static FolderWidget Folder(WidgetHost host, string endsWith)
        => host.Windows.Select(w => w.FolderWidget).First(f => f is not null && f.Model.Path.TrimEnd('\\').EndsWith(endsWith))!;

    private static Win32.RECT CardOf(WidgetHost host, string endsWith)
        => host.Windows.First(w => w.FolderWidget is { } f && f.Model.Path.TrimEnd('\\').EndsWith(endsWith)).CardRect;

    private static Recorder Rec(int x, int y, int width, int height) => new(x, y, width, height, Clock);

    private static (int Handles, uint Gdi, uint User) Counts()
    {
        var self = Process.GetCurrentProcess();
        return (self.HandleCount, PixelUi.Native.GetGuiResources(self.Handle, 0), PixelUi.Native.GetGuiResources(self.Handle, 1));
    }

    private static async Task Idle(int ms)
    {
        await Task.Delay(ms);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>Repeats a moment without the recorder: CPU per round, then 3 s of idle CPU, and the handle counts before and after.</summary>
    private static async Task Cycles(string label, int rounds, Func<Task> round)
    {
        await Idle(800);
        var before = Counts();
        var self = Process.GetCurrentProcess();
        var cpu0 = self.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        for (int i = 0; i < rounds; i++)
            await round();
        self.Refresh();
        var busy = self.TotalProcessorTime - cpu0;
        var idle0 = self.TotalProcessorTime;
        await Idle(3000);
        self.Refresh();
        var idle = self.TotalProcessorTime - idle0;
        var after = Counts();
        Line($"  {label}: {rounds} rounds, CPU {busy.TotalMilliseconds / rounds:0.0} ms per round, 3 s idle {idle.TotalMilliseconds:0} ms; handles {before.Handles} -> {after.Handles}, GDI {before.Gdi} -> {after.Gdi}, USER {before.User} -> {after.User}");
    }

    // ----- A1: the panel opens and closes -----

    private static async Task PanelOpenClose(WidgetHost host, WidgetTheme theme)
    {
        host.SetTheme(theme);
        await Task.Delay(1500);
        Line($"## A1 the panel opens and closes, {theme}");
        var folder = Folder(host, "A");
        var card = CardOf(host, "A");
        int x = card.Right - 40, y = card.Top - 30, w = 760, h = 800;

        folder.OpenPanel(); // a first open makes the window; the measured ones reuse it
        await Task.Delay(900);
        folder.ClosePanelForCapture();
        await Task.Delay(800);

        var open = Rec(x, y, w, h);
        open.Start();
        await Task.Delay(500);
        double t0 = Clock.Elapsed.TotalMilliseconds;
        folder.OpenPanel();
        await Task.Delay(900);
        open.Stop();
        var close = Rec(x, y, w, h);
        close.Start();
        await Task.Delay(400);
        double t1 = Clock.Elapsed.TotalMilliseconds;
        folder.ClosePanelForCapture();
        await Task.Delay(900);
        close.Stop();
        Line($"- open: {open.Describe(t0)}");
        Line($"  late change after +400 ms: {open.DescribeLastChange(t0 + 400)}");
        Line($"- close: {close.Describe(t1)}");
        open.SaveSheet(Path.Combine(s_out, $"a1-{theme}-open.png"), 12, 4);
        close.SaveSheet(Path.Combine(s_out, $"a1-{theme}-close.png"), 12, 4, fromMs: t1 - 1);

        await Cycles("open and close", 20, async () =>
        {
            folder.OpenPanel();
            await Task.Delay(700);
            folder.ClosePanelForCapture();
            await Task.Delay(600);
        });
    }

    // ----- the panel's list changes -----

    private static async Task<(FolderWidget Folder, IFolderPanel Panel, int X, int Y)> OpenPanelFor(WidgetHost host, WidgetTheme theme)
    {
        host.SetTheme(theme);
        await Task.Delay(1500);
        var folder = Folder(host, "A");
        var card = CardOf(host, "A");
        var panel = folder.OpenPanel();
        await Task.Delay(1200);
        return (folder, panel, card.Right - 40, card.Top - 30);
    }

    /// <summary>Records <paramref name="act"/> and what follows it, and writes a line and a contact sheet.</summary>
    private static async Task Recorded(string title, string sheet, int x, int y, Action act, int afterMs = 900, int sheetFrames = 12)
    {
        var recorder = Rec(x, y, 760, 800);
        recorder.Start();
        await Task.Delay(500);
        double t0 = Clock.Elapsed.TotalMilliseconds;
        var watch = Stopwatch.StartNew();
        act();
        double call = watch.Elapsed.TotalMilliseconds;
        await Task.Delay(afterMs);
        recorder.Stop();
        Line($"- {title}: the call took {call:0.0} ms; {recorder.Describe(t0)}");
        recorder.SaveSheet(Path.Combine(s_out, sheet + ".png"), sheetFrames, 4, fromMs: t0 - 1);
    }

    /// <summary>How long a filter change blocks the UI thread, one keystroke at a time (the way typing does it), warmed up.</summary>
    private static async Task SearchTiming(WidgetHost host, WidgetTheme theme)
    {
        Line($"## filter timing, {theme}");
        var (folder, panel, _, _) = await OpenPanelFor(host, theme);
        string[] steps = ["P", "Ph", "Pho", "Phot", "Photo", "Photo ", "Photo 0", "Photo", "Pho", "Ph", "P", ""];
        for (int round = 0; round < 3; round++)
        {
            var times = new List<string>();
            foreach (string text in steps)
            {
                GlassFolderPanel.LongestRefreshMs = 0;
                var watch = Stopwatch.StartNew();
                panel.PrepareForCapture(new FolderCapture(Search: text));
                times.Add($"{watch.Elapsed.TotalMilliseconds:0.0} (one filter {GlassFolderPanel.LongestRefreshMs:0.0})");
                await Task.Delay(350);
            }
            Line($"- round {round + 1}: {string.Join(", ", times)} ms (each call is the panel's capture hook: it filters three times)");
        }
        folder.ClosePanelForCapture();
        await Task.Delay(600);
    }

    private static async Task PanelSortSearch(WidgetHost host, WidgetTheme theme)
    {
        Line($"## A4 search and sort, {theme}");
        var (folder, panel, x, y) = await OpenPanelFor(host, theme);
        foreach (var (label, sort) in new[] { ("sort Newest to Name", FolderSort.Name), ("sort Name to Type", FolderSort.Type), ("sort Type to Newest", FolderSort.Newest) })
            await Recorded(label, $"a4-{theme}-{label.Replace(' ', '-')}", x, y, () => folder.Model.SetSort(sort));
        await Recorded("search Photo", $"a4-{theme}-search", x, y, () => panel.PrepareForCapture(new FolderCapture(Search: "Photo")));
        await Recorded("search Photo 0", $"a4-{theme}-search2", x, y, () => panel.PrepareForCapture(new FolderCapture(Search: "Photo 0")));
        await Recorded("clear the search", $"a4-{theme}-clear", x, y, () => panel.PrepareForCapture(new FolderCapture(Search: "")));
        await Recorded("search with no match", $"a4-{theme}-nomatch", x, y, () => panel.PrepareForCapture(new FolderCapture(Search: "zzzz")));
        await Recorded("clear again", $"a4-{theme}-clear2", x, y, () => panel.PrepareForCapture(new FolderCapture(Search: "")));
        await Cycles("sort and search", 12, async () =>
        {
            folder.Model.SetSort(FolderSort.Name);
            await Task.Delay(500);
            panel.PrepareForCapture(new FolderCapture(Search: "Photo"));
            await Task.Delay(500);
            panel.PrepareForCapture(new FolderCapture(Search: ""));
            await Task.Delay(500);
            folder.Model.SetSort(FolderSort.Newest);
            await Task.Delay(500);
        });
        folder.ClosePanelForCapture();
        await Task.Delay(600);
    }

    private static async Task PanelItems(WidgetHost host, WidgetTheme theme)
    {
        Line($"## A2 items arriving and leaving, {theme}");
        var (folder, panel, x, y) = await OpenPanelFor(host, theme);
        string root = folder.Model.Path;
        string[] added = [Path.Combine(root, "Arrival one.txt"), Path.Combine(root, "Arrival two.pdf")];
        await Recorded("two files arrive (rescan)", $"a2-{theme}-arrive", x, y, () =>
        {
            foreach (string path in added)
                File.WriteAllText(path, "x");
        }, afterMs: 1600);
        await Recorded("one of them leaves, and an older one", $"a2-{theme}-leave", x, y, () =>
        {
            File.Delete(added[0]);
            File.Delete(Path.Combine(root, "Notes.txt"));
        }, afterMs: 1600);
        File.WriteAllText(Path.Combine(root, "Notes.txt"), "");
        File.Delete(added[1]);
        await Task.Delay(1500);
        await Cycles("arrive and leave", 8, async () =>
        {
            File.WriteAllText(added[0], "x");
            await Task.Delay(900);
            File.Delete(added[0]);
            await Task.Delay(900);
        });
        folder.ClosePanelForCapture();
        await Task.Delay(600);
    }

    private static async Task PanelHover(WidgetHost host, WidgetTheme theme)
    {
        Line($"## A3 hover on a cell, {theme} (the pointer is not moved: the panel's own capture state sets the hover)");
        var (folder, panel, x, y) = await OpenPanelFor(host, theme);
        await Recorded("hover on item 3", $"a3-{theme}-hover", x, y, () => panel.PrepareForCapture(new FolderCapture(Hover: 3)), afterMs: 500, sheetFrames: 8);
        await Recorded("hover moves to item 8", $"a3-{theme}-hover2", x, y, () => panel.PrepareForCapture(new FolderCapture(Hover: 8)), afterMs: 500, sheetFrames: 8);
        await Recorded("hover ends", $"a3-{theme}-hover3", x, y, () => panel.PrepareForCapture(new FolderCapture(Hover: -1)), afterMs: 500, sheetFrames: 8);
        folder.ClosePanelForCapture();
        await Task.Delay(600);
    }

    // ----- handles over adding and removing widgets, and over theme switches (Phase 12 leaks 2 and 3) -----

    private static async Task LeakWidgets(WidgetHost host)
    {
        host.SetTheme(WidgetTheme.Glass);
        await Task.Delay(1500);
        Line("## handles over adding and removing a widget (one of each kind per round)");
        foreach (string type in new[] { "clock", "worldclock", "battery", "folder" })
        {
            var factory = WidgetCatalog.Find(type)!;
            await Cycles($"add and remove a {type}", 25, async () =>
            {
                host.AddWidget(factory);
                await Task.Delay(350);
                host.RemoveWidget(host.Windows[^1]);
                await Task.Delay(350);
            });
        }
    }

    // A bare WinUI window (no widget, no glass) made and closed: what WinUI itself leaves behind per window.
    private static async Task LeakBare()
    {
        await Task.Delay(1500);
        var weak = new List<WeakReference>();
        await Cycles("make and close a bare WinUI window", 40, async () =>
        {
            var content = new Microsoft.UI.Xaml.Controls.Grid();
            var window = new Microsoft.UI.Xaml.Window { Content = content };
            weak.Add(new WeakReference(window));
            weak.Add(new WeakReference(content));
            window.Activate();
            await Task.Delay(350);
            window.Close();
            await Task.Delay(350);
        });
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Line($"  bare windows and their content still alive: {weak.Count(w => w.IsAlive)} of {weak.Count}");
    }

    private static async Task LeakOne(WidgetHost host, string type)
    {
        host.SetTheme(WidgetTheme.Glass);
        await Task.Delay(1500);
        var factory = WidgetCatalog.Find(type)!;
        var weak = new List<WeakReference>();
        await Cycles($"add and remove a {type}", 40, async () =>
        {
            host.AddWidget(factory);
            await Task.Delay(350);
            Remember(weak, host.Windows[^1]);
            host.RemoveWidget(host.Windows[^1]);
            await Task.Delay(350);
        });
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Line($"  of {weak.Count / 3} removed: windows still alive {weak.Where((w, i) => i % 3 == 0).Count(w => w.IsAlive)}, widgets {weak.Where((w, i) => i % 3 == 1).Count(w => w.IsAlive)}, views {weak.Where((w, i) => i % 3 == 2).Count(w => w.IsAlive)}");
    }

    private static void Remember(List<WeakReference> weak, WidgetWindow window)
    {
        weak.Add(new WeakReference(window));
        weak.Add(new WeakReference(window.Widget));
        weak.Add(new WeakReference(window.Widget.View));
    }

    private static async Task LeakTheme(WidgetHost host)
    {
        host.SetTheme(WidgetTheme.Glass);
        await Task.Delay(1500);
        Line("## handles over theme switches (four widgets)");
        await Cycles("Glass to Pixel and back", 30, async () =>
        {
            host.SetTheme(WidgetTheme.Pixel);
            await Task.Delay(500);
            host.SetTheme(WidgetTheme.Glass);
            await Task.Delay(500);
        });
    }

    // ----- the card -----

    private static async Task RecordedCard(string title, string sheet, Win32.RECT card, Action act, int afterMs = 900, int sheetFrames = 12)
    {
        var recorder = Rec(card.Left - 12, card.Top - 12, card.Right - card.Left + 24, card.Bottom - card.Top + 24);
        recorder.Start();
        await Task.Delay(500);
        double t0 = Clock.Elapsed.TotalMilliseconds;
        var watch = Stopwatch.StartNew();
        act();
        double call = watch.Elapsed.TotalMilliseconds;
        await Task.Delay(afterMs);
        recorder.Stop();
        Line($"- {title}: the call took {call:0.0} ms; {recorder.Describe(t0)}");
        recorder.SaveSheet(Path.Combine(s_out, sheet + ".png"), sheetFrames, 2, 3, fromMs: t0 - 1);
    }

    /// <summary>A3 on the card: the pointer crosses three cells (and the header), then leaves; frames are recorded across the whole crossing.</summary>
    private static async Task CardHover(WidgetHost host, WidgetTheme theme)
    {
        host.SetTheme(theme);
        await Task.Delay(1500);
        Line($"## A3 the pointer crosses the card's cells, {theme}");
        var folder = Folder(host, "A");
        var card = CardOf(host, "A");
        // Points in DIPs from the card's top-left. Glass cells: x 12 + 80 * column, y 34 + 60 * row (Medium); Pixel cells sit at art x 5, 47, 89, 131 (x2 DIP), y 19 (x2).
        var points = theme == WidgetTheme.Glass
            ? new[] { new Windows.Foundation.Point(52, 60), new Windows.Foundation.Point(132, 60), new Windows.Foundation.Point(212, 60), new Windows.Foundation.Point(100, 21) }
            : new[] { new Windows.Foundation.Point(2 * (5 + 18), 2 * 28), new Windows.Foundation.Point(2 * (47 + 18), 2 * 28), new Windows.Foundation.Point(2 * (89 + 18), 2 * 28), new Windows.Foundation.Point(100, 21) };
        var recorder = Rec(card.Left - 12, card.Top - 12, card.Right - card.Left + 24, card.Bottom - card.Top + 24);
        recorder.Start();
        await Task.Delay(500);
        double t0 = Clock.Elapsed.TotalMilliseconds;
        foreach (var point in points)
        {
            folder.CardPointerMoved(point);
            await Task.Delay(130);
        }
        folder.CardPointerMoved(null);
        await Task.Delay(500);
        recorder.Stop();
        Line($"- crossing: {recorder.Describe(t0)}");
        recorder.SaveSheet(Path.Combine(s_out, $"card-hover-{theme}.png"), 16, 2, 4, fromMs: t0 - 1);
    }

    private static async Task CardMoments(WidgetHost host, WidgetTheme theme)
    {
        host.SetTheme(theme);
        await Task.Delay(1500);
        Line($"## the Big Folder card (A2 drop hint and items, A3 press, A4 sort), {theme}");
        var folder = Folder(host, "A");
        var card = CardOf(host, "A");
        string root = folder.Model.Path;
        await RecordedCard("a drag enters (Move)", $"card-{theme}-drag-enter", card, () => folder.ShowDropHintForCapture(DropVerb.Move), afterMs: 600, sheetFrames: 9);
        await RecordedCard("a modifier key swaps the verb (Copy)", $"card-{theme}-drag-copy", card, () => folder.ShowDropHintForCapture(DropVerb.Copy), afterMs: 600, sheetFrames: 9);
        await RecordedCard("the drag leaves", $"card-{theme}-drag-leave", card, () => folder.ShowDropHintForCapture(null), afterMs: 600, sheetFrames: 9);
        foreach (var (label, sort) in new[] { ("sort Newest to Name", FolderSort.Name), ("sort Name to Type", FolderSort.Type), ("sort Type to Newest", FolderSort.Newest) })
            await RecordedCard(label, $"card-{theme}-{label.Replace(' ', '-')}", card, () => folder.Model.SetSort(sort));
        string added = Path.Combine(root, "Card arrival.txt");
        await RecordedCard("a file arrives", $"card-{theme}-arrive", card, () => File.WriteAllText(added, "x"), afterMs: 1600);
        await RecordedCard("it leaves again", $"card-{theme}-leave", card, () => File.Delete(added), afterMs: 1600);
        await Cycles("drag hint and sort", 12, async () =>
        {
            folder.ShowDropHintForCapture(DropVerb.Move);
            await Task.Delay(500);
            folder.ShowDropHintForCapture(null);
            await Task.Delay(400);
            folder.Model.SetSort(FolderSort.Name);
            await Task.Delay(500);
            folder.Model.SetSort(FolderSort.Newest);
            await Task.Delay(500);
        });
    }
}
