using System.Runtime.InteropServices;

namespace DeskWidgets.Pixel;

/// <summary>
/// Glyphs for characters the pixel fonts lack, from SimSun at 12 pixels with
/// anti-aliasing off: Windows' own bitmap strike, the classic crisp Chinese
/// pixel type. Drawn with GDI one character at a time (<see cref="PixelFont"/>
/// caches them). UI thread only.
/// </summary>
internal static class SystemGlyphs
{
    private const string FaceName = "SimSun";
    private const int EmSize = 12;
    private const int Cell = 32; // side of the scratch bitmap, roomy for any 12-px glyph

    private static nint s_dc;
    private static nint s_bits;
    private static int s_height = EmSize;
    private static bool s_failed;

    /// <summary>Rows in every glyph: the font's cell height.</summary>
    public static int Height => EnsureContext() ? s_height : EmSize;

    public static PixelMask? Render(char c)
    {
        if (!EnsureContext())
            return null;
        if (char.IsSurrogate(c))
            c = '?'; // characters outside the BMP (emoji) are not worth a second code path
        string text = c.ToString();
        var pixels = new int[Cell * Cell];
        Marshal.Copy(pixels, 0, s_bits, pixels.Length); // clear to black
        TextOutW(s_dc, 0, 0, text, 1);
        GdiFlush();
        Marshal.Copy(s_bits, pixels, 0, pixels.Length);
        GetTextExtentPoint32W(s_dc, text, 1, out var extent);

        int width = Math.Clamp(extent.cx, 1, Cell);
        var bits = new bool[width * s_height];
        for (int y = 0; y < s_height; y++)
        {
            for (int x = 0; x < width; x++)
                bits[y * width + x] = (pixels[y * Cell + x] & 0xFFFFFF) != 0;
        }
        return new PixelMask(width, s_height, bits);
    }

    private static bool EnsureContext()
    {
        if (s_dc != 0 || s_failed)
            return !s_failed;
        nint dc = CreateCompatibleDC(0);
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = Cell,
            biHeight = -Cell, // top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        nint bitmap = dc == 0 ? 0 : CreateDIBSection(dc, ref header, 0, out s_bits, 0, 0);
        nint font = CreateFontW(-EmSize, 0, 0, 0, 400, 0, 0, 0, 1 /* DEFAULT_CHARSET */, 0, 0, 3 /* NONANTIALIASED_QUALITY */, 0, FaceName);
        if (bitmap == 0 || font == 0)
        {
            s_failed = true;
            return false;
        }
        // The DC, bitmap and font live as long as the app.
        SelectObject(dc, bitmap);
        SelectObject(dc, font);
        SetTextColor(dc, 0xFFFFFF);
        SetBkMode(dc, 1 /* TRANSPARENT */);
        if (GetTextMetricsW(dc, out var metrics))
            s_height = Math.Clamp(metrics.tmHeight, 1, Cell);
        s_dc = dc;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TEXTMETRICW
    {
        public int tmHeight;
        public int tmAscent;
        public int tmDescent;
        public int tmInternalLeading;
        public int tmExternalLeading;
        public int tmAveCharWidth;
        public int tmMaxCharWidth;
        public int tmWeight;
        public int tmOverhang;
        public int tmDigitizedAspectX;
        public int tmDigitizedAspectY;
        public char tmFirstChar;
        public char tmLastChar;
        public char tmDefaultChar;
        public char tmBreakChar;
        public byte tmItalic;
        public byte tmUnderlined;
        public byte tmStruckOut;
        public byte tmPitchAndFamily;
        public byte tmCharSet;
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint hdc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
        uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(nint hdc, uint color);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(nint hdc, int mode);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextMetricsW(nint hdc, out TEXTMETRICW metrics);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TextOutW(nint hdc, int x, int y, string text, int length);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextExtentPoint32W(nint hdc, string text, int length, out SIZE size);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GdiFlush();
}
