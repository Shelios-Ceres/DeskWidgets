using System.Runtime.InteropServices;

namespace DeskWidgets.Devices.Hid;

/// <summary>One HID top-level collection and its capabilities.</summary>
public sealed record HidInterface
{
    public required string Path { get; init; }
    public ushort VendorId { get; init; }
    public ushort ProductId { get; init; }
    public string? Manufacturer { get; init; }
    public string? Product { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public int InputReportLength { get; init; }
    public int OutputReportLength { get; init; }

    /// <summary>Includes the leading report ID byte (65 for a 64-byte vendor frame).</summary>
    public int FeatureReportLength { get; init; }
}

public static class HidInventory
{
    /// <summary>
    /// Lists present HID collections, optionally only those whose VID/PID pass
    /// <paramref name="filter"/>. Every handle is opened query-only, so this
    /// never sends anything to a device.
    /// </summary>
    public static IReadOnlyList<HidInterface> Enumerate(Func<ushort, ushort, bool>? filter = null)
    {
        var result = new List<HidInterface>();
        foreach (string path in HidNative.EnumerateDevicePaths())
        {
            using var handle = HidNative.OpenQueryOnly(path);
            if (handle is null)
                continue;
            var attributes = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
            if (!HidNative.HidD_GetAttributes(handle, ref attributes))
                continue;
            if (filter is not null && !filter(attributes.VendorID, attributes.ProductID))
                continue;

            var caps = HidNative.GetCaps(handle);
            result.Add(new HidInterface
            {
                Path = path,
                VendorId = attributes.VendorID,
                ProductId = attributes.ProductID,
                Manufacturer = HidNative.GetManufacturerString(handle),
                Product = HidNative.GetProductString(handle),
                UsagePage = caps?.UsagePage ?? 0,
                Usage = caps?.Usage ?? 0,
                InputReportLength = caps?.InputReportByteLength ?? 0,
                OutputReportLength = caps?.OutputReportByteLength ?? 0,
                FeatureReportLength = caps?.FeatureReportByteLength ?? 0,
            });
        }
        return result;
    }
}
