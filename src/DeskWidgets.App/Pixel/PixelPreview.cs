using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using DeskWidgets.Core.Devices;
using DeskWidgets.Widgets;
using DeskWidgets.Widgets.Battery;
using DeskWidgets.Widgets.Clock;
using DeskWidgets.Widgets.WorldClock;

namespace DeskWidgets.Pixel;

/// <summary>
/// Renders every pixel-theme layout with sample data, by day and by night,
/// in English and Chinese, to PNG contact sheets
/// (<c>DeskWidgets.exe --pixel-preview &lt;folder&gt;</c>): the quick way to
/// review sprites and layouts without placing widgets on the desktop.
/// </summary>
internal static class PixelPreview
{
    private const int Factor = 3; // screen pixels per art pixel, as at 150 % scaling
    private const int Margin = 10;
    private const int Gap = 8;
    private const int LabelHeight = 9;

    // 13:41:27 UTC: 09:41 in New York, 21:41 (night) in Beijing.
    private static readonly DateTimeOffset SampleTime = new(2026, 10, 13, 13, 41, 27, TimeSpan.Zero);

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
                    string name = $"pixel-{(current == AppLanguage.Chinese ? "zh" : "en")}-{(dark ? "night" : "day")}.png";
                    SavePng(Path.Combine(folder, name), Sheet(PixelPalette.For(dark), dark), Factor);
                }
            }
        }
        finally
        {
            Strings.Language = language;
        }
    }

    private static PixelCanvas Sheet(PixelPalette palette, bool dark)
    {
        var devices = SampleDevices();
        var connected = devices.Where(d => d.IsConnected).ToList();
        var cities = WorldCities.DefaultIds.Select(id => WorldCities.Resolve(id)!).ToList();
        var morning = new DateTime(2026, 10, 13, 9, 41, 27);
        var evening = morning.AddHours(12);
        var rapoo = new DeviceStatus { Id = "hid:24AE:146D", Name = "VT3 MAX MASTER V2", Kind = DeviceKind.Mouse, Connection = ConnectionKind.Wireless24G, BatteryPercent = 57, Detail = "1600 DPI" };

        Card Battery(WidgetSize size, string label, IReadOnlyList<DeviceStatus> list, bool names = true)
            => new(size, label, c => BatteryPixelScene.Draw(c, palette, size, list, names));
        Card Clock(WidgetSize size, string label, DateTime time)
            => new(size, label, c => ClockPixelScene.Draw(c, palette, size, time));
        Card World(WidgetSize size, string label)
            => new(size, label, c => WorldClockPixelScene.Draw(c, palette, size, cities, SampleTime, showSeconds: true));

        List<Card>[] rows =
        [
            [
                Battery(WidgetSize.Small, "BATTERY S", [rapoo]),
                Battery(WidgetSize.Small, "S CHARGING", [connected[1]]),
                Battery(WidgetSize.Small, "S GRID", devices),
                Battery(WidgetSize.Medium, "BATTERY M", devices),
                Battery(WidgetSize.Medium, "M NO NAMES", connected, names: false),
            ],
            [
                Battery(WidgetSize.Large, "BATTERY L", [rapoo, .. devices]),
                Battery(WidgetSize.Medium, "M ONE DEVICE", [rapoo]),
                Battery(WidgetSize.Small, "EMPTY", []),
                Clock(WidgetSize.Small, "CLOCK S", morning),
                Clock(WidgetSize.Small, "CLOCK S NIGHT", evening),
            ],
            [
                Clock(WidgetSize.Medium, "CLOCK M", morning),
                Clock(WidgetSize.Medium, "CLOCK M NIGHT", evening),
                World(WidgetSize.Small, "WORLD S"),
            ],
            [
                World(WidgetSize.Medium, "WORLD M"),
                World(WidgetSize.Large, "WORLD L"),
            ],
        ];

        int width = rows.Max(r => r.Sum(c => c.Width + Gap) - Gap) + 2 * Margin;
        int height = rows.Sum(r => r.Max(c => c.Height) + LabelHeight + Gap) - Gap + 2 * Margin;
        var sheet = new PixelCanvas(width, height);
        PaintWallpaper(sheet, dark);

        int y = Margin;
        foreach (var row in rows)
        {
            int x = Margin;
            foreach (var card in row)
            {
                PixelText.Layout(card.Label, PixelFonts.Small).Draw(sheet, x, y, dark ? 0xFF94B0C2 : 0xFF333C57);
                var canvas = new PixelCanvas(card.Width, card.Height);
                PixelCard.Draw(canvas, palette);
                card.Paint(canvas);
                Blit(canvas, sheet, x, y + LabelHeight);
                x += card.Width + Gap;
            }
            y += row.Max(c => c.Height) + LabelHeight + Gap;
        }
        return sheet;
    }

    private static List<DeviceStatus> SampleDevices()
    {
        var demo = new DemoDeviceSource(() => Strings.IsChinese).ScanAsync(CancellationToken.None).Result;
        return [.. demo.OrderByDescending(d => d.IsConnected)];
    }

    /// <summary>A soft diagonal two-tone stand-in for a desktop wallpaper.</summary>
    internal static void PaintWallpaper(PixelCanvas sheet, bool dark)
    {
        uint a = dark ? 0xFF262B44 : 0xFF8FA7BF, b = dark ? 0xFF2D3350 : 0xFF9AB2C9;
        for (int y = 0; y < sheet.Height; y++)
        {
            for (int x = 0; x < sheet.Width; x++)
                sheet.Set(x, y, (x + y) / 24 % 2 == 0 ? a : b);
        }
    }

    private static void Blit(PixelCanvas source, PixelCanvas target, int left, int top)
    {
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                uint color = source.Get(x, y);
                if (color != 0)
                    target.Set(left + x, top + y, color);
            }
        }
    }

    internal static void SavePng(string path, PixelCanvas canvas, int factor)
    {
        int width = canvas.Width * factor, height = canvas.Height * factor;
        var bgra = new byte[width * height * 4];
        canvas.CopyScaled(bgra, factor);

        using var pixels = new MemoryStream();
        using (var zlib = new ZLibStream(pixels, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + width * 4]; // filter type 0, then RGBA
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int from = (y * width + x) * 4, to = 1 + x * 4;
                    row[to] = bgra[from + 2];
                    row[to + 1] = bgra[from + 1];
                    row[to + 2] = bgra[from];
                    row[to + 3] = bgra[from + 3];
                }
                zlib.Write(row);
            }
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA
        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(file, "IHDR", header);
        WriteChunk(file, "IDAT", pixels.ToArray());
        WriteChunk(file, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] name = Encoding.ASCII.GetBytes(type);
        var number = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);
        stream.Write(name);
        stream.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(name, data));
        stream.Write(number);
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte[] part in new[] { first, second })
        {
            foreach (byte value in part)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ (0xEDB88320 & (0 - (crc & 1)));
            }
        }
        return ~crc;
    }

    private sealed record Card(WidgetSize Size, string Label, Action<PixelCanvas> Paint)
    {
        public int Width => PixelCard.ArtSize(Size).Width;

        public int Height => PixelCard.ArtSize(Size).Height;
    }
}
