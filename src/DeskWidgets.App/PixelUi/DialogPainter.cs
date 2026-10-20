using DeskWidgets.Pixel;

namespace DeskWidgets.PixelUi;

internal enum DialogBadge
{
    Question,
    Warning,
}

internal enum ButtonLook
{
    Normal,
    Hover,
    Pressed,
    Disabled,
}

/// <summary>One dialog button: its label and whether it is the primary (default) one.</summary>
internal readonly record struct DialogButtonSpec(string Text, bool Primary);

/// <summary>The geometry of a pixel dialog in art pixels: sizes from its content, no drawing.</summary>
internal sealed class DialogLayout
{
    public const int Width = 172; // the large card's width
    public const int Pad = 7;     // content from the panel's outer edge, as on the cards
    public const int BadgeSize = 13;
    public const int ButtonGap = 6; // room for both buttons' focus rings
    public const int ButtonMinWidth = 36;
    public const int ShadowOffset = 2;

    private DialogLayout()
    {
    }

    public required string Title { get; init; }

    public required PixelText TitleText { get; init; }

    public required int TitleHeight { get; init; }

    public PixelText? Heading { get; init; }

    public required PixelText Body { get; init; }

    public required PixelText[] Labels { get; init; }

    /// <summary>Button rectangles (x, y, width, height) inside the panel.</summary>
    public required (int X, int Y, int W, int H)[] Buttons { get; init; }

    public required int SeparatorY { get; init; }

    public required int ContentY { get; init; }

    public required int TextX { get; init; }

    public required int Height { get; init; }

    public static (int W, int H) ButtonSize(PixelText label) => (Math.Max(ButtonMinWidth, label.Width + 16), label.IsFallback ? 18 : label.Height + 10);

    public static DialogLayout Create(string title, string? heading, string body, IReadOnlyList<DialogButtonSpec> buttons)
    {
        var titleText = PixelText.Layout(title, PixelFonts.Small);
        int titleHeight = titleText.IsFallback ? 16 : 13;
        int textX = Pad + BadgeSize + 6;
        int textWidth = Width - textX - Pad;
        var head = heading is null ? null : PixelText.Layout(heading, PixelFonts.Regular, textWidth, 3);
        var text = PixelText.Layout(body, PixelFonts.Regular, textWidth, 8);
        var labels = buttons.Select(b => PixelText.Layout(b.Text, PixelFonts.Regular)).ToArray();
        var sizes = labels.Select(ButtonSize).ToArray();
        int buttonHeight = sizes.Max(s => s.H);

        int separatorY = 2 + titleHeight;
        int contentY = separatorY + 7;
        int textBottom = contentY + (head is null ? 0 : head.Height + 5) + text.Height;
        int buttonsY = Math.Max(textBottom, contentY + BadgeSize) + 9;
        int height = buttonsY + buttonHeight + Pad;

        var rects = new (int, int, int, int)[buttons.Count];
        int x = Width - Pad;
        for (int i = buttons.Count - 1; i >= 0; i--)
        {
            x -= sizes[i].W;
            rects[i] = (x, buttonsY, sizes[i].W, buttonHeight);
            x -= ButtonGap;
        }
        return new DialogLayout
        {
            Title = title,
            TitleText = titleText,
            TitleHeight = titleHeight,
            Heading = head,
            Body = text,
            Labels = labels,
            Buttons = rects,
            SeparatorY = separatorY,
            ContentY = contentY,
            TextX = textX,
            Height = height,
        };
    }
}

/// <summary>What a dialog is showing: which button the pointer is over or holds down, and which has the keyboard focus.</summary>
internal readonly record struct DialogPaint(DialogBadge Badge, bool BodyIsDetail, IReadOnlyList<DialogButtonSpec> Buttons, int Hot, int Pressed, int Focus);

internal static class DialogPainter
{
    public static PixelCanvas Draw(PixelPalette p, DialogLayout layout, DialogPaint state, bool dark)
    {
        var c = new PixelCanvas(DialogLayout.Width, layout.Height);
        PixelFrame.Draw(c, 0, 0, DialogLayout.Width, layout.Height, PixelFrame.DialogCorner, p.Outline, p.Panel, p.Highlight, p.Shade, 2);

        // Title strip: the app's mark and name, then a dotted rule (dragging it moves the dialog).
        c.Draw(PixelIcons.AppMark, DialogLayout.Pad, 2 + (layout.TitleHeight - 7) / 2, ch => ch switch { 'g' => p.Faint, 'o' => p.Ink, _ => p.Green });
        layout.TitleText.Draw(c, DialogLayout.Pad + 11, 2 + (layout.TitleHeight - layout.TitleText.Height) / 2, p.Soft);
        c.DottedLine(DialogLayout.Pad, layout.SeparatorY, DialogLayout.Width - 2 * DialogLayout.Pad, p.Faint);

        if (state.Badge == DialogBadge.Question)
        {
            c.FillCircle(DialogLayout.Pad + 6, layout.ContentY + 6, 6, p.Select);
            PixelText.Layout("?", PixelFonts.Regular).Draw(c, DialogLayout.Pad + 4, layout.ContentY + 3, p.OnSelect);
        }
        else
        {
            c.Draw(PixelIcons.Warning, DialogLayout.Pad, layout.ContentY, ch => ch switch { '+' => p.Yellow, 'x' => 0xFF1A1C2C, _ => dark ? 0xFF0B0C14 : p.Outline });
        }

        int y = layout.ContentY;
        if (layout.Heading is { } heading)
        {
            heading.Draw(c, layout.TextX, y, p.Ink);
            y += heading.Height + 5;
        }
        layout.Body.Draw(c, layout.TextX, y, state.BodyIsDetail ? p.Soft : p.Ink);

        for (int i = 0; i < state.Buttons.Count; i++)
        {
            var (x, by, w, h) = layout.Buttons[i];
            var look = i == state.Pressed ? ButtonLook.Pressed : i == state.Hot ? ButtonLook.Hover : ButtonLook.Normal;
            Button(c, p, x, by, w, h, layout.Labels[i], state.Buttons[i].Primary, look, i == state.Focus);
        }
        return c;
    }

    public static void Button(PixelCanvas c, PixelPalette p, int x, int y, int w, int h, PixelText label, bool primary, ButtonLook look, bool focus)
    {
        bool pressed = look == ButtonLook.Pressed;
        uint outline = look == ButtonLook.Disabled ? p.Faint : p.Outline;
        uint fill, top, bottom, text;
        if (look == ButtonLook.Disabled)
        {
            (fill, top, bottom, text) = (p.Panel, 0u, 0u, p.Faint);
        }
        else if (primary)
        {
            fill = look == ButtonLook.Normal ? p.Select : p.SelectPressed;
            top = pressed ? 0xFF1A1C2Cu : look == ButtonLook.Hover ? p.Select : p.SelectLight;
            bottom = pressed ? 0u : p.SelectPressed == fill ? 0xFF1A1C2Cu : p.SelectPressed;
            text = p.OnSelect;
        }
        else
        {
            fill = look switch { ButtonLook.Hover => p.ButtonHover, ButtonLook.Pressed => p.ButtonPressed, _ => p.Button };
            top = pressed ? p.Outline : p.ButtonTop;
            bottom = pressed ? 0u : p.Shade;
            text = p.Ink;
        }
        PixelFrame.Draw(c, x, y, w, h, PixelFrame.ButtonCorner, outline, fill, top, bottom == 0 ? fill : bottom, bottom == 0 ? 0 : 1);
        label.Draw(c, x + (w - label.Width) / 2, y + (h - label.Height) / 2 + (pressed ? 1 : 0), text);
        if (focus)
            PixelFrame.FocusRing(c, x, y, w, h, p.Ink);
    }
}
