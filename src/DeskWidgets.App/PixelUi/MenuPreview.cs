using DeskWidgets.Pixel;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.WorldClock;

namespace DeskWidgets.PixelUi;

/// <summary>
/// Design aid, like <see cref="PixelPreview"/>: draws every Pixel menu, dialog and the tray icon with the app's own
/// painters, light and dark, English and Chinese, to PNG sheets (<c>--pixel-menu-preview &lt;folder&gt;</c>).
/// </summary>
internal static class MenuPreview
{
    public static void Render(string folder)
    {
        Directory.CreateDirectory(folder);
        string pct100 = Path.Combine(folder, "100pct");
        Directory.CreateDirectory(pct100);
        var language = Strings.Language;
        try
        {
            foreach (bool zh in new[] { false, true })
            {
                Strings.Language = zh ? AppLanguage.Chinese : AppLanguage.English;
                foreach (bool dark in new[] { false, true })
                {
                    var p = PixelPalette.For(dark);
                    string suffix = $"{(zh ? "zh" : "en")}-{(dark ? "dark" : "light")}";
                    Save(folder, $"1-widget-menu-{suffix}", WidgetMenus(p), 3);
                    Save(folder, $"2-tray-menu-{suffix}", TrayMenus(p), 3);
                    Save(folder, $"3-states-{suffix}", States(p, dark), 3);
                    Save(folder, $"4-dialogs-{suffix}", Dialogs(p, dark), 3);
                    Save(folder, $"5-city-list-scroll-{suffix}", CityScroll(p), 3);
                    Save(pct100, $"1-widget-menu-{suffix}-100pct", WidgetMenus(p), 2);
                    Save(pct100, $"4-dialogs-{suffix}-100pct", Dialogs(p, dark), 2);
                }
            }
            Save(folder, "6-tray-icon", TrayIcons(), 1);
        }
        finally
        {
            Strings.Language = language;
        }
    }

    private static void Save(string folder, string name, PixelCanvas canvas, int factor) => PixelPreview.SavePng(Path.Combine(folder, name + ".png"), canvas, factor);

    // --- a sheet: wallpaper with panels and notes on it ----------------------------------------------------------

    private sealed class Sheet
    {
        private int _right, _bottom;

        public Sheet(PixelPalette p, int width, int height)
        {
            Palette = p;
            Canvas = new PixelCanvas(width, height);
            PixelPreview.PaintWallpaper(Canvas, p.Panel == PixelPalette.For(true).Panel);
        }

        public PixelPalette Palette { get; }

        public PixelCanvas Canvas { get; }

        /// <summary>Puts a drawn panel on the sheet with its hard shadow, as a popup window shows it.</summary>
        public void Put(PixelCanvas panel, int x, int y)
        {
            var withShadow = PixelFrame.WithShadow(panel, Palette.Shadow, MenuLayout.ShadowOffset);
            for (int sy = 0; sy < withShadow.Height; sy++)
            {
                for (int sx = 0; sx < withShadow.Width; sx++)
                {
                    uint src = withShadow.Get(sx, sy);
                    if (src == 0)
                        continue;
                    uint a = src >> 24;
                    uint dst = Canvas.Get(x + sx, y + sy);
                    if (a == 255 || dst == 0)
                    {
                        Canvas.Set(x + sx, y + sy, src);
                        continue;
                    }
                    uint Mix(int shift) => ((((src >> shift) & 0xFF) * a) + (((dst >> shift) & 0xFF) * (255 - a))) / 255;
                    Canvas.Set(x + sx, y + sy, 0xFF000000 | (Mix(16) << 16) | (Mix(8) << 8) | Mix(0));
                }
            }
            Grow(x + withShadow.Width, y + withShadow.Height);
        }

        public void Note(string text, int x, int y)
        {
            var t = PixelText.Layout(text, PixelFonts.Small);
            t.Draw(Canvas, x, y, Palette.Panel == PixelPalette.For(true).Panel ? 0xFF94B0C2 : 0xFF1A1C2C);
            Grow(x + t.Width, y + t.Height);
        }

        public PixelCanvas Cropped(int margin = 8)
        {
            int w = Math.Min(Canvas.Width, _right + margin), h = Math.Min(Canvas.Height, _bottom + margin);
            var c = new PixelCanvas(w, h);
            for (int y = 0; y < h; y++)
                Array.Copy(Canvas.Pixels, y * Canvas.Width, c.Pixels, y * w, w);
            return c;
        }

        private void Grow(int right, int bottom)
        {
            _right = Math.Max(_right, right);
            _bottom = Math.Max(_bottom, bottom);
        }
    }

    /// <summary>A menu drawn for a sheet: its entries, the drawing and where it sits.</summary>
    private sealed record Shown(MenuLayout Layout, MenuDrawing Drawing);

    private static Shown Menu(PixelPalette p, IReadOnlyList<MenuEntry> entries, Func<int, RowLook?>? looks = null)
    {
        var layout = MenuLayout.Create(entries);
        return new Shown(layout, MenuPainter.Draw(p, layout, new MenuPaint(layout.ContentHeight, false, 0, -1, -1, false, looks ?? (_ => (RowLook?)RowLook.Normal))));
    }

    private static Func<int, RowLook?> Look(params (int Row, RowLook Look)[] rows)
        => i => rows.Where(r => r.Row == i).Select(r => (RowLook?)r.Look).FirstOrDefault() ?? RowLook.Normal;

    private static (int X, int Y) Beside((int X, int Y) at, Shown parent, int row)
        => (at.X + parent.Layout.Width - 1, at.Y + parent.Drawing.Rows.First(r => r.Index == row).Y - MenuLayout.Top);

    private static readonly Action None = () => { };

    private static MenuEntry I(string text, PixelSprite? icon = null) => MenuEntry.Item(text, None, icon: icon);
    private static MenuEntry Sub(string text, IReadOnlyList<MenuEntry> children, PixelSprite? icon = null) => MenuEntry.Sub(text, children, icon: icon);
    private static MenuEntry Sub(string text, PixelSprite? icon = null) => MenuEntry.Sub(text, [I("x")], icon: icon);
    private static MenuEntry Toggle(string text, bool on) => MenuEntry.Toggle(text, on, None);
    private static MenuEntry Radio(string text, bool on) => MenuEntry.Radio(text, on, None);

    private static List<MenuEntry> WidgetTail() =>
    [
        MenuEntry.Separator(),
        Toggle(Strings.AlwaysOnTop, false),
        Toggle(Strings.LockPosition, false),
        MenuEntry.Separator(),
        Sub(Strings.ThemeMenu, PixelIcons.Theme),
        Sub(Strings.AddWidget, PixelIcons.Add),
        I(Strings.RemoveWidget, PixelIcons.Remove),
        MenuEntry.Separator(),
        I(Strings.Exit),
    ];

    // --- the sheets ----------------------------------------------------------------------------------------------

    private static PixelCanvas WidgetMenus(PixelPalette p)
    {
        var s = new Sheet(p, 1200, 520);
        s.Note("WORLD CLOCK: CHANGE CITIES > CITY 1 OPEN, POINTER ON TOKYO", 8, 6);
        var cities = WorldCities.Catalog.Select(c => c.Name).ToArray();
        var root = Menu(p, [Sub(Strings.Size, PixelIcons.Size), Sub(Strings.ChangeCities, PixelIcons.Globe), Toggle(Strings.ShowSecondHand, true), .. WidgetTail()], Look((1, RowLook.Open)));
        var slots = Menu(p,
        [
            Sub(Strings.CitySlot(1, "New York")),
            Sub(Strings.CitySlot(2, "Los Angeles")),
            Sub(Strings.CitySlot(3, "St. John's")),
            Sub(Strings.CitySlot(4, "Beijing")),
        ], Look((0, RowLook.Open)));
        var list = Menu(p, [.. cities.Select(n => Radio(n, n == "New York"))], Look((Array.IndexOf(cities, "Tokyo"), RowLook.Hover)));
        (int X, int Y) a = (8, 16);
        s.Put(root.Drawing.Panel, a.X, a.Y);
        var b = Beside(a, root, 1);
        s.Put(slots.Drawing.Panel, b.X, b.Y);
        var c = Beside(b, slots, 0);
        s.Put(list.Drawing.Panel, c.X, c.Y);

        int x2 = c.X + list.Layout.Width + 24;
        s.Note("BATTERIES: SIZE OPEN, POINTER ON LARGE", x2, 6);
        var batteries = Menu(p, [Sub(Strings.Size, PixelIcons.Size), Toggle(Strings.ShowDeviceNames, true), Toggle(Strings.ShowDisconnected, false), I(Strings.RefreshDevices, PixelIcons.Refresh), .. WidgetTail()], Look((0, RowLook.Open)));
        var sizes = Menu(p, [Radio(Strings.SizeSmall, false), Radio(Strings.SizeMedium, true), Radio(Strings.SizeLarge, false)], Look((2, RowLook.Hover)));
        (int X, int Y) d = (x2, 16);
        s.Put(batteries.Drawing.Panel, d.X, d.Y);
        var e = Beside(d, batteries, 0);
        s.Put(sizes.Drawing.Panel, e.X, e.Y);

        int y3 = d.Y + batteries.Drawing.Panel.Height + 16;
        s.Note("ADD WIDGET OPEN (THE ONLY SUBMENU WITH ICONS)", x2, y3);
        var batteries2 = Menu(p, [Sub(Strings.Size, PixelIcons.Size), Toggle(Strings.ShowDeviceNames, true), Toggle(Strings.ShowDisconnected, false), I(Strings.RefreshDevices, PixelIcons.Refresh), .. WidgetTail()], Look((9, RowLook.Open)));
        var add = Menu(p, [I(Strings.BatteryWidget, PixelIcons.Battery), I(Strings.ClockWidget, PixelIcons.Clock), I(Strings.WorldClockWidget, PixelIcons.Globe)], Look((0, RowLook.Hover)));
        (int X, int Y) f = (x2, y3 + 10);
        s.Put(batteries2.Drawing.Panel, f.X, f.Y);
        var g = Beside(f, batteries2, 9);
        s.Put(add.Drawing.Panel, g.X, g.Y);
        return s.Cropped();
    }

    private static PixelCanvas TrayMenus(PixelPalette p)
    {
        var s = new Sheet(p, 1000, 400);
        List<MenuEntry> Tray() =>
        [
            Sub(Strings.AddWidget, PixelIcons.Add),
            I(Strings.RefreshDevices, PixelIcons.Refresh),
            MenuEntry.Separator(),
            Toggle(Strings.SnapToDesktopGrid, true),
            Sub(Strings.IconsMenu),
            Toggle(Strings.StartWithWindows, true),
            Sub(Strings.ThemeMenu, PixelIcons.Theme),
            Sub(Strings.LanguageMenu),
            I(Strings.OpenLogs),
            MenuEntry.Separator(),
            I(Strings.Exit),
        ];

        s.Note("TRAY MENU: DESKTOP ICONS OPEN, POINTER ON ARRANGE NOW", 8, 6);
        var root = Menu(p, Tray(), Look((4, RowLook.Open)));
        var icons = Menu(p, [MenuEntry.Status(Strings.IconsStatusArranging(5)), MenuEntry.Separator(), Toggle(Strings.IconsArrangeRight, true), I(Strings.IconsArrangeNow)], Look((3, RowLook.Hover)));
        (int X, int Y) a = (8, 16);
        s.Put(root.Drawing.Panel, a.X, a.Y);
        var b = Beside(a, root, 4);
        s.Put(icons.Drawing.Panel, b.X, b.Y);

        int x2 = b.X + icons.Layout.Width + 24;
        s.Note("THEME OPEN", x2, 6);
        var root2 = Menu(p, Tray(), Look((6, RowLook.Open)));
        var theme = Menu(p, [Radio(Strings.ThemeName(WidgetTheme.Glass), false), Radio(Strings.ThemeName(WidgetTheme.Pixel), true)], Look((0, RowLook.Hover)));
        (int X, int Y) c = (x2, 16);
        s.Put(root2.Drawing.Panel, c.X, c.Y);
        var d = Beside(c, root2, 6);
        s.Put(theme.Drawing.Panel, d.X, d.Y);

        int x3 = d.X + theme.Layout.Width + 24;
        s.Note("LANGUAGE OPEN", x3, 6);
        var root3 = Menu(p, Tray(), Look((7, RowLook.Open)));
        var language = Menu(p, [.. Enum.GetValues<AppLanguage>().Select(l => Radio(Strings.LanguageName(l), l == Strings.Language))]);
        (int X, int Y) e = (x3, 16);
        s.Put(root3.Drawing.Panel, e.X, e.Y);
        var f = Beside(e, root3, 7);
        s.Put(language.Drawing.Panel, f.X, f.Y);
        return s.Cropped();
    }

    private static PixelCanvas States(PixelPalette p, bool dark)
    {
        var s = new Sheet(p, 900, 400);
        s.Note("MENU ROW STATES", 8, 6);
        var items = new List<(MenuEntry Item, RowLook Look, string Note)>
        {
            (I(Strings.RefreshDevices, PixelIcons.Refresh), RowLook.Normal, "NORMAL: INK LABEL, SOFT ICON"),
            (I(Strings.RemoveWidget, PixelIcons.Remove), RowLook.Hover, "HOVER = KEYBOARD FOCUS: SELECT BAR"),
            (I(Strings.AddWidget, PixelIcons.Add), RowLook.Pressed, "PRESSED: SELECT PRESSED BAR"),
            (Sub(Strings.ThemeMenu, PixelIcons.Theme), RowLook.Open, "SUBMENU OPEN, FOCUS IN IT: OPEN FILL"),
            (Sub(Strings.Size, PixelIcons.Size), RowLook.Hover, "SUBMENU ROW, POINTER ON IT"),
            (new MenuEntry { Text = Strings.IconsArrangeNow, Kind = MenuKind.Item, Enabled = false }, RowLook.Normal, "DISABLED: FAINT, NEVER HIGHLIGHTED"),
            (MenuEntry.Separator(), RowLook.Normal, "SEPARATOR: DOTTED FAINT"),
            (Toggle(Strings.AlwaysOnTop, true), RowLook.Normal, "TOGGLE ON: CHECK"),
            (Toggle(Strings.LockPosition, false), RowLook.Normal, "TOGGLE OFF: NO MARK"),
            (Radio(Strings.ThemeName(WidgetTheme.Pixel), true), RowLook.Normal, "RADIO ON: DIAMOND"),
            (Radio(Strings.ThemeName(WidgetTheme.Glass), false), RowLook.Normal, "RADIO OFF: NO MARK"),
            (Toggle(Strings.ShowDeviceNames, true), RowLook.Hover, "TOGGLE ON + HOVER"),
            (MenuEntry.Status(Strings.IconsStatusArranging(5)), RowLook.Normal, "STATUS LINE: SOFT, NOT FOCUSABLE"),
        };
        var panel = Menu(p, [.. items.Select(i => i.Item)], i => items[i].Look);
        s.Put(panel.Drawing.Panel, 8, 16);
        for (int i = 0; i < items.Count; i++)
        {
            var row = panel.Drawing.Rows.First(r => r.Index == i);
            s.Note(items[i].Note, 8 + panel.Layout.Width + 8, 16 + row.Y + (row.Height - 5) / 2);
        }

        int y = 16 + panel.Drawing.Panel.Height + 16;
        s.Note("DIALOG BUTTON STATES", 8, y);
        y += 12;
        int x = 10;
        var states = new (string Note, bool Primary, ButtonLook Look, bool Focus)[]
        {
            ("NORMAL", false, ButtonLook.Normal, false),
            ("HOVER", false, ButtonLook.Hover, false),
            ("PRESSED", false, ButtonLook.Pressed, false),
            ("FOCUS", false, ButtonLook.Normal, true),
            ("DISABLED", false, ButtonLook.Disabled, false),
            ("DEFAULT", true, ButtonLook.Normal, false),
            ("DEF HOVER", true, ButtonLook.Hover, false),
            ("DEF PRESSED", true, ButtonLook.Pressed, false),
            ("DEF FOCUS", true, ButtonLook.Normal, true),
        };
        var strip = new PixelCanvas(states.Length * 50 + 10, 40);
        PixelFrame.Draw(strip, 0, 0, strip.Width, strip.Height, PixelFrame.DialogCorner, p.Outline, p.Panel, p.Highlight, p.Shade, 2);
        foreach (var st in states)
        {
            var label = PixelText.Layout(Strings.Yes, PixelFonts.Regular);
            var (bw, bh) = DialogLayout.ButtonSize(label);
            DialogPainter.Button(strip, p, x, 8, bw, bh, label, st.Primary, st.Look, st.Focus);
            x += 50;
        }
        s.Put(strip, 8, y);
        x = 10;
        foreach (var st in states)
        {
            s.Note(st.Note, 8 + x, y + strip.Height + 4);
            x += 50;
        }
        return s.Cropped();
    }

    private static PixelCanvas Dialogs(PixelPalette p, bool dark)
    {
        var s = new Sheet(p, 800, 300);
        DialogButtonSpec[] exitButtons = [new(Strings.Yes, false), new(Strings.No, true)];
        PixelCanvas Exit(int hot, int focus = 1)
        {
            var layout = DialogLayout.Create(Strings.AppName, Strings.ExitQuestion, Strings.ExitDetail, exitButtons);
            return DialogPainter.Draw(p, layout, new DialogPaint(DialogBadge.Question, true, exitButtons, hot, -1, focus), dark);
        }

        s.Note("EXIT, AS IT OPENS: NO HAS FOCUS", 8, 6);
        var exit = Exit(-1);
        s.Put(exit, 8, 16);
        int x2 = 8 + exit.Width + 20;
        s.Note("EXIT: POINTER ON YES", x2, 6);
        var exit2 = Exit(0);
        s.Put(exit2, x2, 16);
        int x3 = x2 + exit2.Width + 20;
        s.Note("EXIT: FOCUS MOVED TO YES", x3, 6);
        s.Put(Exit(-1, 0), x3, 16);
        x3 += exit2.Width + 20;
        s.Note("START WITH WINDOWS FAILED", x3, 6);
        DialogButtonSpec[] ok = [new(Strings.Ok, true)];
        var errorLayout = DialogLayout.Create(Strings.AppName, null, Strings.StartupFailed + (Strings.IsChinese ? "拒绝访问。" : "Access is denied."), ok);
        s.Put(DialogPainter.Draw(p, errorLayout, new DialogPaint(DialogBadge.Warning, false, ok, -1, -1, 0), dark), x3, 16);
        return s.Cropped();
    }

    private static PixelCanvas CityScroll(PixelPalette p)
    {
        var s = new Sheet(p, 600, 400);
        s.Note("SHORT SCREEN: THE LIST SCROLLS (WHEEL, ARROW ROWS, KEYS)", 8, 6);
        var cities = WorldCities.Catalog.Select(c => c.Name).ToArray();
        var entries = cities.Select(n => Radio(n, n == "New York")).ToArray();
        var layout = MenuLayout.Create(entries);
        // A screen with room for 11 rows.
        var (height, scrollable) = layout.Plan(MenuLayout.Top + MenuLayout.Bottom + 2 * MenuLayout.ScrollHeight + 11 * layout.RowHeight);
        int[] firsts = [0, 9, layout.MaxFirstRow(height)];
        int x = 8;
        foreach (int first in firsts)
        {
            var (_, last) = layout.Visible(first, height, scrollable);
            int hover = first == 0 ? 2 : Math.Min(first + 3, last);
            var drawing = MenuPainter.Draw(p, layout, new MenuPaint(height, scrollable, first, hover, -1, false));
            s.Put(drawing.Panel, x, 16);
            s.Note(first == 0 ? "TOP" : first == firsts[1] ? "MIDDLE" : "END", x, 16 + drawing.Panel.Height + 6);
            x += layout.Width + 16;
        }
        return s.Cropped();
    }

    private static PixelCanvas TrayIcons()
    {
        int[] sizes = [16, 20, 24, 32];
        var sheet = new PixelCanvas(40 + sizes.Sum(z => z * 8 + 24), 32 * 8 + 70);
        sheet.Fill(0, 0, sheet.Width, sheet.Height, 0xFF20242E);
        int x = 20;
        foreach (int size in sizes)
        {
            var icon = PixelTrayIcon.Draw(size);
            Blit(icon, sheet, x, 20, 1);
            sheet.Fill(x + size + 8, 14, size + 12, size + 12, 0xFFEFEFEF);
            Blit(icon, sheet, x + size + 14, 20, 1);
            Blit(icon, sheet, x, 70, 8);
            x += size * 8 + 24;
        }
        return sheet;
    }

    private static void Blit(PixelCanvas src, PixelCanvas dst, int x, int y, int zoom)
    {
        for (int sy = 0; sy < src.Height; sy++)
        {
            for (int sx = 0; sx < src.Width; sx++)
            {
                uint color = src.Get(sx, sy);
                if (color != 0)
                    dst.Fill(x + sx * zoom, y + sy * zoom, zoom, zoom, color);
            }
        }
    }
}
