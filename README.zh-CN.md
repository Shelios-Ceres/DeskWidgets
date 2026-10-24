> 本文是 [README.md](README.md) 的中文翻译，以英文版为准；两者不一致时请看英文版。

# DeskWidgets · 桌面小组件

WinUI 3 写的 Windows 桌面小组件（桌贴）。第一个组件是仿 Apple「电池」小组件的无线设备电量面板：
蓝牙设备、2.4G 接收器鼠标，统一用圆环显示电量与充电状态。

- **薄毛玻璃**：DWM 的 host backdrop 实时模糊窗口背后的内容（壁纸、动态壁纸、图标、其他窗口都算），裁成 22px 抗锯齿圆角卡片；上面只叠一层很薄的底色和斜向高光，没有描边
- **像素主题**：另一套可切换的外观（右键小组件或托盘图标 →「主题」→ 毛玻璃 / 像素）。不是换皮：每个组件在低分辨率画布上逐像素绘制（1 个美术像素 = 2 DIP，小号卡片正好 82×82），再按整数倍放大成屏幕像素（150% 缩放时每个像素 3×3），边缘永远是硬的。电池像游戏背包：设备图标放在凹陷格子里，下面是像素电池条；时钟是方块大数字配太阳/月亮；世界时钟是像素表盘。两套手绘大写像素字体（3×5、5×7），字体里没有的字（如中文）整串改用宋体 12px 点阵，大文件夹除外：它的中文先用打包的 Fusion Pixel Font 8px（每格四个汉字），表情则是从 Segoe UI Emoji 缩成的 7×7 图块；昼夜两套配色（基于 Sweetie 16 色板）跟随系统浅色/深色。像素主题下，程序自己的菜单和对话框也是像素画：小组件菜单、托盘菜单、退出确认和错误框的画法与卡片一致，带硬阴影，托盘图标也换成像素图标；毛玻璃沿用系统菜单
- **主题规范**：新增或修改主题前先读 [docs/THEMES.zh-CN.md](docs/THEMES.zh-CN.md)：所有主题共同遵守的规则（尺寸、信息底线、颜色含义、语言）、各主题规格、改动分级和验收清单
- **三种尺寸**：小（单设备大圆环 / 2×2 圆环）、中（一排 4 个圆环 + 设备名 + 百分比）、大（列表）；设备名可在右键菜单关闭
- **世界时钟**：仿 Apple「世界时钟」小组件，同时显示 4 个城市（默认 New York、Los Angeles、St. John's（纽芬兰）、Beijing，城市名统一用英文）。表盘 6:00–18:00 为白色、夜间为深色。小 = 2×2 表盘；中 = 一排 4 个，下方依次是城市名、24 小时制时间和当地日期（Oct 13 / 10月13日；像素主题中文为 10/13）；大 = 列表，附与本地的时差（如「今天 −12小时」，半小时时区显示为 +1.5），右侧是时间，下方是当地日期。内置 26 个城市可在右键菜单更换，秒针可关闭
- **大文件夹**：手机风格、建在真实文件夹上的卡片。新卡片会在用户目录下、桌面所在的盘上自己建一个文件夹，或者建在你选定的默认位置（右键菜单里的「新文件夹的默认位置…」，保存为 `settings.json` 里的 `FolderRoot`）；「更换文件夹…」可以让某一张卡片指向任何其他文件夹。小号 = 2×2 个图标，中号 = 4×2，大号 = 4×4；项目更多时，最后一格是后面几项的「+N」堆叠。点击某一项会打开它；点击标题或堆叠会打开面板（搜索、排序、Finder 风格选择、重命名、拖出），它在你操作时一直开着（操作弹出的对话框也不会让它关闭），你离开它（Esc、点别处、切到别的程序）或打开了什么之后才关闭；在任何地方拖动仍然是移动卡片。**把文件拖到卡片上**就会放进这个文件夹。从桌面或公共桌面拖来的普通项目总是移进去，跨盘也一样（Ctrl 复制，Alt 创建快捷方式）。其他普通文件按资源管理器的规则（同一个盘移动，另一个盘复制；Ctrl 复制，Shift 移动，Alt 创建快捷方式）。程序绝不会被移动：程序文件、绿色软件所在的文件夹、开始菜单或商店应用，以及 Program Files、Windows 或 ProgramData 下的任何东西、AppData 下的程序，都只创建快捷方式（从桌面拖来的快捷方式会被移进去）。卡片上的标签会说明放下后会怎样，资源管理器自己的进度和冲突窗口也会出现，所以在资源管理器里按 Ctrl+Z 就能撤销。文件类型和 13 个常见应用（微信、QQ、钉钉、腾讯会议、Word、Excel、PowerPoint、Chrome、Edge、Claude、ChatGPT、Cursor、Kimi）有我们自己的绘制（一种颜色加一个图案，绝不用 logo）；其他应用显示它真实的图标，照片显示真实的缩略图。项目菜单有打开、以管理员身份运行、打开文件所在的位置、在资源管理器中显示、重命名、放回原处、移到回收站（快捷方式是「从文件夹中移除」）、属性和显示更多选项（Windows 自己的右键菜单，对这个项目或选中的几个项目，有打开方式、发送到、压缩和各个应用自己的条目；两个主题里它都是 Windows 的菜单，菜单里的打开、重命名和删除用我们自己的）。面板的搜索还能用拼音找中文名：首字母（"bz" 找到 壁纸，"qqyy" 找到 QQ音乐）或完整音节（"bizhi"）。两个主题都有，像素主题有自己的面板。
- **贴在桌面上**：默认处于窗口最底层，不进任务栏和 Alt+Tab；Win+D「显示桌面」会把桌面抬到最上面，这时小组件留在桌面之上，打开任何窗口后立刻回到窗口后面
- **对齐桌面网格**：松手后吸附到桌面图标网格（格子大小实时读取资源管理器的图标间距，跟随图标大小设置），组件占整数个格子并居中；托盘菜单可关闭，关闭后改为吸附屏幕边缘和其他组件
- **桌面图标靠右排列**（托盘 →「Desktop icons / 桌面图标」，默认关闭）：合并了原来独立的 DesktopRightAlign 小程序。图标从右上角往下排，满一列再往左换列，新图标 1–3 秒内归位；图标会让开小组件占的格子，小组件和图标共用一套网格，此时新小组件从左上角开始放。开启后会关掉 Explorer 自己的「自动排列图标」和「将图标与网格对齐」，关闭排列后不会自动打开，需要时在桌面右键 →「查看」里重新勾选。只用微软公开的 Shell 接口（`IFolderView2`），不注入 Explorer；拖动图标、打开菜单或重命名时不会动图标。**手动调整顺序**：把图标拖到别的位置，它就留在那里（落在哪个格子，就排在第几个），其他图标顺势挪开；从文件夹拖进桌面的文件也一样。没有拖动、自己跑位的图标（比如 Explorer 在显示器变化后重排）仍会回到记住的顺序。先关掉排列、自由摆放，再重新打开，会沿用当时的顺序
- **界面语言**：English（默认）或中文，在托盘菜单「Language」里切换，所有小组件、菜单和日期立即生效
- **跟随系统**：浅色/深色主题、每台显示器的 DPI 缩放
- **可扩展**：新增小组件、新增设备数据源/接收器协议都只需实现一个接口

## 运行

需要 .NET 10 SDK（Windows App SDK 已随项目自包含，不用另装运行时）。

```bash
dotnet run --project D:/Widget/src/DeskWidgets.App
```

```bash
dotnet run --project D:/Widget/src/DeskWidgets.App -- --demo
```

`--demo` 用 4 个示例设备预览各种布局（电量低、充电中、未连接）。程序只允许运行一个实例，想切换模式先从托盘退出。

```bash
dotnet run --project D:/Widget/src/DeskWidgets.App -- --pixel-preview D:/Widget/artifacts/pixel-preview
```

`--pixel-preview <文件夹>` 把像素主题的全部布局（示例数据，昼/夜 × 中/英）画成 4 张 PNG 总览图后退出，调像素画不用来回摆组件；程序正在运行时也能用。

其他维护命令（运行完即退出，程序在运行时也能用）：

- `--icons-inspect <文件>`：只读检查桌面图标，把当前位置、小组件占用的区域，以及开启排列后会移动哪些图标写成 JSON。不移动图标，也不写别的文件
- `--pixel-menu-preview <文件夹>`：把像素主题的全部菜单、对话框和托盘图标（浅色/深色 × 中/英，150% 和 100%）画成 PNG 总览图后退出，和 `--pixel-preview` 对卡片的作用一样。`--pixel-ui-open=widget|cities|tray|exit|warn|folder-panel`（加 `--pixel-ui-zh` 用中文）启动程序时直接打开其中一个，用来截取真实窗口。`folder-panel` 在两个主题下都会打开第一个大文件夹的面板并一直开着（加 `--folder-panel-search=文字` 和 `--folder-panel-select=n` 可以显示搜索和选中状态）。
- `--pixel-folder-preview <文件夹>`：把像素主题的大文件夹（各个尺寸的卡片、它们的状态、图标和面板；浅色/深色 × 中/英）画成 PNG 总览图后退出。
- `--folder-panel-cycle=<n> --cycle-out=<文件>`：泄漏检查。它把每个大文件夹的面板开关 n 次（和点标题、按 Esc 一样），每轮把进程的句柄、GDI 和 USER 对象数追加到文件里（`--cycle-dirty` 每次留下搜索和选中，`--cycle-hold` 让最后一次保持打开）。数字应该一轮一轮保持平稳；句柄数一直涨，说明有东西创建了没释放。
- `--enable-startup` / `--disable-startup`：开启或关闭开机启动
- `--data-dir <文件夹>`（或环境变量 `DESKWIDGETS_DATA_DIR`，命令行参数优先）：以**测试实例**运行。四类数据（设置、图标顺序、大文件夹的来源记录和日志）都放在这个文件夹里，不再用 `%LOCALAPPDATA%\DeskWidgets`，所以测试永远不会读写你自己的设置。测试实例也不碰属于你的东西：读不了也改不了 `DeskWidgets` 登录任务（「开机自动启动」是灰的）；除非它自己的 `settings.json` 打开了，否则不排列桌面图标（托盘里只能关，不能开）；不接管 DesktopRightAlign 的设置；新建的大文件夹放在 `<文件夹>\Big Folders\`，不放在你的用户目录里。托盘菜单第一行写着「测试实例」。参数不可用时程序会以退出码 2 退出，不会退回去用你的数据。单实例互斥量是共用的，所以测试实例和你自己的程序不会同时运行。

编译好的程序在 `src\DeskWidgets.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\DeskWidgets.exe`，整个文件夹可以复制到任意位置直接运行。发布版：

```bash
dotnet publish D:/Widget/src/DeskWidgets.App -c Release -r win-x64 -o D:/Widget/artifacts/DeskWidgets
```

### 操作

| 操作 | 方式 |
|---|---|
| 移动 | 按住小组件拖动（松手时吸附） |
| 改大小 / 置顶 / 锁定位置 / 移除 / 退出（会先确认） | 右键小组件 |
| 添加小组件 / 对齐桌面网格 / 开机启动 / 语言 / 退出 | 右键或左键托盘图标 |
| 切换主题（毛玻璃 / 像素，所有组件一起换） | 右键小组件或托盘图标 →「主题」 |
| 桌面图标靠右排列 / 立即整理 | 托盘图标 →「桌面图标」（第一行显示当前状态） |
| 显示设备名称 / 显示未连接的设备 | 电池组件右键菜单 |
| 往大文件夹里放文件 | 拖到卡片上（Ctrl 复制，Shift 移动，Alt 创建快捷方式） |
| 打开项目 / 面板（大文件夹） | 点击某一项；点击标题或「+N」堆叠 |
| 打开文件夹 / 添加应用 / 排序 / 更换或重命名文件夹 / 选择新文件夹的建立位置 | 大文件夹的右键菜单 |
| 更换城市 / 显示秒针 | 世界时钟右键菜单 |

设置保存在 `%LOCALAPPDATA%\DeskWidgets\settings.json`，日志在 `logs\app.log`（上限 1 MB，自动轮转）。`--data-dir`（见上）可以把这一切搬到别的文件夹。设置不容易丢：好的副本存在 `backups\` 里（启动时存一份，之后设置有变化时每小时最多存一份；保留最新的 10 份，以及最近 7 天里每天最新的一份），`settings.json` 损坏或为空时，用最新一份能读的副本替换它（坏文件另存为 `settings.bad-<时间>.json`），并由 Windows 通知你；程序退出、Windows 注销、重启或关机时会把没写的改动写下来；如果一直存不进去，也会由 Windows 通知你。
桌面图标的排列顺序在同一文件夹的 `desktop-icons.json`；如果旧的 DesktopRightAlign 留下了它的设置，第一次使用时会直接接管。旧版本写的 `desktop-icons-original.json`（「恢复原来的布局」的备份，该功能已移除）不再使用，可以删掉。

「开机自动启动」用「任务计划程序」里当前用户的登录任务 `DeskWidgets`（登录 15 秒后启动，不需要密码和管理员权限），不用注册表的 Run 项：从 Claude 会话里写的 Run 项只进了 Claude 的私有注册表（见下文「在 Claude 会话里开发」），Explorer 看不到，2026-10-13 开机时就是这样没启动的。本机开着「开机自动启动」，程序就是由这个任务启动的。2026-10-14 到 10-19 期间曾改由单独的「AI Apps - Wait for TUN and Network」登录任务启动，那个任务现在已经删除。

### 在 Claude 会话里开发

Claude 桌面版是 MSIX 打包应用。从它的会话（包括 Claude Code 和其中的 `dotnet run`）启动的进程，写 `%LOCALAPPDATA%` 和 `HKCU` 时会被重定向到 Claude 的私有副本（`%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\` 和私有注册表），读的时候也优先读这份副本。所以：

- 在会话里启动的 DeskWidgets 用的是另一套设置、日志和图标数据，和登录时启动的那个互不影响；
- 会话里看到的 `%LOCALAPPDATA%\DeskWidgets` 不一定是真正运行的那份。要看真实文件，用资源管理器打开，或在会话里通过 WMI 读（`Get-CimInstance CIM_DataFile`）；
- 要在真实环境里试新版本，在资源管理器里双击 exe；会话里用 WMI 启动（`Invoke-CimMethod Win32_Process -MethodName Create -Arguments @{ CommandLine = '"<exe 路径>"' }`），不要用 `dotnet run`；
- 想试新版本又不碰自己的设置，在那条命令行后面加 `--data-dir <文件夹>`，文件夹放在 `%LOCALAPPDATA%` 之外（比如 `D:\Widget\.scratch\` 下面：容器看到的 `%LOCALAPPDATA%` 是私有副本，而 D: 盘上的文件夹对谁都一样）。`notes\tools\relaunch.ps1 -DataDir <文件夹>` 就是这么做的。先退出自己的程序，单实例互斥量只允许运行一个。

### 测试

```bash
dotnet test D:/Widget/tests/DeskWidgets.Tests -c Release
```

测试覆盖不依赖界面的逻辑：桌面网格吸附、图标排列、像素字体排版、世界时钟的时差和日期、卡片尺寸、`DeviceHub` 的汇总和扫描合并，以及大文件夹的拖放规则表、名称缩短、排序、来源记录和面板逻辑。用 Release 配置，程序运行时（Debug 版 exe 被占用）也能编译和跑测试。测试运行用自己的数据文件夹（`%TEMP%\DeskWidgets.Tests\data`，通过 `DESKWIDGETS_DATA_DIR`），不会碰你的设置和日志。

## 目录结构

```
docs/
  THEMES.md                主题设计规范（新增或修改主题前必读）
src/
  DeskWidgets.Core/        设备模型 DeviceStatus、IDeviceSource 接口、DeviceHub（汇总+轮询）、日志、DataRoot（唯一决定数据文件夹的地方）
  DeskWidgets.Devices/     Windows 数据源
    Bluetooth/               已配对蓝牙设备电量（读 Windows 设置页同款设备属性，不和设备通信）
    Hid/                     2.4G 接收器：HID 枚举、Feature Report 收发、只读监听、插拔监听、各厂商驱动
    SystemBatterySource      本机电池（默认不启用，只在 DeviceProbe 里用）
  DeskWidgets.App/         WinUI 3 宿主
    Desktop/                 桌面图标靠右排列：Shell 接口读写图标位置（ShellDesktop）、布局计算（IconLayout）、拖动检测（DesktopDragWatcher）、后台线程（IconArranger）、排列数据（IconStore）
    Hosting/                 透明窗口、实时毛玻璃（GlassBackdrop）、拖动吸附、托盘、设置、开机启动（登录任务）
    Pixel/                   像素主题：画布、手绘字体和精灵图、配色、卡片、整数倍放大显示（PixelSurface）、预览图
    PixelUi/                 像素菜单与对话框：分层弹出窗口（PixelPopupWindow）、菜单模型（MenuEntry）及其绘制（MenuPainter）、级联与键盘、鼠标、滚动（PixelMenu、MenuLevel）、对话框（PixelDialog、DialogPainter）、给 Narrator 用的 UI Automation 和 MSAA（Uia、Accessible）、像素托盘图标（PixelTrayIcon）
    Widgets/                 小组件接口 + 电池组件 + 时钟组件 + 世界时钟（WorldClock/）+ 大文件夹（Folder/：模型、拖放规则和文件操作，Shell 层在 Folder/Shell/，两个主题的卡片和面板）；各组件的 *PixelScene 是像素主题的画法
tests/
  DeskWidgets.Tests/       xUnit 单元测试（引用 App 和 Core，自带 Windows App SDK 运行时）
tools/
  DeskWidgets.DeviceProbe/ 命令行诊断：列出 HID 设备、各数据源扫描结果（--hid-all / --self-test / --listen 秒数）
  Make-AppIcon.ps1         重新生成程序图标
  Make-FusionFont.ps1      打包像素大文件夹内嵌的 Fusion Pixel Font 8px 字表（字体是 SIL OFL 1.1：许可文本是 Assets/Fonts/OFL-FusionPixel.txt，会复制到 exe 旁）
  Make-PinyinTable.ps1     用 Unicode 汉字数据库（Unihan_Readings.txt）生成大文件夹搜索用的拼音表（Unicode License v3：许可文本是 Assets/Pinyin/UNICODE-LICENSE.txt，会作为 UNICODE-LICENSE-Unihan.txt 复制到 exe 旁）
```

数据流：`IDeviceSource` → `DeviceHub`（每个源独立轮询，收到插拔/连接事件立即重扫，结果有变化才通知）→ 小组件在 UI 线程刷新。

## 新增一个小组件

1. 在 `src/DeskWidgets.App/Widgets/` 下新建文件夹，写一个实现 `IWidget` 的 `UserControl`，和一个 `IWidgetFactory`（参考 `Clock/ClockWidget.cs`，不到 100 行）：

```csharp
internal sealed class WeatherWidgetFactory : IWidgetFactory
{
    public string TypeId => "weather";            // 存进 settings.json，定了就别改
    public string DisplayName => Strings.WeatherWidget; // 界面文字都放在 Strings 里，中英各一份
    public string Glyph => "\uE706";              // Segoe Fluent Icons
    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];
    public WidgetSize DefaultSize => WidgetSize.Small;
    public IWidget Create(WidgetContext context) => new WeatherWidget(context);
}
```

2. 在 `WidgetCatalog.All` 里加上这个工厂。右键菜单和托盘菜单的「添加小组件」会自动出现它。

窗口、毛玻璃卡片、拖动、尺寸切换、位置保存都由宿主负责，组件只管自己的内容（背景保持透明）。
需要持久化的选项用 `context.Options`；设备数据用 `context.Services.Devices`；后台线程的事件用 `context.Services.Dispatcher` 切回 UI 线程。
界面文字一律取自 `Strings`，日期用 `Strings.Culture` 格式化；用户切换语言时宿主会调用 `ApplyLanguage()`，在里面重建文字即可。

像素主题（可选）：构造时 `context.Theme == WidgetTheme.Pixel` 的话，把 `Content` 换成 `new PixelSurface((canvas, palette) => XxxPixelScene.Draw(canvas, palette, …))`，在 `ApplySize` 里调 `SetArtSize`（尺寸取 `PixelCard.ArtSize(size)`），数据变了调 `Invalidate()`。
画法写成静态的 `XxxPixelScene`（参考 `Clock/ClockPixelScene.cs`），坐标是美术像素，只用 `PixelCanvas`、`PixelText`、`PixelSprites` 和调色板里的颜色；加进 `PixelPreview` 就能出预览图。
不做也能用：组件的 XAML 内容会直接放在像素卡片上。切换主题时宿主会重新创建组件，组件自己不用处理主题变化。
新组件在每个主题里都要满足 [docs/THEMES.zh-CN.md](docs/THEMES.zh-CN.md) 的信息底线、颜色含义和验收清单，并把它的信息底线补进那张表。

## 新增一种 2.4G 接收器

实现 `IHidBatteryDriver`（参考 `Hid/Drivers/WlMouseDriver.cs`），然后加到 `HidDongleSource.DefaultDrivers()`。

安全规则（沿用上一个项目）：**只发送从官方驱动抓包验证过的只读命令**；不写 DPI/回报率/配置，不碰配对和固件，不匹配 Bootloader 的 PID。
没有协议资料的接收器宁可不支持，也不要试探性地发命令。

已支持：

| 接收器 | 方式 |
|---|---|
| 雷柏 VT3 MAX MASTER V2（`24AE:146D`，A HUB 驱动） | **纯只读监听**：鼠标活动时每隔几秒主动推送状态报告 `07 40 …`，`[8]`=电量、`[7]`=电池状态、`[3..4]`=DPI；本程序从不发送任何数据 |
| LAMZU Thorn V2 系列 8K 接收器（`37B0:002E` / `37B0:0032`） | 抓包验证过的只读查询命令，按配对鼠标 PID 识别型号 |
| WLMOUSE 幻 8K 接收器（`36A7:A863`） | 抓包验证过的只读查询命令，含回报率 |

雷柏协议的来源：A HUB 的 `RapooDevice.dll` 中报告处理函数读取电量的偏移（`[7]` 状态、`[8]` 百分比），加上一次只读抓包，其中 DPI 字段与 A HUB 保存的配置完全一致；读出的电量（57%）已和 A HUB 界面显示核对一致。
`DeskWidgets.DeviceProbe --listen 60` 可以重复这次抓包。

## 已知限制 / 待办

- 雷柏鼠标：启动后要等鼠标动一下、推送第一份报告才有电量；超过 30 分钟没有报告（关机/休眠）显示为未连接。电量和充电状态都已实测验证。
- 桌面网格以工作区左上角为原点、按图标间距划分；系统没有公开图标的实际格位，所以和「将图标与网格对齐」关闭时手动摆放的图标不一定严格对齐。
- 蓝牙电量依赖 Windows 能否读到（设置 → 蓝牙和其他设备里能看到电量的设备才有）。
- 关闭 Windows「透明效果」或开启节电模式时，系统会把模糊背景换成纯色。
- 像素主题：缩放比例不是 50% 的整数倍时（125%、175% 等），卡片按整数倍放大，会比毛玻璃卡片略大一点。大文件夹之外，中文用宋体 12px 点阵，比像素字体高，长名字会换行或截断。
- 桌面图标排列：Explorer 重启、显示器热插拔、缩放变化都沿用了原工具的处理逻辑，但还没有在真机上测过。
- 大文件夹：直接从开始菜单拖出来的应用会被拒绝（Windows 显示禁止标志，根本不会询问卡片）。请用小组件菜单里的「添加应用…」，它会在资源管理器里打开「应用程序」文件夹，从那里拖；桌面快捷方式或程序文件也可以。
- 自包含的 Windows App SDK 让输出约 90 MB。项目只引用 Foundation 和 WinUI 两个组件包（不引用 `Microsoft.WindowsAppSDK` 元包，它会多带约 50 MB 用不到的 AI 和 ML 组件）。剩下的主要是 WinRT 投影（`Microsoft.Windows.SDK.NET.dll`，24 MB）和 WinUI 本身。
