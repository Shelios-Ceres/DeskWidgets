using System.Runtime.InteropServices;
using DeskWidgets.Pixel;

namespace DeskWidgets.PixelUi;

/// <summary>
/// The Pixel theme's tray icon: a dark stepped tile with a hard-edged ring and a pixel mouse, drawn at the exact
/// size Windows asks for (one icon pixel per screen pixel, never scaled from another size).
/// </summary>
internal static class PixelTrayIcon
{
    private static readonly string[] Mouse4 = [".##.", "#..#", "####", "#..#", "#..#", ".##."];
    private static readonly string[] Mouse6 = [".####.", "#.##.#", "#.##.#", "######", "#....#", "#....#", "#....#", ".####."];
    private static readonly string[] Mouse8 = [".######.", "#..##..#", "#..##..#", "#..##..#", "########", "#......#", "#......#", "#......#", "#......#", ".######."];

    public static PixelCanvas Draw(int size)
    {
        var c = new PixelCanvas(size, size);
        int[] corner = size >= 24 ? [3, 2, 1] : [2, 1];
        PixelFrame.Draw(c, 0, 0, size, size, corner, 0xFF0B0C14, 0xFF1A1C2C, 0xFF333C57, 0xFF111220, 1);
        double center = size / 2.0;
        double outer = size * 0.40, thick = size >= 24 ? 3 : 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x + 0.5 - center, dy = y + 0.5 - center;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d > outer || d <= outer - thick)
                    continue;
                double angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
                c.Set(x, y, angle < 270 ? 0xFFA7F070 : 0xFF566C86);
            }
        }
        string[] mouse = size >= 32 ? Mouse8 : size >= 24 ? Mouse6 : Mouse4;
        int mw = mouse[0].Length, mh = mouse.Length;
        int mx = (size - mw) / 2, my = (size - mh) / 2;
        for (int y = 0; y < mh; y++)
        {
            for (int x = 0; x < mw; x++)
            {
                if (mouse[y][x] == '#')
                    c.Set(mx + x, my + y, 0xFFF4F4F4);
            }
        }
        return c;
    }

    /// <summary>An HICON of exactly <paramref name="size"/> × <paramref name="size"/> pixels (the caller destroys it); 0 on failure.</summary>
    public static nint Create(int size)
    {
        var canvas = Draw(size);
        var bgra = new byte[size * size * 4];
        canvas.CopyScaled(bgra, 1);

        var header = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = -size, // top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        nint screen = Native.GetDC(0);
        nint color = Native.CreateDIBSection(screen, ref header, 0, out nint bits, 0, 0);
        Native.ReleaseDC(0, screen);
        if (color == 0)
            return 0;
        Marshal.Copy(bgra, 0, bits, bgra.Length);
        nint mask = Native.CreateBitmap(size, size, 1, 1, 0); // all zero: the colour bitmap's alpha decides
        var info = new Native.ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
        nint icon = Native.CreateIconIndirect(ref info);
        Native.DeleteObject(color);
        Native.DeleteObject(mask);
        return icon;
    }
}
