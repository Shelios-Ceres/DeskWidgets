using DeskWidgets.PixelUi;
using Microsoft.UI.Xaml.Controls;

namespace DeskWidgets.Hosting;

/// <summary>Turns a <see cref="MenuEntry"/> list into WinUI flyout items: the Glass theme's menus.</summary>
internal static class FlyoutMenuAdapter
{
    private static int s_groups;

    public static List<MenuFlyoutItemBase> Build(IReadOnlyList<MenuEntry> entries)
    {
        string defaultGroup = "menu" + ++s_groups; // radio rows without a group name exclude each other within their own list
        var items = new List<MenuFlyoutItemBase>();
        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                case MenuKind.Separator:
                    items.Add(new MenuFlyoutSeparator());
                    break;
                case MenuKind.Submenu:
                {
                    var sub = new MenuFlyoutSubItem { Text = entry.Text, Icon = IconFor(entry) };
                    foreach (var child in Build(entry.Children))
                        sub.Items.Add(child);
                    items.Add(sub);
                    break;
                }
                case MenuKind.Check:
                {
                    var item = new ToggleMenuFlyoutItem { Text = entry.Text, IsChecked = entry.Checked, IsEnabled = entry.Enabled };
                    item.Click += (_, _) => entry.Click?.Invoke();
                    items.Add(item);
                    break;
                }
                case MenuKind.Radio:
                {
                    var item = new RadioMenuFlyoutItem { Text = entry.Text, GroupName = entry.Group ?? defaultGroup, IsChecked = entry.Checked, IsEnabled = entry.Enabled };
                    item.Click += (_, _) => entry.Click?.Invoke();
                    items.Add(item);
                    break;
                }
                default:
                {
                    var item = new MenuFlyoutItem { Text = entry.Text, Icon = IconFor(entry), IsEnabled = entry.Enabled && entry.Kind != MenuKind.Status };
                    item.Click += (_, _) => entry.Click?.Invoke();
                    items.Add(item);
                    break;
                }
            }
        }
        return items;
    }

    private static FontIcon? IconFor(MenuEntry entry) => entry.Glyph is { } glyph ? new FontIcon { Glyph = glyph } : null;
}
