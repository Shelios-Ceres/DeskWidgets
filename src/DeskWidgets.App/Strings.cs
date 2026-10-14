using System.Globalization;
using DeskWidgets.Widgets;

namespace DeskWidgets;

/// <summary>The app's UI languages, picked in the tray menu and saved in settings.</summary>
internal enum AppLanguage
{
    English,
    Chinese,
}

/// <summary>UI text in the language picked in the tray menu (English by default).</summary>
internal static class Strings
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo ChineseCulture = CultureInfo.GetCultureInfo("zh-CN");

    /// <summary>Set from settings at startup and when the user switches language.</summary>
    public static AppLanguage Language { get; set; } = AppLanguage.English;

    public static bool IsChinese => Language == AppLanguage.Chinese;

    /// <summary>Formats dates (weekday and month names) in the UI language.</summary>
    public static CultureInfo Culture => IsChinese ? ChineseCulture : EnglishCulture;

    private static string T(string zh, string en) => IsChinese ? zh : en;

    /// <summary>A language's own name, whichever language is active.</summary>
    public static string LanguageName(AppLanguage language) => language == AppLanguage.Chinese ? "中文" : "English";

    public static string AppName => T("桌面小组件", "Desk Widgets");
    public static string BatteryWidget => T("电池", "Batteries");
    public static string ClockWidget => T("时钟", "Clock");
    public static string WorldClockWidget => T("世界时钟", "World Clock");

    public static string Size => T("大小", "Size");
    public static string SizeSmall => T("小", "Small");
    public static string SizeMedium => T("中", "Medium");
    public static string SizeLarge => T("大", "Large");
    public static string AlwaysOnTop => T("置顶显示", "Always on top");
    public static string LockPosition => T("锁定位置", "Lock position");
    public static string AddWidget => T("添加小组件", "Add widget");
    public static string RemoveWidget => T("移除小组件", "Remove widget");
    public static string RefreshDevices => T("刷新设备", "Refresh devices");
    public static string StartWithWindows => T("开机自动启动", "Start with Windows");
    public static string SnapToDesktopGrid => T("对齐桌面网格", "Snap to desktop grid");
    public static string LanguageMenu => T("语言 / Language", "Language");
    public static string ThemeMenu => T("主题", "Theme");
    public static string ThemeName(WidgetTheme theme) => theme == WidgetTheme.Pixel ? T("像素", "Pixel") : T("毛玻璃", "Glass");
    public static string OpenLogs => T("打开日志文件夹", "Open log folder");
    public static string Exit => T("退出", "Exit");

    public static string ThisPc => T("此电脑", "This PC");
    public static string ShowDeviceNames => T("显示设备名称", "Show device names");
    public static string ShowDisconnected => T("显示未连接的设备", "Show disconnected devices");
    public static string NoDevices => T("没有已连接的设备", "No connected devices");
    public static string Bluetooth => T("蓝牙", "Bluetooth");
    public static string BuiltIn => T("内置电池", "Built-in");
    public static string Disconnected => T("未连接", "Not connected");
    public static string Charging => T("正在充电", "Charging");
    public static string PluggedIn => T("已接通电源", "Plugged in");

    public static string ChangeCities => T("更换城市", "Change cities");
    public static string CitySlot(int number, string city) => T($"城市 {number}：{city}", $"City {number}: {city}");
    public static string ShowSecondHand => T("显示秒针", "Show second hand");
    public static string Today => T("今天", "Today");
    public static string Yesterday => T("昨天", "Yesterday");
    public static string Tomorrow => T("明天", "Tomorrow");
    public static string DayAndOffset(string day, string hours) => T($"{day} {hours}小时", $"{day} {hours}h");

    /// <summary>Date under each world-clock time: "Oct 13" / "10月13日".</summary>
    public static string CityDatePattern => T("M月d日", "MMM d");
}
