using DeskWidgets.Pixel;
using DeskWidgets.Widgets.Folder;

namespace DeskWidgets.Dock;

/// <summary>
/// Renders the Pixel dock with sample programs to PNG sheets (<c>DeskWidgets.exe --pixel-dock-preview &lt;folder&gt;</c>), light and dark,
/// English and Chinese, at factor 3: the three sizes with a hover, every state of section 4, the drop gap and a reorder, the empty dock,
/// and the frames of D1, D2, D7, D10, D11 and D13. It makes no window and reads no settings (the real icons of Notepad and Explorer
/// come from the Shell, read-only), so it works beside the running app. The pictures are <see cref="PixelDockPainter"/>'s, the window's own.
/// </summary>
internal static class PixelDockPreview
{
    private const int Factor = 3;
    private const int Margin = 8, Gap = 8, NoteHeight = 9;

    /// <summary>Room for a name plate at either end of a sample bar (the real window has 60 art pixels).</summary>
    private const int Overhang = 30;

    public static void Render(string folder)
    {
        Directory.CreateDirectory(folder);
        var language = Strings.Language;
        try
        {
            foreach (var current in new[] { AppLanguage.English, AppLanguage.Chinese })
            {
                Strings.Language = current;
                foreach (bool dark in new[] { false, true })
                {
                    string tag = $"{(current == AppLanguage.Chinese ? "zh" : "en")}-{(dark ? "night" : "day")}";
                    var palette = PixelPalette.For(dark);
                    var art = new PixelDockArt(() => { }, Fetch);
                    PixelPreview.SavePng(Path.Combine(folder, $"dock-1-sizes-{tag}.png"), Sizes(palette, art, dark), Factor);
                    PixelPreview.SavePng(Path.Combine(folder, $"dock-2-states-{tag}.png"), States(palette, art, dark), Factor);
                    PixelPreview.SavePng(Path.Combine(folder, $"dock-3-drops-{tag}.png"), Drops(palette, art, dark), Factor);
                    PixelPreview.SavePng(Path.Combine(folder, $"dock-4-empty-{tag}.png"), Empty(palette, art, dark), Factor);
                    PixelPreview.SavePng(Path.Combine(folder, $"dock-5-motion-{tag}.png"), Motion(palette, art, dark), Factor);
                }
            }
        }
        finally
        {
            Strings.Language = language;
        }
    }

    // ----- sample programs -----

    private static DockItem App(string name, string exe) => new() { Name = name, ExePath = exe };

    private static readonly string Notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
    private static readonly string Explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>The design's demo set (its group is a later build: Word stands in its place).</summary>
    private static List<DockItemView> Demo(bool zh) =>
    [
        .. new[]
        {
            App(zh ? "微信" : "WeChat", @"C:\Sample\Weixin.exe"),
            App("QQ", @"C:\Sample\QQ.exe"),
            App(zh ? "腾讯会议" : "Tencent Meeting", @"C:\Sample\WeMeetApp.exe"),
            App("Word", @"C:\Sample\WINWORD.EXE"),
            App("Chrome", @"C:\Sample\chrome.exe"),
            App("Edge", @"C:\Sample\msedge.exe"),
            new DockItem { Name = "Claude", Aumid = "Claude_pzs8sxrjxfjjc!Claude" },
            new DockItem { Name = "ChatGPT", Aumid = "OpenAI.Codex_2p2nqsd0c76g0!App" },
            App("Cursor", @"C:\Sample\Cursor.exe"),
            App("Kimi", @"C:\Sample\Kimi.exe"),
            App(zh ? "记事本" : "Notepad", Notepad),
            App(zh ? "文件资源管理器" : "File Explorer", Explorer),
        }.Select(item => new DockItemView(item, false)),
    ];

    /// <summary>The design QA's stand-in for the icon service: the Shell's 64-pixel icon at once, on this thread.</summary>
    private static Pixels? Fetch(DockItem item)
    {
        if (string.IsNullOrEmpty(item.ExePath) || !File.Exists(item.ExePath))
            return null;
        try
        {
            if (Shell.FromPath(item.ExePath) is not IShellItemImageFactory factory)
                return null;
            int hr = factory.GetImage(new SIZE { cx = 64, cy = 64 }, ShellNative.SIIGBF_BIGGERSIZEOK | ShellNative.SIIGBF_ICONONLY, out var bitmap);
            if (hr < 0 || bitmap == IntPtr.Zero)
                return null;
            try
            {
                return Bitmaps.Read(bitmap);
            }
            finally
            {
                ShellNative.DeleteObject(bitmap);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ----- one dock, drawn by the window's painter -----

    private static PixelDockPainter Painter(PixelPalette p, PixelDockArt art, IReadOnlyList<DockItemView> items, DockIconSize size, int overhang = Overhang)
    {
        var painter = new PixelDockPainter(art)
        {
            CanvasWidth = DockGeometry.PixelBarLengthArt(size, items.Count) + 2 * overhang,
            BarLeft = overhang,
        };
        painter.Change(items, size, new DockChange(DockChangeKind.Reset), p, animate: false, keepMotion: false);
        return painter;
    }

    private static PixelCanvas Paint(PixelDockPainter painter, PixelPalette p)
    {
        var canvas = new PixelCanvas(painter.CanvasWidth, painter.CanvasHeight);
        painter.Paint(canvas, p);
        return canvas;
    }

    private static void Hover(PixelDockPainter painter, int index)
    {
        painter.LabelIndex = index;
        painter.Label = painter.Items[index].Name;
        painter.StepLifts(painter.Items[index].Item.Key, snap: true);
    }

    private static PixelCanvas Dock(PixelPalette p, PixelDockArt art, IReadOnlyList<DockItemView> items, DockIconSize size, Action<PixelDockPainter>? setup = null, int overhang = Overhang)
    {
        var painter = Painter(p, art, items, size, overhang);
        setup?.Invoke(painter);
        return Paint(painter, p);
    }

    /// <summary>Plays a change's frames as the window would (frame 0 at once, then one per tick) and keeps each picture.</summary>
    private static List<PixelCanvas> Frames(PixelDockPainter painter, PixelPalette p, List<Action>? frames)
    {
        var shots = new List<PixelCanvas>();
        foreach (var frame in frames ?? [])
        {
            frame();
            shots.Add(Paint(painter, p));
        }
        return shots;
    }

    // ----- sheets -----

    private static PixelCanvas Sizes(PixelPalette p, PixelDockArt art, bool dark)
    {
        // As the design's sheet: WeChat, Chrome (hovered), ChatGPT and Explorer running.
        var demo = Demo(Strings.IsChinese).Select((v, i) => i is 0 or 4 or 7 or 11 ? v with { State = DockRunState.Running } : v).ToList();
        var rows = new List<(string, PixelCanvas)>();
        foreach (var size in Enum.GetValues<DockIconSize>())
        {
            int icon = DockGeometry.PixelIconArt(size);
            rows.Add(($"{size.ToString().ToUpperInvariant()}: ICONS {icon} ART PX, BAR {DockGeometry.PixelBarHeightArt(size)}. CHROME (HOVER)", Dock(p, art, demo, size, d => Hover(d, 4))));
        }
        return Compose(rows, dark);
    }

    private static PixelCanvas States(PixelPalette p, PixelDockArt art, bool dark)
    {
        var word = new List<DockItemView> { new(App("Word", @"C:\Sample\WINWORD.EXE"), false) };
        var missing = new List<DockItemView> { new(App(Strings.IsChinese ? "记事本" : "Notepad", Notepad), true) };
        var rows = new List<(string, PixelCanvas)>
        {
            ("IDLE", Dock(p, art, word, DockIconSize.Medium)),
            ("RUNNING: AWAKE ART + HALO", Dock(p, art, [word[0] with { State = DockRunState.Running }], DockIconSize.Medium)),
            ("RUNNING + HOVER", Dock(p, art, [word[0] with { State = DockRunState.Running }], DockIconSize.Medium, d => Hover(d, 0))),
            ("OPENING: HELD HALF-WAY (D3)", Dock(p, art, [word[0] with { State = DockRunState.Opening }], DockIconSize.Medium)),
            ("RUNNING, REAL ICONS: THEIR OWN HALO COLOUR", Dock(p, art, Demo(Strings.IsChinese).Skip(10).Select(v => v with { State = DockRunState.Running }).ToList(), DockIconSize.Medium)),
            ("HOVER: UP 2 PX, NAME ABOVE", Dock(p, art, word, DockIconSize.Medium, d => Hover(d, 0))),
            ("PRESSED: SHADE PLATE, 1 PX DOWN", Dock(p, art, word, DockIconSize.Medium, d => d.Pressed = 0)),
            ("CLICK: ONE SELECT FRAME", Dock(p, art, word, DockIconSize.Medium, d => d.Flash = 0)),
            ("MISSING: DIMMED (FAINT + TRACK)", Dock(p, art, missing, DockIconSize.Medium)),
            ("MISSING + HOVER: THE CONTROLLER'S LABEL", Dock(p, art, missing, DockIconSize.Medium, d =>
            {
                Hover(d, 0);
                d.Label = Strings.DockCantFind(missing[0].Name);
            }, overhang: 40)),
            ("A LONG NAME, MIDDLE-SHORTENED", Dock(p, art, [new(App(Strings.IsChinese ? "二零二六年第三季度财务报告查看器" : "Quarterly Financial Report Viewer 2026", @"C:\Sample\viewer.exe"), false)], DockIconSize.Medium, d => Hover(d, 0), overhang: 50)),
        };
        foreach (var size in Enum.GetValues<DockIconSize>())
        {
            // A Steam link with no icon file: no real icon ever comes, so the generic program tile stays (dimmed when the link is missing).
            var steam = new DockItemView(new DockItem { Name = "Steam game", Url = "steam://rungameid/1" }, false);
            var gone = new DockItemView(new DockItem { Name = "Old game", Url = "steam://rungameid/2" }, true);
            rows.Add(($"NO ICON: THE GENERIC TILE, {size.ToString().ToUpperInvariant()} (IDLE, MISSING)", Dock(p, art, [steam, gone], size)));
        }
        return Compose(rows, dark);
    }

    private static PixelCanvas Drops(PixelPalette p, PixelDockArt art, bool dark)
    {
        var demo = Demo(Strings.IsChinese);
        var rows = new List<(string, PixelCanvas)>
        {
            ("A PROGRAM DRAGGED OVER THE BAR: THE OTHERS MOVE ASIDE, A DOTTED GAP WHERE IT LANDS", Dock(p, art, demo, DockIconSize.Medium, d => d.SetGap(5, animate: false))),
            ("REORDER: THE DRAGGED APP RIDES 6 PX UP, ITS SLOT DOTTED", Dock(p, art, demo, DockIconSize.Medium, d =>
            {
                d.DragIndex = 1;
                d.DragCenter = DockGeometry.PixelIconLeftArt(DockIconSize.Medium, 2) + 4;
            })),
        };
        return Compose(rows, dark);
    }

    private static PixelCanvas Empty(PixelPalette p, PixelDockArt art, bool dark)
    {
        var rows = new List<(string, PixelCanvas)>();
        foreach (var size in Enum.GetValues<DockIconSize>())
            rows.Add(($"EMPTY, {size.ToString().ToUpperInvariant()}", Dock(p, art, [], size, overhang: 4)));
        rows.Add(("EMPTY, A PROGRAM DRAGGED OVER IT", Dock(p, art, [], DockIconSize.Medium, d => d.SetGap(0, animate: false), overhang: 4)));
        return Compose(rows, dark);
    }

    private static PixelCanvas Motion(PixelPalette p, PixelDockArt art, bool dark)
    {
        var demo = Demo(Strings.IsChinese).Take(7).ToList();
        const DockIconSize Size = DockIconSize.Medium;
        var rows = new List<(string, PixelCanvas)>();
        void Strip(string name, List<PixelCanvas> shots)
        {
            for (int i = 0; i < shots.Count; i++)
                rows.Add(($"{name} FRAME {i + 1}/{shots.Count}", shots[i]));
        }

        // D3, then D4: the hop, DISSOLVE frames 1 and 2, the hold (nothing ticks), then frames 3 and 4 when the window comes.
        {
            var d = Painter(p, art, demo, Size);
            string key = demo[3].Item.Key;
            var shots = new List<PixelCanvas>();
            bool more = d.SetRun(key, DockRunState.Opening, DockRunTransition.Opening, animate: true);
            shots.Add(Paint(d, p));
            while (more)
            {
                more = d.StepRuns();
                shots.Add(Paint(d, p));
            }
            more = d.SetRun(key, DockRunState.Running, DockRunTransition.OpenedHere, animate: true);
            shots.Add(Paint(d, p));
            while (more)
            {
                more = d.StepRuns();
                shots.Add(Paint(d, p));
            }
            Strip("D3 OPENING, D4 OPENED:", shots);
        }

        // D5: the last window went; and D3's end, no window in time (back the way it came). Two icons at once.
        {
            var d = Painter(p, art, demo.Select((v, i) => i == 4 ? v with { State = DockRunState.Running } : i == 5 ? v with { State = DockRunState.Opening } : v).ToList(), Size);
            var shots = new List<PixelCanvas>();
            bool more = d.SetRun(demo[4].Item.Key, DockRunState.Idle, DockRunTransition.Closed, animate: true);
            more |= d.SetRun(demo[5].Item.Key, DockRunState.Idle, DockRunTransition.TimedOut, animate: true);
            shots.Add(Paint(d, p));
            while (more)
            {
                more = d.StepRuns();
                shots.Add(Paint(d, p));
            }
            Strip("D5 CLOSED (CHROME) + TIMED OUT (EDGE):", shots);
        }

        // D4 for a program started elsewhere: all four DISSOLVE frames, no hop.
        {
            var d = Painter(p, art, demo, Size);
            var shots = new List<PixelCanvas>();
            bool more = d.SetRun(demo[1].Item.Key, DockRunState.Running, DockRunTransition.OpenedElsewhere, animate: true);
            shots.Add(Paint(d, p));
            while (more)
            {
                more = d.StepRuns();
                shots.Add(Paint(d, p));
            }
            Strip("D4 OPENED ELSEWHERE:", shots);
        }

        // D1: lift 1, then 2 with the plate; leaving: down 1 and the plate gone at once, then at rest.
        {
            var d = Painter(p, art, demo, Size);
            var shots = new List<PixelCanvas>();
            string key = demo[4].Item.Key;
            d.LabelIndex = 4;
            d.Label = demo[4].Name;
            d.StepLifts(key, snap: false);
            shots.Add(Paint(d, p));
            d.StepLifts(key, snap: false);
            shots.Add(Paint(d, p));
            d.LabelShown = false;
            d.LabelIndex = -1;
            d.StepLifts(null, snap: false);
            shots.Add(Paint(d, p));
            d.StepLifts(null, snap: false);
            shots.Add(Paint(d, p));
            Strip("D1 HOVER, THEN LEAVE:", shots);
        }

        // D7: the pointer passes the middle of the next slot; that icon MOVEs over.
        {
            var d = Painter(p, art, demo, Size);
            d.DragIndex = 1;
            d.DragCenter = DockGeometry.PixelIconLeftArt(Size, 1) + 12;
            var moved = demo.ToList();
            (moved[1], moved[2]) = (moved[2], moved[1]);
            var frames = d.Change(moved, Size, new DockChange(DockChangeKind.Moved, 1, 2), p, animate: true, keepMotion: true);
            d.DragIndex = 2;
            d.DragCenter = DockGeometry.PixelIconLeftArt(Size, 2) + 6;
            Strip("D7 REORDER:", Frames(d, p, frames));
        }

        // D10: the gap opens (MOVE), then the drop: the new list with the program in the gap, which POPs.
        {
            var d = Painter(p, art, demo, Size, overhang: Overhang + 16);
            Strip("D10 GAP OPENS:", Frames(d, p, d.SetGap(3, animate: true)));
            var added = demo.Take(3).Append(new DockItemView(App("Excel", @"C:\Sample\EXCEL.EXE"), false)).Concat(demo.Skip(3)).ToList();
            // The drop closes the gap and the controller places the longer bar, centred: half a slot further left on the same canvas.
            d.ClearGap();
            d.BarLeft -= d.Pitch / 2;
            Strip("D10 DROP, POP:", Frames(d, p, d.Change(added, Size, new DockChange(DockChangeKind.Added, 3), p, animate: true, keepMotion: false)));
        }

        // D10 without a gap (Add apps, taskbar apps): the icons after it MOVE aside as the bar grows, then the POP.
        {
            var d = Painter(p, art, demo, Size, overhang: Overhang + 16);
            var added = demo.Take(3).Append(new DockItemView(App("Excel", @"C:\Sample\EXCEL.EXE"), false)).Concat(demo.Skip(3)).ToList();
            d.BarLeft -= d.Pitch / 2;
            Strip("D10 ADD FROM A MENU:", Frames(d, p, d.Change(added, Size, new DockChange(DockChangeKind.Added, 3), p, animate: true, keepMotion: false)));
        }

        // D11: LEAVE, then the icons after it MOVE in as the bar shrinks.
        {
            var d = Painter(p, art, demo, Size);
            var removed = demo.Where((_, i) => i != 1).ToList();
            d.BarLeft += d.Pitch / 2;
            Strip("D11 REMOVE:", Frames(d, p, d.Change(removed, Size, new DockChange(DockChangeKind.Removed, 1), p, animate: true, keepMotion: false)));
        }

        // D13: the new size at once, the icons DISSOLVE in.
        {
            var d = Painter(p, art, demo, DockIconSize.Small);
            d.CanvasWidth = DockGeometry.PixelBarLengthArt(DockIconSize.Large, demo.Count) + 2 * Overhang;
            Strip("D13 SIZE CHANGE:", Frames(d, p, d.Change(demo, DockIconSize.Large, new DockChange(DockChangeKind.Resized), p, animate: true, keepMotion: false)));
        }
        return Compose(rows, dark);
    }

    // ----- layout of a sheet -----

    private static PixelCanvas Compose(List<(string Note, PixelCanvas Canvas)> rows, bool dark)
    {
        int width = rows.Max(r => Math.Max(r.Canvas.Width, PixelText.Layout(r.Note, PixelFonts.Small).Width)) + 2 * Margin;
        int height = rows.Sum(r => NoteHeight + r.Canvas.Height + Gap) - Gap + 2 * Margin;
        var sheet = new PixelCanvas(width, height);
        PixelPreview.PaintWallpaper(sheet, dark);
        uint note = dark ? 0xFF94B0C2 : 0xFF1A1C2C;
        int y = Margin;
        foreach (var (text, canvas) in rows)
        {
            PixelText.Layout(text, PixelFonts.Small).Draw(sheet, Margin, y, note);
            y += NoteHeight;
            Over(canvas, sheet, Margin, y);
            y += canvas.Height + Gap;
        }
        return sheet;
    }

    /// <summary>Puts a dock window's picture on the wallpaper as the screen would: its alpha pixels (the hard shadow) blended over what is behind.</summary>
    private static void Over(PixelCanvas source, PixelCanvas target, int left, int top)
    {
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                uint src = source.Get(x, y);
                uint a = src >> 24;
                if (a == 0)
                    continue;
                if (a == 255)
                {
                    target.Set(left + x, top + y, src);
                    continue;
                }
                uint dst = target.Get(left + x, top + y);
                uint Mix(int shift) => ((((src >> shift) & 0xFF) * a) + (((dst >> shift) & 0xFF) * (255 - a))) / 255;
                target.Set(left + x, top + y, 0xFF000000 | (Mix(16) << 16) | (Mix(8) << 8) | Mix(0));
            }
        }
    }
}
