using System.Globalization;
using DeskWidgets.Pixel;

namespace DeskWidgets.Widgets.WorldClock;

/// <summary>
/// The world clock in the pixel theme: little pixel clock faces (white by
/// day, navy by night) drawn with hard-edged hands. Small = 2×2 faces with
/// three-letter city codes, medium = a row of four with names, times and
/// dates, large = a list with each city's offset, time and date.
/// </summary>
internal static class WorldClockPixelScene
{
    public static void Draw(PixelCanvas canvas, PixelPalette palette, WidgetSize size, IReadOnlyList<WorldCity> cities, DateTimeOffset now, bool showSeconds)
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        var times = cities.Select(c => TimeZoneInfo.ConvertTime(now, c.Zone)).ToList();
        if (size == WidgetSize.Small)
            DrawGrid(canvas, palette, cities, times, showSeconds);
        else if (size == WidgetSize.Medium)
            DrawRow(canvas, palette, cities, times, showSeconds);
        else
            DrawList(canvas, palette, cities, times, local, showSeconds);
    }

    /// <summary>Small: 2×2 faces, each over its city code.</summary>
    private static void DrawGrid(PixelCanvas canvas, PixelPalette palette, IReadOnlyList<WorldCity> cities, IReadOnlyList<DateTimeOffset> times, bool showSeconds)
    {
        const int cell = 34, radius = 11, cellHeight = 2 * radius + 1 + 3 + 5, rowGap = 6;
        int top = 1 + (canvas.Height - 4 - (2 * cellHeight + rowGap)) / 2;
        for (int i = 0; i < cities.Count; i++)
        {
            int x = 7 + i % 2 * cell, y = top + i / 2 * (cellHeight + rowGap);
            DrawFace(canvas, palette, x + cell / 2, y + radius, radius, times[i].DateTime, showSeconds);
            var code = PixelText.Layout(cities[i].Code, PixelFonts.Small);
            code.Draw(canvas, x, y + 2 * radius + 4, palette.Ink, PixelAlign.Center, cell);
        }
    }

    /// <summary>Medium: four faces in a row, each over its city's name, time and date. Names share one block height.</summary>
    private static void DrawRow(PixelCanvas canvas, PixelPalette palette, IReadOnlyList<WorldCity> cities, IReadOnlyList<DateTimeOffset> times, bool showSeconds)
    {
        const int left = 8, column = 39, radius = 13, lineGap = 2;
        int line = PixelFonts.Small.Height;
        var names = cities.Select(c => PixelText.Layout(c.Name, PixelFonts.Small, column - 2, maxLines: 2)).ToList();
        int namesHeight = names.Max(n => n.Height);
        int height = 2 * radius + 1 + 5 + namesHeight + 3 + line + lineGap + line;
        int top = 1 + (canvas.Height - 4 - height) / 2;
        for (int i = 0; i < cities.Count; i++)
        {
            int x = left + i * column;
            DrawFace(canvas, palette, x + column / 2, top + radius, radius, times[i].DateTime, showSeconds);
            int y = top + 2 * radius + 1 + 5;
            names[i].Draw(canvas, x + 1, y, palette.Ink, PixelAlign.Center, column - 2);
            y += namesHeight + 3;
            PixelText.Layout(WorldCities.FormatTime(times[i]), PixelFonts.Small).Draw(canvas, x, y, palette.Soft, PixelAlign.Center, column);
            PixelText.Layout(PixelDate(times[i]), PixelFonts.Small).Draw(canvas, x, y + line + lineGap, palette.Soft, PixelAlign.Center, column);
        }
    }

    /// <summary>
    /// Large: one row per city: its face, then the name sitting on the big
    /// digital time's baseline. Underneath, the offset from local time runs
    /// from the left (room enough for "今天 +12小时") and the date sits
    /// right-aligned under the time.
    /// </summary>
    private static void DrawList(PixelCanvas canvas, PixelPalette palette, IReadOnlyList<WorldCity> cities, IReadOnlyList<DateTimeOffset> times, DateTimeOffset local, bool showSeconds)
    {
        const int left = 8, right = 164, rowHeight = 39, radius = 15, textLeft = 44, gap = 3;
        for (int i = 0; i < cities.Count; i++)
        {
            int y = 8 + i * rowHeight, inner = rowHeight - 1;
            DrawFace(canvas, palette, left + radius, y + inner / 2, radius, times[i].DateTime, showSeconds);

            var time = PixelText.Layout(WorldCities.FormatTime(times[i]), PixelFonts.Regular, scale: 2);
            var date = PixelText.Layout(PixelDate(times[i]), PixelFonts.Small);
            var name = PixelText.Layout(cities[i].Name, PixelFonts.Regular, right - time.Width - 5 - textLeft);
            var offset = PixelText.Layout(WorldCities.DescribeOffset(times[i], local), PixelFonts.Small, right - date.Width - 5 - textLeft);
            int top = y + (inner - (time.Height + gap + offset.Height)) / 2;
            int below = top + time.Height + gap;
            time.Draw(canvas, right - time.Width - 1, top, palette.Ink, shadow: palette.TextShadow);
            name.Draw(canvas, textLeft, top + time.Height - name.Height, palette.Ink);
            offset.Draw(canvas, textLeft, below, palette.Soft);
            date.Draw(canvas, right - date.Width - 1, below + (offset.Height - date.Height) / 2, palette.Soft);

            if (i < cities.Count - 1)
                canvas.DottedLine(textLeft, y + rowHeight - 1, right - textLeft, palette.Faint);
        }
    }

    /// <summary>
    /// The city's date in pixel letters: "OCT 13", or "10/13" in Chinese. The
    /// pixel fonts have no Chinese glyphs, and "10月13日" in the 12-px fallback
    /// is wider than a medium column.
    /// </summary>
    private static string PixelDate(DateTimeOffset time)
        => Strings.IsChinese ? time.ToString("M/d", CultureInfo.InvariantCulture) : WorldCities.FormatDate(time);

    /// <summary>A clock face of the given radius centred on (cx, cy), showing <paramref name="time"/>.</summary>
    private static void DrawFace(PixelCanvas canvas, PixelPalette palette, int cx, int cy, int radius, DateTime time, bool showSeconds)
    {
        bool day = time.Hour is >= 6 and < 18;
        uint hands = day ? palette.DayHands : palette.NightHands;
        canvas.FillCircle(cx, cy, radius, day ? palette.DayFace : palette.NightFace);
        canvas.Circle(cx, cy, radius, palette.Rim);

        for (int hour = 0; hour < 12; hour++)
        {
            bool major = hour % 3 == 0;
            double angle = hour * Math.PI / 6;
            for (int depth = 2; depth <= (major && radius >= 12 ? 3 : 2); depth++)
                canvas.Set(cx + Round((radius - depth) * Math.Sin(angle)), cy - Round((radius - depth) * Math.Cos(angle)), major ? hands : day ? palette.DayTicks : palette.NightTicks);
        }

        double minutes = time.Minute + time.Second / 60.0;
        DrawHand(canvas, cx, cy, (time.Hour % 12 + minutes / 60) * 30, radius * 0.5, hands, thick: true);
        DrawHand(canvas, cx, cy, minutes * 6, radius * 0.78, hands, thick: radius >= 13);
        if (showSeconds)
            DrawHand(canvas, cx, cy, time.Second * 6, radius * 0.86, palette.Orange, thick: false);
        canvas.Set(cx, cy, showSeconds ? palette.Orange : hands);
    }

    /// <summary>A hand from the centre at <paramref name="degrees"/> clockwise from 12; thick hands get a parallel second line.</summary>
    private static void DrawHand(PixelCanvas canvas, int cx, int cy, double degrees, double length, uint color, bool thick)
    {
        double angle = degrees * Math.PI / 180;
        double sin = Math.Sin(angle), cos = Math.Cos(angle);
        int x = cx + Round(length * sin), y = cy - Round(length * cos);
        canvas.Line(cx, cy, x, y, color);
        if (!thick)
            return;
        // Offset across the hand's direction: sideways for upright hands, down for flat ones.
        (int dx, int dy) = Math.Abs(sin) < Math.Abs(cos) ? (1, 0) : (0, 1);
        canvas.Line(cx + dx, cy + dy, x + dx, y + dy, color);
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
