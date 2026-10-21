using DeskWidgets.Widgets.Battery;
using DeskWidgets.Widgets.Clock;
using DeskWidgets.Widgets.Folder;
using DeskWidgets.Widgets.WorldClock;

namespace DeskWidgets.Widgets;

/// <summary>Every widget type the app offers. Add new widget factories here.</summary>
internal static class WidgetCatalog
{
    public static IReadOnlyList<IWidgetFactory> All { get; } =
    [
        new BatteryWidgetFactory(),
        new ClockWidgetFactory(),
        new WorldClockWidgetFactory(),
        new FolderWidgetFactory(),
    ];

    public static IWidgetFactory? Find(string typeId)
        => All.FirstOrDefault(f => f.TypeId.Equals(typeId, StringComparison.OrdinalIgnoreCase));
}
