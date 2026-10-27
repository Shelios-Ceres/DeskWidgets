using DeskWidgets.Core.Devices;
using DeskWidgets.Motion;
using DeskWidgets.Pixel;
using DeskWidgets.PixelUi;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskWidgets.Widgets.Battery;

/// <summary>
/// Battery levels of wireless devices, laid out like Apple's Batteries widget:
/// small = one big ring (or a 2×2 grid), medium = a row of four rings with
/// names and percentages, large = a list. The pixel theme draws the same
/// layouts as pixel art (<see cref="BatteryPixelScene"/>).
/// </summary>
internal sealed class BatteryWidget : UserControl, IWidget
{
    private const string ShowDisconnectedKey = "showDisconnected";
    private const string ShowNamesKey = "showNames";
    private const int RingSlots = 4;
    private const int ListRows = 6;

    private readonly DeviceHub _hub;
    private readonly DispatcherQueue _dispatcher;
    private readonly WidgetOptions _options;
    private readonly Grid _root = new();
    private readonly Dictionary<(string Id, double Diameter), BatteryRing> _rings = new();
    private readonly FontFamily _displayFont = new("Segoe UI Variable Display");
    private readonly FontFamily _textFont = new("Segoe UI Variable Text");
    private readonly PixelSurface? _pixel;
    private WidgetSize _size = WidgetSize.Medium;
    private int _renderQueued;
    private readonly BatteryMotion? _motion; // Pixel B6: the gauge's step and the bolt's pop
    private readonly Dictionary<string, bool> _charged = []; // Glass B6: who showed a bolt in the last render, so a change can fade it
    private bool _rendered;

    public BatteryWidget(WidgetContext context)
    {
        _hub = context.Services.Devices;
        _dispatcher = context.Services.Dispatcher;
        _options = context.Options;
        if (context.Theme == WidgetTheme.Pixel)
        {
            _motion = new BatteryMotion();
            Content = _pixel = new PixelSurface((canvas, palette) => BatteryPixelScene.Draw(canvas, palette, _size, VisibleDevices(), ShowNames, _motion));
        }
        else
            Content = _root;
        _hub.DevicesChanged += OnDevicesChanged;
        ActualThemeChanged += (_, _) => Render();
    }

    public FrameworkElement View => this;

    public void ApplySize(WidgetSize size)
    {
        _size = size;
        var (width, height) = PixelCard.ArtSize(size);
        _pixel?.SetArtSize(width, height);
        Render();
    }

    public IEnumerable<MenuEntry> CreateMenuItems()
    {
        bool showDisconnected = _options.GetBool(ShowDisconnectedKey);
        return
        [
            MenuEntry.Toggle(Strings.ShowDeviceNames, ShowNames, () =>
            {
                _options.SetBool(ShowNamesKey, !ShowNames);
                Render();
            }),
            MenuEntry.Toggle(Strings.ShowDisconnected, showDisconnected, () =>
            {
                _options.SetBool(ShowDisconnectedKey, !showDisconnected);
                Render();
            }),
            MenuEntry.Item(Strings.RefreshDevices, _hub.RefreshAll, glyph: "", icon: PixelIcons.Refresh),
        ];
    }

    private bool ShowNames => _options.GetBool(ShowNamesKey, fallback: true);

    public void ApplyLanguage() => Render();

    public void Dispose()
    {
        _hub.DevicesChanged -= OnDevicesChanged;
        _motion?.Dispose();
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        // Raised on a background thread, possibly in bursts: render once on the UI thread.
        if (Interlocked.Exchange(ref _renderQueued, 1) == 1)
            return;
        _dispatcher.TryEnqueue(() =>
        {
            Volatile.Write(ref _renderQueued, 0);
            Render();
        });
    }

    private List<DeviceStatus> VisibleDevices()
    {
        bool showDisconnected = _options.GetBool(ShowDisconnectedKey);
        return _hub.Devices
            .Where(d => d.IsConnected ? d.BatteryPercent is not null : showDisconnected)
            .OrderByDescending(d => d.IsHost)
            .ThenByDescending(d => d.IsConnected)
            .ThenBy(d => KindOrder(d.Kind))
            .ThenBy(d => d.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static int KindOrder(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => 0,
        DeviceKind.Keyboard => 1,
        DeviceKind.Headphones or DeviceKind.Headset or DeviceKind.Earbuds => 2,
        DeviceKind.GameController => 3,
        _ => 4,
    };

    private void Render()
    {
        if (_pixel is not null)
        {
            if (_motion is not null && _motion.Observe(VisibleDevices(), _pixel.RenderNow)) // the first look only notes the devices
                return; // the motion paints its own first frame
            _rendered = true;
            _pixel.Invalidate();
            return;
        }
        var palette = GlassPalette.For(ActualTheme);
        var devices = VisibleDevices();

        // Rings are reused across renders so their levels animate; detach them first.
        foreach (var ring in _rings.Values)
        {
            if (ring.Parent is Panel parent)
                parent.Children.Remove(ring);
        }
        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _root.ColumnDefinitions.Clear();
        _root.RowSpacing = 0;
        _root.ColumnSpacing = 0;

        if (devices.Count == 0)
            RenderEmpty(palette);
        else if (_size == WidgetSize.Small && devices.Count == 1)
            RenderSingle(devices[0], palette);
        else if (_size == WidgetSize.Small)
            RenderRingGrid(devices, palette);
        else if (_size == WidgetSize.Medium)
            RenderRingRow(devices, palette);
        else
            RenderList(devices, palette);

        var visible = devices.Select(d => d.Id).ToHashSet();
        foreach (var key in _rings.Keys.Where(k => !visible.Contains(k.Id)).ToList())
            _rings.Remove(key);
        _rendered = true;
    }

    /// <summary>Small, one device: ring (and its name) top-left, big percentage bottom-left.</summary>
    private void RenderSingle(DeviceStatus device, GlassPalette palette)
    {
        _root.Padding = new Thickness(18);
        bool names = ShowNames;
        var header = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var ring = Ring(device, names ? 56 : 62, names ? 6 : 6.5, palette, chargingBadge: false);
        ring.HorizontalAlignment = HorizontalAlignment.Left;
        header.Children.Add(ring);
        if (names)
            header.Children.Add(NameText(device, 12, palette, maxLines: 1, centered: false));
        var percent = PercentLine(device, 36, palette, _displayFont);
        percent.HorizontalAlignment = HorizontalAlignment.Left;
        percent.VerticalAlignment = VerticalAlignment.Bottom;
        percent.Margin = new Thickness(0, 0, 0, -6);
        _root.Children.Add(header);
        _root.Children.Add(percent);
    }

    /// <summary>Small, several devices: 2×2 rings with charging badges, no text.</summary>
    private void RenderRingGrid(IReadOnlyList<DeviceStatus> devices, GlassPalette palette)
    {
        _root.Padding = new Thickness(16);
        _root.RowSpacing = 10;
        _root.ColumnSpacing = 10;
        for (int i = 0; i < 2; i++)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (int slot = 0; slot < RingSlots; slot++)
        {
            var ring = slot < devices.Count ? Ring(devices[slot], 54, 5.5, palette, chargingBadge: true) : Placeholder(54, 5.5, palette);
            ring.HorizontalAlignment = HorizontalAlignment.Center;
            ring.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(ring, slot / 2);
            Grid.SetColumn(ring, slot % 2);
            _root.Children.Add(ring);
        }
    }

    /// <summary>
    /// Medium: four rings in a row, each with its name and percentage below.
    /// One shared table keeps the rows aligned across columns even when a
    /// long name wraps onto a second line.
    /// </summary>
    private void RenderRingRow(IReadOnlyList<DeviceStatus> devices, GlassPalette palette)
    {
        _root.Padding = new Thickness(10, 0, 10, 0);
        bool names = ShowNames;
        double diameter = names ? 54 : 60, thickness = names ? 5.5 : 6;
        var table = new Grid { VerticalAlignment = VerticalAlignment.Center };
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // rings
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // names
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // percentages

        for (int slot = 0; slot < RingSlots; slot++)
        {
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var device = slot < devices.Count ? devices[slot] : null;

            var ring = device is null ? Placeholder(diameter, thickness, palette) : Ring(device, diameter, thickness, palette, chargingBadge: false);
            ring.HorizontalAlignment = HorizontalAlignment.Center;
            AddCell(table, ring, row: 0, column: slot);
            if (device is null)
                continue;

            if (names)
            {
                var name = NameText(device, 11, palette, maxLines: 2, centered: true);
                name.Margin = new Thickness(2, 6, 2, 0);
                AddCell(table, name, row: 1, column: slot);
            }
            var percent = PercentLine(device, 15, palette, _textFont);
            percent.HorizontalAlignment = HorizontalAlignment.Center;
            percent.Margin = new Thickness(0, names ? 3 : 10, 0, 0);
            AddCell(table, percent, row: 2, column: slot);
        }
        _root.Children.Add(table);
    }

    private static void AddCell(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private TextBlock NameText(DeviceStatus device, double fontSize, GlassPalette palette, int maxLines, bool centered) => new()
    {
        Text = device.IsHost ? Strings.ThisPc : device.Name,
        FontFamily = _textFont,
        FontSize = fontSize,
        Foreground = palette.Secondary,
        TextWrapping = maxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = maxLines,
        TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
        HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
    };

    /// <summary>Large: one row per device with name, connection and percentage.</summary>
    private void RenderList(IReadOnlyList<DeviceStatus> devices, GlassPalette palette)
    {
        _root.Padding = new Thickness(18, 16, 18, 16);
        var list = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top }; // six 48 rows end at y 344 of 364
        foreach (var device in devices.Take(ListRows))
        {
            var row = new Grid { Height = 48, ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var ring = Ring(device, 40, 4.5, palette, chargingBadge: false);
            ring.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(ring);

            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            labels.Children.Add(new TextBlock
            {
                Text = device.IsHost ? Strings.ThisPc : device.Name,
                FontFamily = _textFont,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.Primary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });
            labels.Children.Add(new TextBlock
            {
                Text = Describe(device),
                FontFamily = _textFont,
                FontSize = 12,
                Foreground = palette.Secondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);

            var percent = PercentLine(device, 17, palette, _displayFont);
            Grid.SetColumn(percent, 2);
            row.Children.Add(percent);

            list.Children.Add(row);
        }
        _root.Children.Add(list);
    }

    private void RenderEmpty(GlassPalette palette)
    {
        _root.Padding = new Thickness(16);
        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new FontIcon { Glyph = "", FontSize = 26, Foreground = palette.Secondary });
        stack.Children.Add(new TextBlock
        {
            Text = Strings.NoDevices,
            FontFamily = _textFont,
            FontSize = 13,
            Foreground = palette.Secondary,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        _root.Children.Add(stack);
    }

    private BatteryRing Ring(DeviceStatus device, double diameter, double thickness, GlassPalette palette, bool chargingBadge)
    {
        if (!_rings.TryGetValue((device.Id, diameter), out var ring))
            _rings[(device.Id, diameter)] = ring = new BatteryRing(diameter, thickness);
        ring.Update(device, palette, chargingBadge);
        return ring;
    }

    private static BatteryRing Placeholder(double diameter, double thickness, GlassPalette palette)
    {
        var ring = new BatteryRing(diameter, thickness);
        ring.Update(null, palette, chargingBadge: false);
        return ring;
    }

    /// <summary>"76%" plus a bolt while charging; an em dash when the level is unknown.</summary>
    private StackPanel PercentLine(DeviceStatus device, double fontSize, GlassPalette palette, FontFamily font)
    {
        bool known = device.IsConnected && device.BatteryPercent is not null;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Math.Round(fontSize * 0.12), VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(new TextBlock
        {
            Text = known ? $"{device.BatteryPercent}%" : "—",
            FontFamily = font,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = known ? palette.Primary : palette.Inactive,
        });
        bool charging = known && device.IsCharging;
        _charged.TryGetValue(device.Id, out bool was);
        _charged[device.Id] = charging;
        bool change = _rendered && was != charging && MotionSettings.Enabled;
        if (charging || (change && was))
        {
            var bolt = new FontIcon
            {
                Glyph = "",
                FontSize = Math.Round(fontSize * 0.62),
                Foreground = palette.Primary,
                VerticalAlignment = VerticalAlignment.Center,
            };
            line.Children.Add(bolt);
            if (change && charging)
            {
                // B6: the bolt fades in (Quick).
                GlassMotion.Preset(bolt, 0, 1, default);
                GlassMotion.FadeTo(bolt, 1, GlassMotion.Quick, GlassEase.Out);
            }
            else if (change)
            {
                // B6: charging stopped: the bolt fades out (Exit), then gives its room back.
                GlassMotion.FadeTo(bolt, 0, GlassMotion.Exit, GlassEase.In);
                FrameClock.After((int)GlassMotion.LeaveDuration.TotalMilliseconds, () => line.Children.Remove(bolt));
            }
        }
        return line;
    }

    /// <summary>"Bluetooth · Charging", "2.4G · 1600 DPI": connection, then state or detail.</summary>
    internal static string Describe(DeviceStatus device)
    {
        string connection = device.IsHost
            ? Strings.BuiltIn
            : device.Connection switch
            {
                ConnectionKind.Bluetooth or ConnectionKind.BluetoothLE => Strings.Bluetooth,
                ConnectionKind.Wireless24G => "2.4G",
                ConnectionKind.Usb => "USB",
                _ => string.Empty,
            };
        string state = !device.IsConnected ? Strings.Disconnected
            : device.IsCharging ? (device.IsHost ? Strings.PluggedIn : Strings.Charging)
            : device.Detail ?? string.Empty;
        return string.Join(" · ", new[] { connection, state }.Where(s => s.Length > 0));
    }
}
