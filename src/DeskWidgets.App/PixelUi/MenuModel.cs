using DeskWidgets.Pixel;

namespace DeskWidgets.PixelUi;

internal enum MenuKind
{
    Item,
    Check,
    Radio,
    Submenu,
    Separator,

    /// <summary>A greyed line of information, not focusable (the tray's icon-arranging status).</summary>
    Status,
}

/// <summary>
/// One row of a menu, independent of how the menu is drawn: the Glass theme turns the rows into WinUI
/// flyout items (or a Win32 menu for the tray), the Pixel theme draws them on its own panels.
/// </summary>
internal sealed class MenuEntry
{
    public string Text { get; init; } = "";

    public MenuKind Kind { get; init; }

    public bool Checked { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>What picking the row does. A toggle or radio row recomputes its state from its owner's own.</summary>
    public Action? Click { get; init; }

    public IReadOnlyList<MenuEntry> Children { get; init; } = [];

    /// <summary>Segoe Fluent Icons glyph (Glass); null for none.</summary>
    public string? Glyph { get; init; }

    /// <summary>7×7 sprite (Pixel); null for none.</summary>
    public PixelSprite? Icon { get; init; }

    /// <summary>Radio rows of one group exclude each other (Glass's flyout needs the name); null picks one per submenu.</summary>
    public string? Group { get; init; }

    /// <summary>Whether the keyboard and the pointer can land on this row.</summary>
    public bool Focusable => Enabled && Kind is not (MenuKind.Separator or MenuKind.Status);

    public static MenuEntry Separator() => new() { Kind = MenuKind.Separator, Enabled = false };

    public static MenuEntry Status(string text) => new() { Text = text, Kind = MenuKind.Status, Enabled = false };

    public static MenuEntry Item(string text, Action click, string? glyph = null, PixelSprite? icon = null)
        => new() { Text = text, Kind = MenuKind.Item, Click = click, Glyph = glyph, Icon = icon };

    public static MenuEntry Toggle(string text, bool isChecked, Action click)
        => new() { Text = text, Kind = MenuKind.Check, Checked = isChecked, Click = click };

    public static MenuEntry Radio(string text, bool isChecked, Action click, string? group = null)
        => new() { Text = text, Kind = MenuKind.Radio, Checked = isChecked, Click = click, Group = group };

    public static MenuEntry Sub(string text, IReadOnlyList<MenuEntry> children, string? glyph = null, PixelSprite? icon = null)
        => new() { Text = text, Kind = MenuKind.Submenu, Children = children, Glyph = glyph, Icon = icon };
}

/// <summary>Keyboard navigation over a menu's rows (plain logic, no window).</summary>
internal static class MenuNav
{
    public static int First(IReadOnlyList<MenuEntry> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Focusable)
                return i;
        }
        return -1;
    }

    public static int Last(IReadOnlyList<MenuEntry> items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].Focusable)
                return i;
        }
        return -1;
    }

    /// <summary>The next focusable row after <paramref name="current"/> in <paramref name="direction"/> (+1 down, -1 up), wrapping round; -1 when none.</summary>
    public static int Step(IReadOnlyList<MenuEntry> items, int current, int direction)
    {
        if (current < 0 || current >= items.Count)
            return direction > 0 ? First(items) : Last(items);
        for (int step = 1; step <= items.Count; step++)
        {
            int index = ((current + direction * step) % items.Count + items.Count) % items.Count;
            if (items[index].Focusable)
                return index;
        }
        return -1;
    }

    /// <summary>The row a submenu opens on: its checked radio row if it has one, else its first focusable row; -1 when none.</summary>
    public static int Entry(IReadOnlyList<MenuEntry> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == MenuKind.Radio && items[i].Checked && items[i].Focusable)
                return i;
        }
        return First(items);
    }
}
