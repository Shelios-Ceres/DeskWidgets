using DeskWidgets.Core.Devices;

namespace DeskWidgets.Devices.Hid.Drivers;

/// <summary>
/// LAMZU 54H20 8K receivers (Thorn V2 family). Uses only the two frames
/// verified on-device in the earlier Thorn V2 widget project:
///   GET_DEVICE_VIDPID 00 00 01 06 00 8B 02 — answers A1 even while the mouse
///     is off, so it doubles as the control-collection probe; reply [10..11]
///     is the paired mouse PID, used to name the model.
///   GET_BATTERY 00 00 02 02 00 83 — A1: [6]=charging [7]=percent; A4: mouse off.
/// The receiver PID alone does not identify the model: the 8K dongle V2 is
/// shared across mice, hence both 0x002E and 0x0032 and the PID lookup.
/// </summary>
public sealed class LamzuAuroraDriver : IHidBatteryDriver
{
    private const ushort VendorId = 0x37B0;
    private const byte CmdGetBattery = 0x83;
    private const byte CmdGetDeviceVidPid = 0x8B;
    private static readonly TimeSpan CommandDelay = TimeSpan.FromMilliseconds(100);

    private static readonly HashSet<ushort> ReceiverIds = [0x002E, 0x0032];

    private static readonly Dictionary<ushort, string> MouseNames = new()
    {
        [0x0030] = "LAMZU Thorn V2",
        [0x0040] = "LAMZU Thorn V2",
        [0x002C] = "LAMZU Maya M",
    };

    private static readonly byte[] GetDeviceVidPid = AuroraFrames.Build(1, 6, 0, CmdGetDeviceVidPid, 2);
    private static readonly byte[] GetBattery = AuroraFrames.Build(2, 2, 0, CmdGetBattery);

    private string? _controlPath;

    public string Name => "LAMZU Aurora";

    public bool Supports(ushort vendorId, ushort productId)
        => vendorId == VendorId && ReceiverIds.Contains(productId);

    public DeviceStatus? Read(IReadOnlyList<HidInterface> collections, CancellationToken cancellationToken)
    {
        var (channel, identity) = OpenControlCollection(collections);
        if (channel is null)
            return null;
        using (channel)
        {
            string name = "LAMZU";
            if (identity is not null)
            {
                ushort mousePid = (ushort)((identity[10] << 8) | identity[11]);
                name = MouseNames.GetValueOrDefault(mousePid, "LAMZU");
            }

            cancellationToken.ThrowIfCancellationRequested();
            byte[]? reply = channel.Exchange(GetBattery, CommandDelay);
            if (!AuroraFrames.IsReply(reply, AuroraFrames.Online, CmdGetBattery) &&
                !AuroraFrames.IsReply(reply, AuroraFrames.Offline, CmdGetBattery))
            {
                // One retry for a garbled reply, as the vendor driver does.
                reply = channel.Exchange(GetBattery, CommandDelay);
            }

            var receiver = collections[0];
            var status = new DeviceStatus
            {
                Id = $"hid:{receiver.VendorId:X4}:{receiver.ProductId:X4}",
                Name = name,
                Kind = DeviceKind.Mouse,
                Connection = ConnectionKind.Wireless24G,
                IsConnected = false,
                Source = Name,
            };
            if (AuroraFrames.IsReply(reply, AuroraFrames.Online, CmdGetBattery))
            {
                return status with
                {
                    IsConnected = true,
                    BatteryPercent = Math.Min((int)reply![7], 100),
                    IsCharging = reply[6] == 1,
                };
            }
            return status; // A4 (mouse off) or no valid answer
        }
    }

    /// <summary>
    /// Finds the collection that answers feature reports, trying the one that
    /// worked last time first. Returns it with the identity reply.
    /// </summary>
    private (FeatureReportChannel? Channel, byte[]? Identity) OpenControlCollection(IReadOnlyList<HidInterface> collections)
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
            byte[]? reply = channel.Exchange(GetDeviceVidPid, CommandDelay);
            if (AuroraFrames.IsReply(reply, AuroraFrames.Online, CmdGetDeviceVidPid))
            {
                _controlPath = collection.Path;
                return (channel, reply);
            }
            channel.Dispose();
        }
        _controlPath = null;
        return (null, null);
    }
}
