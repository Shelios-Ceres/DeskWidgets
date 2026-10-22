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

### 2.3 Minimum information

A theme may change how information is presented (rings ↔ battery bars, full city names ↔ three-letter codes), and may show a little more when there is room, but every size **must show at least** what this table lists:

| Widget | Small | Medium | Large |
|---|---|---|---|
| Battery | One device: icon, charge percentage and charging state, plus the name when "Show device names" is on<br>Several devices: 2×2, each cell is an icon + charge (graphic or number) + charging state | Up to 4 devices: icon, charge (graphic + number), charging state, name (can be turned off) | Up to 6 rows: icon, name, connection type and state, charge percentage |
| Clock | Weekday, time, date | Same as small | Not supported |
| World clock | 4 dials + a city label (full name or three-letter code) | 4 dials, each with city name, time and local date | One row per city: dial, city name, time difference from local time (including today/yesterday/tomorrow), time, local date |
| Big Folder | Header with the folder's name and item count; 2×2 cells | Same header; 4×2 cells | Same header; 4×4 cells |

- With no devices, show the empty-state text (`Strings.NoDevices`).
- When "Show disconnected devices" is on, disconnected devices must be clearly dimmed, with "—" in place of the charge.
- The options in the right-click menu (show device names, show second hand, change cities, …) do the same thing in every theme.
- Big Folder:
  - Each cell is the item's icon and a name: one line on Small and Medium, up to two on Large (the panel's breaks, `NameShortener.TwoLines`; a name that fits stays one line, top-aligned; Pixel's SimSun metric set stays one line on Large, as two lines would not fit four rows). When there are more items than cells, the last cell is the stack: four small icons of the next items and "+K", where K = items − (cells − 1). A folder with exactly 4, 8 or 16 items has no stack. Items follow the folder's sort order (Newest, Name, Type or Manual).
  - States: empty ("Drop files here"); folder not found ("Folder not found": the header can't be clicked and drops are refused); drop hover (a tint and a label saying what the drop will do: Move to, Copy to, Create shortcut in, or Add to for a mixed drop).
  - A click on an item opens it; a click on the header or the stack opens the panel. In the panel a click selects (Ctrl and Shift extend the selection), a double-click or Enter opens, typing anywhere searches, the first Esc clears the search and the second closes the panel, and the footer shows the path and "{n} selected". The selection can be dragged out. **The panel stays open while the user works in it** (a rename, Move to Recycle Bin, Put back, sort, search, selection, the item menu, the card's menu with "Rename folder…", Properties, a drop on the card, a drag out) **and through every window that work brings up**, ours or Explorer's: a dialog, a file operation's progress or conflict window, the UAC prompt. **It closes when the user leaves it:** Esc (the second one when there is search text), a click on the card's header or stack, a press on any other card, a click on the desktop or on another program's window, or switching to another program (Alt+Tab, the taskbar). **It also closes after opening something:** a double-click, Enter, Open, Run as administrator, Show in Explorer, Open file location. After a rename the renamed item is selected; after a removal the item after it is.
  - A name that doesn't fit is shortened by one shared function, `NameShortener`, measured in the theme's own font (2.9).
  - Where a new card's folder is made: in the **default location** if the user has chosen one (the card's menu, "Default location for new folders…", saved as the top-level `FolderRoot` setting; the folder is created if it is missing), else under the user profile on the Desktop's drive. "Change folder…" still points one card at any folder.

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
- **Must**: purely decorative layers set `IsHitTestVisible = false`.
- **Must** (opaque themes): contrast between body text and background ≥ 4.5:1, and ≥ 3:1 for big numbers. The faint colors used only for tick marks and separators (Pixel's `Faint`) must not be used for body text.
- Glass's background is live, so contrast can't be guaranteed; see "Known trade-off" in 4.1.

### 2.8 Performance

- **Must**: use almost no CPU when idle. Redraw only when data changes or the clock ticks (aligned to the whole second or minute); no loop that refreshes every frame; transition animations last at most 1 second.
- **Should**: reuse canvases, bitmaps and brushes instead of allocating large objects on every redraw.

### 2.9 Code layering

- **Must**: data and copy live in the shared layer (`DeviceStatus`, `WorldCities`, `Strings`, …); the theme layer only does the rendering. The same piece of information is formatted by the same shared function in every theme (for example `WorldCities.FormatTime` / `FormatDate` / `DescribeOffset`), and a theme may shorten it only when it doesn't fit, and only in the recorded way of 2.5.
- **Must**: each theme's rendering lives in its own files. Glass = the XAML and code in the widget classes; Pixel = the `Pixel/` infrastructure + `Widgets/<widget>/<widget>PixelScene.cs`. A new theme creates a `<ThemeName>/` folder and `<Widget><ThemeName>Scene.cs` in the same way.
- **Must**: widgets use only the color roles of their theme's palette (section 8), with no color values hard-coded in widgets.
- **Should**: a widget without its own rendering should still work (its XAML content is placed directly on the current theme's card). A theme that ships officially, though, should give every widget its own rendering.

### 2.10 Where assets come from

- **Must**: fonts, icons and sprites must be original, come with the system, or be licensed for redistribution. Don't bundle Apple's SF fonts, and don't lift assets from game screenshots. The existing themes use system fonts (Segoe UI Variable, Segoe Fluent Icons, SimSun), hand-drawn pixel fonts and sprites, and one bundled font: **Fusion Pixel Font 8 px** (Simplified Chinese, proportional, release 2026.10.16, by TakWolf, SIL Open Font License 1.1) for the Big Folder's Chinese. Its license text ships beside the exe (`OFL-FusionPixel.txt`), the packed table is built by `tools/Make-FusionFont.ps1`, and the font is only ever used as is. Emoji are not bundled: the system's Segoe UI Emoji is rendered at run time through Win2D (`Microsoft.Graphics.Win2D`, MIT) and shrunk onto the Pixel palette.

---

## 3. Design tokens every theme must define

Tokens of one kind go in one place, not scattered across widgets.

| Category | What to define | Where Glass has it | Where Pixel has it |
|---|---|---|---|
| Card | Shape, corner radius, material, edge, highlight and shadow | `Hosting/WidgetFrame.xaml`, `Hosting/GlassBackdrop.cs` | `Pixel/PixelCard.cs` |
| Palette | A light and a dark set, named by the roles of section 8 | `Widgets/GlassPalette.cs` | `Pixel/PixelPalette.cs` |
| Fonts | Family, type scale, weights, fallback fonts | Segoe UI Variable Display / Text | `Pixel/PixelFonts.cs` (3×5, 5×7) + `Pixel/SystemGlyphs.cs` (SimSun 12 px) + `Pixel/FusionGlyphs.cs` (Fusion 8 px, Big Folder only) |
| Icons | Device icons, charging indicator, empty-state icon | Segoe Fluent Icons (`Widgets/Battery/DeviceGlyphs.cs`) | `Pixel/PixelSprites.cs` |
| Motion | Which changes animate, duration, easing | Battery ring: 700 ms cubic ease-out; the second hand ticks once a second | No tweening; state changes swap frames directly; the second hand ticks once a second |

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

**Known trade-off**: the glass is very thin, so on a very dark wallpaper the contrast of light mode's dark text drops (and the other way round). The direction for improvement (not implemented yet): pick the text color from the brightness behind the card, instead of thickening the tint.

**Large cards** (344 × 364, 2026-10-22): the 20 new DIP go to rows and air only, with no new content and no font changes. Battery: padding 16 top and bottom, row spacing 8, so six 48 rows end at y 344. World clock: padding 14, four rows of 84, dial 60 (with numerals, like the Medium's; it had ticks at 56). Big Folder: rows 80, so four rows from y 34 end at 354.

**Big Folder** (added 2026-10-21; sizes in DIP):

- **Card:** the usual Glass card. Header from y 12 to 30: the folder name (Text 13 SemiBold, middle-shortened), the count (Text 12, Secondary) and a chevron. Cells: Small 2×2 (column 70, row 60, tile 40), Medium 4×2 (column 80, row 60, tile 40), Large 4×4 (column 80, row 80, tile 44 at row + 4). Names are Text 10 with a 13 line height (10.5 on Large): one line 68 wide on Small and 78 on Medium, two lines 78 wide on Large; the name box starts at row + 46 (row + 52 on Large). The stack's minis are 16 (17 on Large). (Phase 8, 2026-10-21: the names were Text 11 and 12, one line, 64 and 74 wide.)
- **Icons:** every item sits on a rounded tile (radius 0.225 × its size) with a faint sheen. The 14 file types and the 13 first-batch apps are our own drawings (a color and a motif, never a logo; path data in the design's `ICONS.md`, drawn through `SvgPath`). Any other app shows its real icon on a `Tile plate` at 0.68 of the tile, or edge to edge when the icon already fills its square. Images and videos show their real thumbnail clipped to the tile (a video adds a play mark). A cloud-only file never downloads: its type tile stays until a cached thumbnail exists.
- **States:** hover and pressed plates (`Hover`, `Pressed`) fade in over 100 ms and out over 150 ms; an app whose target is gone is at 40 % opacity with a Secondary name; empty shows a disc with a download glyph; folder not found shows a warning glyph, the path on every size and the hint "Right-click › Choose folder…" on Medium and Large; a drop tints the card (`Drop tint`) and shows a `Select` pill with the verb.
- **Panel:** a 440 × 500 glass window 12 DIP beside the card (right if it fits with 8 to spare, else left; moved up if it would run off the bottom). It closes by the rules in 2.3. Title and count, the sort button, the search field, five columns of 80 × 92 cells with two-line names (Text 11, line height 14, in a 76 × 28 box), and a footer with the path and "{n} selected". Selection is Finder-style: a `Pressed` plate under the tile and a `Select` pill under each name line; the keyboard focus is a 2 DIP `Select` ring. Renaming is a one-line text box in place of the name.
- **Menus:** `MenuEntry` rows through `MenuFlyout`, as for the other widgets.

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
- **Motion**: no tweening; state changes swap frames directly; the second hand ticks once a second.
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
  - **Motion:** none. Menus, submenus and dialogs appear and disappear in one frame.
  - **Keyboard and Narrator:** arrows, Home/End, Enter/Space, Esc and Left/Right work as in a native menu; the popups expose UI Automation roles (menu, menu items with Toggle, SelectionItem or ExpandCollapse patterns, dialog with buttons).
  - **Tray icon:** the Pixel theme has its own (a dark stepped tile with a hard-edged ring and a pixel mouse), drawn at the exact small-icon size for the display's scale and swapped live on a theme or scale change. The exe's own icon stays as it is.
- **Big Folder** (added 2026-10-21; all numbers in art pixels):
  - **Card:** the usual panel. The header has the folder name in `PixelFonts.Regular` (`Ink`) at x 7 and the count (`Soft`) right-aligned 7 from the edge. Cells are 36 wide: a 16×16 icon over a name in `PixelFonts.Small`: one line, or up to two on Large (the panel's breaks; each line keeps its font's pitch, 7, 10 or 13; the cell is 30 tall in the pixel-font set (16, 2, 5, 2, 5) and 36 in Fusion (16, 1, 9, 1, 9); the click rect is the cell plus 2 above and below; the SimSun set stays one line, as two lines need a 42 cell). Columns at x 4 and 42 (Small) or 5, 47, 89 and 131 (Medium, Large); rows at y 19 and 52 (Small, Medium) or 19, 61, 103 and 145 (Large; 19, 59, 99, 139 in the Fusion set and 22, 63, 105, 146 in the SimSun set), so the last name ends at y 175. "+K" is on line 1. The stack is four 7×7 minis (the icon's signature color inside a 1 px ink outline) and "+K".
  - **States:** hover `Open fill`, pressed `Shade` with the contents 1 px lower; the header takes the same plates. A missing target dims its icon (ink to `Faint`, every other color to `Track`) and its name (`Faint`). Empty: a dotted rectangle with a drop arrow and "Drop files here". Folder not found: a warning sprite and "Folder not found". Drop hover: every other panel-colored pixel turns `Select`, and a `Select` plate shows the verb.
  - **Icons:** 14 file-type sprites and 13 app sprites, 16×16, in the 16 fixed sprite colors of section 8. Any other app: its real icon shrunk to 14×14 ("tier 2": box filter, nearest Sweetie color by redmean distance, no dithering, a 1 px ink outline round the shape). Photos and videos: the center square shrunk to 12×12 with 4×4 Bayer dithering, inside a white print border and an ink outline with the corner pixels cut (a video adds a play mark). Until the real picture arrives, the type's sprite shows.
  - **Panel:** 220 × 196 art pixels in a `PixelPopupWindow` (corner stair [4, 2, 1, 1] and the hard shadow of the dialogs), 4 art pixels from the card (right if it fits, else left, else over it). One row with the folder name and "{n} items"; one row with the search field (well, magnifier, 1 px caret) and the sort button (label and chevron); then five columns of cells (pitch 41 across and 33 down, 39 in the Fusion set or 45 in the SimSun set) with two-line names in `PixelFonts.Small`, a 3 px scrollbar, a dotted rule, and the footer (the path in `Soft`, "{n} selected" in `Ink`). Selected cells are `Select` plates with `OnSelect` text; the keyboard focus is a dotted ring; the wheel scrolls a row per notch.
  - **Behavior:** the same as Glass (2.3). Typing anywhere searches (a Backspace removes a character); the fields take text through the system's text services, so the Pinyin IME (its candidate window opens beside the field), the emoji panel and Ctrl+V all work, and an emoji stays whole in the name; F2 renames in place, with a field in the cell or the title row; Delete sends the selection to the Recycle Bin; F4 or Alt+Down opens the sort list. Esc cancels a rename, then clears the search, then closes the panel. The panel closes by the rules in 2.3: it stays through its own menus, dialogs and file-operation windows, and closes when another program takes the foreground. The selection drags out through OLE, so Explorer and the desktop accept it as they accept Explorer's own drags.
  - **Keyboard and Narrator:** the panel is a window with the search field (Edit with the Value pattern), the sort button (ExpandCollapse) and one list item per shown item (Invoke and SelectionItem).
  - **Menus:** the item menu and the widget's own rows use the Pixel menu described above.

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
| Primary text (Primary / Ink) | Names, values | `#1C1C1E` / `#FFFFFF` | `#1A1C2C` / `#F4F4F4` |
| Secondary text (Secondary / Soft) | State, time, date, time difference | `#3C3C43` 60% / `#EBEBF5` 60% | `#566C86` / `#94B0C2` |
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
