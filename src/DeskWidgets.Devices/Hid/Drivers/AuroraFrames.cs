namespace DeskWidgets.Devices.Hid.Drivers;

/// <summary>
/// The 64-byte feature-report frame family shared by LAMZU Aurora ("dm")
/// receivers and WLMOUSE receivers.
///   request: [2]=target (0 receiver, 2 mouse) [3]=length [4] [5]=command [6..]=args
///   reply:   [0]=0xA1 mouse online / 0xA4 mouse off, [5]=command echo, payload at [6..]
/// </summary>
internal static class AuroraFrames
{
    public const int FrameLength = 64;
    public const byte Online = 0xA1;
    public const byte Offline = 0xA4;

    public static byte[] Build(byte target, byte length, byte b4, byte command, params byte[] args)
    {
        var frame = new byte[FrameLength];
        frame[2] = target;
        frame[3] = length;
        frame[4] = b4;
        frame[5] = command;
        args.CopyTo(frame, 6);
        return frame;
    }

    /// <summary>True when <paramref name="reply"/> carries <paramref name="header"/> and echoes <paramref name="command"/>.</summary>
    public static bool IsReply(byte[]? reply, byte header, byte command)
        => reply is { Length: >= 12 } && reply[0] == header && reply[5] == command;
}
