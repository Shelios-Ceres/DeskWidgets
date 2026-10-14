using DeskWidgets.Core.Devices;

namespace DeskWidgets.Devices.Hid.Drivers;

/// <summary>
/// WLMOUSE HUAN 8K receiver (0x36A7:0xA863). Same frame family as LAMZU; all
/// frames were verified against captures of the official WL MOUSE HUB:
///   GET_FW (receiver) 00 00 00 10 00 81 — answers A1 while the mouse is off: control probe.
///   GET_BATTERY       00 00 02 02 00 83 — A1: [6]=charging [7]=percent; A4: mouse off.
///   GET_POLLING_RATE  00 00 02 02 01 80 01 — [7]=rate code.
/// Bootloader PIDs (0xB863/0xB864) are never matched.
/// </summary>
public sealed class WlMouseDriver : IHidBatteryDriver
{
    private const ushort VendorId = 0x36A7;
    private const ushort ReceiverId = 0xA863;
    private const byte CmdGetFirmware = 0x81;
    private const byte CmdGetBattery = 0x83;
    private const byte CmdGetPollingRate = 0x80;
    private static readonly TimeSpan CommandDelay = TimeSpan.FromMilliseconds(100);

    private static readonly byte[] GetReceiverFirmware = AuroraFrames.Build(0, 0x10, 0, CmdGetFirmware);
    private static readonly byte[] GetBattery = AuroraFrames.Build(2, 2, 0, CmdGetBattery);
    private static readonly byte[] GetPollingRate = AuroraFrames.Build(2, 2, 1, CmdGetPollingRate, 1);

    private string? _controlPath;

    public string Name => "WLMOUSE";

    public bool Supports(ushort vendorId, ushort productId)
        => vendorId == VendorId && productId == ReceiverId;

    public DeviceStatus? Read(IReadOnlyList<HidInterface> collections, CancellationToken cancellationToken)
    {
        using var channel = OpenControlCollection(collections);
        if (channel is null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        byte[]? reply = channel.Exchange(GetBattery, CommandDelay);
        if (!AuroraFrames.IsReply(reply, AuroraFrames.Online, CmdGetBattery) &&
            !AuroraFrames.IsReply(reply, AuroraFrames.Offline, CmdGetBattery))
        {
            reply = channel.Exchange(GetBattery, CommandDelay);
        }

        var status = new DeviceStatus
        {
            Id = $"hid:{VendorId:X4}:{ReceiverId:X4}",
            Name = "WLMOUSE HUAN",
            Kind = DeviceKind.Mouse,
            Connection = ConnectionKind.Wireless24G,
            IsConnected = false,
            Source = Name,
        };
        if (!AuroraFrames.IsReply(reply, AuroraFrames.Online, CmdGetBattery))
            return status;

        int? rate = null;
        byte[]? rateReply = channel.Exchange(GetPollingRate, CommandDelay);
        if (AuroraFrames.IsReply(rateReply, AuroraFrames.Online, CmdGetPollingRate))
            rate = PollingRateHz(rateReply![7]);

        return status with
        {
            IsConnected = true,
            BatteryPercent = Math.Min((int)reply![7], 100),
            IsCharging = reply[6] == 1,
            Detail = rate is int hz ? $"{hz} Hz" : null,
        };
    }

    /// <summary>Verified on-device at 0x80 = 8000 Hz and 0x40 = 4000 Hz; unknown codes are not guessed.</summary>
    private static int? PollingRateHz(byte code) => code switch
    {
        2 => 125,
        4 => 250,
        8 => 500,
        16 => 1000,
        32 => 2000,
        64 => 4000,
        128 => 8000,
        _ => null,
    };

    private FeatureReportChannel? OpenControlCollection(IReadOnlyList<HidInterface> collections)
    {
        var candidates = collections
            .Where(c => c.FeatureReportLength >= AuroraFrames.FrameLength)
            .OrderByDescending(c => c.Path == _controlPath)
            .ThenByDescending(c => c.UsagePage == 0xFFFF);
        foreach (var collection in candidates)
        {
            var channel = FeatureReportChannel.Open(collection);
            if (channel is null)
                continue;
            byte[]? reply = channel.Exchange(GetReceiverFirmware, CommandDelay);
            if (AuroraFrames.IsReply(reply, AuroraFrames.Online, CmdGetFirmware))
            {
                _controlPath = collection.Path;
                return channel;
            }
            channel.Dispose();
        }
        _controlPath = null;
        return null;
    }
}
