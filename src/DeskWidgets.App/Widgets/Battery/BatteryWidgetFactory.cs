namespace DeskWidgets.Widgets.Battery;

internal sealed class BatteryWidgetFactory : IWidgetFactory
{
    public string TypeId => "battery";

    public string DisplayName => Strings.BatteryWidget;

    public string Glyph => "";

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium, WidgetSize.Large];

    public WidgetSize DefaultSize => WidgetSize.Medium;

    public IWidget Create(WidgetContext context) => new BatteryWidget(context);
}
