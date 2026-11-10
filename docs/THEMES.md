# Theme design spec

[中文版](THEMES.zh-CN.md)

> Version 1 · 2026-10-13 · Applies to all DeskWidgets themes (currently Glass and Pixel).
> Read this document before adding a theme, changing a theme, or adding a new rendering for a widget. When the spec and the code disagree: **change the spec first, then the code**.

Wording convention:

- **Must**: a hard rule. Breaking it causes a bug, or overturns a decision the user has already confirmed (see Appendix B).
- **Should**: the default. An exception needs a good reason and goes into the changelog at the end.
- **May**: optional; use your judgement.

---

## 1. What a theme is

A theme is one complete look shared by all widgets: the **card** (shape, material, edge), the **palette** (one light set, one dark set), the **fonts**, the **icons** and the **motion**.

A theme does **not** decide: which information a widget shows, card size and position, how you interact with it, or the copy. Those are the same in every theme (section 2).

| Theme | Enum value | In one line | Implementation |
|---|---|---|---|
| Glass | `WidgetTheme.Glass` (default) | Apple-style widgets: thin, live-blurred glass with 22 DIP rounded corners, no outline and no shadow | The widget's XAML content + `GlassBackdrop` (DWM host backdrop) + the tint and highlight layers of `WidgetFrame` |
| Pixel | `WidgetTheme.Pixel` | An opaque pixel-art card, drawn pixel by pixel at low resolution and scaled up by a whole number, and its edges are always hard | One static `XxxPixelScene` per widget, drawn on a `PixelCanvas` and shown 1:1 by `PixelSurface` |

Where to switch: tray menu or any widget's right-click menu → "Theme" ("主题" in the Chinese UI). The setting is saved in the `Theme` field of `settings.json`.

---

## 2. Rules every theme must follow

### 2.1 Global switching

- **Must**: the theme is global. All widgets switch together; having some widgets in Glass and others in Pixel isn't supported.
- **Must**: switching takes effect immediately, without a restart. The host (`WidgetHost.SetTheme` → `WidgetWindow.ApplyTheme`) recreates every widget with the new theme. A widget instance's `WidgetContext.Theme` never changes during its lifetime, so widgets don't need to handle theme switching themselves.

### 2.2 Size and position

- **Must**: each size has a fixed footprint: small 164×164, medium 344×164, large 344×364 (in DIP; a medium is two small cards plus a 16 DIP gap; a large is two mediums stacked with the 36 DIP gap that the desktop grid leaves between them at Windows' default icon spacing, so a large beside a stacked pair has the same top and bottom edges). Desktop grid snapping and saved positions depend on these sizes, so changing theme must not disturb the user's desktop. A large still covers the same four icon rows as before, and a card re-snaps when it loads.
- **Known trade-off**: with "Snap to desktop grid" off, cards keep 16 DIP gaps (`SnapToEdges`), so there a large is 20 DIP taller than two stacked mediums.
- **May**: deviate slightly because of whole-number scaling (Pixel is slightly larger than Glass at 125% and 175% scaling).
- **Must**: draw content inside the card. The window is the card, and anything drawn outside is clipped.
- **Must**: don't add a size that exists for only one theme.
- **Dock** (the exception to the footprint rule above; see 2.11): it is not on the desktop grid and has no size of its own. It sits on a screen's bottom edge, centred unless the user has slid it, and its length follows its icons. Small, Medium and Large are icon sizes: Glass 40, 48 or 64 DIP; Pixel 20, 24 or 32 art pixels, which are the same 40, 48 or 64 DIP. Padding and gap are 12 DIP (6 art pixels) at every size. The bar is 64, 72 or 88 DIP high, and its bottom edge sits 8 DIP (4 art pixels) above the screen's bottom edge. There is one dock per screen. The desktop icon arranger counts the dock's area, so desktop icons keep out from under it.

### 2.3 Minimum information

A theme may change how information is presented (rings ↔ battery bars, full city names ↔ three-letter codes), and may show a little more when there is room, but every size **must show at least** what this table lists:

| Widget | Small | Medium | Large |
|---|---|---|---|
| Battery | One device: icon, charge percentage and charging state, plus the name when "Show device names" is on<br>Several devices: 2×2, each cell is an icon + charge (graphic or number) + charging state | Up to 4 devices: icon, charge (graphic + number), charging state, name (can be turned off) | Up to 6 rows: icon, name, connection type and state, charge percentage |
| Clock | Weekday, time, date | Same as small | Not supported |
| World clock | 4 dials + a city label (full name or three-letter code) | 4 dials, each with city name, time and local date | One row per city: dial, city name, time difference from local time (including today/yesterday/tomorrow), time, local date |
| Big Folder | Header with the folder's name and item count; 2×2 cells | Same header; 4×2 cells | Same header; 4×4 cells |
| Dock | Every app at 40 DIP (Pixel 20 art px) | Every app at 48 DIP (Pixel 24 art px) | Every app at 64 DIP (Pixel 32 art px) |

- With no devices, show the empty-state text (`Strings.NoDevices`).
- When "Show disconnected devices" is on, disconnected devices must be clearly dimmed, with "—" in place of the charge.
- The options in the right-click menu (show device names, show second hand, change cities, …) do the same thing in every theme.
- Big Folder:
  - Each cell is the item's icon and a name: one line on Small and Medium, up to two on Large (the panel's breaks, `NameShortener.TwoLines`; a name that fits stays one line, top-aligned; Pixel's SimSun metric set stays one line on Large, as two lines would not fit four rows). When there are more items than cells, the last cell is the stack: four small icons of the next items and "+K", where K = items − (cells − 1). A folder with exactly 4, 8 or 16 items has no stack. Items follow the folder's sort order (Newest, Name, Type or Manual).
  - States: empty ("Drop files here"); folder not found ("Folder not found": the header can't be clicked and drops are refused); drop hover (a tint and a label saying what the drop will do: Move to, Copy to, Create shortcut in, or Add to for a mixed drop).
  - A click on an item opens it; a click on the header or the stack opens the panel. In the panel a click selects (Ctrl and Shift extend the selection), a double-click or Enter opens, typing anywhere searches, the first Esc clears the search and the second closes the panel, and the footer shows the path and "{n} selected". The selection can be dragged out. **The panel stays open while the user works in it** (a rename, Move to Recycle Bin, Put back, sort, search, selection, the item menu, the card's menu with "Rename folder…", Properties, a drop on the card, a drag out) **and through every window that work brings up**, ours or Explorer's: a dialog, a file operation's progress or conflict window, the UAC prompt. **It closes when the user leaves it:** Esc (the second one when there is search text), a click on the card's header or stack, a press on any other card, a click on the desktop or on another program's window, or switching to another program (Alt+Tab, the taskbar). **It also closes after opening something:** a double-click, Enter, Open, Run as administrator, Show in Explorer, Open file location. After a rename the renamed item is selected; after a removal the item after it is.
  - A name that doesn't fit is shortened by one shared function, `NameShortener`, measured in the theme's own font (2.9).
  - **Search** (both panels, one shared matcher, `Search/NameSearch.cs`; Phase 13, 2026-10-24): a name matches if it contains the typed text in any case, or, when the text is Latin letters and digits only and the name has Chinese in it, if the text spells a run of its characters by their pinyin **initials** ("bz" finds 壁纸, "qqyy" finds QQ音乐, across spaces and "&") or by **whole syllables with the last one cut short** ("bizhi", "biz"). A character with several readings (行, 乐) matches any of them. The table is built from the Unicode Han Database by `tools/Make-PinyinTable.ps1` (Unicode License v3, notice shipped beside the exe).
  - **Show more options** (the last row of the item menu, in both themes and in the card's menu; Phase 13, 2026-10-24): Windows' own context menu for the item or the selection (Open with, Send to, compress, the apps' own entries), through `IContextMenu`. It is **Windows' menu in both themes**, drawn by the system as Glass's message boxes are; Pixel cannot restyle it. Open, Rename and Delete in it run our own code (the Recycle Bin, the origin log, the in-place rename); Paste is left out. The panel stays up while the menu is up and while a command it starts runs. **Risk:** the entries come from shell extensions that run in our process (as thumbnail handlers do); a slow one blocks the UI thread while the menu builds (about 0.3 to 1.2 s with a dozen extensions installed), and a crash inside one cannot be caught.
  - Where a new card's folder is made: in the **default location** if the user has chosen one (the card's menu, "Default location for new folders…", saved as the top-level `FolderRoot` setting; the folder is created if it is missing), else under the user profile on the Desktop's drive. "Change folder…" still points one card at any folder.

- Dock (one row, at the chosen icon size):
  - Every app at that size, with its name in a label above the bar on hover.
  - **Groups** (2026-11-03): a group is one icon at the bar's size and holds two or more programs; a group of one becomes its program, and a group is never inside another group. Its icon shows its first four members as minis in a 2 × 2. A new group is called "Group" until it is renamed. Its look is in 4.1 and 4.2, and its panel and menu are in 2.7. Members in the group's panel are drawn at 48 DIP (Pixel 24 art pixels) at every bar size.
  - States: idle; hover (the icon lifts and the label shows the name); pressed; missing (the app can no longer be found: its icon is dimmed, and a click offers to remove it); running, opening and closing (below). A running app shows its running icon, with no dot, and its opening and closing are animated.
    - **Running:** the program has a taskbar window. The window is matched by its AppUserModelID, then by its process's, then by its exe path (for the apps with custom icons, also by the launcher table the icons use), never by title. A program with only a tray icon is not running. A link that has only a URL, such as a Steam link, has no running state.
    - **Click:** a click on a running program brings its frontmost window forward, restoring it first if it is minimized. If that window is already in front, the click minimizes it when it is the program's only window; with several windows, the click brings the next one forward, in a fixed order by window handle. Middle-click or Shift+click starts another copy instead.
    - **Opening:** a program the dock starts goes into the opening state at once. It stays there until its first window comes, or for 10 s, and then goes back to idle, which is not an error. A start that fails also goes back to idle at once. Each start first checks, at the click, that the program is still there: a missing one is marked as missing, and one that has come back starts. A click while the program is opening does nothing (it would only start a second copy); middle-click or Shift+click still does.
    - **Forming a group and Open with:** an app held over another for 500 ms can be dropped to make a group. Files that are not programs, dragged over the bar, light the apps that open them, and the one under the pointer reads "Open with {name}" (2.7; the look is in 4.1 and 4.2).
  - Empty: "Drop apps here", centred in the bar.

### 2.4 What colors mean

A theme decides the exact color values, but **must not change the meaning**:

- Charge ≤ 20% and not charging: red; power-saving mode: yellow; anything else: green (as in iOS).
- Charging: show a lightning bolt.
- World clock: use the day dial for local 6:00–18:00 and the night dial otherwise; the second hand uses the orange accent.
- The clock's weekday uses the accent color (red or orange).

Each theme's exact values are in section 8.

### 2.5 Language and text

- **Must**: all visible text comes from `Strings` (English by default + Chinese); no copy hard-coded in widgets. Format dates with `Strings.Culture` or an explicit format string.
- **Must**: city names are in English in both languages (the user's decision).
- **Must**: time uses the 24-hour `HH:mm` format.
- **Must**: fonts either cover both Chinese and English or have a fallback, and **one string of text must not mix two font sizes** (the Pixel approach: text the pixel fonts can't spell is set entirely in one bitmap font: the 12 px SimSun bitmap font, or, in the Big Folder, Fusion Pixel 8 px when it covers all of the text).
- **Must**: both languages must fit. English is usually wider and Chinese usually taller (in Pixel, Chinese lines are 12 px high while the pixel fonts are 5 px or 7 px). When something really doesn't fit, a more compact wording may be used (for example, Pixel writes the Chinese date as "10/13" instead of "10月13日"), and it goes into section 4.

### 2.6 Following the system

- **Must**: provide a light and a dark palette, and redraw immediately when the system switches. XAML content listens to `ActualThemeChanged`; `PixelSurface` redraws by itself.
- **Must**: stay sharp at 100%–200% scaling and across monitors with different DPI.
- **Should**: a theme that uses transparency should say what it looks like when Windows "Transparency effects" is off or "Energy saver" is on (Glass turns into a solid color, which is normal).

### 2.7 Interaction and legibility

- **Must**: the whole card can be dragged, so content must not intercept pointer events. No buttons, text boxes or scroll areas on the card; all settings go in the right-click menu.
  - **Exception (approved 2026-10-21, Big Folder only):** the Big Folder's cells and header take a click: a press and release that stay within the system's drag threshold. A press that moves farther is still a drag of the card, wherever it starts. The Big Folder card also accepts dropped files. It still has no buttons, text boxes or scroll areas: searching, selecting, sorting and renaming happen in the panel, which is a separate window.
- **Dock (2026-10-31; an exception to the card rule above, because the dock holds apps and is not a card):** its apps take clicks, and it takes drops. A click on an app starts it, or, for a running app, brings its window forward or minimizes it (2.3); a click on a group opens or closes its panel. A drag that starts on an app reorders it; an app held over another for 500 ms and then let go makes a group (2.3). A drag that starts on the bar's ends moves the bar along the bottom edge, or onto another screen's bottom edge.
- **Dock, drops:** a program dropped on the bar is added, never opened with it; dropped on a group, it joins the group. The dock keeps a reference, and the file is not moved, copied or made. Real files that are not programs (folders are not opened) light the apps registered for every one of their types. Dropped on one of those apps, the files open with it, and for an app whose running state the dock tracks, its opening state starts (2.3). Dropped anywhere else on the bar, they do nothing. Over a group, its panel opens after 500 ms, so that one of its members can take the drop; dropped on a member of the open panel, the files open with that member. "Add apps…" opens Windows' Applications folder to drag from. "Add taskbar apps" adds the apps pinned to the taskbar that the dock does not have yet, in the taskbar's order. It never changes the taskbar.
- **Dock, menus:** right-click an app for Open, Run as administrator (not for Store apps), Open file location, Remove from dock and Show more options (Windows' own menu, as in the Big Folder). A member in a group's panel has the same menu. Right-click a group for Open all, Rename, Ungroup and Remove group, which asks first; its programs are not deleted, they just stop showing on the bar. Right-click the bar for Add apps…, Add taskbar apps, Icon size (Small, Medium and Large) and Remove dock, which asks first. Removing goes only through these menus; dragging an app off the bar never removes it.
- **Dock, group panel:** a click on a member starts it, or switches to its window, and closes the panel. Esc or a click anywhere else closes it. A drag inside the panel reorders the members. A member dragged out and dropped on the bar is taken out of the group. Right-click a member for the app menu, which opens at the member and not above the bar. A group of more than twelve members scrolls while a drag is held near the panel's top or bottom edge, so a member can be moved from the first row to the last. Rename keeps the new name on Enter and cancels on Esc. "Open all" is the panel's button and the first row of the group's menu.
- **Must**: purely decorative layers set `IsHitTestVisible = false`.
- **Must** (opaque themes): contrast between body text and background ≥ 4.5:1, and ≥ 3:1 for big numbers. The faint colors used only for tick marks and separators (Pixel's `Faint`) must not be used for body text.
- Glass's background is live, so contrast can't be guaranteed; see "Known trade-off" in 4.1.

### 2.8 Performance

- **Must**: use almost no CPU when idle. Redraw only when data changes or the clock ticks (aligned to the whole second or minute); no loop that refreshes every frame; transition animations last at most 1 second.
- **Should**: reuse canvases, bitmaps and brushes instead of allocating large objects on every redraw.
- **Must** (the dock): window tracking runs on window events only, never on a timer, and a hidden dock costs nothing. Every dock moment lasts at most 1 s. An app that is opening holds still, without a loop, until its first window appears.
- **Must** (motion, added 2026-10-22), for every moment in both themes:
  - With Windows' "Animation effects" off (`UISettings.AnimationsEnabled`, read when each animation starts, not relied on to signal), everything snaps to its final state.
  - Input never waits: a click, key, drag or drop during an animation acts at once, on the final layout.
  - A new state cuts the running animation short and goes on from where it is.
  - Nothing runs once still: no `CompositionTarget.Rendering` left hooked, no timer left running. All motion shares one timer (`Motion/FrameClock.cs`), re-armed for the next animation due and stopped when none is.
- **Known trade-off** (2026-10-22): every WinUI window made and closed leaves about 2.6 kernel handles behind (a bare empty window measured the same), so adding and removing a widget by hand costs about 3 to 4 handles. Widgets are added by hand and rarely, so the windows are not pooled; the rest of the leak (handlers left on closed windows, per-instance timers, undisposed Composition objects) is fixed.

### 2.9 Code layering

- **Must**: data and copy live in the shared layer (`DeviceStatus`, `WorldCities`, `Strings`, …); the theme layer only does the rendering. The same piece of information is formatted by the same shared function in every theme (for example `WorldCities.FormatTime` / `FormatDate` / `DescribeOffset`), and a theme may shorten it only when it doesn't fit, and only in the recorded way of 2.5.
- **Must**: each theme's rendering lives in its own files. Glass = the XAML and code in the widget classes; Pixel = the `Pixel/` infrastructure + `Widgets/<widget>/<widget>PixelScene.cs`. A new theme creates a `<ThemeName>/` folder and `<Widget><ThemeName>Scene.cs` in the same way.
- **Must**: widgets use only the color roles of their theme's palette (section 8), with no color values hard-coded in widgets.
- **Should**: a widget without its own rendering should still work (its XAML content is placed directly on the current theme's card). A theme that ships officially, though, should give every widget its own rendering.

### 2.10 Where assets come from

- **Must**: fonts, icons and sprites must be original, come with the system, or be licensed for redistribution. Don't bundle Apple's SF fonts, and don't lift assets from game screenshots. The existing themes use system fonts (Segoe UI Variable, Segoe Fluent Icons, SimSun), hand-drawn pixel fonts and sprites, and one bundled font: **Fusion Pixel Font 8 px** (Simplified Chinese, proportional, release 2026.10.16, by TakWolf, SIL Open Font License 1.1) for the Big Folder's Chinese. Its license text ships beside the exe (`OFL-FusionPixel.txt`), the packed table is built by `tools/Make-FusionFont.ps1`, and the font is only ever used as is. Emoji are not bundled: the system's Segoe UI Emoji is rendered at run time through Win2D (`Microsoft.Graphics.Win2D`, MIT) and shrunk onto the Pixel palette.

### 2.11 The dock: showing and hiding

The dock is the one widget above windows. It is not a widget type and has no card; every other widget stays on the desktop layer.

- **Must**: the dock is always on top, but it never takes the focus. A click on it leaves the window in front where it was.
- **Must**: it shows while no window on its screen is maximized, over the desktop and over ordinary windows.
- **Must**: it hides, sliding down off the bottom edge, while a window on its screen is maximized. Minimized windows, windows on another virtual desktop and tool windows don't count. A large window that is not maximized stays under the dock.
- **Must**: it hides for a full-screen window (a game, a video or a slide show). A window counts as full screen when it covers its whole monitor **and** the Shell reports a busy state (`SHQueryUserNotificationState`: full-screen Direct3D or presentation mode). Both must be true; the Shell's state alone is not enough. While such a window is in front, the bottom edge does not bring the dock back.
- **Should**: when the full-screen test is unclear, hide. A dock that hides once too often is less harmful than one that sits over a game.
- **Must**: it comes back, sliding up, when the pointer reaches the bottom edge under the dock or about one icon slot (an icon and its gap) beside it on either side, or when a file drag reaches that stretch. The edge strip covers only that stretch, not the whole bottom edge. It slides away again 600 ms after the pointer is off both the edge strip and the bar, in both themes; while the pointer rests on either, it stays. It never slides away while its menu, a group's panel or a drag is in use. A dock opened over a maximized window starts hidden. Once it has slid away, its window is hidden, not parked below the screen.
- **Must**: it reserves no space. Maximized windows keep the whole screen, which is why the dock hides for them.

---

## 3. Design tokens every theme must define

Tokens of one kind go in one place, not scattered across widgets.

| Category | What to define | Where Glass has it | Where Pixel has it |
|---|---|---|---|
| Card | Shape, corner radius, material, edge, highlight and shadow | `Hosting/WidgetFrame.xaml`, `Hosting/GlassBackdrop.cs` | `Pixel/PixelCard.cs` |
| Palette | A light and a dark set, named by the roles of section 8 | `Widgets/GlassPalette.cs` | `Pixel/PixelPalette.cs` |
| Fonts | Family, type scale, weights, fallback fonts | Segoe UI Variable Display / Text | `Pixel/PixelFonts.cs` (3×5, 5×7) + `Pixel/SystemGlyphs.cs` (SimSun 12 px) + `Pixel/FusionGlyphs.cs` (Fusion 8 px, Big Folder only) |
| Icons | Device icons, charging indicator, empty-state icon | Segoe Fluent Icons (`Widgets/Battery/DeviceGlyphs.cs`) | `Pixel/PixelSprites.cs` |
| Motion | Which changes animate, duration, easing | Battery ring: 700 ms cubic ease-out; the Big Folder's moments (4.1): Quick 100, Exit 150, Enter 240, Move 280, Press 80, Release 200, Blend 250 ms with three easings; Start 600 ms for the dock's opening (4.1); the second hand ticks once a second | Stepped frames of T = 40 ms (GROW, SHRINK, DISSOLVE, MOVE, POP, LEAVE, FLASH, STEP, WIPE, HOP), whole art pixels, no alpha (4.2); the second hand ticks once a second |

---

## 4. Theme specs

### 4.1 Glass

Confirmed by the user (2026-10-12):

- **Thin and see-through**: the DWM host backdrop blurs whatever is behind the window in real time, with only a 24% tint on top (white in light mode, black in dark mode) and a diagonal highlight.
- **No outline** (the user found the white line around the card ugly) and **no shadow** (a XAML shadow would sit above the system-layer glass and dirty it).
- **Don't use a thicker tint to raise contrast** (the "too thick" version was rejected by the user).
- Corner radius 22 DIP, anti-aliased.

Font sizes (current values; new widgets pick the nearest):

| Use | Font | Size / weight |
|---|---|---|
| Hero number (clock time, single-device charge) | Segoe UI Variable Display | 36–64, SemiBold |
| List number (world clock time, list charge) | Display | 17–28, SemiBold |
| List title (device name, city name) | Segoe UI Variable Text | 14–15, SemiBold |
| Body (name, time, medium-size charge) | Text | 11–15 |
| Secondary information (date, state, time difference) | Text | 11–12, secondary color |

**Known trade-off**: the glass is very thin, so on a very dark wallpaper the contrast of light mode's dark text drops (and the other way round). Since 2026-10-27 all text is solid, black in light mode and white in dark mode (only absent things stay dimmed: Inactive in section 8), the most a 24% tint allows: on the user's dark navy wallpaper the world clock's time and date went from about 1.5:1 to about 3.5:1. The direction for further improvement (not implemented yet): pick the text color from the brightness behind the card, instead of thickening the tint.

**Large cards** (344 × 364, 2026-10-22): the 20 new DIP go to rows and air only, with no new content and no font changes. Battery: padding 16 top and bottom, row spacing 8, so six 48 rows end at y 344. World clock: padding 14, four rows of 84, dial 60 (with numerals, like the Medium's; it had ticks at 56). Big Folder: rows 80, so four rows from y 34 end at 354.

**Big Folder** (added 2026-10-21; sizes in DIP):

- **Card:** the usual Glass card. Header from y 12 to 30: the folder name (Text 13 SemiBold, middle-shortened), the count (Text 12, Secondary) and a chevron. Cells: Small 2×2 (column 70, row 60, tile 40), Medium 4×2 (column 80, row 60, tile 40), Large 4×4 (column 80, row 80, tile 44 at row + 4). Names are Text 10 with a 13 line height (10.5 on Large): one line 68 wide on Small and 78 on Medium, two lines 78 wide on Large; the name box starts at row + 46 (row + 52 on Large). The stack's minis are 16 (17 on Large). (Phase 8, 2026-10-21: the names were Text 11 and 12, one line, 64 and 74 wide.)
- **Icons:** every item sits on a rounded tile (radius 0.225 × its size) with a faint sheen. The 14 file types and the 13 first-batch apps are our own drawings (a color and a motif, never a logo; path data in the design's `ICONS.md`, drawn through `SvgPath`). Any other app shows its real icon on a `Tile plate` at 0.68 of the tile, or edge to edge when the icon already fills its square. Images and videos show their real thumbnail clipped to the tile (a video adds a play mark). A cloud-only file never downloads: its type tile stays until a cached thumbnail exists.
- **States:** hover and pressed plates (`Hover`, `Pressed`) fade in over 100 ms and out over 150 ms; an app whose target is gone is at 40 % opacity with a Secondary name; empty shows a disc with a download glyph; folder not found shows a warning glyph, the path on every size and the hint "Right-click › Choose folder…" on Medium and Large; a drop tints the card (`Drop tint`) and shows a `Select` pill with the verb.
- **Panel:** a 440 × 500 glass window 12 DIP beside the card (right if it fits with 8 to spare, else left; moved up if it would run off the bottom). It closes by the rules in 2.3. Title and count, the sort button, the search field, five columns of 80 × 92 cells with two-line names (Text 11, line height 14, in a 76 × 28 box), and a footer with the path and "{n} selected". Selection is Finder-style: a `Pressed` plate under the tile and a `Select` pill under each name line; the keyboard focus is a 2 DIP `Select` ring. Renaming is a one-line text box in place of the name.
- **Menus:** `MenuEntry` rows through `MenuFlyout`, as for the other widgets. The item menu ends with "Show more options" (glyph E712, "显示更多选项" in Chinese), which opens Windows' own menu (2.3).
- **Motion** (Phase 12, 2026-10-22; all built: the Big Folder's A1 to A4, then the battery B6, size and theme changes B7 and widgets added or removed B8; menus and message boxes B5 keep WinUI's and the system's own motion). The rules are in 2.8. Vocabulary: Quick 100 ms (Out), Exit 150 ms (In), Enter 240 ms (Out), Move 280 ms (Move), Press 80 ms and Release 200 ms (a tile to 94 % and back), Blend 250 ms (tint and sheen), Ring 700 ms (the battery ring, unchanged). Out = cubic-bezier (0.2, 0.8, 0.2, 1), In = (0.4, 0, 1, 1), Move = (0.3, 0, 0.1, 1). Whole-window fades use layered-window alpha, so the frost and the XAML stay in step; the frost and the XAML (two compositors) otherwise run about one frame apart.
  - **Panel (A1):** opening fades the window in (Enter) while the frost and the card travel 8 DIP away from the card and scale from 97 % about the corner on the card's side; closing fades out, 6 DIP back at 98 % (Exit), and the window hides when it has gone. The window is at its final place and size from the first frame, and takes no clicks while it fades out. A reopen during a close goes on from the alpha it has.
  - **Items (A2, A4), on the card and in the panel:** every item has one cell, keyed by its file name (a rename keeps its cell). A new item's cell enters (opacity and scale 85 % about its tile, Enter); a cell whose item changed place slides there (Move); a cell whose item left goes to opacity 0 and 85 % where it stands (Exit) and is removed; they start together, an entering cell 80 ms after the leavers. Only cells in view animate. A drag out of the panel takes the dragged cells to 40 % (Quick) until it ends; after a move they leave.
  - **A drag over the card (A2):** the drop tint fades in (Quick), the label fades in and rises 6 DIP (Enter); a modifier key that changes the verb swaps the words at once; leaving takes both out (Exit), a drop too.
  - **Hover and press (A3):** plates (cells, header, stack, the panel's sort button) fade in (Quick) and out (Exit); the tile scales to 94 % on a press (Press) and back on release, on leaving or when the press turns into a drag (Release), which is a click's confirm; the search field's background goes between `Hover` and `Pressed` in 120 ms. "No matches" fades in (Enter) and out (Exit).
  - **Battery (B6):** the ring glides to a new level over Ring and the number changes at once (unchanged); the charging bolt, on the percentage line and as the ring's badge, fades in (Quick) when charging starts and out (Exit) when it stops.
  - **Size change (B7):** the old content fades out (Quick); the window takes the union of both sizes while the frost's clip and a matching rounded clip on the card move from the old rectangle to the new one, anchored top-left (Move); the new content is laid out at its own size when the old has gone and fades in (Enter) from the Move's half-way point; the window then settles at the new size. A new size finishes the running one first.
  - **Theme (B7):** light to dark blends the tint and the sheen (`BrushTransition`, Blend); text, tiles and plates change at once. Pixel to Glass: the old card goes at once and the new one fades in (Enter, layered alpha, no scale). Glass to Pixel is Pixel's (4.2).
  - **Widget added or removed (B8):** the card fades in and scales from 96 % about its centre (Enter; layered alpha, so the frost, the tint and the content stay together); removing takes it to opacity 0 and 96 % (Exit), and then the window closes. A leaving card takes no clicks.

**Dock** (added 2026-10-31; sizes in DIP; the detailed source is `notes/design/glass/dock-20261031/HANDOFF.md`):

| | Small | Medium | Large |
|---|---|---|---|
| Icon | 40 | 48 | 64 |
| Padding (top, bottom, each end) | 12 | 12 | 12 |
| Gap between icons | 12 | 12 | 12 |
| Bar height | 64 | 72 | 88 |
| Corner radius | 20 | 22 | 28 |
| Hover lift | 4 | 5 | 6 |
| Group minis (inset, gap) | 15 (4, 2) | 18 (5, 2) | 24 (6, 4) |
| Empty bar length | 240 | 280 | 340 |

- **Length:** 2 × padding + n × icon + (n − 1) × gap, where n is the number of apps. Pixel uses the same geometry (4.2).
- **Window:** the bar, with 42 DIP of transparent room above it for the name label. Clicks in that room pass through to the window behind. The bar sits 8 DIP above the screen's bottom edge.
- **Glass:** the bar uses the cards' glass (the host-backdrop blur, a 24 % tint and the diagonal highlight) with the radius above. No outline and no shadow, in any state. The user decided that the bar keeps this thin glass over windows too: over a white or black window it nearly disappears, and the icons float. That is accepted; don't thicken the tint.
- **Icons:** a custom app or a file type uses the Big Folder's tile art (its `ICONS.md`). A real icon sits on the `Tile plate` at 0.68 of the tile, or fills the tile edge to edge when it already fills its square. The tile radius is 0.225 × the icon size.
- **Group icon:** a tile-sized `Hover` plate (radius 0.225 × the icon size) with the first four members as minis in a 2 × 2, sized as in the table above, with no text on a mini. A running member's mini shows its running art; the group itself has no running art.
- **Name label:** a pill 26 DIP tall, with its bottom 8 DIP above the bar's top edge. It is centred on the icon and kept 8 DIP inside the screen. Its width is the text plus 20 DIP, at most 240 DIP; a longer name is shortened in the middle by `NameShortener`.
- **Empty:** a download glyph (`E896`, 16 DIP) and "Drop apps here" in Text 12, Primary, centred in the bar.
- **Group panel:** its own window above the dock, centred on the group icon and kept inside the screen. It is 20 + columns × 80 + 20 DIP wide, with columns = clamp(count, 3, 4): 280 DIP for three columns, 360 for four. It shows at most three rows of 80 × 92 cells and then scrolls. The title row holds the group's name, which can be renamed in place (Enter keeps it, Esc cancels), and an "Open all" button at the right. Each cell is a 48-DIP tile with a name of up to two lines in Text 11; a running member shows its running art, and the plates for hover and press are the Big Folder's. Its behaviour is in 2.7.
- **States:**
  - Idle: the icon.
  - Hover: the icon rises by the lift, and the label shows the name (a group's name for a group).
  - Pressed: the tile scales to 94 % about its centre.
  - Missing: the tile at 40 % opacity, and the label reads "Can't find {name}". The missing app is dimmed by opacity, not with the `Inactive` role. A click opens a small menu: a disabled row "{name} can't be found", then "Remove from dock".
  - Adding (a program dragged over the bar): a tile-sized slot in `Hover` (radius 0.225) opens where the program will land. The icons after it slide aside, and the bar grows by one icon and one gap.
  - Running, opening and closing: the running art swaps the tile's light and colour (a custom icon on a coloured tile turns white, and one on a white tile turns coloured), with no dot, no outline and no shadow. The 13 custom apps use the paint table of the design handoff. A generic app tile (an app with no icon of its own) swaps by the same rule.
    - **A real icon** keeps its picture, drawn at 68 % of the tile on an accent plate instead of the white tile plate. The plate is computed once for each icon picture and cached:
      - its hue is the saturation-weighted mean hue of the icon's pixels that are opaque (alpha ≥ 200) and saturated (HSL saturation ≥ 0.35);
      - its saturation is the larger of the mean saturation of those pixels and 0.55, and its lightness is 0.38 on the light bar or 0.42 on the dark bar;
      - an icon that is white, grey or mostly transparent (fewer than 2 % of its opaque pixels are saturated) gets the neutral plate `#3A3A3C`.
  - Forming a group: an icon held over another for 500 ms gets a `Pressed` plate, 6 larger than the tile on each side (radius 0.225 × (icon + 12)), and the dragged icon floats over it at 85 % opacity. Letting go groups the two.
  - Open with (files dragged over the bar): each app that opens all of the files lights up with a `Drop tint` plate, 4 larger than the tile on each side (radius 0.225 × (icon + 8)), and lifts as on hover. The app under the pointer shows its label as a `Select` pill with white text, "Open with {name}". The other apps do not change. Over a group, its panel opens after 500 ms.
- **Colours:** the dock uses the section 8 roles Primary, Hover, Pressed, Select, On select, Drop tint, Tile plate and Tile sheen: Hover for the group icon's plate, Pressed for the forming plate, Select and On select for the "Open with" pill, and Drop tint for the lit plates. The running look adds the running white tile (`#FFFFFF` to `#F2F2F7`) and the accent plate, both listed in section 8.
- **Menus:** `MenuEntry` rows through `MenuFlyout`, with the words of 2.7.
- **Motion:** Phase 12's vocabulary (above), plus one new token, **Start** 600 ms Out, for the opening wipe. With "Animation effects" off, every moment is one frame. The moments, by name:
  - **D1 Hover:** the icon lifts (Quick 100). The label fades in and rises 4 DIP (Enter 240). Moving to the next icon slides the label (Quick 100); leaving the bar drops the icon back and fades the label out (Exit 150).
  - **D2 Press:** the tile presses (Press 80); the release (Release 200) confirms the click.
  - **D3 Opening:** the running art's clip goes from 100 % to 50 % (Start 600), so the art rises from the bottom, then holds without a loop until the first window appears. With no window after 10 s, the art wipes back down (Move 280) and the icon is idle again, which is not an error.
  - **D4 Opened:** for an app started from the dock, the clip goes to 0 (Enter 240), and the tile pops from 1 to 1.06 at 45 % and back to 1 (Move 280). An app started elsewhere wipes from 100 % to 0 (Move 280), without the pop.
  - **D5 Closing:** the clip goes from 0 to 100 % (Move 280), so the running art sinks away.
  - **D6 Hide and reveal:** hiding slides the bar down by its height + 12 DIP (Move 280); revealing slides it up (Enter 240). It stays up while its menu, a group's panel or a drag is in use. It slides away 600 ms after the pointer leaves; that is a delay, not a motion.
  - **D7 Reorder:** the dragged icon follows the pointer, lifted, at full opacity; the others slide to their new places (Move 280); the drop settles it (Move 280). A member dragged out of a group's panel is a picture of its 48-DIP tile that follows the pointer in a window of its own; over the bar it eases to the bar's icon size (40, 48 or 64; Release 200, ease out) about the pointer, and back to 48 over the panel, so it lands at the size of the icon it becomes. With "Animation effects" off the size changes in one step. In the panel, a drag held within 28 DIP of the list's top or bottom edge scrolls it at 60 to 420 DIP per second, faster towards the edge, only while the pointer is there. A member's menu in the panel opens with its top-left corner at the pointer (WinUI flips it to stay on the screen), over the member.
  - **D8 Group forming:** after 500 ms over a target, the target's plate fades in (Quick 100) and the dragged icon floats at 85 % (Quick). Release replaces both with the group icon, which settles from 92 % (Release 200). Moving away fades the plate out (Exit 150) and the dragged icon back to full opacity (Exit 150).
  - **D9 Group panel:** opening fades it in (Enter 240) as it rises 8 DIP and grows from 97 %; closing fades it out (Exit 150) as it sinks 6 DIP and shrinks to 98 %.
  - **D10 Add:** the gap opens and the bar grows (Move 280). On the drop, the new icon enters (opacity and 85 % to 100 %, Enter 240). A drag that leaves closes the gap (Move 280).
  - **D11 Remove:** the icon goes to opacity 0 and 85 % (Exit 150); then the others close up and the bar shortens (Move 280).
  - **D12 Open with:** the lit plates and the lift fade in (Quick 100) and out (Exit 150). The pill moves with the pointer, as the label does. Over a group, its panel opens after 500 ms (D9).
  - **D13 Icon size:** the frost clip and the bar's length move from the old size to the new one (Move 280), anchored at the bar's centre. The old icons fade out (Quick 100), and the new ones fade in from the Move's half-way point (Enter 240).
  - **D14 Missing:** no motion.
  - **D15 Light ↔ dark and Glass ↔ Pixel:** the tint and the sheen blend (Blend 250), and everything else changes at once. A new Glass dock fades in (Enter 240, opacity only).

### 4.2 Pixel

Added 2026-10-13.

- **Large cards (172×182)**: content ends by y 175, 7 from the bottom edge. Battery: the list runs from y 8 to 175; a full list (six rows, or no room for another) stretches its rows to floor(167 / count), with text, slot and gauge centred and the separators moving with the rows, and a short list stays top-aligned. World clock: four rows of 41 from y 9.
- **Grid**: 1 art pixel = 2 DIP. Card art sizes: small 82×82, medium 172×82, large 172×182.
- **Scaling**: the scale factor N = round(DPI scale × 2), so 100% is 2, 150% is 3 and 200% is 4. The bitmap is shown 1:1, and every art pixel must be a solid N×N block: no filtering, no anti-aliasing, no fractional coordinates. Big numbers are only scaled by whole numbers (×2, ×3).
- **Card**: an opaque panel. 1 px outline; stepped corners (from the top down, inset by 4, 2, 1, 1 pixels); a 1 px highlight at the top and a 2 px shadow at the bottom. Glass's blur layer is hidden in the Pixel theme. The pixel card's outline is part of the pixel-art style and doesn't conflict with the decision that Glass has no outline.
- **Palette**: based on the Sweetie 16 palette, with a light (day) and a dark (night) set. Widgets must use only the roles in `PixelPalette`.
- **Fonts**: two hand-drawn, all-caps pixel fonts.
  - 3×5 (`PixelFonts.Small`): labels, names, small numbers.
  - 5×7 (`PixelFonts.Regular`): values, titles; big numbers use it ×2 or ×3.
  - Lowercase letters are turned into capitals automatically and accents are dropped (São → SAO).
  - Text the pixel fonts can't spell is set entirely in the 12 px SimSun bitmap font (`PixelFonts.System`), so layouts must leave a 12 px line height for Chinese. The Big Folder is the exception, below.
  - **Big Folder, Fusion Pixel 8 px** (`PixelFonts.Fusion`, Phase 8): everything the Big Folder draws in Chinese (names, the header, the empty, missing and drop texts, the panel's words and path) is set in Fusion Pixel Font 8 px: a 9-row cell (6 rows above the baseline, the baseline, 2 below), hanzi 7 × 7 on an 8-pixel advance, a 10-pixel line pitch, whole-pixel rendering. One font per text, in this order: the pixel font if it spells all of it, else Fusion if it covers all of it (Latin included, so a mixed name shows in mixed case), else SimSun for all of it; a shortened or wrapped piece keeps its text's font. Menus, dialogs and the other widgets keep SimSun.
  - **Big Folder, emoji:** an emoji (one grapheme cluster: a ZWJ sequence, a skin tone or a variation selector is one emoji) is the system's Segoe UI Emoji at 64 × 64, cropped to its ink and shrunk to 7 × 7 (11 × 11 in a SimSun line): each tile pixel takes the most common Sweetie colour of the source pixels it covers, and is transparent when the area's average alpha is under one half; no dithering, no outline. It takes one 8-pixel advance in Fusion (12 in SimSun) and counts as one character for shortening and wrapping. The pixel fonts never cover an emoji, so a name or title with one is set in Fusion. With no picture, a 7 × 7 outline box in the text's colour.
  - Both fonts have a backslash, so a path such as `C:\Users\asus\Work` needs no fallback.
  - A line of Chinese never ends with an opening mark (“ （ 「 『 《): the mark comes down with the text it opens.
- **Icons**: hand-drawn sprites in `PixelSprites`; disconnected devices are dimmed; while charging, an outlined lightning bolt is laid over the battery bar.
- **Layout**: all coordinates are in art pixels; content stays at least 7 pixels from the card edge; dotted lines (`PixelCanvas.DottedLine`) are used for separation.
- **Motion** (Phase 12, 2026-10-22): stepped frames, never tweens. Each frame is a whole picture on whole art pixels in palette colours (no alpha, no fractional positions), one tick T = 40 ms (`Motion/StepAnimator.cs`). Vocabulary: GROW 3 frames (an empty box of the target's own frame at 1/3 and 2/3 of its size, then the full target), SHRINK 2, DISSOLVE 4 (the 4×4 Bayer mask), MOVE 3 (along a row), POP 3 (a 7×7 mini tile, the cell 1 px above its place, at rest), LEAVE 2 and gone (the cell's pixels at Bayer ≥ 8, then ≥ 12), FLASH 1, STEP ≤ 6, WIPE 2. All of it is built: the Big Folder's A1 to A4 (below), menus and dialogs (B5, in Menus and dialogs below), and, on every card:
  - **Battery (B6):** a level change STEPs the gauge's fill in whole pixels (at most 6 frames, step ceil(distance / frames)); the number and the level colour change on its last frame. When charging starts the bolt POPs in 2 frames (the small bolt, then the outlined full one); when it stops it is gone at once. A new change cuts a running one short.
  - **Size and theme (B7):** a size change takes the new window size at once, draws the card's frame at once, and DISSOLVEs the content in from the empty card (4 frames). Light to dark DISSOLVEs the whole card (frame and content) from the old look to the new. Glass to Pixel: each new card draws its frame at once and its content DISSOLVEs in. The mask lives once in `PixelSurface`, so every widget has it.
  - **Widget added or removed (B8):** GROW from the card's centre on both axes (1/3, 2/3, then the card with its content), and on removal SHRINK to the centre (2/3, 1/3) and the window closes. A leaving card takes no clicks.
  The rules are in 2.8. The second hand ticks once a second.
- **Recorded compressions**:
  - The small world clock uses three-letter city codes (NYC, LAX, STJ, PEK, …).
  - The Chinese date is written as "10/13". SimSun's "10月13日" is about 48 pixels wide and doesn't fit the 39-pixel column of the medium size.
  - Big Folder, metric sets: a card or panel takes the set of its highest-ranked text (pixel font < Fusion < SimSun). Fusion set: the header box is 9 tall at y 7, the icon rows are y 19 and 49 (Large 19, 59, 99, 139) and a name is 9 tall at icon y + 17, which keeps the usual 7-pixel margins; in the panel the search field is 15 tall, a cell is 36 (16, 1, 9, 1, 9), the row pitch is 39 (3 rows) and name lines are 10 apart. Four hanzi fit a 36-pixel cell, so a longer name is shortened.
  - Big Folder, SimSun metric set (for a name Fusion cannot spell): when the folder name or any visible item name needs SimSun, the whole card uses the SimSun metrics, so the 12-pixel-tall header sits at y 5 on the Small and Medium cards (a 5-pixel margin instead of the usual 7) and the cell rows move to y 19 and 48.
  - Big Folder, shortening: a shortened name keeps at least 2 characters (3 in Latin) before the "…". With fewer, the tail is dropped and the name is cut at the end ("腾讯会议" becomes "腾讯…"). A visible extension wins over this minimum: with one, a head of 1 character is enough ("度…报告.docx", not "度财务报告.d…"); only when not even 1 character fits beside the extension is the name cut at the end.
  - Big Folder, extensions: with an extension shown, a shortened name's tail is the extension and the last 2 characters of the name before its dot ("cro…ve.txt", never "cross-dri….txt"). A name on two lines breaks after "-" or "_" first, then before the extension's dot, and inside a word only when none of those fits ("cross- / drive.txt"). One font per name: Chinese text is set in one font as a whole (Fusion, or SimSun when Fusion lacks a character), even where a shortened piece is only digits.
  - Big Folder, folder not found: no hint line, and the path only on Large.
- **Menus and dialogs** (added 2026-10-20): everything the app draws itself, not only the cards, looks like the cards under Pixel. Glass keeps its `MenuFlyout`, Win32 tray menu and MessageBoxes. The items, order, words and effects stay the same (section 2); only the look changes.
  - **How it is drawn:** the widget menu, the tray menu and both dialogs are our own borderless popup windows (`PixelUi/`) that show a `PixelCanvas` 1:1 at the same factor as the cards. Text uses the hand-drawn fonts through `PixelText`, with the SimSun fallback. The widget menu and the tray menu are built from one list of `MenuEntry` rows, which Glass turns into WinUI flyout items or a Win32 menu.
  - **Frame:** `PixelCard`'s panel algorithm on any rectangle: 1 px outline, fill, 1 px highlight at the top, 2 px shade lip at the bottom. Corner stair: menus [2, 1], dialogs [4, 2, 1, 1], buttons [1]. A hard shadow under menus and dialogs only (the panel's own shape, 2 art pixels down and right, role `Shadow`, no blur). Cards keep no shadow.
  - **Menu rows:** 15 art pixels tall when every label is in the pixel font, 18 when any label in the panel needs SimSun (one height per panel); separators 7. Lead column at x 4 (check 7×6, radio diamond 5×5, or a 7×7 icon; no row has two), label at x 14 in `PixelFonts.Regular`, submenu arrow 3×5. Width = 14 + widest label + (16 with a submenu row, else 8), at least 64. Submenus open 250 ms after hover, or at once on click, Right or Enter, with their outline sharing the parent's edge column and their first row level with the parent row.
  - **States:** the pointer's row and the keyboard's current row are the same highlight (`Select`, text `OnSelect`); pressed `SelectPressed`; the parent row of a submenu that has the focus `OpenFill`; disabled rows `Faint`, never highlighted and skipped by the keyboard; a status line is `Soft` and not focusable. **Exception to 2.7:** `Faint` on disabled labels is deliberate; disabled text has no contrast requirement and must look unavailable.
  - **Long lists:** a panel taller than its monitor's work area is cut to whole rows with a 9-pixel scroll row at each end (the wheel scrolls a row per notch, hovering a scroll row scrolls a row every 100 ms, the current row is always scrolled into view, and the checked row is visible on opening). The scroll rows keep their space even when there is nothing more in that direction, so the panel does not change height while scrolling.
  - **Dialogs:** 172 art pixels wide (the large card's width), a title strip with the app mark and name (drag it to move the dialog), a 13×13 badge (a `Select` disc with "?" for a question, a yellow triangle for a warning), text in `PixelFonts.Regular`, and 17-pixel buttons (18 with SimSun). The default button is the primary one (`Select` face); Exit's question opens with No focused; Esc answers No (OK in the error box); Y and N answer; Tab and the arrows move the focus. Modal to the widget that asked, centred on its monitor, topmost.
  - **Motion (B5, Phase 12):** a menu GROWs downward from the pointer's corner (upward when it opens above) at full width, height 1/3, 2/3, then the rows, and SHRINKs the same way (2/3, 1/3) after a choice, Esc, a click outside or losing the foreground; a submenu GROWs sideways from its parent's edge (half the width, then all) and SHRINKs the same way; a dialog GROWs from its centre on both axes (1/3, 2/3, then all) and SHRINKs the same way after the answer. Popup windows are at their final place and size from the first frame, with the rest transparent, and are disposed only after the last SHRINK frame; a shrinking popup takes no input. A row's `Select` bar WIPEs in from the left (half its width, then all; the label stays readable on both halves) for the pointer's row and the keyboard's current row alike, and a new row cuts the last wipe short; a press is `SelectPressed` at once, and the chosen action runs on release, at once. Any input on a growing popup draws it whole.
  - **Keyboard and Narrator:** arrows, Home/End, Enter/Space, Esc and Left/Right work as in a native menu; the popups expose UI Automation roles (menu, menu items with Toggle, SelectionItem or ExpandCollapse patterns, dialog with buttons).
  - **Tray icon:** the Pixel theme has its own (a dark stepped tile with a hard-edged ring and a pixel mouse), drawn at the exact small-icon size for the display's scale and swapped live on a theme or scale change. The exe's own icon stays as it is.
- **Big Folder** (added 2026-10-21; all numbers in art pixels):
  - **Card:** the usual panel. The header has the folder name in `PixelFonts.Regular` (`Ink`) at x 7 and the count (`Soft`) right-aligned 7 from the edge. Cells are 36 wide: a 16×16 icon over a name in `PixelFonts.Small`: one line, or up to two on Large (the panel's breaks; each line keeps its font's pitch, 7, 10 or 13; the cell is 30 tall in the pixel-font set (16, 2, 5, 2, 5) and 36 in Fusion (16, 1, 9, 1, 9); the click rect is the cell plus 2 above and below; the SimSun set stays one line, as two lines need a 42 cell). Columns at x 4 and 42 (Small) or 5, 47, 89 and 131 (Medium, Large); rows at y 19 and 52 (Small, Medium) or 19, 61, 103 and 145 (Large; 19, 59, 99, 139 in the Fusion set and 22, 63, 105, 146 in the SimSun set), so the last name ends at y 175. "+K" is on line 1. The stack is four 7×7 minis (the icon's signature color inside a 1 px ink outline) and "+K".
  - **States:** hover `Open fill`, pressed `Shade` with the contents 1 px lower; the header takes the same plates. A missing target dims its icon (ink to `Faint`, every other color to `Track`) and its name (`Faint`). Empty: a dotted rectangle with a drop arrow and "Drop files here". Folder not found: a warning sprite and "Folder not found". Drop hover: every other panel-colored pixel turns `Select`, and a `Select` plate shows the verb.
  - **Icons:** 14 file-type sprites and 13 app sprites, 16×16, in the 16 fixed sprite colors of section 8. Any other app: its real icon shrunk to 14×14 ("tier 2": box filter, nearest Sweetie color by redmean distance, no dithering, a 1 px ink outline round the shape). Photos and videos: the center square shrunk to 12×12 with 4×4 Bayer dithering, inside a white print border and an ink outline with the corner pixels cut (a video adds a play mark). Until the real picture arrives, the type's sprite shows.
  - **Panel:** 220 × 196 art pixels in a `PixelPopupWindow` (corner stair [4, 2, 1, 1] and the hard shadow of the dialogs), 4 art pixels from the card (right if it fits, else left, else over it). One row with the folder name and "{n} items"; one row with the search field (well, magnifier, 1 px caret) and the sort button (label and chevron); then five columns of cells (pitch 41 across and 33 down, 39 in the Fusion set or 45 in the SimSun set) with two-line names in `PixelFonts.Small`, a 3 px scrollbar, a dotted rule, and the footer (the path in `Soft`, "{n} selected" in `Ink`). Selected cells are `Select` plates with `OnSelect` text; the keyboard focus is a dotted ring; the wheel scrolls a row per notch.
  - **Behavior:** the same as Glass (2.3). Typing anywhere searches (a Backspace removes a character); the fields take text through the system's text services, so the Pinyin IME (its candidate window opens beside the field), the emoji panel and Ctrl+V all work, and an emoji stays whole in the name; F2 renames in place, with a field in the cell or the title row; Delete sends the selection to the Recycle Bin; F4 or Alt+Down opens the sort list. Esc cancels a rename, then clears the search, then closes the panel. The panel closes by the rules in 2.3: it stays through its own menus, dialogs and file-operation windows, and closes when another program takes the foreground. The selection drags out through OLE, so Explorer and the desktop accept it as they accept Explorer's own drags.
  - **Keyboard and Narrator:** the panel is a window with the search field (Edit with the Value pattern), the sort button (ExpandCollapse) and one list item per shown item (Invoke and SelectionItem).
  - **Menus:** the item menu and the widget's own rows use the Pixel menu described above. The item menu ends with "Show more options", after a separator and with no icon (like Open and Properties); it opens Windows' own context menu, which keeps the system's look under Pixel (2.3).
  - **Motion (A1 to A4):** the panel GROWs from the card's side at full height (width 1/3, 2/3, then the panel; 120 ms), in a window of its final size with the rest transparent, and SHRINKs to that side (2/3, 1/3; 80 ms); it counts as closed from the first moment. On a drag over the card the tint shows at 1 dot in 16 with no label, then as it is with the label (the reverse on leaving; a drop snaps it off). A changed list (a drop, a rescan, a sort, a search) moves the cells that stay in their row, POPs the new ones and those that changed rows, and LEAVEs the old ones (3 frames, 120 ms, only cells in view). Hover lifts a cell's content 1 px, with the `Open fill` plate (the header too); a press lowers it 1 px on `Shade`; a completed click FLASHes the plate in `Select` once; cells being dragged out of the panel are drawn at half their pixels while the drag lasts.

**Dock** (added 2026-10-31; all numbers in art pixels; the detailed source is `notes/design/pixel/dock-20261031/HANDOFF.md`):

| | Small | Medium | Large |
|---|---|---|---|
| Icon | 20 | 24 | 32 |
| Slot (icon + 2 of halo room on each side) | 24 | 28 | 36 |
| Bar height (2 × pad 4 + slot) | 32 (64 DIP) | 36 (72 DIP) | 44 (88 DIP) |
| Group minis (size, gap) | 7 (1) | 9 (1) | 12 (2) |

- **Spacing:** 4 from the card's outline to a slot, plus 2 of halo room, so 6 (12 DIP) from the outline to the icon. Slot to slot is 2, so icon to icon is 6 (12 DIP), and two halos never touch. Length = 2 × 4 + n × slot + (n − 1) × 2. The empty bar is 120, 140 or 170 art pixels long (240, 280 or 340 DIP). Padding and gap match Glass at every size (4.1).
- **Window:** the bar sits 4 art pixels (8 DIP) above the screen's bottom edge. The window has 21 art pixels (42 DIP) of transparent room above the bar for the name plate, and is wide enough for the widest label at either end.
- **Icons:** each size is drawn on its own grid and is never scaled. Custom apps and file types use the sprite roles of section 8. A tile has a 1-px ink outline, its fill, a shade lip along its bottom edge and a corner stair from the top: [3, 2, 1] at 20, [4, 2, 1, 1] at 24 and [5, 3, 2, 1, 1] at 32. Real icons use the Big Folder's tier-2 rule at the dock's size.
- **Group tile:** the app tile's shape in silver (`g`, shade `s`), holding up to four minis in a 2 × 2. Each mini is the app's 32-art-pixel icon (awake while it runs), reduced to 7, 9 or 12 art pixels at Small, Medium or Large, with the gap from the table between minis. The 2 × 2 is centred on the tile.
- **Frame and shadow:** the card's frame (outline, Panel fill, a 1-px Highlight row, two Shade rows at the bottom and the stair [4, 2, 1, 1]), with the menus' hard 2-px Shadow down and to the right. The dock is the one card with a shadow, because it floats over windows. Desktop cards keep none.
- **Halo:** a running icon has a 1-px halo that follows its shape, 2 art pixels out. Its colour is the custom app's own colour from its sprite, the generic tile's slate, or else the most common coloured sprite-role pixel of the icon (ink, white, silver, slate and dusk do not count; blue when there is none). Every halo is then kept readable on the bar: on the light bar yellow steps down to orange, and cyan and lime step down two places (to blue and teal); on the dark bar purple, navy, teal and dusk step up one place. The halo uses the sprite roles; the dock adds no colour roles.
- **States:**
  - Idle: the idle drawing.
  - Hover: the icon lifts 2 art pixels (4 DIP) at every size, and the name plate shows. The hit box stays where the slot is; the lift moves only the picture.
  - Pressed: a `Shade` plate behind the slot (corners [2, 1]), and the icon 1 px down.
  - Missing: ink becomes `Faint` and every other colour becomes `Track`, as in the Big Folder. A click opens a Pixel question dialog, "{app} can't be found. Remove it from the dock?", with Remove (primary, focused) and Cancel.
  - Adding: a dotted box in `Soft`, one slot and one gap wide, opens where the program will land; the icons after it move aside by one slot.
  - Reordering: the dragged icon rides with the pointer, 6 px up and undimmed. Its old slot is a dotted box in `Faint` until the others move.
  - Empty: a dotted box in `Faint`, inset by the halo room, with "DROP APPS HERE" in `Soft`, centred. The pixel font draws it in capitals.
  - Running, opening and closing: the awake drawing and the halo, with the hop and dissolve of the motion below. A real icon and the generic tile keep their one drawing, with the halo.
  - **Forming a group:** when an app held over another has waited 500 ms, the target turns into a group tile with its own mini at once, under the dragged icon. On the drop, the 2 × 2 group tile is in place.
  - **Open with:** each app that opens the files gets a `Select` plate at once. The one under the pointer also lifts 2 px, and its name plate reads "Open with {name}" in `Select` with `OnSelect` text. Over a group, its panel opens after 500 ms.
- **Name plate:** a menu-style plate (outline, Panel fill, a 1-px Highlight row, one Shade row, corners [2, 1] and the hard 2-px Shadow). It is 13 art pixels tall for pixel-font text, or 15 for Fusion (26 or 30 DIP). Its text sits 5 px in from each side. Its bottom is 4 art pixels above the bar's top; it is centred over the icon and kept 4 pixels inside the screen. It is at most 120 art pixels wide (240 DIP); a longer name is shortened in the middle by `NameShortener`.
- **Fonts:** the name plate, the empty text and the dock's other labels follow the Fusion rule of the Big Folder above. Each text uses `PixelFonts.Regular` (5×7) if it spells all of it; otherwise Fusion Pixel 8 px if it covers all of it; otherwise the 12 px SimSun bitmap font for all of it. This extends that rule to the dock's labels and empty text. Menus and dialogs keep SimSun.
- **Group panel:** a popup with the dialogs' frame (corners [4, 2, 1, 1]) and the hard shadow, centred on the group icon and kept 4 art pixels inside the screen. Its bottom is 6 art pixels above the bar's top, and a stepped 4-row pointer in that gap points at the icon. Cells are 40 × 46 art pixels with 10 pixels of padding on each side, so the panel is 140 art pixels wide with three columns or 180 with four (columns = clamp(count, 3, 4)). It shows at most three rows and then scrolls, with a 3-pixel scrollbar. The title row has the group's name and an "Open all" button on the right. Each member is a 24-pixel icon (with its halo while it runs) and a name of up to two lines below it; hover is an `OpenFill` plate. Its behaviour is in 2.7.
- **Menus:** the Pixel menus of this section, with the words of 2.7. "Show more options" has no icon, as in the Big Folder, and opens Windows' own menu.
- **Motion:** stepped frames of T = 40 ms, on whole art pixels with no alpha. The moments are Glass's (4.1), D1 to D15, in pixel form:
  - **D1 Hover:** lift 1 px, then 2 px (2 frames); the plate shows on the second frame. Leaving: down 1 px in one frame, and the plate goes at once.
  - **D2 Press:** a `Shade` plate, 1 px down. Release: one FLASH frame in `Select`, and the app starts at once.
  - **D3 Opening:** a HOP (the icon lifts 1, 2, 3, 3, 2, 1 and 0 art pixels, 280 ms), then DISSOLVE frames 1 and 2 into the awake drawing. It holds, without a loop, until the first window appears.
  - **D4 Opened:** DISSOLVE frames 3 and 4. An app started elsewhere dissolves in all four frames, from idle. There is no pop in Pixel.
  - **D5 Closing:** DISSOLVE from the awake drawing back to idle (4 frames, 160 ms). A timeout (no window after 10 s) dissolves back to idle from the hold, in two frames.
  - **D6 Hide and reveal:** STEP. The window moves once a Pixel frame (40 ms), to where the controller's slide is by then (down on Exit 150, up on Enter 240; Glass's own D6 hides on Move 280), so the bar steps rather than glides, up and back down. The scene is drawn once and the window moves at each step. It slides away 600 ms after the pointer leaves, as in Glass. Once it has slid away, the window is hidden, not parked below the screen.
  - **D7 Reorder:** the dragged icon rides 6 px up with the pointer. When the pointer passes the middle of the next slot, that icon MOVEs over (3 frames) and the gap follows. The drop is instant. A member dragged out of a group's panel is the panel's 24-pixel icon until it crosses onto the bar, and then steps to the bar's size (halfway, then the full 20 or 32; one frame each, nothing for a 24 bar) about the pointer; with "Animation effects" off it is the bar's size at once. In the panel, a drag held within 8 art pixels of the grid's top or bottom edge scrolls the list one row every 5 frames, only while the pointer is there.
  - **D8 Group forming:** when the hold timer fires, the target turns into a group tile at once. On release, the dragged app's slot closes up (MOVE), and the group POPs (1 px up, then at rest).
  - **D9 Group panel:** GROW up from the dock at full width (heights 1/3, 2/3, then all; 120 ms); SHRINK back (2/3, 1/3; 80 ms).
  - **D10 Add:** as the drag passes, the icons after the pointer MOVE one slot aside (3 frames) and the bar grows; on the drop, the new icon POPs in.
  - **D11 Remove:** LEAVE (Bayer ≥ 8, then ≥ 12, then gone); then the icons after it MOVE in (3 frames) and the bar shrinks.
  - **D12 Open with:** the lit plates show at once; the target lifts 2 px with its `Select` plate and label. A drop snaps them all off, and the app's D3 starts for an app the dock tracks. Over a group, its panel opens after 500 ms (D9).
  - **D13 Icon size:** the window takes the new size at once, the frame draws at once, and the icons DISSOLVE in (4 frames).
  - **D14 Missing:** no motion; it snaps.
  - **D15 Light ↔ dark and Glass ↔ Pixel:** a light/dark change DISSOLVEs the whole dock; Glass to Pixel draws the frame at once and DISSOLVEs the icons in.
  - Sliding the bar along the edge follows the pointer, with no motion of its own. The rules of 2.8 hold: with "Animation effects" off, everything snaps; input never waits; a new state cuts the old one short; nothing loops.

---

## 5. Adding a theme

1. **Design first**: fill in the design brief of Appendix A and put it in section 4. Make static previews (every widget × every size × light/dark × Chinese/English) and have the user confirm them before writing code.
2. **Write the code**:
   - Add a value at the end of the `WidgetTheme` enum. The name is saved in `settings.json` as text, so once chosen it must never be renamed.
   - Add the Chinese and English names to `Strings.ThemeName`. The tray menu and the right-click menu list the new theme automatically.
   - Card: change `WidgetFrame.SetCard`. Call `GlassBackdrop.SetVisible(true)` only if you need the live blur. If the card's pixel size isn't "DIP × scale", also change `WidgetLayout.GetCardPixels`.
   - Palette: create a class that has every role of section 8, in a light and a dark set.
   - Each widget: pick the rendering from `context.Theme` in the constructor, covering all the sizes it supports.
   - Preview: following `Pixel/PixelPreview.cs`, let the new theme export an overview of all layouts with one command.
3. **Write the docs**: update the README's introduction and controls table, section 4 of this file, and the changelog at the end.
4. **Acceptance**: everything in section 7 passes.

---

## 6. Changing an existing theme

### 6.1 Change levels

| Level | Examples | What it takes |
|---|---|---|
| Tweak | Spacing ±1–2, a different color within the same role, wording | Run the relevant checks of section 7 and write a changelog row |
| Redesign within a theme | A new layout for one size, new icons or fonts, changed motion | Show the user before/after comparisons; update section 4 |
| Change to a shared rule | Card sizes, which information is shown, color meanings, how interaction works, copy, settings fields | Ask the user first; change all themes in the same change; update section 2 |
| Overturn a user decision | For example adding an outline or shadow to Glass, or putting city names back into Chinese | Must have the user's explicit agreement first |

### 6.2 Other rules

- **Compatibility**: identifiers already saved in `settings.json` must never be renamed or removed:
  - `TypeId`: battery, clock, worldclock, folder
  - Enum names: `WidgetTheme`, `WidgetSize`, `AppLanguage`
  - Option keys: showNames, showDisconnected, cities, showSeconds; for the Big Folder, `path`, `sort` (newest, name, type or manual) and `order` (a JSON array of file names, used by Manual)
  - Top-level setting `FolderRoot` (optional text, the folder new Big Folders are made in; absent keeps the default)

  The default of a new option must keep the existing behavior.
- **Working together**: several sessions may be changing this repository at once.
  - Re-read a file before editing it.
  - Keep each file's own line endings (the repository mixes CRLF and LF).
  - Don't reformat code you aren't changing, and don't revert other people's changes.
- **Keep evidence**: attach a screenshot or preview image to every visual change.
- **Record**: write one line in the changelog at the end: date, theme, what changed, why.

---

## 7. Acceptance checklist

**Build**

- [ ] `dotnet build src/DeskWidgets.App -c Release` has zero errors and zero warnings. (A running Debug app locks its exe, so check compilation with Release.)

**Appearance**: every widget × every supported size

- [ ] Check both English and Chinese.
- [ ] Check both light and dark system modes. Check dark with the preview sheets; **don't change the user's system settings for testing**. If you need to see it on the real machine, ask the user to switch it themselves.
- [ ] Extreme data:
  - Long names: Los Angeles, Mexico City, device names that need two lines
  - The Chinese fallback font
  - Charge ≤ 20%, charging, disconnected, no devices
  - A city that is already on the next day, day and night dials
- [ ] Scaling: check at least 100% and 150%. For Pixel, confirm there are no blurry pixels or pixels of uneven width.

**Behavior**

- [ ] Switching theme from both the tray menu and the right-click menu works without a restart; positions re-snap after the switch; no layer from the previous theme is left over (for example, no blurred glass under the Pixel theme).
- [ ] Switching language takes effect immediately.
- [ ] Dragging and the right-click menu work.
- [ ] CPU is close to zero when idle.

**How to check**

- Pixel theme: `DeskWidgets.exe --pixel-preview <new folder>` exports four overview sheets (day/night × Chinese/English). It also works while the app is running.
- Big Folder in the Pixel theme: `DeskWidgets.exe --pixel-folder-preview <new folder>` exports its sheets (cards at every size, states, icons and the panel; day/night × Chinese/English). It also works while the app is running.
- A test layout:
  1. First back up `%LOCALAPPDATA%\DeskWidgets\settings.json`.
  2. Start with an "always on top" test layout and take screenshots.
  3. When done, restore the user's original settings and restart the program.
- Capturing the real widgets on the desktop: other windows often cover widgets, so an ordinary screenshot can't see them. Use a process with Per-Monitor V2 DPI awareness to call `PrintWindow(hwnd, PW_RENDERFULLCONTENT)`. Note that this can't capture Glass's blur.

---

## 8. Color roles

| Role | Used for | Glass (light / dark) | Pixel (light / dark) |
|---|---|---|---|
| Primary text (Primary / Ink) | Names, values | `#000000` / `#FFFFFF` | `#1A1C2C` / `#F4F4F4` |
| Secondary text (Secondary / Soft) | State, time, date, time difference | Solid like Primary since 2026-10-27: `#000000` / `#FFFFFF` (was `#3C3C43` 60% / `#EBEBF5` 60%) | `#566C86` / `#94B0C2` |
| Inactive | Something absent: a disconnected device, an unknown level, an app whose target is gone | `#3C3C43` 60% / `#EBEBF5` 60% | `#566C86` / `#94B0C2` (Soft) |
| Faint | Tick marks, separators; not for body text | Dial minor ticks: the dial's ink at 45% | `#94B0C2` / `#566C86` |
| Track | Empty groove of the battery ring and bar | Black 14% / white 24% | `#D5DFE6` / `#333C57` |
| Placeholder / Well | Empty device slot, recessed groove | Black 8% / white 12% | `#E3E9ED` / `#131423` |
| Green | Charge is normal | `#34C759` / `#30D158` | `#38B764` / `#A7F070` |
| Yellow | Power-saving mode; Pixel's charging bolt | `#FFCC00` / `#FFD60A` | `#FFCD75` / `#FFCD75` |
| Red | Charge ≤ 20% | `#FF3B30` / `#FF453A` | `#B13E53` / `#EF5D57` |
| Accent | The clock's weekday | `#FF453A` | `#B13E53` / `#EF7D57` |
| Orange | Second hand | `#FF9500` | `#EF7D57` |
| Day dial | Local 6:00–18:00 | Face `#FFFFFF`, ink (hands, numerals, ticks) `#1C1C1E` | Face `#FFFFFF` / `#F4F4F4`, hands `#1A1C2C` |
| Night dial | Any other time | Face `#1C1C1E`, ink `#FFFFFF` | Face `#29366F`, hands `#F4F4F4` |
| On accent | Glyph on a coloured badge (the charging bolt) | `#FFFFFF` / `#000000` | — |
| Card | Base | White 24% / black 24% + diagonal highlight | Panel `#F4F4F4` / `#1A1C2C`, outline `#333C57` / `#0B0C14` |

A new theme gives color values row by row from this table. It may add roles but must not leave any out.

Roles added by the Pixel menus and dialogs (Glass keeps its system menus, so it has no values for them):

| Role | Used for | Pixel (light / dark) |
|---|---|---|
| Select | Menu: the current row; dialog: the default button's face and the question badge | `#3B5DC9` / `#3B5DC9` |
| Select pressed | Menu: a pressed row; dialog: the default button's hover, pressed and bottom row | `#29366F` / `#29366F` |
| Select light | The default button's top row (decoration only, never under text) | `#41A6F6` / `#41A6F6` |
| On select | Text and marks on Select and Select pressed | `#F4F4F4` / `#F4F4F4` |
| Open fill | Menu: the parent row of a submenu that has the focus | `#D5DFE6` / `#333C57` |
| Button, top, hover, pressed | The secondary button's face, top row, hover face and pressed face | `#FFFFFF`, `#FFFFFF`, `#E3E9ED`, `#C5D3DC` / `#333C57`, `#566C86`, `#566C86`, `#1A1C2C` |
| Shadow | The hard shadow under menus and dialogs | `#1A1C2C` at 40 % / `#000000` at 60 % |

Roles added by the Big Folder:

| Role | Used for | Glass (light / dark) |
|---|---|---|
| Hover | Hover plates, the stack's plate, the empty-state disc, the loading plate, the search field | black 8 % `#14000000` / white 12 % `#1FFFFFFF` |
| Pressed | A pressed plate, the selected item's tile plate, the focused search field | black 14 % `#24000000` / white 24 % `#3DFFFFFF` |
| Select | The drop label, the selected name's pill, the keyboard focus ring | `#007AFF` / `#0A84FF` |
| On select | Text and glyphs on Select | `#FFFFFF` / `#FFFFFF` |
| Drop tint | The tint over the whole card while a drop hovers | Select at 22 % `#38007AFF` / at 30 % `#4D0A84FF` |
| Tile plate | The plate under a real app icon | `#FFFFFF` / `#E9E9EE` |
| Tile sheen | Over every designed tile, under its symbol | white 16 % at the top to 0 % at the bottom, both modes |

The file-type tile colors (light and dark pairs) are in the Glass design's `ICONS.md` and in `GlassTiles`. Under Pixel, the Big Folder uses the roles above (Select, Open fill, Shade and so on) and the sprite roles below.

Roles added by the dock (Glass; the Pixel dock adds none, because its halo uses the sprite roles below):

| Role | Used for | Glass (light / dark) |
|---|---|---|
| Running tile | The running look of a custom icon or the generic app tile: the white tile under the tile sheen | `#FFFFFF` to `#F2F2F7`, top to bottom, in both modes |
| Accent plate | The running look of a real icon: the plate behind its picture | Computed for each icon, not a fixed value (4.1): the hue comes from the icon's own colours, lightness 0.38 (light) or 0.42 (dark); `#3A3A3C` for an icon that is white, grey or mostly transparent |

Pixel sprite roles: 16 fixed colors, the same by day and by night, for the Big Folder's file-type and app sprites (`PixelPalette.Sprite(symbol)`; the symbols are those of `sprites.txt`):

| Symbol | Color | Symbol | Color | Symbol | Color | Symbol | Color |
|---|---|---|---|---|---|---|---|
| `#` ink | `#1A1C2C` | `p` purple | `#5D275D` | `r` red | `#B13E53` | `o` orange | `#EF7D57` |
| `y` yellow | `#FFCD75` | `L` lime | `#A7F070` | `G` green | `#38B764` | `t` teal | `#257179` |
| `n` navy | `#29366F` | `b` blue | `#3B5DC9` | `l` sky | `#41A6F6` | `c` cyan | `#73EFF7` |
| `w` white | `#F4F4F4` | `g` silver | `#94B0C2` | `s` slate | `#566C86` | `d` dusk | `#333C57` |

---

## Appendix A: design brief template for a new theme

```
Theme name: enum value (English) / Chinese name / English name
Character and references (one or two sentences, with reference images):
Card: shape, corner radius, material (transparent or not), edge, highlight and shadow
Palette: one light set, one dark set (color values row by row from section 8)
Fonts: families, type scale, how Chinese is handled
Icons: source and style (mind 2.10, where assets come from)
Motion: what moves, duration, easing
Layout: one sentence for each widget × size (with a sketch or preview image)
Compressed wording when it doesn't fit:
Known risks and trade-offs:
```

## Appendix B: decisions the user has confirmed

Check this table before making a change. To overturn any of them, you must first get the user's agreement.

| Date | Decision |
|---|---|
| 2026-10-12 | Glass must be thin and see-through; no thicker tint, no shadow |
| 2026-10-12 | No white outline around the card |
| 2026-10-12 | The battery widget shows only peripherals, not the laptop's own battery |
| 2026-10-12 | The device name is shown under the icon (can be turned off in the right-click menu) |
| 2026-10-12 | Widgets snap to the desktop icon grid |
| 2026-10-13 | The interface language defaults to English and can be switched to Chinese, taking effect immediately |
| 2026-10-13 | The world clock defaults to New York, Los Angeles, St. John's (Newfoundland) and Beijing; city names are always in English; the full time is shown under the city name and the local date under the time |
| 2026-10-13 | Add a switchable Pixel theme |
| 2026-10-20 | Under Pixel, the app's own menus, dialogs and tray icon are pixel art too; Glass keeps the system ones |
| 2026-10-20 | Add a Big Folder widget: a phone-style card on a real folder, with a panel for the rest; files dropped on the card go into the folder |
| 2026-10-21 | A new Big Folder makes its own folder under the user profile, on the Desktop's drive; "Change folder…" points it at any other. Glass uses the Tile look for icons; Pixel gets a full set of sprites too |
| 2026-10-21 | A drop never moves a program: a program file, a portable app's folder, a Start menu or Store app, or anything under Program Files, Windows or ProgramData, or a program under AppData, becomes a shortcut (a shortcut dragged from the desktop moves in). Ordinary files follow Explorer's rules |
| 2026-10-21 | App icons: our own drawings (a color and a motif, never a logo) for the first 13 apps, the real icon for the rest |
| 2026-10-21 | In the Big Folder panel a click selects (Ctrl and Shift extend), a double-click or Enter opens; on the card a click opens. Esc clears the search first, then closes |
| 2026-10-21 | 2.7 amended: the Big Folder card takes clicks on its cells and header and accepts dropped files |
| 2026-10-21 | The Big Folder panel stays open through its own work and every window that work brings up, and closes only when the user leaves it (Esc, a click on the header or stack, a press on another card, the desktop or another program, Alt+Tab) or after opening something |
| 2026-10-21 | Ordinary items taken from the Desktop or the Public Desktop always move into a Big Folder, even across drives (Ctrl copies, Alt makes a shortcut); programs keep their rules, and items from anywhere else keep Explorer's. New Big Folders are made in the user's chosen default location (the `FolderRoot` setting, picked from the card's menu), else as before |
| 2026-10-22 | Every Large card is 344×364 DIP (Pixel 172×182), so a Big Folder beside two stacked Mediums has level edges, and the Pixel Big Folder Large card shows names on two lines, as in the panel. Snapping stays on; with it off, cards keep 16 DIP gaps |
| 2026-10-22 | Motion in both themes: Glass with the vocabulary of 4.1, Pixel as stepped frames of 40 ms with the vocabulary of 4.2 (no alpha, whole art pixels); the same moments in both (the Big Folder's panel, drag feedback and items, hover and press, search and sort first); with Windows' "Animation effects" off everything snaps, and input never waits |
| 2026-10-27 | Glass text is solid: black in light mode, white in dark mode, for names and secondary text alike (time, date, device names); only absent things (a disconnected device, an unknown level, a missing app) stay dimmed |
| 2026-10-31 | The dock is a real dock above windows, at the bottom edge; it is not a desktop widget. The user: "在桌面、窗口没有最大化的时候常驻；有最大化的时候隐藏，鼠标移到上面后出现；游戏、视频等全屏的时候不弹出" |
| 2026-10-31 | The dock hides only while a window on its screen is maximized (Plank calls this "dodge maximized"); a large window that is not maximized stays under the dock |
| 2026-10-31 | No running dot. A running program shows its own running icon, with an animation when it opens and when it closes. The user: "运行中的程序额外设计一套图标，打开、关闭的时候都加上相应的动画" |
| 2026-10-31 | A running program that is not in the dock is not shown; the taskbar shows it |
| 2026-10-31 | A new dock starts empty, with "Drop apps here", and "Add taskbar apps" is in its menu |
| 2026-10-31 | The taskbar's pinned apps are moved into the dock with "Add taskbar apps", and the user unpins them from the taskbar by hand; the app never changes the taskbar |
| 2026-10-31 | The Glass dock keeps the cards' thin glass (24 % tint) over windows too; don't thicken the tint. Over a white or black window the bar nearly disappears, and the user accepted that |
| 2026-10-31 | The dock designs are approved as previewed: Glass with its running look (the tile's light and colour swap), and Pixel with running look B, "awake + halo" (the user's choice; approved with "可以") |

---

## Changelog

| Date | Theme | Change | Why |
|---|---|---|---|
| 2026-10-12 | Glass | From "blurred wallpaper screenshot + 52% tint + shadow" to live blur + 24% tint, shadow removed | The user found it too thick |
| 2026-10-12 | Glass | Removed the 1 px white highlight outline around the card | The user found it ugly |
| 2026-10-13 | Pixel | Added the Pixel theme | The user wanted a switchable pixel look |
| 2026-10-13 | All | The world clock shows the local date under the time; the "+1" next-day marker was removed | The user asked for it; the date already shows the day change |
| 2026-10-13 | — | Wrote this spec, v1 | To make later theme design and changes easier |
| 2026-10-19 | Glass | Moved the clocks' hard-coded colors into one palette, `GlassPalette` (which also took over `BatteryPalette`); every color keeps its exact value | Paid off the debt recorded in 2.9 |
| 2026-10-20 | Pixel | Menus and dialogs now look Pixel too: the widget menu, the tray menu, Exit's question and the error box are our own popups with a hard shadow; new roles Select, Select pressed, Select light, On select, Open fill, Button, Button top, Button hover, Button pressed and Shadow; the Pixel tray icon; Chinese text no longer starts a line with closing punctuation | The user asked for the whole app, not only the cards, to be Pixel |
| 2026-10-21 | All | Added the Big Folder widget (TypeId `folder`) in both themes; 2.3 and 2.7 changed (a Folder row; clicks and drops on its card); the name shortening of 2.9 and the panel behavior are shared | The user asked for a phone-style folder on the desktop |
| 2026-10-21 | Glass | New roles Hover, Pressed, Select, On select, Drop tint, Tile plate and Tile sheen; the tile look and a 440 × 500 panel | The Big Folder |
| 2026-10-21 | Pixel | 16 fixed sprite roles; a backslash in both pixel fonts; a Chinese line never ends with an opening mark; a 220 × 196 panel; recorded compressions for the SimSun metric set, name shortening and a missing folder | The Big Folder |
| 2026-10-21 | All | Big Folder design QA, rounds 1 and 2: the shared shortening keeps the extension and 2 characters before its dot, and two lines break at "-" and "_" first; Glass: the drop label's width, the rename box (a Pressed plate, as wide as the name), the panel grid and its result position, one clear button, selected names that do not rewrap, the fades; Pixel: the drop tint leaves icons and text alone, one font per name, a rename field as wide as the cell, a plain page for a file with no extension, the line pitch of each font in the panel; the Pixel panel count is the folder's total | Design QA |
| 2026-10-21 | All | The Big Folder panel no longer closes after almost every action: one rule for both themes (2.3), decided by whose window took the foreground; nothing visible changes | The user: "it shrinks back after one operation" |
| 2026-10-21 | Pixel | The Big Folder panel's search and rename fields take text through the system's text services (TSF): the Pinyin IME, the emoji panel (Win+.) and Ctrl+V work, and an emoji stays whole in a name; nothing visible changes | The user: "the title takes no emoji" (Pinyin did not work in the field at all) |
| 2026-10-21 | Glass | Big Folder names are smaller so more of them show: Text 10 on Small and Medium (68 and 78 wide), Text 10.5 on Large in two lines with a 44 tile and 17 minis, Text 11 in the panel; a visible extension wins over the shortening's minimum head | The user: "the file names are too big" |
| 2026-10-21 | Pixel | The Big Folder's Chinese is set in Fusion Pixel Font 8 px (OFL-1.1, bundled with its license) before SimSun: four hanzi per cell, a third metric set (header 9, rows 19/49, panel cell 36, 3 panel rows), and a font order of pixel font, Fusion, SimSun; emoji are 7 × 7 tiles shrunk from Segoe UI Emoji; recorded compression: 4 hanzi per cell | The user: "the Pixel names are too big too" |
| 2026-10-21 | All | Big Folder: a "Default location for new folders…" row in the card's menu (a folder picker; the `FolderRoot` setting), and an ordinary item dragged from the Desktop or the Public Desktop always moves in, even across drives; the hover label follows (Move to) | The user: new Big Folders should live in the workspace's `big folder` folder, and desktop items should simply move |
| 2026-10-22 | All | Every Large card is 344×364 DIP (Pixel 172×182) so it matches two stacked Mediums on the desktop grid; 2.2, 2.3 and 4 changed. Glass: Battery, World clock and Big Folder Large use the 20 new DIP (padding, row spacing, rows of 80, dial 60). Pixel: the Big Folder Large card wraps names onto two lines like the panel (pixel-font and Fusion sets; SimSun stays one line), new icon rows, Battery rows stretch to fill the list, World clock rows 41; saved Large cards re-snap on load | The user: "can the names wrap as in the panel" and "can the Big Folder be as tall as the two cards on its left plus the gap" |
| 2026-10-24 | All | Big Folder: a "Show more options" row ends the item menu and opens Windows' own context menu (it keeps the system's look in both themes; Pixel has no icon on it); search finds Chinese names by pinyin initials or syllables; 2.3, 4.1 and 4.2 changed | The user, 2026-10-24: "做第一档的三项+5" |
| 2026-10-22 | All | Motion, the rest (Phase 12, Build B): Pixel menus, submenus and dialogs GROW and SHRINK and their rows WIPE; the battery's gauge STEPs and its bolt POPs (Glass: the bolt fades); a size change morphs the Glass card's outline and DISSOLVEs the Pixel card's content in; a light/dark change blends Glass's tint and DISSOLVEs Pixel's card; a theme switch fades or dissolves the new cards in; a widget added or removed fades and scales (Glass) or GROWs and SHRINKs (Pixel); 3, 4.1 and 4.2 changed | The user: "add some motion, in both themes"; both previews approved 2026-10-22 |
| 2026-10-22 | All | Added motion (Phase 12), Big Folder first: the panel opens and closes (Glass fades and travels 8 DIP, Pixel GROWs and SHRINKs), items enter, leave and slide, a drag over the card fades or steps in, hover and press react, search and sort move the cells; 2.8, 3, 4.1, 4.2 and Appendix B changed; Pixel hover now lifts a cell's content 1 px | The user: "add some motion, in both themes"; both previews approved 2026-10-22 |
| 2026-10-27 | Glass | Text is solid: Primary `#1C1C1E` → `#000000` in light mode, Secondary `#3C3C43` 60% / `#EBEBF5` 60% → `#000000` / `#FFFFFF`; the old Secondary values become the new role Inactive (a disconnected device's icon, an unknown level's dash, a missing app's name); 4.1, 8 and Appendix B changed | The user, with a screenshot on a dark navy wallpaper: the world clock's times and the battery's device names were hard to read; "浅色下能不能把字体变成黑色，深色下用白色" |
| 2026-10-31 | All | Added the dock (Phase 15, build A): a bar of apps at the bottom edge of each screen. 2.2, 2.3, 2.7 and 2.8 changed; new 2.11 (above windows; showing and hiding); the section 3 motion row (Start and HOP); Appendix B changed | The user asked for a dock bar for programs: "现在我们来做一个新的widget。这个widget有点不太一样，算一个dock栏，里面只能放一些程序" |
| 2026-10-31 | Glass | Dock subsection in 4.1: geometry, the cards' glass with no outline or shadow, the name label, states, motion D1 to D15 and the Start 600 ms token | The user approved the Glass dock design on 2026-10-31 |
| 2026-10-31 | Pixel | Dock subsection in 4.2: art sizes 20, 24 and 32, bar geometry, the card's frame with a hard 2-px shadow (the dock is the one card with one), the name plate, the Fusion rule of Phase 8 extended to the dock's labels and empty text, stepped motion with a new HOP move | The user approved the Pixel dock design on 2026-10-31; the dock floats over windows, so it takes the menus' hard shadow |
| 2026-11-01 | All | Dock running programs (Phase 15.4): a running app shows its running icon and its opening and closing moments (D3 to D5), and a click brings its window forward or minimizes it. Level: a change to a shared rule for the click and for hiding and reveal (2.3, 2.7, 2.11, made in both themes in one change), and a redesign within each theme for the running look and its motion (4.1, 4.2, 8). The edge strip covers only the stretch under the dock | The user asked for running programs to show a running icon, with opening and closing animations: "运行中的程序额外设计一套图标，打开、关闭的时候都加上相应的动画"; the user approved the designs and decided "只在程序坞下方那一段" for the edge strip |
| 2026-11-03 | All | Dock groups, "Open all" and "Open with" (Phase 15.5): an app held over another for 500 ms and let go makes a group, with its own panel above the bar (Open all, Rename, Ungroup, Remove group); files that are not programs, dragged over the bar, light the apps registered for all their types and open with the app they are dropped on; a program dropped on a group joins it. 2.3 and 2.7 changed; 4.1 and 4.2 gain the group icon, the group panel and the Open with look, and D6, D8, D9 and D12 are no longer planned; section 8 adds no roles. Level: a change to a shared rule for the interaction (2.3, 2.7; made in both themes in one change) and a redesign within each theme (4.1, 4.2) | The user asked for a dock with groups, "Open all" and opening files with an app, approved the designs and said "开工吧" |
| 2026-11-10 | All | Dock follow-ups (Phase 16): a classic app saved with only an AppUserModelID gets its exe path from its Start entry, so it shows its real icon and its running look (Glass and Pixel; nothing visible changes for an app that already had a path); a Glass group panel's member menu opens at the member, as the Pixel one does; a member dragged out of a group's panel eases (Glass) or steps (Pixel) to the bar's icon size when it crosses onto the bar; a group of more than twelve members scrolls while a drag is held near the list's edge. 2.7, 4.1 D7 and 4.2 D7 changed. Level: a placement fix and motion details inside the approved dock designs; no shared rule changed | The user saw two blank tiles in the Pixel dock ("为什么dock里的反重力和bot图标是这样的") and the 15.5 follow-ups were queued |
