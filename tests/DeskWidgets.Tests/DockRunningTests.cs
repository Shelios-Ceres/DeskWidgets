using DeskWidgets.Pixel;
using DeskWidgets.Dock;

namespace DeskWidgets.Tests;

/// <summary>Phase 15.4: which dock programs are running (window matching), the opening state, and the click's switch plan.</summary>
public class DockRunningTests
{
    private static WindowIdentity Window(string? windowAumid = null, string? processAumid = null, string? exe = null) => new(windowAumid, processAumid, exe);

    // ----- the match order -----

    [Fact]
    public void ByTheWindowsAumid_First()
    {
        var chrome = new DockItem { Name = "Chrome", Aumid = "Chrome", ExePath = @"C:\Chrome\chrome.exe" };
        Assert.True(DockRunning.Matches(chrome, Window(windowAumid: "Chrome", exe: @"C:\Elsewhere\other.exe")));
        Assert.False(DockRunning.Matches(chrome, Window(windowAumid: "MSEdge", exe: @"C:\Edge\msedge.exe")));
    }

    [Fact]
    public void ByTheProcessAumid_WhenTheWindowHasNone_ForElectronStoreApps()
    {
        var claude = new DockItem { Name = "Claude", Aumid = "Claude_pzs8sxrjxfjjc!Claude" };
        Assert.True(DockRunning.Matches(claude, Window(processAumid: "Claude_pzs8sxrjxfjjc!Claude", exe: @"C:\Program Files\WindowsApps\Claude_4\app\claude.exe")));
        // The same exe name in another package is not it.
        Assert.False(DockRunning.Matches(claude, Window(processAumid: "Other_x!App", exe: @"C:\Program Files\WindowsApps\Claude_4\app\claude.exe")));
    }

    [Fact]
    public void ByTheExePath_WhenThereIsNoAumid_IgnoringCase()
    {
        var antigravity = new DockItem { Name = "Antigravity", ExePath = @"C:\Users\me\AppData\Local\Programs\antigravity\Antigravity.exe" };
        Assert.True(DockRunning.Matches(antigravity, Window(exe: @"c:\users\me\appdata\local\programs\antigravity\antigravity.exe")));
        Assert.False(DockRunning.Matches(antigravity, Window(exe: @"C:\Users\me\AppData\Local\Programs\cursor\Cursor.exe")));
    }

    [Fact]
    public void AKnownFolderRelativeAumid_IsExpandedToTheExe()
    {
        // WeChat's "AUMID" is a path relative to Program Files (FOLDERID_ProgramFilesX64); the window only knows its exe.
        string? programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetEnvironmentVariable("ProgramFiles");
        Assert.NotNull(programFiles);
        var wechat = new DockItem { Name = "WeChat", Aumid = @"{6D809377-6AF0-444B-8957-A3773F02200E}\Tencent\Weixin\Weixin.exe" };
        Assert.True(DockRunning.Matches(wechat, Window(exe: Path.Combine(programFiles!, @"Tencent\Weixin\Weixin.exe"))));
    }

    [Fact]
    public void TheLauncherTable_MatchesTwoExesOfOneCustomIconApp()
    {
        // DingTalk's start entry names its launcher; the window belongs to DingTalk.exe (AppIconPack lists both under one key).
        var dingtalk = new DockItem { Name = "钉钉", ExePath = @"C:\DingDing\DingtalkLauncher.exe" };
        Assert.True(DockRunning.Matches(dingtalk, Window(exe: @"C:\DingDing\main\current\DingTalk.exe")));
        Assert.False(DockRunning.Matches(dingtalk, Window(exe: @"C:\Other\Notes.exe")));
    }

    [Fact]
    public void ASteamLink_HasNoRunningState()
    {
        var game = new DockItem { Name = "Game", Url = "steam://rungameid/431960" };
        Assert.False(game.SupportsRunning);
        Assert.False(DockRunning.Matches(game, Window(exe: @"C:\Steam\steamapps\common\Game\game.exe")));
    }

    [Fact]
    public void NeverByTheTitle()
    {
        // WindowIdentity has no title: a program is its AUMIDs and its exe, nothing else.
        Assert.Equal(["WindowAumid", "ProcessAumid", "ExePath"], typeof(WindowIdentity).GetProperties().Select(p => p.Name));
    }

    // ----- the taskbar's idea of a window -----

    [Theory]
    [InlineData(false, false, 0x00000000L, false, false, true)] // an ordinary window
    [InlineData(true, false, 0x00000000L, false, false, false)] // owned by another window (a dialog)
    [InlineData(false, true, 0x00000000L, false, false, false)] // a shell surface (the taskbar, the desktop)
    [InlineData(false, false, 0x00000080L, false, false, false)] // a tool window
    [InlineData(false, false, 0x00040080L, false, false, true)] // a tool window that asks for the taskbar
    [InlineData(false, false, 0x08000000L, false, false, false)] // WS_EX_NOACTIVATE: a dock or an overlay
    [InlineData(false, false, 0x00000000L, true, false, false)] // cloaked: another virtual desktop
    [InlineData(false, false, 0x00000000L, false, true, false)] // ours
    public void TheTaskbarRule_OnFacts(bool owned, bool shell, long exStyle, bool cloaked, bool ours, bool counts)
        => Assert.Equal(counts, WindowTracker.NativeMethods.CountsOnTheTaskbar(owned, shell, exStyle, cloaked, ours));

    // ----- the opening state: click, hold, window, timeout, cut short -----

    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static IReadOnlySet<string> Running(params string[] keys) => new HashSet<string>(keys);

    [Fact]
    public void AClick_StartsTheOpeningState_AndItHoldsUntilTheFirstWindow()
    {
        var states = new DockRunStates();
        Assert.True(states.Started("word", TimeSpan.Zero));
        Assert.Equal(DockRunState.Opening, states.StateOf("word"));

        // Nothing changes while no window comes, however many times the windows are read.
        Assert.Empty(states.WindowsNow(None, ["word"]));
        Assert.Empty(states.WindowsNow(Running("chrome"), ["word"]));
        Assert.Equal(DockRunState.Opening, states.StateOf("word"));

        var changes = states.WindowsNow(Running("word"), ["word"]);
        var change = Assert.Single(changes);
        Assert.Equal(("word", DockRunState.Running, DockRunTransition.OpenedHere), change);
        Assert.Null(states.NextExpiry()); // nothing left to time out
    }

    [Fact]
    public void WithNoWindowWithinTenSeconds_TheIconGoesBackToIdle()
    {
        var states = new DockRunStates();
        states.Started("word", TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(13), states.NextExpiry());
        Assert.Empty(states.Expire(TimeSpan.FromSeconds(12.9)));
        Assert.Equal(["word"], states.Expire(TimeSpan.FromSeconds(13)));
        Assert.Equal(DockRunState.Idle, states.StateOf("word"));
        Assert.Null(states.NextExpiry());
    }

    [Fact]
    public void AProgramThatIsAlreadyRunning_IsNotOpenedAgainByAClick()
    {
        var states = new DockRunStates();
        states.WindowsNow(Running("chrome"), ["chrome"]);
        Assert.Equal(DockRunState.Running, states.StateOf("chrome"));
        Assert.False(states.Started("chrome", TimeSpan.Zero)); // a Shift+click starts another copy; the icon stays as it is
        Assert.Equal(DockRunState.Running, states.StateOf("chrome"));
    }

    [Fact]
    public void AProgramStartedElsewhere_WipesInWithoutThePop_AndItsLastWindowClosesIt()
    {
        var states = new DockRunStates();
        var opened = Assert.Single(states.WindowsNow(Running("edge"), ["edge", "word"]));
        Assert.Equal(("edge", DockRunState.Running, DockRunTransition.OpenedElsewhere), opened);

        var closed = Assert.Single(states.WindowsNow(None, ["edge", "word"]));
        Assert.Equal(("edge", DockRunState.Idle, DockRunTransition.Closed), closed);
    }

    [Fact]
    public void TheOpeningHold_IsCutShortWhenTheWindowComesBeforeTheTimeout_AndALateExpiryDoesNothing()
    {
        var states = new DockRunStates();
        states.Started("word", TimeSpan.Zero);
        states.WindowsNow(Running("word"), ["word"]);
        Assert.Empty(states.Expire(TimeSpan.FromSeconds(60)));
        Assert.Equal(DockRunState.Running, states.StateOf("word"));
    }

    [Fact]
    public void ARemovedProgram_IsForgottenAndNeverExpires()
    {
        var states = new DockRunStates();
        states.Started("word", TimeSpan.Zero);
        states.Forget("word");
        Assert.Empty(states.Expire(TimeSpan.FromSeconds(60)));
        Assert.Null(states.NextExpiry());
        Assert.Equal(DockRunState.Idle, states.StateOf("word"));
    }

    // ----- a click on a running program -----

    [Fact]
    public void NotInFront_BringsTheFrontmostWindowForward()
    {
        var plan = DockRunning.PlanSwitch([50, 20, 30], foreground: 99);
        Assert.Equal(new DockRunning.SwitchPlan(DockRunning.SwitchKind.Activate, 50), plan);
    }

    [Fact]
    public void InFrontWithOneWindow_MinimizesIt()
    {
        var plan = DockRunning.PlanSwitch([50], foreground: 50);
        Assert.Equal(new DockRunning.SwitchPlan(DockRunning.SwitchKind.Minimize, 50), plan);
    }

    [Fact]
    public void InFrontWithSeveralWindows_EachClickBringsTheNextOneForward_ThroughAllOfThem()
    {
        // The windows in z-order, front first. After a click the activated window is in front, so the order changes: the cycle must still reach all.
        var order = new List<nint> { 30, 10, 20 };
        nint front = 30;
        var visited = new List<nint>();
        for (int i = 0; i < 6; i++)
        {
            var plan = DockRunning.PlanSwitch(order, front);
            Assert.Equal(DockRunning.SwitchKind.Activate, plan.Kind);
            visited.Add(plan.Window);
            front = plan.Window;
            order.Remove(front);
            order.Insert(0, front);
        }
        Assert.Equal([10, 20, 30, 10, 20, 30], visited);
    }
}

/// <summary>Phase 15.4: the colours a running icon is marked with (Glass accent plate, Pixel halo).</summary>
public class DockRunningLookTests
{
    private static byte[] Solid(byte r, byte g, byte b, int count, byte alpha = 255)
    {
        var bytes = new byte[count * 4];
        for (int i = 0; i < count; i++)
        {
            bytes[i * 4] = b;
            bytes[i * 4 + 1] = g;
            bytes[i * 4 + 2] = r;
            bytes[i * 4 + 3] = alpha;
        }
        return bytes;
    }

    [Fact]
    public void AnAllRedIcon_GetsARedPlate_DarkerOnTheLightBar()
    {
        var red = Solid(255, 0, 0, 100);
        Assert.Equal(0xFFC20000u, GlassDockRunning.AccentPlate(red, dark: false));
        uint dark = GlassDockRunning.AccentPlate(red, dark: true);
        var (hue, _, lightness) = GlassDockRunning.ToHsl((byte)(dark >> 16), (byte)(dark >> 8), (byte)dark);
        Assert.InRange(hue, 0, 1);
        Assert.InRange(lightness, 0.41, 0.43);
    }

    [Fact]
    public void AGreyOrMostlyTransparentIcon_FallsBackToTheNeutralPlate()
    {
        Assert.Equal(GlassDockRunning.NeutralPlate, GlassDockRunning.AccentPlate(Solid(200, 200, 200, 100), dark: false));
        Assert.Equal(GlassDockRunning.NeutralPlate, GlassDockRunning.AccentPlate(Solid(255, 0, 0, 100, alpha: 40), dark: false));
        Assert.Equal(GlassDockRunning.NeutralPlate, GlassDockRunning.AccentPlate([], dark: false));
    }

    [Fact]
    public void TheMajorityHueWins_ASpeckDoesNot()
    {
        var pixels = Solid(0, 0, 255, 200).Concat(Solid(255, 0, 0, 2)).ToArray();
        uint plate = GlassDockRunning.AccentPlate(pixels, dark: false);
        var (hue, _, _) = GlassDockRunning.ToHsl((byte)(plate >> 16), (byte)(plate >> 8), (byte)plate);
        Assert.InRange(hue, 235, 245);
    }

    private static uint[] Pixels(PixelPalette palette, params (char Symbol, int Count)[] runs)
        => [.. runs.SelectMany(r => Enumerable.Repeat(palette.Sprite(r.Symbol), r.Count))];

    [Fact]
    public void TheHalo_IsTheMostCommonColouredPixel_IgnoringInkWhiteAndGrey()
    {
        var palette = PixelPalette.For(dark: false);
        var icon = Pixels(palette, ('#', 50), ('w', 40), ('s', 30), ('g', 20), ('r', 5), ('b', 3));
        Assert.Equal('r', PixelDockArt.HaloSymbol(icon, dark: false));
        Assert.Equal(palette.Sprite('r'), PixelDockArt.HaloColor(icon, dark: false));
    }

    [Fact]
    public void TheHalo_IsBlueWhenNothingIsColoured_AndATieGoesToTheFirstMet()
    {
        var palette = PixelPalette.For(dark: false);
        Assert.Equal('b', PixelDockArt.HaloSymbol(Pixels(palette, ('#', 5), ('w', 5)), dark: false));
        Assert.Equal('b', PixelDockArt.HaloSymbol([], dark: false));
        Assert.Equal('o', PixelDockArt.HaloSymbol(Pixels(palette, ('o', 4), ('r', 4)), dark: false));
    }

    [Theory]
    [InlineData('y', false, 'o')] // yellow steps down to orange on the light bar
    [InlineData('c', false, 'b')] // cyan to blue
    [InlineData('L', false, 't')] // lime to teal
    [InlineData('r', false, 'r')]
    [InlineData('p', true, 'r')] // purple steps up to red on the dark bar
    [InlineData('n', true, 'b')] // navy to blue
    [InlineData('t', true, 'G')] // teal to green
    [InlineData('d', true, 's')] // dusk to slate
    [InlineData('y', true, 'y')]
    public void TheHalo_StepsToStayReadableOnTheBar(char symbol, bool dark, char expected)
        => Assert.Equal(expected, PixelDockArt.ForBar(symbol, dark));
}
