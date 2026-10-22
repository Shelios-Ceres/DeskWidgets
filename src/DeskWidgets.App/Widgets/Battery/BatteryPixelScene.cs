using DeskWidgets.Core.Devices;
using DeskWidgets.Pixel;

namespace DeskWidgets.Widgets.Battery;

/// <summary>
/// The battery widget in the pixel theme, drawn like a game inventory: each
/// device sits in a recessed slot above a little battery gauge. Small = one
/// device with its level in big digits (or a 2×2 grid), medium = a row of
/// four, large = a list.
/// </summary>
internal static class BatteryPixelScene
{
    private const int Slots = 4;
    private const int ListRows = 6;

    public static void Draw(PixelCanvas canvas, PixelPalette palette, WidgetSize size, IReadOnlyList<DeviceStatus> devices, bool showNames)
    {
        if (devices.Count == 0)
            DrawEmpty(canvas, palette);
        else if (size == WidgetSize.Small && devices.Count == 1)
            DrawSingle(canvas, palette, devices[0], showNames);
        else if (size == WidgetSize.Small)
            DrawGrid(canvas, palette, devices);
        else if (size == WidgetSize.Medium)
            DrawRow(canvas, palette, devices, showNames);
        else
            DrawList(canvas, palette, devices);
    }

    /// <summary>Small, one device: slot and big percentage on top, name, then a wide gauge.</summary>
    private static void DrawSingle(PixelCanvas canvas, PixelPalette palette, DeviceStatus device, bool showNames)
    {
        const int left = 7, right = 75;
        DrawSlot(canvas, palette, left, 7, 24, device);

        var percent = PixelText.Layout(PercentText(device), PixelFonts.Regular, scale: 2);
        percent.Draw(canvas, right - percent.Width - 1, 7 + (24 - percent.Height) / 2, Known(device) ? palette.Ink : palette.Soft, shadow: palette.TextShadow);

        if (showNames)
        {
            var name = PixelText.Layout(Name(device), PixelFonts.Small, right - left, maxLines: 2);
            int nameTop = 31 + (31 - name.Height) / 2;
            name.Draw(canvas, left, nameTop, palette.Soft);
        }
        DrawGauge(canvas, palette, left, 62, right - left, 11, device);
    }

    /// <summary>Small, several devices: four cells of icon, gauge and percentage.</summary>
    private static void DrawGrid(PixelCanvas canvas, PixelPalette palette, IReadOnlyList<DeviceStatus> devices)
    {
        // Each cell: a 13-pixel sprite band, the gauge, the percentage.
        const int cell = 34, cellHeight = 13 + 3 + 8 + 3 + 5, rowGap = 5;
        int top = 1 + (canvas.Height - 4 - (2 * cellHeight + rowGap)) / 2;
        for (int slot = 0; slot < Slots; slot++)
        {
            int x = 7 + slot % 2 * cell, y = top + slot / 2 * (cellHeight + rowGap);
            var device = slot < devices.Count ? devices[slot] : null;
            if (device is null)
            {
                DrawEmptyGauge(canvas, palette, x + 5, y + 16, 24, 8);
                continue;
            }
            DrawDevice(canvas, palette, device, x + cell / 2, y + 6);
            DrawGauge(canvas, palette, x + 5, y + 16, 24, 8, device);
            var percent = PixelText.Layout(PercentText(device), PixelFonts.Small);
            percent.Draw(canvas, x, y + 27, Known(device) ? palette.Ink : palette.Soft, PixelAlign.Center, cell);
        }
    }

    /// <summary>
    /// Medium: four columns of slot, gauge, name and percentage. Names share
    /// one block height so the rows line up across columns.
    /// </summary>
    private static void DrawRow(PixelCanvas canvas, PixelPalette palette, IReadOnlyList<DeviceStatus> devices, bool showNames)
    {
        // Names stay 3 pixels narrower than their column so neighbours never run together.
        const int left = 8, column = 39, nameWidth = column - 3, gaugeHeight = 9, percentHeight = 7;
        var names = new PixelText?[Slots];
        int namesHeight = 0;
        for (int slot = 0; slot < Slots && showNames && slot < devices.Count; slot++)
        {
            names[slot] = PixelText.Layout(Name(devices[slot]), PixelFonts.Small, nameWidth, maxLines: 2);
            namesHeight = Math.Max(namesHeight, names[slot]!.Height);
        }

        // Two lines of Chinese are twice as tall as two of the pixel font: tighten everything else.
        bool compact = namesHeight > 2 * PixelFonts.Small.Height + PixelFonts.Small.LineGap;
        int slotSize = compact ? 22 : 24, gap = compact ? 3 : 5;
        int height = slotSize + 4 + gaugeHeight + (namesHeight > 0 ? gap + namesHeight : 0) + gap + percentHeight;
        int top = 1 + (canvas.Height - 4 - height) / 2;
        for (int slot = 0; slot < Slots; slot++)
        {
            int x = left + slot * column;
            var device = slot < devices.Count ? devices[slot] : null;
            DrawSlot(canvas, palette, x + (column - slotSize) / 2, top, slotSize, device);
            int y = top + slotSize + 4;
            if (device is null)
            {
                DrawEmptyGauge(canvas, palette, x + 6, y, column - 12, gaugeHeight);
                continue;
            }
            DrawGauge(canvas, palette, x + 6, y, column - 12, gaugeHeight, device);
            y += gaugeHeight + gap;
            names[slot]?.Draw(canvas, x, y, palette.Soft, PixelAlign.Center, column);
            if (namesHeight > 0)
                y += namesHeight + gap;
            var percent = PixelText.Layout(PercentText(device), PixelFonts.Regular);
            percent.Draw(canvas, x, y, Known(device) ? palette.Ink : palette.Soft, PixelAlign.Center, column);
        }
    }

    /// <summary>
    /// Large: one row per device with slot, name, connection, percentage and
    /// gauge. Rows grow (and fewer fit) when Chinese text needs two tall lines.
    /// A full list (six rows, or no room for one more) spreads over the whole height; a short one stays top-aligned.
    /// </summary>
    private static void DrawList(PixelCanvas canvas, PixelPalette palette, IReadOnlyList<DeviceStatus> devices)
    {
        const int left = 8, right = 164, top = 8, bottom = 175, textLeft = 36, valueWidth = 25, slotSize = 22;
        const int textWidth = right - valueWidth - 6 - textLeft;
        var rows = devices.Take(ListRows).Select(device =>
        {
            var name = PixelText.Layout(Name(device), PixelFonts.Regular, textWidth);
            var detail = PixelText.Layout(BatteryWidget.Describe(device), PixelFonts.Small, textWidth);
            return (Device: device, Name: name, Detail: detail, Gap: name.IsFallback || detail.IsFallback ? 1 : 3);
        }).ToList();
        int minHeight = Math.Max(26, rows.Max(r => r.Name.Height + r.Gap + r.Detail.Height) + 4);
        int count = Math.Min(rows.Count, (bottom - top) / minHeight);
        bool full = count == ListRows || (count + 1) * minHeight > bottom - top;
        int rowHeight = full ? (bottom - top) / count : minHeight;

        for (int i = 0; i < count; i++)
        {
            var (device, name, detail, gap) = rows[i];
            int y = top + i * rowHeight, inner = rowHeight - 1;
            DrawSlot(canvas, palette, left, y + (inner - slotSize) / 2, slotSize, device);

            int textTop = y + (inner - (name.Height + gap + detail.Height)) / 2;
            name.Draw(canvas, textLeft, textTop, device.IsConnected ? palette.Ink : palette.Soft);
            detail.Draw(canvas, textLeft, textTop + name.Height + gap, palette.Soft);

            int valueTop = y + (inner - 18) / 2; // percentage (7) + 3 + gauge (8)
            var percent = PixelText.Layout(PercentText(device), PixelFonts.Regular);
            percent.Draw(canvas, right - valueWidth, valueTop, Known(device) ? palette.Ink : palette.Soft, PixelAlign.Right, valueWidth);
            DrawGauge(canvas, palette, right - valueWidth, valueTop + 10, valueWidth, 8, device);

            if (i < count - 1)
                canvas.DottedLine(textLeft, y + rowHeight - 1, right - textLeft, palette.Faint);
        }
    }

    private static void DrawEmpty(PixelCanvas canvas, PixelPalette palette)
    {
        var sprite = PixelSprites.Bluetooth;
        var text = PixelText.Layout(Strings.NoDevices, PixelFonts.Small, canvas.Width - 16, maxLines: 2);
        int height = sprite.Height * 2 + 7 + text.Height;
        int top = 1 + (canvas.Height - 4 - height) / 2;
        canvas.Draw(sprite, (canvas.Width - sprite.Width * 2) / 2, top, _ => palette.Faint, scale: 2);
        text.Draw(canvas, 8, top + sprite.Height * 2 + 7, palette.Soft, PixelAlign.Center, canvas.Width - 16);
    }

    /// <summary>A recessed square (an inventory slot) with the device's sprite in it; empty when <paramref name="device"/> is null.</summary>
    private static void DrawSlot(PixelCanvas canvas, PixelPalette palette, int x, int y, int size, DeviceStatus? device)
    {
        canvas.Fill(x + 1, y, size - 2, size, palette.Well);
        canvas.Fill(x, y + 1, size, size - 2, palette.Well);
        canvas.Fill(x + 1, y, size - 2, 1, palette.WellEdge);
        canvas.Fill(x, y + 1, 1, size - 2, palette.WellEdge);
        if (device is not null)
            DrawDevice(canvas, palette, device, x + size / 2, y + size / 2);
    }

    /// <summary>The device's sprite centred on (cx, cy); dimmed while disconnected.</summary>
    private static void DrawDevice(PixelCanvas canvas, PixelPalette palette, DeviceStatus device, int cx, int cy)
    {
        var sprite = PixelSprites.For(device.Kind);
        uint ink = device.IsConnected ? palette.Ink : palette.Faint;
        uint tone = device.IsConnected ? palette.Faint : 0;
        canvas.Draw(sprite, cx - sprite.Width / 2, cy - sprite.Height / 2, symbol => symbol == '+' ? tone : ink);
    }

    /// <summary>
    /// A battery: outlined body with a tip on the right, filled from the left
    /// in the level colour, with a bolt across it while charging.
    /// </summary>
    private static void DrawGauge(PixelCanvas canvas, PixelPalette palette, int x, int y, int width, int height, DeviceStatus device)
    {
        bool known = Known(device);
        DrawBatteryShape(canvas, x, y, width, height, device.IsConnected ? palette.Ink : palette.Faint);
        int innerWidth = width - 6, innerHeight = height - 4;
        canvas.Fill(x + 2, y + 2, innerWidth, innerHeight, palette.Track);
        if (known)
        {
            int percent = Math.Clamp(device.BatteryPercent!.Value, 0, 100);
            int filled = percent == 0 ? 0 : Math.Max(1, (int)Math.Round(innerWidth * percent / 100.0));
            canvas.Fill(x + 2, y + 2, filled, innerHeight, LevelColor(device, palette));
        }
        if (known && device.IsCharging)
        {
            var bolt = height >= 10 ? PixelSprites.Bolt : PixelSprites.SmallBolt;
            int bodyWidth = width - 2;
            canvas.DrawOutlined(bolt, x + (bodyWidth - bolt.Width) / 2, y + (height - bolt.Height) / 2, palette.Yellow, palette.Outline);
        }
    }

    /// <summary>The outline of an empty slot's gauge.</summary>
    private static void DrawEmptyGauge(PixelCanvas canvas, PixelPalette palette, int x, int y, int width, int height)
        => DrawBatteryShape(canvas, x, y, width, height, palette.Faint);

    private static void DrawBatteryShape(PixelCanvas canvas, int x, int y, int width, int height, uint edge)
    {
        int body = width - 2;
        canvas.Fill(x + 1, y, body - 2, 1, edge);
        canvas.Fill(x + 1, y + height - 1, body - 2, 1, edge);
        canvas.Fill(x, y + 1, 1, height - 2, edge);
        canvas.Fill(x + body - 1, y + 1, 1, height - 2, edge);
        int tip = Math.Max(3, height / 2 - 1 + height % 2);
        canvas.Fill(x + body, y + (height - tip) / 2, 2, tip, edge);
    }

    private static bool Known(DeviceStatus device) => device.IsConnected && device.BatteryPercent is not null;

    private static string PercentText(DeviceStatus device) => Known(device) ? $"{device.BatteryPercent}%" : "—";

    private static string Name(DeviceStatus device) => device.IsHost ? Strings.ThisPc : device.Name;

    /// <summary>Red at 20 % or below (unless charging), yellow in power-saving mode, green otherwise.</summary>
    private static uint LevelColor(DeviceStatus device, PixelPalette palette)
    {
        if (device.BatteryPercent is <= 20 && !device.IsCharging)
            return palette.Red;
        return device.IsPowerSaving ? palette.Yellow : palette.Green;
    }
}
