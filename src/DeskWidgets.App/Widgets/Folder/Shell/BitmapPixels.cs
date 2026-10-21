using System.Runtime.InteropServices;

namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// What an HBITMAP from the Shell holds: top-down BGRA pixels. The Shell's images are straight (not premultiplied)
/// alpha, so <see cref="Premultiply"/> before handing them to XAML.
/// </summary>
internal sealed class Pixels
{
    public Pixels(int width, int height, byte[] bgra)
    {
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Four bytes per pixel, rows top to bottom.</summary>
    public byte[] Bgra { get; }

    public byte AlphaAt(int x, int y) => Bgra[(y * Width + x) * 4 + 3];

    /// <summary>Converts straight alpha to premultiplied alpha, in place.</summary>
    public void Premultiply()
    {
        for (int i = 0; i < Bgra.Length; i += 4)
        {
            int a = Bgra[i + 3];
            if (a == 255)
                continue;
            Bgra[i] = (byte)((Bgra[i] * a + 127) / 255);
            Bgra[i + 1] = (byte)((Bgra[i + 1] * a + 127) / 255);
            Bgra[i + 2] = (byte)((Bgra[i + 2] * a + 127) / 255);
        }
    }

    /// <summary>
    /// Whether the picture is already a filled square (a full-bleed icon): alpha is at least 200 at the four points 12 % in from
    /// each corner and at the four edge midpoints 4 % in. Run on straight alpha.
    /// </summary>
    public bool IsFullBleed()
    {
        int w = Width, h = Height;
        (int X, int Y)[] points =
        [
            (w * 12 / 100, h * 12 / 100), (w - 1 - w * 12 / 100, h * 12 / 100),
            (w * 12 / 100, h - 1 - h * 12 / 100), (w - 1 - w * 12 / 100, h - 1 - h * 12 / 100),
            (w / 2, h * 4 / 100), (w / 2, h - 1 - h * 4 / 100),
            (w * 4 / 100, h / 2), (w - 1 - w * 4 / 100, h / 2),
        ];
        return points.All(p => AlphaAt(Math.Clamp(p.X, 0, w - 1), Math.Clamp(p.Y, 0, h - 1)) >= 200);
    }
}

internal static class Bitmaps
{
    /// <summary>Copies an HBITMAP into managed BGRA pixels (the bitmap is not freed).</summary>
    public static Pixels Read(IntPtr hbitmap)
    {
        ShellNative.GetObject(hbitmap, Marshal.SizeOf<ShellNative.BITMAP>(), out var bitmap);
        int width = bitmap.bmWidth, height = Math.Abs(bitmap.bmHeight);
        var info = new ShellNative.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<ShellNative.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var bits = new byte[width * height * 4];
        nint dc = ShellNative.CreateCompatibleDC(0);
        ShellNative.GetDIBits(dc, hbitmap, 0, (uint)height, bits, ref info, 0);
        ShellNative.DeleteDC(dc);
        return new Pixels(width, height, bits);
    }
}
