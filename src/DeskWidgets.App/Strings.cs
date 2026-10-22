using System.Globalization;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Folder;

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
    public static string ExitQuestion => T("退出桌面小组件？", "Quit Desk Widgets?");
    public static string Yes => T("是", "Yes");
    public static string No => T("否", "No");
    public static string Ok => T("确定", "OK");
    public static string ExitDetail => T("所有小组件都会关闭，直到你再次启动本程序。", "The widgets will close until you start the app again.");

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

    public static string StartupFailed => T("没能设置开机自动启动：", "Couldn't change Start with Windows: ");

    /// <summary>The tray's first line in a test instance (--data-dir): it is not the user's own app.</summary>
    public static string TestInstanceStatus(string folder) => T($"测试实例 · 数据：{folder}", $"Test instance · data: {folder}");
    public static string StartWithWindowsTest => T("开机自动启动（测试实例中不可用）", "Start with Windows (not in a test instance)");
    public static string IconsTestOff => T("测试实例：只有测试设置能开启", "Test instance: only its settings file can turn this on");

    public static string IconsMenu => T("桌面图标", "Desktop icons");
    public static string IconsArrangeRight => T("靠右自动排列", "Keep arranged on the right");
    public static string IconsArrangeNow => T("立即整理", "Arrange now");
    public static string IconsStatusOff => T("未开启自动排列", "Not arranging");
    public static string IconsStatusArranging(int count) => T($"自动排列中 · {count} 个图标", $"Arranging · {count} icons");
    public static string IconsStatusStarting => T("正在整理图标…", "Starting to arrange…");
    public static string IconsDesktopNotReady => T("等待桌面就绪", "Waiting for the desktop");
    public static string IconsWaitingForIcons => T("等待桌面图标加载", "Waiting for the icons to load");
    public static string IconsNotIconView => T("桌面当前不是图标视图", "The desktop isn't showing icons");
    public static string IconsDuplicate => T("桌面上有两个项目标识相同，没有移动图标", "Two desktop items look the same; nothing was moved");
    public static string IconsNoRoom => T("桌面放不下所有图标，这次没有移动任何图标", "Not enough room for every icon; nothing was moved");
    public static string IconsChanging => T("图标正在变化，稍后重试", "Icons are changing; trying again shortly");
    public static string IconsNotKept => T("Explorer 还没保留新位置，稍后再核对", "Explorer hasn't kept the new positions yet; checking again");
    // The Big Folder widget. Words are settled in notes/builder/QUEUE.md (Phase 4, "Main's alignment for Phase 5").
    public static string FolderWidget => T("大文件夹", "Big Folder");
    public static string FolderEmpty => T("将文件拖到这里", "Drop files here");
    public static string FolderDropMove(string folder) => T($"移动到“{folder}”", $"Move to {folder}");
    public static string FolderDropCopy(string folder) => T($"复制到“{folder}”", $"Copy to {folder}");
    public static string FolderDropShortcut(string folder) => T($"在“{folder}”中创建快捷方式", $"Create shortcut in {folder}");
    public static string FolderDropMixed(string folder) => T($"添加到“{folder}”", $"Add to {folder}");
    public static string FolderMissing => T("找不到文件夹", "Folder not found");
    public static string FolderMissingHint => T("右键 › 选择文件夹…", "Right-click › Choose folder…");
    public static string FolderSearch => T("搜索", "Search");
    public static string FolderItemCount(int count) => T($"{count} 项", count == 1 ? "1 item" : $"{count} items");
    public static string FolderSelected(int count) => T($"已选择 {count} 项", $"{count} selected");
    public static string FolderNoMatches => T("没有匹配项", "No matches");
    public static string FolderSortButton(FolderSort sort) => sort switch
    {
        FolderSort.Name => T("名称", "Name"),
        FolderSort.Type => T("类型", "Type"),
        FolderSort.Manual => T("手动", "Manual"),
        _ => T("最新", "Newest"),
    };

    public static string FolderSortRow(FolderSort sort) => sort switch
    {
        FolderSort.Name => T("名称", "Name"),
        FolderSort.Type => T("类型", "Type"),
        FolderSort.Manual => T("手动", "Manual"),
        _ => T("最新在前", "Newest first"),
    };

    public static string FolderOpen => T("打开", "Open");
    public static string FolderRunAsAdministrator => T("以管理员身份运行", "Run as administrator");
    public static string FolderOpenFileLocation => T("打开文件所在的位置", "Open file location");
    public static string FolderShowInExplorer => T("在资源管理器中显示", "Show in Explorer");
    public static string FolderRename => T("重命名", "Rename");
    public static string FolderPutBack => T("放回原处", "Put back");
    public static string FolderMoveToRecycleBin => T("移到回收站", "Move to Recycle Bin");
    public static string FolderRemoveFromFolder => T("从文件夹中移除", "Remove from folder");
    public static string FolderProperties => T("属性", "Properties");
    public static string FolderOpenInExplorer => T("在资源管理器中打开文件夹", "Open folder in Explorer");
    public static string FolderAddApps => T("添加应用…", "Add apps…");
    public static string FolderSortBy => T("排序方式", "Sort by");
    public static string FolderChange => T("更换文件夹…", "Change folder…");
    public static string FolderChoose => T("选择文件夹…", "Choose folder…");
    public static string FolderRenameFolder => T("重命名文件夹…", "Rename folder…");
    public static string FolderPickerTitle => T("选择大文件夹要显示的文件夹", "Choose the folder this card shows");
    public static string FolderDefaultName => T("大文件夹", "Big Folder");
    public static string FolderRootRow => T("新文件夹的默认位置…", "Default location for new folders…");
    public static string FolderRootPickerTitle => T("选择新的大文件夹的默认位置", "Choose where new Big Folders are made");
    public static string FolderOperationFailed(string detail) => T($"没能完成操作：{detail}", $"That didn't work: {detail}");

    public static string IconsLegacyToolRunning => T("旧的「桌面图标靠右排列」程序还在运行，请先退出它", "The old DesktopRightAlign tool is still running; quit it first");
}
