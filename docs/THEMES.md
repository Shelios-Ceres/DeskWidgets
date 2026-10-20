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

- **Must**: each size has a fixed footprint: small 164×164, medium 344×164, large 344×344 (in DIP; medium and large = two small cards plus a 16 DIP gap). Desktop grid snapping and saved positions depend on these sizes, so changing theme must not disturb the user's desktop.
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

- With no devices, show the empty-state text (`Strings.NoDevices`).
- When "Show disconnected devices" is on, disconnected devices must be clearly dimmed, with "—" in place of the charge.
- The options in the right-click menu (show device names, show second hand, change cities, …) do the same thing in every theme.

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
- **Must**: fonts either cover both Chinese and English or have a fallback, and **one string of text must not mix two font sizes** (the Pixel approach: text the pixel fonts can't spell is set entirely in the 12 px SimSun bitmap font).
- **Must**: both languages must fit. English is usually wider and Chinese usually taller (in Pixel, Chinese lines are 12 px high while the pixel fonts are 5 px or 7 px). When something really doesn't fit, a more compact wording may be used (for example, Pixel writes the Chinese date as "10/13" instead of "10月13日"), and it goes into section 4.

### 2.6 Following the system

- **Must**: provide a light and a dark palette, and redraw immediately when the system switches. XAML content listens to `ActualThemeChanged`; `PixelSurface` redraws by itself.
- **Must**: stay sharp at 100%–200% scaling and across monitors with different DPI.
- **Should**: a theme that uses transparency should say what it looks like when Windows "Transparency effects" is off or "Energy saver" is on (Glass turns into a solid color, which is normal).

### 2.7 Interaction and legibility

- **Must**: the whole card can be dragged, so content must not intercept pointer events. No buttons, text boxes or scroll areas on the card; all settings go in the right-click menu.
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

- **Must**: fonts, icons and sprites must be original, come with the system, or be licensed for redistribution. Don't bundle Apple's SF fonts, and don't lift assets from game screenshots. The existing themes use only system fonts (Segoe UI Variable, Segoe Fluent Icons, SimSun) and hand-drawn pixel fonts and sprites.

---

## 3. Design tokens every theme must define

Tokens of one kind go in one place, not scattered across widgets.

| Category | What to define | Where Glass has it | Where Pixel has it |
|---|---|---|---|
| Card | Shape, corner radius, material, edge, highlight and shadow | `Hosting/WidgetFrame.xaml`, `Hosting/GlassBackdrop.cs` | `Pixel/PixelCard.cs` |
| Palette | A light and a dark set, named by the roles of section 8 | `Widgets/GlassPalette.cs` | `Pixel/PixelPalette.cs` |
| Fonts | Family, type scale, weights, fallback fonts | Segoe UI Variable Display / Text | `Pixel/PixelFonts.cs` (3×5, 5×7) + `Pixel/SystemGlyphs.cs` (SimSun 12 px) |
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

### 4.2 Pixel

Added 2026-10-13.

- **Grid**: 1 art pixel = 2 DIP. Card art sizes: small 82×82, medium 172×82, large 172×172.
- **Scaling**: the scale factor N = round(DPI scale × 2), so 100% is 2, 150% is 3 and 200% is 4. The bitmap is shown 1:1, and every art pixel must be a solid N×N block: no filtering, no anti-aliasing, no fractional coordinates. Big numbers are only scaled by whole numbers (×2, ×3).
- **Card**: an opaque panel. 1 px outline; stepped corners (from the top down, inset by 4, 2, 1, 1 pixels); a 1 px highlight at the top and a 2 px shadow at the bottom. Glass's blur layer is hidden in the Pixel theme. The pixel card's outline is part of the pixel-art style and doesn't conflict with the decision that Glass has no outline.
- **Palette**: based on the Sweetie 16 palette, with a light (day) and a dark (night) set. Widgets must use only the roles in `PixelPalette`.
- **Fonts**: two hand-drawn, all-caps pixel fonts.
  - 3×5 (`PixelFonts.Small`): labels, names, small numbers.
  - 5×7 (`PixelFonts.Regular`): values, titles; big numbers use it ×2 or ×3.
  - Lowercase letters are turned into capitals automatically and accents are dropped (São → SAO).
  - Text the pixel fonts can't spell is set entirely in the 12 px SimSun bitmap font (`PixelFonts.System`), so layouts must leave a 12 px line height for Chinese.
- **Icons**: hand-drawn sprites in `PixelSprites`; disconnected devices are dimmed; while charging, an outlined lightning bolt is laid over the battery bar.
- **Layout**: all coordinates are in art pixels; content stays at least 7 pixels from the card edge; dotted lines (`PixelCanvas.DottedLine`) are used for separation.
- **Motion**: no tweening; state changes swap frames directly; the second hand ticks once a second.
- **Recorded compressions**:
  - The small world clock uses three-letter city codes (NYC, LAX, STJ, PEK, …).
  - The Chinese date is written as "10/13". SimSun's "10月13日" is about 48 pixels wide and doesn't fit the 39-pixel column of the medium size.

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
  - `TypeId`: battery, clock, worldclock
  - Enum names: `WidgetTheme`, `WidgetSize`, `AppLanguage`
  - Option keys: showNames, showDisconnected, cities, showSeconds

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
