using System.Diagnostics;
using DeskWidgets.Core.Devices;
using DeskWidgets.Core.Diagnostics;
using DeskWidgets.Desktop;
using DeskWidgets.Devices.Bluetooth;
using DeskWidgets.Devices.Hid;
using DeskWidgets.Interop;
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
    private const uint CommandRefresh = 1;
    private const uint CommandStartup = 2;
    private const uint CommandLogs = 3;
    private const uint CommandGrid = 4;
    private const uint CommandIconsToggle = 5;
    private const uint CommandIconsArrangeNow = 6;
    private const uint CommandExit = 9;
    private const uint CommandLanguageBase = 50;
    private const uint CommandThemeBase = 70;
    private const uint CommandAddBase = 100;

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
        _settings.Current.ArrangeDesktopIcons = on;
        _icons.Enabled = on;
        RequestSave();
        ResnapAll();
        _tray?.SetTooltip(TrayTooltip());
    }

    private static void ToggleStartup()
    {
        try
        {
            StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled);
        }
        catch (Exception ex)
        {
            Log.Warn("Changing Start with Windows failed", ex);
            ShowMessage(Strings.StartupFailed + ex.Message, Win32.MB_ICONWARNING);
        }
    }

    private static int ShowMessage(string text, uint type)
        => Win32.MessageBox(0, text, Strings.AppName, type | Win32.MB_SETFOREGROUND | Win32.MB_TOPMOST);

    private void ShowIconStatus(IconArrangerStatus status)
    {
        _iconStatus = status;
        _tray?.SetTooltip(TrayTooltip());
    }

    private string TrayTooltip() => ArrangeDesktopIcons ? $"{Strings.AppName} · {IconStatusText()}" : Strings.AppName;

    private string IconStatusText() => _iconStatus.State switch
    {
        IconArrangerState.Arranging when ArrangeDesktopIcons => Strings.IconsStatusArranging(_iconStatus.IconCount),
        IconArrangerState.Waiting when ArrangeDesktopIcons => _iconStatus.Problem ?? Strings.IconsDesktopNotReady,
        _ => ArrangeDesktopIcons ? Strings.IconsStatusStarting : Strings.IconsStatusOff,
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

    private void ShowTrayMenu(int x, int y)
    {
        nint menu = Win32.CreatePopupMenu();
        nint addMenu = Win32.CreatePopupMenu();
        for (int i = 0; i < WidgetCatalog.All.Count; i++)
            Win32.AppendMenu(addMenu, Win32.MF_STRING, CommandAddBase + (uint)i, WidgetCatalog.All[i].DisplayName);
        nint languageMenu = Win32.CreatePopupMenu();
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            uint check = language == Strings.Language ? Win32.MF_CHECKED : 0;
            Win32.AppendMenu(languageMenu, Win32.MF_STRING | check, CommandLanguageBase + (uint)language, Strings.LanguageName(language));
        }
        nint themeMenu = Win32.CreatePopupMenu();
        foreach (var theme in Enum.GetValues<WidgetTheme>())
        {
            uint check = theme == Theme ? Win32.MF_CHECKED : 0;
            Win32.AppendMenu(themeMenu, Win32.MF_STRING | check, CommandThemeBase + (uint)theme, Strings.ThemeName(theme));
        }
        nint iconsMenu = Win32.CreatePopupMenu();
        Win32.AppendMenu(iconsMenu, Win32.MF_STRING | Win32.MF_GRAYED, 0, IconStatusText());
        Win32.AppendMenu(iconsMenu, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenu(iconsMenu, Win32.MF_STRING | (ArrangeDesktopIcons ? Win32.MF_CHECKED : 0), CommandIconsToggle, Strings.IconsArrangeRight);
        Win32.AppendMenu(iconsMenu, Win32.MF_STRING, CommandIconsArrangeNow, Strings.IconsArrangeNow);
        Win32.AppendMenu(menu, Win32.MF_POPUP, (nuint)addMenu, Strings.AddWidget);
        Win32.AppendMenu(menu, Win32.MF_STRING, CommandRefresh, Strings.RefreshDevices);
        Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenu(menu, Win32.MF_STRING | (SnapToDesktopGrid ? Win32.MF_CHECKED : 0), CommandGrid, Strings.SnapToDesktopGrid);
        Win32.AppendMenu(menu, Win32.MF_POPUP, (nuint)iconsMenu, Strings.IconsMenu);
        Win32.AppendMenu(menu, Win32.MF_STRING | (StartupRegistration.IsEnabled ? Win32.MF_CHECKED : 0), CommandStartup, Strings.StartWithWindows);
        Win32.AppendMenu(menu, Win32.MF_POPUP, (nuint)themeMenu, Strings.ThemeMenu);
        Win32.AppendMenu(menu, Win32.MF_POPUP, (nuint)languageMenu, Strings.LanguageMenu);
        Win32.AppendMenu(menu, Win32.MF_STRING, CommandLogs, Strings.OpenLogs);
        Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenu(menu, Win32.MF_STRING, CommandExit, Strings.Exit);

        uint command = _tray!.ShowMenu(menu, x, y);
        Win32.DestroyMenu(menu); // also destroys the submenus

        switch (command)
        {
            case CommandRefresh:
                Services.Devices.RefreshAll();
                break;
            case CommandStartup:
                ToggleStartup();
                break;
            case CommandGrid:
                _settings.Current.SnapToDesktopGrid = !SnapToDesktopGrid;
                RequestSave();
                ResnapAll(); // under the new rule
                break;
            case CommandIconsToggle:
                SetArrangeIcons(!ArrangeDesktopIcons);
                break;
            case CommandIconsArrangeNow:
                SetArrangeIcons(true);
                _icons.Invalidate();
                break;
            case CommandLogs:
                Directory.CreateDirectory(Log.LogDirectory);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.LogDirectory}\"") { UseShellExecute = true });
                break;
            case CommandExit:
                Exit();
                break;
            case >= CommandLanguageBase and < CommandThemeBase:
                SetLanguage((AppLanguage)(command - CommandLanguageBase));
                break;
            case >= CommandThemeBase and < CommandAddBase:
                SetTheme((WidgetTheme)(command - CommandThemeBase));
                break;
            case >= CommandAddBase when command - CommandAddBase < WidgetCatalog.All.Count:
                AddWidget(WidgetCatalog.All[(int)(command - CommandAddBase)]);
                break;
        }
    }
}
