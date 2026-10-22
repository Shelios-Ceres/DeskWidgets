using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace DeskWidgets.Pixel;

/// <summary>Renders one emoji with the system's colour emoji font (Segoe UI Emoji, DirectWrite colour glyphs) at 64 × 64, offscreen.</summary>
internal static class EmojiRenderer
{
    /// <summary>Straight-alpha BGRA, 64 × 64, rows top to bottom; null when rendering fails or draws nothing.</summary>
    public static byte[]? Render(string emoji)
    {
        int size = PixelEmoji.SourceSize;
        var device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, size, size, 96f);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(0, 0, 0, 0));
            var format = new CanvasTextFormat
            {
                FontFamily = "Segoe UI Emoji",
                FontSize = 56,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                VerticalAlignment = CanvasVerticalAlignment.Center,
                WordWrapping = CanvasWordWrapping.NoWrap,
                Options = CanvasDrawTextOptions.EnableColorFont,
            };
            session.DrawText(emoji, new Rect(0, 0, size, size), Color.FromArgb(255, 0, 0, 0), format);
        }
        byte[] bytes = target.GetPixelBytes(); // premultiplied BGRA
        bool ink = false;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            int alpha = bytes[i + 3];
            if (alpha == 0 || alpha == 255)
            {
                ink |= alpha == 255;
                continue;
            }
            ink = true;
            bytes[i] = (byte)Math.Min(255, bytes[i] * 255 / alpha); // straight alpha
            bytes[i + 1] = (byte)Math.Min(255, bytes[i + 1] * 255 / alpha);
            bytes[i + 2] = (byte)Math.Min(255, bytes[i + 2] * 255 / alpha);
        }
        return ink ? bytes : null;
    }
}
