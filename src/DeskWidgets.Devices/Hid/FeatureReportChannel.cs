using Microsoft.Win32.SafeHandles;

namespace DeskWidgets.Devices.Hid;

/// <summary>
/// Request/response over HID feature reports on one collection:
/// SetFeature(request) → delay → GetFeature. Windows buffers carry the report
/// ID in byte 0; requests are sent with report ID 0 and the ID byte is
/// stripped from the reply, so callers work with the raw vendor frame.
/// </summary>
internal sealed class FeatureReportChannel : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly int _reportLength;

    private FeatureReportChannel(SafeFileHandle handle, int reportLength)
    {
        _handle = handle;
        _reportLength = reportLength;
    }

    public static FeatureReportChannel? Open(HidInterface collection)
    {
        if (collection.FeatureReportLength < 2)
            return null;
        var handle = HidNative.OpenReadWrite(collection.Path);
        return handle is null ? null : new FeatureReportChannel(handle, collection.FeatureReportLength);
    }

    /// <summary>Returns the reply frame (without report ID), or null when either transfer failed.</summary>
    public byte[]? Exchange(ReadOnlySpan<byte> frame, TimeSpan delay)
    {
        var request = new byte[_reportLength];
        frame[..Math.Min(frame.Length, _reportLength - 1)].CopyTo(request.AsSpan(1));
        if (!HidNative.HidD_SetFeature(_handle, request, request.Length))
            return null;

        Thread.Sleep(delay);

        var response = new byte[_reportLength];
        if (!HidNative.HidD_GetFeature(_handle, response, response.Length))
            return null;
        return response[1..];
    }

    public void Dispose() => _handle.Dispose();
}
