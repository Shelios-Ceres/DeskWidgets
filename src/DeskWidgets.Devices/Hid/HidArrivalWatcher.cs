using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskWidgets.Core.Diagnostics;

namespace DeskWidgets.Devices.Hid;

/// <summary>
/// Raises <see cref="Changed"/> when a HID interface arrives or is removed
/// (receiver plugged/unplugged), via CM_Register_Notification.
/// </summary>
internal sealed class HidArrivalWatcher : IDisposable
{
    private const int CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE = 0;
    private const int CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL = 0;
    private const int CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL = 1;

    // Must match sizeof(CM_NOTIFY_FILTER) == 416: a 16-byte header plus a
    // 400-byte union (WCHAR InstanceId[200]). The OS rejects any other cbSize
    // with CR_INVALID_DATA — the bug that silently disabled the old watcher.
    [StructLayout(LayoutKind.Sequential)]
    private struct CM_NOTIFY_FILTER
    {
        public int cbSize;
        public int Flags;
        public int FilterType;
        public int Reserved;
        public Guid ClassGuid;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 384)]
        public byte[] UnionTail;
    }

    private delegate int CM_NOTIFY_CALLBACK(nint hNotify, nint context, int action, nint eventData, int eventDataSize);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Register_Notification(ref CM_NOTIFY_FILTER filter, nint context, CM_NOTIFY_CALLBACK callback, out nint notifyContext);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Unregister_Notification(nint notifyContext);

    private nint _handle;
    private CM_NOTIFY_CALLBACK? _callback; // keeps the delegate alive while registered

    public event Action? Changed;

    public void Start()
    {
        if (_handle != 0)
            return;
        var filter = new CM_NOTIFY_FILTER
        {
            cbSize = Marshal.SizeOf<CM_NOTIFY_FILTER>(),
            FilterType = CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE,
            ClassGuid = HidNative.HidInterfaceGuid,
            UnionTail = new byte[384],
        };
        Debug.Assert(filter.cbSize == 416);
        _callback = OnNotification;
        int cr = CM_Register_Notification(ref filter, 0, _callback, out _handle);
        if (cr != 0)
        {
            _handle = 0;
            Log.Warn($"CM_Register_Notification failed (CR=0x{cr:X}); plug/unplug is detected by polling only.");
        }
    }

    private int OnNotification(nint hNotify, nint context, int action, nint eventData, int eventDataSize)
    {
        if (action is CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL or CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL)
            Changed?.Invoke();
        return 0;
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            CM_Unregister_Notification(_handle);
            _handle = 0;
        }
    }
}
