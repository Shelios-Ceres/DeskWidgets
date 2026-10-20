using DeskWidgets.Interop;
using DeskWidgets.PixelUi;

namespace DeskWidgets.Hosting;

/// <summary>Turns a <see cref="MenuEntry"/> list into a native popup menu: the Glass theme's tray menu.</summary>
internal static class Win32MenuAdapter
{
    /// <summary>Builds the menu; <paramref name="actions"/> maps each command id TrackPopupMenu can return to what it does.</summary>
    public static nint Build(IReadOnlyList<MenuEntry> entries, out Dictionary<uint, Action> actions)
    {
        actions = [];
        uint next = 1;
        return Build(entries, actions, ref next);
    }

    private static nint Build(IReadOnlyList<MenuEntry> entries, Dictionary<uint, Action> actions, ref uint next)
    {
        nint menu = Win32.CreatePopupMenu();
        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                case MenuKind.Separator:
                    Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
                    break;
                case MenuKind.Submenu:
                {
                    nint sub = Build(entry.Children, actions, ref next);
                    Win32.AppendMenu(menu, Win32.MF_POPUP, (nuint)sub, entry.Text);
                    break;
                }
                default:
                {
                    uint flags = Win32.MF_STRING;
                    if (entry.Checked)
                        flags |= Win32.MF_CHECKED;
                    if (!entry.Enabled || entry.Kind == MenuKind.Status)
                        flags |= Win32.MF_GRAYED;
                    uint id = next++;
                    if (entry.Click is { } click)
                        actions[id] = click;
                    Win32.AppendMenu(menu, flags, id, entry.Text);
                    break;
                }
            }
        }
        return menu;
    }
}
