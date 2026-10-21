# DeskWidgets

[中文版](README.zh-CN.md)

Desktop widgets for Windows, written in WinUI 3. The first widget is a battery panel in the style of Apple's Battery widget for wireless devices: Bluetooth devices and 2.4 GHz receiver mice, all shown as rings with their charge level and charging state.

- **Thin frosted glass**: DWM's host backdrop blurs whatever is behind the window in real time (wallpaper, animated wallpaper, icons, other windows), clipped to an anti-aliased card with 22 px rounded corners. Only a very thin tint and a diagonal highlight sit on top, with no outline.
- **Pixel theme**: a second look you can switch to (right-click a widget or the tray icon → "Theme" → Glass / Pixel). It isn't a reskin: every widget is drawn pixel by pixel on a low-resolution canvas (1 art pixel = 2 DIP, so a small card is exactly 82×82), then scaled up by a whole number to screen pixels (3×3 at 150% scaling), so edges are always hard. The battery widget works like a game inventory: device icons sit in recessed slots above a pixel battery bar. The clock has block-shaped big digits with a sun or moon. The world clock has pixel dials. There are two hand-drawn all-caps pixel fonts (3×5 and 5×7); text the fonts can't spell (Chinese, for example) is set entirely in 12 px SimSun bitmap glyphs. Day and night palettes (based on the Sweetie 16 palette) follow the system's light or dark mode. Under Pixel the app's own menus and dialogs are pixel art too: the widget menu, the tray menu, Exit's question and the error box are drawn the same way as the cards, with a hard shadow, and the tray icon is a pixel icon. Glass keeps the system menus.
- **Theme spec**: read [docs/THEMES.md](docs/THEMES.md) before adding or changing a theme. It holds the rules every theme shares (sizes, minimum information, meaning of colors, language), each theme's spec, the change levels and the acceptance checklist.
- **Three sizes**: small (one big ring for a single device, or 2×2 rings), medium (a row of 4 rings with device names and percentages) and large (a list). Device names can be turned off in the right-click menu.
- **World clock**: modeled on Apple's World Clock widget, showing 4 cities at once (New York, Los Angeles, St. John's (Newfoundland) and Beijing by default; city names are always in English). A dial is white from 6:00 to 18:00 and dark at night. Small = 2×2 dials. Medium = a row of 4, each with the city name, the 24-hour time and the local date (Oct 13 / 10月13日; in the Pixel theme, Chinese writes 10/13) below it. Large = a list that also shows the time difference from local (for example "Today −12h"; half-hour zones show +1.5), with the time on the right and the local date below it. 26 built-in cities can be swapped in the right-click menu, and the second hand can be turned off.
- **Big Folder**: a phone-style card on a real folder. A new card makes a folder under your user profile, on the Desktop's drive; "Change folder…" in its right-click menu points it at any other. Small = 2×2 icons, Medium = 4×2, Large = 4×4; when there are more items, the last cell is a "+N" stack of the next ones. A click opens an item; a click on the header or the stack opens a panel (search, sort, Finder-style selection, rename, drag out); a drag anywhere still moves the card. **Drop files on the card** and they go into the folder. Ordinary files follow Explorer's rules (the same drive moves, another drive copies; Ctrl copies, Shift moves, Alt makes a shortcut). Programs are never moved: a program file, a portable app's folder, a Start menu or Store app, or anything under Program Files, Windows or ProgramData, or a program under AppData, becomes a shortcut (a shortcut dragged from the desktop moves in). A label on the card says what the drop will do, and Explorer's own progress and conflict windows appear, so Ctrl+Z in Explorer undoes it. File types and 13 common apps (WeChat, QQ, DingTalk, Tencent Meeting, Word, Excel, PowerPoint, Chrome, Edge, Claude, ChatGPT, Cursor, Kimi) have our own drawings (a color and a motif, never a logo); other apps show their real icon and photos their real thumbnail. The item menu has Open, Run as administrator, Open file location, Show in Explorer, Rename, Put back, Move to Recycle Bin (Remove from folder for a shortcut) and Properties. Both themes have it, and the Pixel one has its own panel.
- **Sits on the desktop**: windows stay at the very bottom of the z-order and don't appear in the taskbar or Alt+Tab. Win+D ("Show desktop") lifts the desktop over everything, so while it is up the widgets stay just above it, and they drop back under the windows as soon as one is opened.
- **Snaps to the desktop grid**: after you let go, a widget snaps to the desktop icon grid (the cell size is read live from Explorer's icon spacing, so it follows the icon size setting). A widget covers a whole number of cells and is centered on them. The tray menu can turn this off, and widgets then snap to screen edges and to each other.
- **Desktop icons arranged on the right** (tray → "Desktop icons", off by default): this merges the former standalone DesktopRightAlign program. Icons fill from the top-right corner downwards; when a column is full they start a new one to the left, and a new icon settles into place within 1–3 seconds. Icons make way for the cells widgets occupy, so widgets and icons share one grid, and new widgets then start from the top-left. Turning it on switches off Explorer's own "Auto arrange icons" and "Align icons to grid"; turning arranging off doesn't switch them back on, so tick them again under desktop right-click → "View" if you want them. It only uses Microsoft's public Shell interface (`IFolderView2`) and doesn't inject into Explorer. Icons don't move while you drag, have a menu open or rename. **Reordering by hand**: drag an icon somewhere else and it stays there (the cell you drop it on becomes its place in the order) while the other icons shift out of the way; a file dragged onto the desktop from a folder works the same way. Icons that move by themselves, without a drag (for example when Explorer reflows them after a display change), still go back to the remembered order. If you switch arranging off, place icons freely and switch it back on, the order at that moment is kept.
- **Interface language**: English (default) or Chinese, switched in the tray menu under "Language"; every widget, menu and date changes immediately.
- **Follows the system**: light and dark mode, and the DPI scaling of each monitor.
- **Extensible**: a new widget, or a new device source or receiver protocol, only needs one interface implemented.

## Running

You need the .NET 10 SDK (the Windows App SDK is bundled with the project, so no separate runtime is needed).

```bash
dotnet run --project D:/Widget/src/DeskWidgets.App
```

```bash
dotnet run --project D:/Widget/src/DeskWidgets.App -- --demo
```

`--demo` previews the layouts with 4 sample devices (low battery, charging, disconnected). Only one instance may run at a time, so quit from the tray before switching modes.

```bash
dotnet run --project D:/Widget/src/DeskWidgets.App -- --pixel-preview D:/Widget/artifacts/pixel-preview
```

`--pixel-preview <folder>` draws every Pixel layout (sample data, day/night × English/Chinese) into 4 overview PNGs and exits, so you can work on pixel art without arranging widgets back and forth. It also works while the app is running.

Other maintenance commands (they exit when done and also work while the app is running):

- `--icons-inspect <file>`: a read-only check of the desktop icons. It writes a JSON file with their current positions, the area the widgets occupy and which icons would move if arranging were on. It moves no icons and writes no other file.
- `--pixel-menu-preview <folder>`: draws every Pixel menu, dialog and the tray icon (light/dark × English/Chinese, 150 % and 100 %) into PNG sheets and exits, the same way `--pixel-preview` does for the cards. `--pixel-ui-open=widget|cities|tray|exit|warn` (with `--pixel-ui-zh` for Chinese) starts the app with one of them open, for capturing the real windows. `--pixel-ui-open=folder-panel` opens the first Big Folder's panel in either theme and keeps it open (add `--folder-panel-search=text` and `--folder-panel-select=n` to show a search and a selection).
- `--pixel-folder-preview <folder>`: draws the Big Folder in the Pixel theme (the cards at every size, their states, the icons and the panel; light/dark × English/Chinese) into PNG sheets and exits.
- `--folder-panel-cycle=<n> --cycle-out=<file>`: a leak check. It opens and closes every Big Folder's panel n times, as a click on the header and an Esc would, and appends the process's handle, GDI and USER counts to the file (`--cycle-dirty` leaves a search and a selection behind each time; `--cycle-hold` keeps the last open up). The numbers stay flat from round to round; a growing handle count means something is created and not released.
- `--enable-startup` / `--disable-startup`: turn Start with Windows on or off.

The built program is at `src\DeskWidgets.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\DeskWidgets.exe`, and the whole folder can be copied anywhere and run directly. For a release build:

```bash
dotnet publish D:/Widget/src/DeskWidgets.App -c Release -r win-x64 -o D:/Widget/artifacts/DeskWidgets
```

### Controls

| Action | How |
|---|---|
| Move | Press and drag a widget (it snaps when you let go) |
| Change size / always on top / lock position / remove / exit (asks first) | Right-click the widget |
| Add a widget / snap to the desktop grid / start with Windows / language / exit | Right-click or left-click the tray icon |
| Switch theme (Glass / Pixel, all widgets together) | Right-click a widget or the tray icon → "Theme" |
| Arrange desktop icons on the right / arrange now | Tray icon → "Desktop icons" (the first line shows the current status) |
| Show device names / show disconnected devices | Battery widget's right-click menu |
| Add files to a Big Folder | Drag them onto the card (Ctrl copies, Shift moves, Alt makes a shortcut) |
| Open an item / the panel (Big Folder) | Click an item; click the header or the "+N" stack |
| Open folder / add apps / sort / change or rename the folder | Big Folder's right-click menu |
| Change cities / show the second hand | World clock's right-click menu |

Settings are saved in `%LOCALAPPDATA%\DeskWidgets\settings.json` and the log is in `logs\app.log` (capped at 1 MB, rotated automatically). The order of the arranged desktop icons is in `desktop-icons.json` in the same folder. If the old DesktopRightAlign left settings behind, they are taken over the first time. `desktop-icons-original.json`, written by an older version (the backup for "Restore original layout", a feature that has been removed), is no longer used and can be deleted.

"Start with Windows" uses a per-user logon task named `DeskWidgets` in Task Scheduler (it starts 15 seconds after sign-in and needs neither a password nor administrator rights). It doesn't use the registry Run key: a Run entry written from a Claude session only landed in Claude's private registry (see "Developing inside a Claude session" below), so Explorer never saw it, which is why the app didn't start at boot on 2026-10-13. On this PC, "Start with Windows" is on, and this task is what starts the app. From 2026-10-14 to 10-19, a separate "AI Apps - Wait for TUN and Network" logon task started it instead; that task has since been removed.

### Developing inside a Claude session

The Claude desktop app is an MSIX-packaged app. Processes started from its sessions (including Claude Code and a `dotnet run` inside it) have their writes to `%LOCALAPPDATA%` and `HKCU` redirected to a private copy of Claude's (`%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\` and a private registry), and reads prefer that copy too. So:

- A DeskWidgets started inside a session uses different settings, logs and icon data from the one started at sign-in, and the two don't affect each other.
- The `%LOCALAPPDATA%\DeskWidgets` you see inside a session isn't necessarily the one really in use. To see the real files, open them in Explorer, or read them from a session through WMI (`Get-CimInstance CIM_DataFile`).
- To try a new build in the real environment, double-click the exe in Explorer. From a session, start it through WMI (`Invoke-CimMethod Win32_Process -MethodName Create -Arguments @{ CommandLine = '"<path to exe>"' }`), and don't use `dotnet run`.

### Tests

```bash
dotnet test D:/Widget/tests/DeskWidgets.Tests -c Release
```

The tests cover logic that doesn't depend on the UI: desktop grid snapping, icon arrangement, pixel text layout, the world clock's time differences and dates, card sizes, `DeviceHub`'s summary and scan merging, and the Big Folder's drop table, name shortening, sorting, origin log and panel logic. They use the Release configuration, which compiles and runs even while the program is running (the Debug exe is locked then).

## Layout

```
docs/
  THEMES.md                Theme design spec (read before adding or changing a theme)
src/
  DeskWidgets.Core/        Device model DeviceStatus, the IDeviceSource interface, DeviceHub (summary + polling), logging
  DeskWidgets.Devices/     Windows data sources
    Bluetooth/               Battery level of paired Bluetooth devices (reads the same device properties as the Windows Settings page; never talks to the device)
    Hid/                     2.4 GHz receivers: HID enumeration, feature report exchange, read-only listening, plug/unplug watching, per-vendor drivers
    SystemBatterySource      This PC's battery (off by default, only used in DeviceProbe)
  DeskWidgets.App/         WinUI 3 host
    Desktop/                 Desktop icons arranged on the right: reading and writing icon positions through Shell interfaces (ShellDesktop), layout calculation (IconLayout), drag detection (DesktopDragWatcher), the background thread (IconArranger), arrangement data (IconStore)
    Hosting/                 Transparent windows, live glass (GlassBackdrop), drag and snap, tray, settings, start with Windows (logon task)
    Pixel/                   Pixel theme: canvas, hand-drawn fonts and sprites, palette, card, integer-scaled display (PixelSurface), preview sheets
    PixelUi/                 Pixel menus and dialogs: layered popup windows (PixelPopupWindow), the menu model (MenuEntry) and its painter (MenuPainter), the cascade and its keyboard, pointer and scrolling (PixelMenu, MenuLevel), dialogs (PixelDialog, DialogPainter), UI Automation and MSAA for Narrator (Uia, Accessible), the Pixel tray icon (PixelTrayIcon)
    Widgets/                 Widget interface + battery widget + clock widget + world clock (WorldClock/) + Big Folder (Folder/: the model, drop policy and file operations, the Shell layer in Folder/Shell/, both themes' card and panel); each widget's *PixelScene is how it is drawn in the Pixel theme
tests/
  DeskWidgets.Tests/       xUnit unit tests (reference App and Core, bring their own Windows App SDK runtime)
tools/
  DeskWidgets.DeviceProbe/ Command-line diagnostics: lists HID devices and each data source's scan results (--hid-all / --self-test / --listen seconds)
  Make-AppIcon.ps1         Regenerates the program icon
```

Data flow: `IDeviceSource` → `DeviceHub` (each source polls on its own; a plug/unplug or connection event triggers an immediate rescan, and listeners are notified only when the result changed) → widgets refresh on the UI thread.

## Adding a widget

1. Create a folder under `src/DeskWidgets.App/Widgets/`, then write a `UserControl` that implements `IWidget` and an `IWidgetFactory` (see `Clock/ClockWidget.cs`, under 100 lines):

```csharp
internal sealed class WeatherWidgetFactory : IWidgetFactory
{
    public string TypeId => "weather";            // saved in settings.json, so never change it once chosen
    public string DisplayName => Strings.WeatherWidget; // all UI text lives in Strings, one copy per language
    public string Glyph => "\uE706";              // Segoe Fluent Icons
    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];
    public WidgetSize DefaultSize => WidgetSize.Small;
    public IWidget Create(WidgetContext context) => new WeatherWidget(context);
}
```

2. Add the factory to `WidgetCatalog.All`. "Add widget" in the right-click and tray menus then lists it automatically.

The host looks after the window, the glass card, dragging, size switching and saving the position; a widget only deals with its own content (keep the background transparent).
Use `context.Options` for options that need to persist, `context.Services.Devices` for device data, and `context.Services.Dispatcher` to get events from background threads back onto the UI thread.
All UI text comes from `Strings`, and dates are formatted with `Strings.Culture`. When the user switches language the host calls `ApplyLanguage()`, so rebuild your text there.

The Pixel theme (optional): if `context.Theme == WidgetTheme.Pixel` at construction, replace `Content` with `new PixelSurface((canvas, palette) => XxxPixelScene.Draw(canvas, palette, …))`, call `SetArtSize` in `ApplySize` (take the size from `PixelCard.ArtSize(size)`), and call `Invalidate()` when the data changes.
Write the drawing as a static `XxxPixelScene` (see `Clock/ClockPixelScene.cs`), with coordinates in art pixels, using only `PixelCanvas`, `PixelText`, `PixelSprites` and the palette's colors. Add it to `PixelPreview` to get preview sheets.
You can skip it: a widget's XAML content is placed directly on the pixel card. When the theme changes the host recreates the widgets, so a widget doesn't need to handle theme changes itself.
In every theme, a new widget must meet the minimum-information rules, the color meanings and the acceptance checklist in [docs/THEMES.md](docs/THEMES.md), and its minimum information must be added to that table.

## Adding a 2.4 GHz receiver

Implement `IHidBatteryDriver` (see `Hid/Drivers/WlMouseDriver.cs`), then add it to `HidDongleSource.DefaultDrivers()`.

Safety rules (carried over from the previous project): **only send read-only commands that were captured from the official driver and verified**; never write DPI, polling rate or configuration, never touch pairing or firmware, and never match a bootloader's PID.
A receiver without protocol documentation is better left unsupported than probed with guessed commands.

Supported so far:

| Receiver | Method |
|---|---|
| Rapoo VT3 MAX MASTER V2 (`24AE:146D`, A HUB driver) | **Purely passive listening**: while the mouse is active it pushes a status report `07 40 …` every few seconds, where `[8]` = battery level, `[7]` = battery state and `[3..4]` = DPI. This program never sends any data. |
| LAMZU Thorn V2 series 8K receiver (`37B0:002E` / `37B0:0032`) | Read-only query commands verified from captures; the model is identified by the paired mouse's PID |
| WLMOUSE Huan 8K receiver (`36A7:A863`) | Read-only query commands verified from captures, including the polling rate |

Where the Rapoo protocol comes from: the offsets at which the report handler in A HUB's `RapooDevice.dll` reads the battery level (`[7]` state, `[8]` percentage), plus one read-only capture in which the DPI field matches A HUB's saved configuration exactly. The level it reads (57%) was checked against what A HUB's own interface shows.
`DeskWidgets.DeviceProbe --listen 60` can repeat that capture.

## Known limits / to do

- Rapoo mouse: after startup you have to move the mouse once so that it pushes its first report before a battery level shows up; with no report for more than 30 minutes (powered off or asleep) it shows as not connected. The battery level and the charging state have both been verified on the real device.
- The desktop grid takes the top-left of the work area as its origin and divides it by the icon spacing. The system doesn't publish the icons' real cell positions, so icons placed by hand while "Align icons to grid" was off won't necessarily line up exactly.
- Bluetooth battery levels depend on Windows being able to read them (only devices whose level appears under Settings → Bluetooth & devices have one).
- When Windows "Transparency effects" are off, or Energy saver is on, the system replaces the blurred background with a solid color.
- Pixel theme: at scaling that isn't a multiple of 50% (125%, 175%, …) the card is scaled up by a whole number, so it comes out slightly larger than a Glass card. Chinese is set in 12 px SimSun bitmap glyphs, which are taller than the pixel fonts, so long names wrap or are truncated.
- Desktop icon arranging: Explorer restarts, monitor hot-plugging and scaling changes reuse the original tool's handling, but haven't been tested on the real machine.
- Big Folder: an app dragged straight out of the Start menu is refused (Windows shows the no-entry sign and never asks the card). Use "Add apps…" in the widget's menu instead, which opens the Applications folder in Explorer, and drag from there; a desktop shortcut or a program file works too.
- The self-contained Windows App SDK makes the output about 90 MB. The project references only the Foundation and WinUI component packages (not the `Microsoft.WindowsAppSDK` metapackage, which adds about 50 MB of unused AI and ML components). The rest is mostly the WinRT projection (`Microsoft.Windows.SDK.NET.dll`, 24 MB) and WinUI itself.
