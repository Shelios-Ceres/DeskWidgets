using DeskWidgets.Core.Devices;

namespace DeskWidgets.Widgets.Battery;

/// <summary>Segoe Fluent Icons glyph for each kind of device.</summary>
internal static class DeviceGlyphs
{
    public static string For(DeviceStatus device) => device.Kind switch
    {
        DeviceKind.Laptop => "",
        DeviceKind.Desktop => "",
        DeviceKind.Mouse => "",
        DeviceKind.Keyboard => "",
        DeviceKind.Headphones or DeviceKind.Earbuds => "",
        DeviceKind.Headset => "",
        DeviceKind.Speaker => "",
        DeviceKind.GameController => "",
        DeviceKind.Phone => "",
        DeviceKind.Tablet => "",
        DeviceKind.Pen => "",
        DeviceKind.Watch => "",
        _ => "", // Bluetooth
    };
}
