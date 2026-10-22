using System.Diagnostics;
using DeskWidgets.Core;
using DeskWidgets.Core.Devices;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Desktop;
using DeskWidgets.Devices.Bluetooth;
using DeskWidgets.Devices.Hid;
using DeskWidgets.Interop;
using DeskWidgets.PixelUi;
using DeskWidgets.Widgets;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DeskWidgets.Hosting;

/// <summary>
/// Owns the app's long-lived parts: the device hub, the settings, the tray
/// icon, the desktop icon arranger and one <see cref="WidgetWindow"/> per
/// widget. Everything here runs on the UI thread.
/// </summary>
internal sealed class WidgetHost
{
    private readonly SettingsStore _settings = new();
    private readonly List<WidgetWindow> _windows = [];
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly IconArranger _icons;
    private IconArrangerStatus _iconStatus = new(IconArrangerState.Off, 0, null);
    private TrayIcon? _tray;
    private ShowDesktopWatcher? _showDesktop;
    private bool _overDesktop; // Win+D has lifted the desktop over the widgets, so they sit above it for now
    private bool _exiting;

    public WidgetHost(bool demoMode)
    {
        Strings.Language = _settings.Current.Language;
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        IDeviceSource[] sources = demoMode
            ? [new DemoDeviceSource(() => Strings.IsChinese)]
            : [new HidDongleSource(), new BluetoothBatterySource()];
        Services = new AppServices { Devices = new DeviceHub(sources), Dispatcher = dispatcher };

        _saveTimer = dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => _settings.Save();

        _icons = new IconArranger(_settings.Current.ArrangeDesktopIcons);
        _icons.StatusChanged += status => dispatcher.TryEnqueue(() => ShowIconStatus(status));
        _icons.MarginsChanged += () => dispatcher.TryEnqueue(ResnapAll);
    }

    public AppServices Services { get; }

    public bool SnapToDesktopGrid => _settings.Current.SnapToDesktopGrid;

    public bool ArrangeDesktopIcons => _settings.Current.ArrangeDesktopIcons;

    /// <summary>The desktop grid at <paramref name="point"/>, sharing the icons' columns while they are arranged.</summary>
    public DesktopGrid GridAt(Win32.POINT point) => DesktopGrid.At(point, _icons.AnchorAt(point));

    /// <summary>Where new Big Folders are made; null keeps the built-in default. Saved with the settings.</summary>
    public string? FolderRoot
    {
        get => _settings.Current.FolderRoot;
        set
        {
            _settings.Current.FolderRoot = string.IsNullOrWhiteSpace(value) ? null : value;
            RequestSave();
        }
    }

    public WidgetTheme Theme => _settings.Current.Theme;

    /// <summary>Redraws every widget in <paramref name="theme"/> and saves it.</summary>
    public void SetTheme(WidgetTheme theme)
    {
        if (theme == Theme)
            return;
        _settings.Current.Theme = theme;
        RequestSave();
        foreach (var window in _windows)
            window.ApplyTheme(theme);
        _tray?.SetPixelIcon(theme == WidgetTheme.Pixel);
    }

    public void Start()
    {
        Win32.TryEnableDarkMenus();
        Services.Devices.Start();

        var settings = _settings.Current;
        if (!settings.Initialized)
        {
            var battery = WidgetCatalog.Find("battery")!;
            var position = GetDefaultPosition(battery.DefaultSize, except: null);
            settings.Widgets.Add(new WidgetPlacement { Type = battery.TypeId, Size = battery.DefaultSize, X = position.X, Y = position.Y });
            settings.Initialized = true;
            RequestSave();
        }
        foreach (var placement in settings.Widgets.ToList())
            Open(placement);
        _icons.SetBlocked(GetCardRects(except: null));

        _tray = new TrayIcon(TrayTooltip(), Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        _tray.MenuRequested += ShowTrayMenu;
        _tray.SetPixelIcon(Theme == WidgetTheme.Pixel);

        _showDesktop = new ShowDesktopWatcher(Services.Dispatcher, CheckShowDesktop);
        CheckShowDesktop();
    }

    /// <summary>
    /// Win+D (Show desktop) lifts the desktop window over everything that isn't
    /// topmost, which hides widgets sitting at the bottom of the z-order. While
    /// that is so, keep the widgets just above the desktop; as soon as an
    /// application window takes the foreground, put them back at the bottom.
    /// </summary>
    private void CheckShowDesktop()
    {
        if (_exiting)
            return;
        if (!_overDesktop)
        {
            var widget = _windows.FirstOrDefault(w => !w.Placement.AlwaysOnTop);
            if (widget is null || !Win32.IsDesktopAbove(widget.Handle))
                return;
            _overDesktop = true;
        }
        else if (Win32.ForegroundIsAppWindow())
        {
            _overDesktop = false;
        }
        else
        {
            return;
        }
        foreach (var window in _windows)
            window.SetOverDesktop(_overDesktop);
    }

    public void AddWidget(IWidgetFactory factory)
    {
        var position = GetDefaultPosition(factory.DefaultSize, except: null);
        var placement = new WidgetPlacement { Type = factory.TypeId, Size = factory.DefaultSize, X = position.X, Y = position.Y };
        _settings.Current.Widgets.Add(placement);
        Open(placement);
        RequestSave();
    }

    public void RemoveWidget(WidgetWindow window)
    {
        _settings.Current.Widgets.Remove(window.Placement);
        _windows.Remove(window);
        window.CloseFromHost();
        RequestSave();
    }

    /// <summary>Saves soon. Every change to the widgets' layout comes through here, so the icon arranger hears of it too.</summary>
    public void RequestSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
        _icons.SetBlocked(GetCardRects(except: null));
    }

    public IEnumerable<Win32.RECT> GetCardRects(WidgetWindow? except)
        => _windows.Where(w => w != except).Select(w => w.CardRect).ToList();

    public void OnDisplayChanged()
    {
        foreach (var window in _windows)
            window.EnsureOnScreen();
        _icons.Invalidate();
    }

    /// <summary>
    /// Window position for a new widget: top-right of the primary work area,
    /// stepping down (then left) past the widgets already there — on the
    /// desktop grid when grid snapping is on, from the top-left while the
    /// icons are arranged on the right.
    /// </summary>
    public PointInt32 GetDefaultPosition(WidgetSize size, WidgetWindow? except)
    {
        var work = Win32.GetWorkArea(default);
        var corner = new Win32.POINT { X = work.Left + 1, Y = work.Top + 1 };
        double scale = Win32.GetScaleAt(corner);
        var card = WidgetLayout.GetCardPixels(size, Theme, scale);
        int width = card.Width, height = card.Height;
        int gap = (int)Math.Round(16 * scale);
        var occupied = GetCardRects(except).ToList();

        if (SnapToDesktopGrid)
        {
            var slots = GridAt(corner).Slots(width, height, fromLeft: ArrangeDesktopIcons).ToList();
            var free = slots.FirstOrDefault(p => !occupied.Any(o => Intersects(o, new Win32.RECT { Left = p.X, Top = p.Y, Right = p.X + width, Bottom = p.Y + height })), slots.FirstOrDefault());
            return free;
        }

        int left = work.Right - gap - width;
        int top = work.Top + gap;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            var candidate = new Win32.RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
            var blocker = occupied.FirstOrDefault(o => Intersects(o, candidate), new Win32.RECT());
            if (blocker.Width == 0)
                break;
            top = blocker.Bottom + gap;
            if (top + height > work.Bottom)
            {
                top = work.Top + gap;
                left -= width + gap;
            }
        }
        return new PointInt32(left, top);
    }

    private static bool Intersects(Win32.RECT a, Win32.RECT b)
        => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    /// <summary>Quits from a widget's menu, but only if the user says Yes; No is the default button, so a stray Enter doesn't quit.</summary>
    public void ConfirmExit(nint owner)
    {
        if (Theme == WidgetTheme.Pixel)
        {
            PixelDialog.AskExit(owner, yes =>
            {
                if (yes)
                    Exit();
            });
            return;
        }
        int answer = Win32.MessageBox(owner, Strings.ExitQuestion + "\n\n" + Strings.ExitDetail, Strings.AppName,
            Win32.MB_YESNO | Win32.MB_ICONQUESTION | Win32.MB_DEFBUTTON2 | Win32.MB_SETFOREGROUND | Win32.MB_TOPMOST);
        if (answer == Win32.IDYES)
            Exit();
    }

    public void Exit()
    {
        if (_exiting)
            return;
        _exiting = true;
        _saveTimer.Stop();
        _settings.Save();
        foreach (var window in _windows.ToList())
            window.CloseFromHost();
        _windows.Clear();
        _showDesktop?.Dispose();
        _icons.Dispose();
        _tray?.Dispose();
        Services.Devices.Dispose();
        Log.Info("exit");
        Application.Current.Exit();
    }

    private void Open(WidgetPlacement placement)
    {
        var factory = WidgetCatalog.Find(placement.Type);
        if (factory is null)
        {
            Log.Warn($"Unknown widget type '{placement.Type}' in settings, skipped");
            return;
        }
        if (!factory.SupportedSizes.Contains(placement.Size))
            placement.Size = factory.DefaultSize;
        try
        {
            var window = new WidgetWindow(this, factory, placement);
            _windows.Add(window);
            window.Show();
            if (_overDesktop)
                window.SetOverDesktop(true);
        }
        catch (Exception ex)
        {
            Log.Error($"Opening a '{placement.Type}' widget failed", ex);
        }
    }

    private void ResnapAll()
    {
        foreach (var window in _windows)
            window.EnsureOnScreen();
    }

    /// <summary>Turns icon arranging on or off. Widgets re-snap, because the grid's origin follows the icons.</summary>
    private void SetArrangeIcons(bool on)
    {
        if (on == ArrangeDesktopIcons)
            return;
        if (on && !IconArranger.MaySwitchOn(DataRoot.IsTestInstance))
            return; // moves the user's real desktop icons: a test instance only arranges if its settings file says so at startup
        _settings.Current.ArrangeDesktopIcons = on;
        _icons.Enabled = on;
        RequestSave();
        ResnapAll();
        _tray?.SetTooltip(TrayTooltip());
    }

    private void ToggleStartup()
    {
        try
        {
            StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled);
        }
        catch (Exception ex)
        {
            Log.Warn("Changing Start with Windows failed", ex);
            ShowWarning(Strings.StartupFailed + ex.Message);
        }
    }

    public void ShowWarning(string text)
    {
        if (Theme == WidgetTheme.Pixel)
            PixelDialog.ShowWarning(0, text);
        else
            Win32.MessageBox(0, text, Strings.AppName, Win32.MB_ICONWARNING | Win32.MB_SETFOREGROUND | Win32.MB_TOPMOST);
    }

    private void ShowIconStatus(IconArrangerStatus status)
    {
        _iconStatus = status;
        _tray?.SetTooltip(TrayTooltip());
    }

    private string TrayTooltip()
    {
        string name = DataRoot.IsTestInstance ? Strings.AppName + " (test)" : Strings.AppName;
        return ArrangeDesktopIcons ? $"{name} · {IconStatusText()}" : name;
    }

    private string IconStatusText() => _iconStatus.State switch
    {
        IconArrangerState.Arranging when ArrangeDesktopIcons => Strings.IconsStatusArranging(_iconStatus.IconCount),
        IconArrangerState.Waiting when ArrangeDesktopIcons => _iconStatus.Problem ?? Strings.IconsDesktopNotReady,
        _ when ArrangeDesktopIcons => Strings.IconsStatusStarting,
        _ => DataRoot.IsTestInstance ? Strings.IconsTestOff : Strings.IconsStatusOff,
    };

    /// <summary>Switches every widget, menu and the tray tooltip to <paramref name="language"/> and saves it.</summary>
    private void SetLanguage(AppLanguage language)
    {
        if (language == Strings.Language)
            return;
        Strings.Language = language;
        _settings.Current.Language = language;
        RequestSave();
        foreach (var window in _windows)
            window.ApplyLanguage();
        _tray?.SetTooltip(TrayTooltip());
        Services.Devices.RefreshAll(); // demo devices are named in the UI language
    }

    /// <summary>
    /// Design QA (<c>--pixel-ui-open=widget|cities|tray|exit|warn|folder-panel</c>, <c>--pixel-ui-zh</c> for Chinese): opens one menu, dialog or
    /// Big Folder panel as the keyboard would, so it can be captured; <c>folder-panel</c> works in both themes and takes
    /// <c>--folder-panel-search=text</c> and <c>--folder-panel-select=n</c>. The language change is in memory only.
    /// </summary>
    internal async void OpenForCapture(string what, bool chinese, Widgets.Folder.FolderCapture? folder = null)
    {
        folder ??= new Widgets.Folder.FolderCapture();
        if (chinese)
            Strings.Language = AppLanguage.Chinese;
        await Task.Delay(1500);
        var work = Win32.GetWorkArea(default);
        int x = work.Left + 400, y = work.Top + 200;
        var window = _windows.FirstOrDefault(w => w.TypeId == (what == "cities" ? "worldclock" : what is "folder-panel" or "folder-item-menu" ? "folder" : "battery")) ?? _windows[0];
        switch (what)
        {
            case "folder-panel":
                window.FolderWidget?.OpenPanelForCapture(folder);
                break;
            case "folder-cards":
                foreach (var card in _windows)
                    card.FolderWidget?.ShowDropHintForCapture(folder.Drop);
                break;
            case "folder-item-menu":
                if (window.FolderWidget?.FirstItemMenuForCapture() is { } rows)
                    PixelMenu.Show(rows, x, y);
                break;
            case "widget":
                PixelMenu.Show(window.BuildMenu(), x, y);
                break;
            case "cities":
            {
                var menu = PixelMenu.Show(window.BuildMenu(), x, y);
                foreach (int key in new[] { PixelUi.Native.VK_DOWN, PixelUi.Native.VK_DOWN, PixelUi.Native.VK_RIGHT, PixelUi.Native.VK_RIGHT })
                    menu.OnKey(key);
                break;
            }
            case "tray":
                PixelMenu.Show(BuildTrayMenu(), work.Right - 140, work.Top);
                break;
            case "exit":
                PixelDialog.AskExit(window.Handle, _ => { });
                break;
            case "warn":
                PixelDialog.ShowWarning(0, Strings.StartupFailed + (Strings.IsChinese ? "拒绝访问。" : "Access is denied."));
                break;
        }
    }

    /// <summary>
    /// Leak check (<c>--folder-panel-cycle=n --cycle-out=file</c>): opens and closes every Big Folder's panel <paramref name="cycles"/> times, as
    /// a click on the header and an Esc would (<c>--cycle-dirty</c> leaves a search and a selection behind each time, <c>--cycle-hold</c> keeps the
    /// last open up for a capture), and appends the process's handle, GDI and USER counts to <paramref name="file"/> after each round.
    /// </summary>
    internal async void CyclePanels(int cycles, string? file, bool dirty, bool hold)
    {
        await Task.Delay(2500);
        var folders = _windows.Select(w => w.FolderWidget).OfType<Widgets.Folder.FolderWidget>().ToList();
        void Report(string line)
        {
            var self = Process.GetCurrentProcess();
            string text = $"{line}: handles={self.HandleCount} gdi={PixelUi.Native.GetGuiResources(self.Handle, 0)} user={PixelUi.Native.GetGuiResources(self.Handle, 1)} private={self.PrivateMemorySize64 / 1024 / 1024} MB threads={self.Threads.Count}";
            if (file is not null)
                File.AppendAllText(file, text + Environment.NewLine);
        }
        Report($"start ({folders.Count} folder cards)");
        for (int round = 1; round <= cycles; round++)
        {
            foreach (var folder in folders)
            {
                var panel = folder.OpenPanel();
                if (dirty) // leave a search, a selection and a hover behind, for the next open to clear
                    panel.PrepareForCapture(new Widgets.Folder.FolderCapture(Search: "note", Select: 2, Hover: 1));
                await Task.Delay(700);
                folder.ClosePanelForCapture();
                await Task.Delay(500);
            }
            if (round % 5 == 0 || round == cycles)
                Report($"round {round}");
        }
        await Task.Delay(3000);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Report("done, after a GC");
        if (hold) // one more open, which stays up for a capture of the reset panel
            folders[^1].OpenPanelForCapture(new Widgets.Folder.FolderCapture());
    }

    /// <summary>The tray menu as a model: both themes show these rows, in this order.</summary>
    private List<MenuEntry> BuildTrayMenu() =>
    [
        .. DataRoot.IsTestInstance ? new[] { MenuEntry.Status(Strings.TestInstanceStatus(DataRoot.Path)), MenuEntry.Separator() } : Array.Empty<MenuEntry>(),
        MenuEntry.Sub(Strings.AddWidget,
            [.. WidgetCatalog.All.Select(factory => MenuEntry.Item(factory.DisplayName, () => AddWidget(factory), glyph: factory.Glyph, icon: PixelIcons.ForWidget(factory.TypeId)))],
            icon: PixelIcons.Add),
        MenuEntry.Item(Strings.RefreshDevices, Services.Devices.RefreshAll, icon: PixelIcons.Refresh),
        MenuEntry.Separator(),
        MenuEntry.Toggle(Strings.SnapToDesktopGrid, SnapToDesktopGrid, () =>
        {
            _settings.Current.SnapToDesktopGrid = !SnapToDesktopGrid;
            RequestSave();
            ResnapAll(); // under the new rule
        }),
        MenuEntry.Sub(Strings.IconsMenu,
        [
            MenuEntry.Status(IconStatusText()),
            MenuEntry.Separator(),
            // A test instance can switch arranging off, never on: that would move the user's real desktop icons.
            MenuEntry.Toggle(Strings.IconsArrangeRight, ArrangeDesktopIcons, () => SetArrangeIcons(!ArrangeDesktopIcons), enabled: IconArranger.MaySwitchOn(DataRoot.IsTestInstance) || ArrangeDesktopIcons),
            MenuEntry.Item(Strings.IconsArrangeNow, () =>
            {
                SetArrangeIcons(true);
                _icons.Invalidate();
            }, enabled: IconArranger.MaySwitchOn(DataRoot.IsTestInstance)),
        ]),
        MenuEntry.Toggle(StartupRegistration.Available ? Strings.StartWithWindows : Strings.StartWithWindowsTest, StartupRegistration.IsEnabled, ToggleStartup, enabled: StartupRegistration.Available),
        MenuEntry.Sub(Strings.ThemeMenu,
            [.. Enum.GetValues<WidgetTheme>().Select(theme => MenuEntry.Radio(Strings.ThemeName(theme), theme == Theme, () => SetTheme(theme), group: "theme"))],
            icon: PixelIcons.Theme),
        MenuEntry.Sub(Strings.LanguageMenu,
            [.. Enum.GetValues<AppLanguage>().Select(language => MenuEntry.Radio(Strings.LanguageName(language), language == Strings.Language, () => SetLanguage(language), group: "language"))]),
        MenuEntry.Item(Strings.OpenLogs, () =>
        {
            Directory.CreateDirectory(Log.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.LogDirectory}\"") { UseShellExecute = true });
        }),
        MenuEntry.Separator(),
        MenuEntry.Item(Strings.Exit, Exit),
    ];

    private void ShowTrayMenu(int x, int y)
    {
        var entries = BuildTrayMenu();
        if (Theme == WidgetTheme.Pixel)
        {
            PixelMenu.Show(entries, x, y);
            return;
        }
        nint menu = Win32MenuAdapter.Build(entries, out var actions);
        uint command = _tray!.ShowMenu(menu, x, y);
        Win32.DestroyMenu(menu); // also destroys the submenus
        if (actions.TryGetValue(command, out var action))
            action();
    }
}
