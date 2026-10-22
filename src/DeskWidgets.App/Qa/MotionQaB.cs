using System.Diagnostics;
using DeskWidgets.Hosting;
using DeskWidgets.Interop;
using DeskWidgets.Widgets;
using Microsoft.UI.Xaml;

namespace DeskWidgets.Qa;

/// <summary>The Phase 12 Build B moments (B5 to B8): what a widget does when it is added or removed, resized, or re-themed.</summary>
internal static partial class MotionQa
{
    /// <summary>Records <paramref name="act"/> and what follows it in a screen rectangle, and writes a line and a contact sheet.</summary>
    private static async Task RecordedRect(string title, string sheet, int x, int y, int width, int height, Action act, int afterMs = 900, int sheetFrames = 12, int shrink = 2, int columns = 4)
    {
        var recorder = Rec(x, y, width, height);
        recorder.Start();
        await Task.Delay(500);
        double t0 = Clock.Elapsed.TotalMilliseconds;
        var watch = Stopwatch.StartNew();
        act();
        double call = watch.Elapsed.TotalMilliseconds;
        await Task.Delay(afterMs);
        recorder.Stop();
        Line($"- {title}: the call took {call:0.0} ms; {recorder.Describe(t0)}");
        recorder.SaveSheet(Path.Combine(s_out, sheet + ".png"), sheetFrames, shrink, columns, fromMs: t0 - 1);
    }

    /// <summary>The region around a card of a given kind at a place, with room for the motion.</summary>
    private static (int X, int Y, int Width, int Height) AroundCard(WidgetHost host, IWidgetFactory factory, WidgetSize size, int x, int y)
    {
        double scale = Win32.GetScaleAt(new Win32.POINT { X = x + 1, Y = y + 1 });
        var card = WidgetLayout.GetCardPixels(size, host.Theme, scale);
        return (x - 16, y - 16, card.Width + 32, card.Height + 32);
    }

    // ----- B5: Pixel menus, submenus and dialogs -----

    private static async Task MenusAndDialogs(WidgetHost host)
    {
        host.SetTheme(WidgetTheme.Pixel);
        await Task.Delay(1500);
        Line("## B5 Pixel menus, submenus and dialogs");
        var window = host.Windows.First(w => w.TypeId == "battery");
        var work = Win32.GetWorkArea(default);
        int x = work.Left + 700, y = work.Top + 120;
        PixelUi.PixelMenu? menu = null;
        await RecordedRect("the menu opens", "b5-menu-open", x - 16, y - 16, 1100, 820, () => menu = PixelUi.PixelMenu.Show(window.BuildMenu(), x, y), afterMs: 500, sheetFrames: 6, shrink: 3, columns: 3);
        await RecordedRect("a row becomes current (Down, Down)", "b5-menu-wipe", x - 16, y - 16, 1100, 820, () => { menu!.OnKey(PixelUi.Native.VK_DOWN); menu.OnKey(PixelUi.Native.VK_DOWN); }, afterMs: 400, sheetFrames: 6, shrink: 3, columns: 3);
        // The Size row is first; its submenu opens with Right.
        await RecordedRect("the submenu opens (Home, Right)", "b5-submenu-open", x - 16, y - 16, 1100, 820, () => { menu!.OnKey(PixelUi.Native.VK_HOME); menu.OnKey(PixelUi.Native.VK_RIGHT); }, afterMs: 500, sheetFrames: 6, shrink: 3, columns: 3);
        await RecordedRect("the submenu closes (Left)", "b5-submenu-close", x - 16, y - 16, 1100, 820, () => menu!.OnKey(PixelUi.Native.VK_LEFT), afterMs: 500, sheetFrames: 6, shrink: 3, columns: 3);
        await RecordedRect("the menu closes (Esc)", "b5-menu-close", x - 16, y - 16, 1100, 820, () => menu!.OnKey(PixelUi.Native.VK_ESCAPE), afterMs: 500, sheetFrames: 6, shrink: 3, columns: 3);
        await Task.Delay(300);

        int cx = work.Left + work.Width / 2, cy = work.Top + work.Height / 2;
        PixelUi.PixelDialog? dialog = null;
        await RecordedRect("a dialog opens", "b5-dialog-open", cx - 520, cy - 200, 1040, 400, () => dialog = PixelUi.PixelDialog.AskExit(0, _ => { }), afterMs: 500, sheetFrames: 6, shrink: 2, columns: 3);
        await RecordedRect("the dialog answers (Esc)", "b5-dialog-close", cx - 520, cy - 200, 1040, 400, () => dialog!.OnKey(PixelUi.Native.VK_ESCAPE), afterMs: 500, sheetFrames: 6, shrink: 2, columns: 3);
        await Task.Delay(300);

        await Cycles("menu, submenu and dialog", 12, async () =>
        {
            var m = PixelUi.PixelMenu.Show(window.BuildMenu(), x, y);
            await Task.Delay(300);
            m.OnKey(PixelUi.Native.VK_DOWN);
            await Task.Delay(150);
            m.OnKey(PixelUi.Native.VK_RIGHT);
            await Task.Delay(300);
            m.OnKey(PixelUi.Native.VK_ESCAPE);
            await Task.Delay(100);
            m.OnKey(PixelUi.Native.VK_ESCAPE);
            await Task.Delay(300);
            var d = PixelUi.PixelDialog.AskExit(0, _ => { });
            await Task.Delay(300);
            d.OnKey(PixelUi.Native.VK_ESCAPE);
            await Task.Delay(300);
        });
    }

    // ----- B6: the battery's level and charging -----

    private static async Task BatteryChanges(WidgetHost host, WidgetTheme theme)
    {
        host.SetTheme(theme);
        await Task.Delay(1500);
        Line($"## B6 the battery's level and charging, {theme}");
        var window = host.Windows.First(w => w.TypeId == "battery");
        var card = window.CardRect;
        double scale = Win32.GetScaleAt(new Win32.POINT { X = card.Left + 1, Y = card.Top + 1 });
        var size = WidgetLayout.GetCardPixels(window.Placement.Size, theme, scale);
        void Change(Func<Core.Devices.DeviceStatus, Core.Devices.DeviceStatus> tweak)
        {
            Core.Devices.DemoDeviceSource.Tweak = tweak;
            host.Services.Devices.RefreshAll();
        }
        try
        {
            await RecordedRect("the mouse goes from 76 to 40", $"b6-{theme}-level", card.Left - 16, card.Top - 16, size.Width + 32, size.Height + 32,
                () => Change(d => d.Id == "demo:mouse" ? d with { BatteryPercent = 40 } : d), afterMs: 900, sheetFrames: 12, shrink: 2, columns: 4);
            await Task.Delay(400);
            await RecordedRect("the mouse goes from 40 to 42", $"b6-{theme}-small", card.Left - 16, card.Top - 16, size.Width + 32, size.Height + 32,
                () => Change(d => d.Id == "demo:mouse" ? d with { BatteryPercent = 42 } : d), afterMs: 700, sheetFrames: 8, shrink: 2, columns: 4);
            await Task.Delay(400);
            await RecordedRect("the headphones start charging", $"b6-{theme}-charge", card.Left - 16, card.Top - 16, size.Width + 32, size.Height + 32,
                () => Change(d => d.Id == "demo:mouse" ? d with { BatteryPercent = 42 } : d.Id == "demo:headphones" ? d with { IsCharging = true } : d), afterMs: 700, sheetFrames: 8, shrink: 2, columns: 4);
            await Task.Delay(400);
            await RecordedRect("charging stops (the keyboard and the headphones)", $"b6-{theme}-stop", card.Left - 16, card.Top - 16, size.Width + 32, size.Height + 32,
                () => Change(d => d.Id == "demo:mouse" ? d with { BatteryPercent = 42 } : d.Id == "demo:keyboard" ? d with { IsCharging = false } : d), afterMs: 700, sheetFrames: 8, shrink: 2, columns: 4);
            await Task.Delay(400);
            await Cycles("level and charging changes", 10, async () =>
            {
                Change(d => d.Id == "demo:mouse" ? d with { BatteryPercent = 20 } : d.Id == "demo:headphones" ? d with { IsCharging = true } : d);
                await Task.Delay(700);
                Change(d => d.Id == "demo:mouse" ? d with { BatteryPercent = 80 } : d);
                await Task.Delay(700);
            });
        }
        finally
        {
            Core.Devices.DemoDeviceSource.Tweak = null;
            host.Services.Devices.RefreshAll();
        }
    }

    // ----- B7: size, light and dark, Glass and Pixel -----

    private static async Task SizeAndShade(WidgetHost host, WidgetTheme theme)
    {
        host.SetTheme(theme);
        await Task.Delay(1500);
        Line($"## B7 size and light/dark, {theme}");
        foreach (string type in new[] { "battery", "worldclock", "folder" })
        {
            var window = host.Windows.First(w => w.TypeId == type);
            var factory = WidgetCatalog.Find(type)!;
            var sizes = factory.SupportedSizes.ToList();
            var card = window.CardRect;
            var largest = sizes.Max(s => WidgetLayout.GetCardPixels(s, theme, Win32.GetScaleAt(new Win32.POINT { X = card.Left + 1, Y = card.Top + 1 })).Height);
            double scale = Win32.GetScaleAt(new Win32.POINT { X = card.Left + 1, Y = card.Top + 1 });
            var widest = sizes.Max(s => WidgetLayout.GetCardPixels(s, theme, scale).Width);
            var tallest = sizes.Max(s => WidgetLayout.GetCardPixels(s, theme, scale).Height);
            foreach (var next in sizes.Where(s => s != window.Placement.Size).Concat([window.Placement.Size]).Take(sizes.Count))
            {
                await RecordedRect($"{type} to {next}", $"b7-{theme}-{type}-{next}", card.Left - 16, card.Top - 16, widest + 32, tallest + 32, () => window.SetSize(next), afterMs: 800, sheetFrames: 12, shrink: 2, columns: 4);
                await Task.Delay(300);
            }
        }
        var battery = host.Windows.First(w => w.TypeId == "battery");
        var bc = battery.CardRect;
        double s2 = Win32.GetScaleAt(new Win32.POINT { X = bc.Left + 1, Y = bc.Top + 1 });
        var (bw, bh) = (WidgetLayout.GetCardPixels(battery.Placement.Size, theme, s2).Width + 32, WidgetLayout.GetCardPixels(battery.Placement.Size, theme, s2).Height + 32);
        await RecordedRect("light to dark", $"b7-{theme}-dark", bc.Left - 16, bc.Top - 16, bw, bh, () => { foreach (var w in host.Windows) w.ForceElementTheme(ElementTheme.Dark); }, afterMs: 800, sheetFrames: 12, shrink: 2, columns: 4);
        await Task.Delay(500);
        await RecordedRect("dark to light", $"b7-{theme}-light", bc.Left - 16, bc.Top - 16, bw, bh, () => { foreach (var w in host.Windows) w.ForceElementTheme(ElementTheme.Light); }, afterMs: 800, sheetFrames: 12, shrink: 2, columns: 4);
        var clock = host.Windows.First(w => w.TypeId == "worldclock");
        await Cycles("size changes and light/dark", 8, async () =>
        {
            clock.SetSize(WidgetSize.Small);
            await Task.Delay(700);
            clock.SetSize(WidgetSize.Medium);
            await Task.Delay(700);
            foreach (var w in host.Windows) w.ForceElementTheme(ElementTheme.Dark);
            await Task.Delay(500);
            foreach (var w in host.Windows) w.ForceElementTheme(ElementTheme.Light);
            await Task.Delay(500);
        });
    }

    private static async Task ThemeSwitch(WidgetHost host)
    {
        host.SetTheme(WidgetTheme.Glass);
        await Task.Delay(1500);
        Line("## B7 Glass to Pixel and back");
        var work = Win32.GetWorkArea(default);
        int x = work.Left + 70, y = work.Top + 100;
        await RecordedRect("Glass to Pixel", "b7-switch-to-pixel", x, y, 1250, 800, () => host.SetTheme(WidgetTheme.Pixel), afterMs: 900, sheetFrames: 12, shrink: 3, columns: 3);
        await Task.Delay(500);
        await RecordedRect("Pixel to Glass", "b7-switch-to-glass", x, y, 1250, 800, () => host.SetTheme(WidgetTheme.Glass), afterMs: 900, sheetFrames: 12, shrink: 3, columns: 3);
    }

    // ----- B8: a widget added or removed -----

    private static async Task WidgetAddRemove(WidgetHost host, WidgetTheme theme)
    {
        host.SetTheme(theme);
        await Task.Delay(1500);
        Line($"## B8 a widget is added and removed, {theme}");
        var work = Win32.GetWorkArea(default);
        int x = work.Left + 1200, y = work.Top + 160; // inside the stage (60 to 1860 across), room for a Large card
        foreach (string type in new[] { "clock", "battery", "folder" })
        {
            var factory = WidgetCatalog.Find(type)!;
            var size = factory.DefaultSize;
            var (rx, ry, rw, rh) = AroundCard(host, factory, size, x, y);
            WidgetWindow? window = null;
            await RecordedRect($"{type} added", $"b8-{theme}-{type}-add", rx, ry, rw, rh, () => window = host.AddForCapture(factory, x, y), afterMs: 700, sheetFrames: 12, shrink: 1, columns: 6);
            await Task.Delay(300);
            await RecordedRect($"{type} removed", $"b8-{theme}-{type}-remove", rx, ry, rw, rh, () => host.RemoveWidget(window!), afterMs: 700, sheetFrames: 12, shrink: 1, columns: 6);
            await Task.Delay(300);
        }
        var plain = WidgetCatalog.Find("clock")!;
        await Cycles("add and remove a clock with the motion", 20, async () =>
        {
            var window = host.AddForCapture(plain, x, y)!;
            await Task.Delay(450);
            host.RemoveWidget(window);
            await Task.Delay(350);
        });
    }
}
