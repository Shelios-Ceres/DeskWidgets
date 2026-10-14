using DeskWidgets.Pixel;

namespace DeskWidgets.Widgets.Clock;

/// <summary>
/// The clock in the pixel theme: weekday, the time in big blocky digits and
/// the date, with a sun or moon in the corner depending on the hour.
/// </summary>
internal static class ClockPixelScene
{
    public static void Draw(PixelCanvas canvas, PixelPalette palette, WidgetSize size, DateTime now)
    {
        var culture = Strings.Culture;
        bool medium = size != WidgetSize.Small;
        int left = medium ? 10 : 8;
        // English abbreviates the month ("OCT 13") the way digital watches do.
        string datePattern = Strings.IsChinese ? culture.DateTimeFormat.MonthDayPattern : "MMM d";

        var weekday = PixelText.Layout(now.ToString("dddd", culture), PixelFonts.Regular, canvas.Width - left * 2);
        var time = PixelText.Layout(now.ToString("HH:mm", culture), PixelFonts.Regular, scale: medium ? 4 : 2);
        var date = PixelText.Layout(now.ToString(datePattern, culture), PixelFonts.Regular, canvas.Width - left * 2);

        int gap = medium ? 6 : 5;
        int height = weekday.Height + gap + time.Height + gap + date.Height;
        int top = 1 + (canvas.Height - 4 - height) / 2;
        weekday.Draw(canvas, left, top, palette.Accent);
        time.Draw(canvas, left, top + weekday.Height + gap, palette.Ink, shadow: palette.TextShadow);
        date.Draw(canvas, left, top + weekday.Height + gap + time.Height + gap, palette.Soft);

        bool day = now.Hour is >= 6 and < 18;
        var sky = day ? PixelSprites.Sun : PixelSprites.Moon;
        uint color = day ? palette.Orange : palette.Yellow;
        if (medium)
        {
            const int scale = 3;
            int x = canvas.Width - 12 - (PixelSprites.Sun.Width * scale + sky.Width * scale) / 2;
            canvas.Draw(sky, x, (canvas.Height - 3 - sky.Height * scale) / 2, _ => color, scale);
        }
        else
        {
            canvas.Draw(sky, canvas.Width - 8 - sky.Width, top - 1, _ => color);
        }
    }
}
