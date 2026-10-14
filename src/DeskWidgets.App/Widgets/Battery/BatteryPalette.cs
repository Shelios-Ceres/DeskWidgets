using DeskWidgets.Core.Devices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DeskWidgets.Widgets.Battery;

/// <summary>Apple system colours for the battery widget, per theme.</summary>
internal sealed class BatteryPalette
{
    private static BatteryPalette? s_light;
    private static BatteryPalette? s_dark;

    public required SolidColorBrush Track { get; init; }
    public required SolidColorBrush Placeholder { get; init; }
    public required SolidColorBrush Green { get; init; }
    public required SolidColorBrush Yellow { get; init; }
    public required SolidColorBrush Red { get; init; }
    public required SolidColorBrush Primary { get; init; }
    public required SolidColorBrush Secondary { get; init; }
    public required SolidColorBrush OnAccent { get; init; }

    public static BatteryPalette For(ElementTheme theme)
        => theme == ElementTheme.Dark ? (s_dark ??= CreateDark()) : (s_light ??= CreateLight());

    /// <summary>Red at 20% or below (unless charging), yellow in power-saving mode, green otherwise — like iOS.</summary>
    public SolidColorBrush LevelBrush(DeviceStatus device)
    {
        if (device.BatteryPercent is int percent && percent <= 20 && !device.IsCharging)
            return Red;
        return device.IsPowerSaving ? Yellow : Green;
    }

    private static BatteryPalette CreateLight() => new()
    {
        Track = Brush(0x24, 0x00, 0x00, 0x00),
        Placeholder = Brush(0x14, 0x00, 0x00, 0x00),
        Green = Brush(0xFF, 0x34, 0xC7, 0x59),
        Yellow = Brush(0xFF, 0xFF, 0xCC, 0x00),
        Red = Brush(0xFF, 0xFF, 0x3B, 0x30),
        Primary = Brush(0xFF, 0x1C, 0x1C, 0x1E),
        Secondary = Brush(0x99, 0x3C, 0x3C, 0x43),
        OnAccent = Brush(0xFF, 0xFF, 0xFF, 0xFF),
    };

    private static BatteryPalette CreateDark() => new()
    {
        Track = Brush(0x3D, 0xFF, 0xFF, 0xFF),
        Placeholder = Brush(0x1F, 0xFF, 0xFF, 0xFF),
        Green = Brush(0xFF, 0x30, 0xD1, 0x58),
        Yellow = Brush(0xFF, 0xFF, 0xD6, 0x0A),
        Red = Brush(0xFF, 0xFF, 0x45, 0x3A),
        Primary = Brush(0xFF, 0xFF, 0xFF, 0xFF),
        Secondary = Brush(0x99, 0xEB, 0xEB, 0xF5),
        OnAccent = Brush(0xFF, 0x00, 0x00, 0x00),
    };

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) => new(ColorHelper.FromArgb(a, r, g, b));
}
