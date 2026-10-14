using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DeskWidgets.Devices.Hid;

/// <summary>
/// Minimal Win32 HID interop: setupapi enumeration plus hid.dll attribute,
/// capability, string and feature-report calls. Callers size report buffers
/// from <see cref="HIDP_CAPS"/>; nothing here assumes a report length.
/// </summary>
internal static class HidNative
{
    public static readonly Guid HidInterfaceGuid = new(0x4D1E55B2, 0xF16F, 0x11CF, 0x88, 0xCB, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30);

    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern nint SetupDiGetClassDevs(ref Guid classGuid, nint enumerator, nint hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(nint deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, nint detailData, int detailDataSize, out int requiredSize, nint deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out nint preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(nint preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(nint preparsedData, out HIDP_CAPS capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetFeature(SafeFileHandle device, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetFeature(SafeFileHandle device, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetProductString(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetManufacturerString(SafeFileHandle device, byte[] buffer, int bufferLength);

    /// <summary>Device paths of all present HID collections.</summary>
    public static IReadOnlyList<string> EnumerateDevicePaths()
    {
        var paths = new List<string>();
        var guid = HidInterfaceGuid;
        nint set = SetupDiGetClassDevs(ref guid, 0, 0, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == 0 || set == -1)
            return paths;
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, 0, 0, out int required, 0);
                if (required <= 0)
                    continue;
                nint buffer = Marshal.AllocHGlobal(required);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on x64, 6 on x86.
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out _, 0))
                    {
                        string? path = Marshal.PtrToStringUni(buffer + 4);
                        if (!string.IsNullOrEmpty(path))
                            paths.Add(path);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return paths;
    }

    /// <summary>
    /// Opens without read/write access. Enough for attributes, caps and
    /// strings, works for system-owned mice/keyboards, and cannot send data.
    /// </summary>
    public static SafeFileHandle? OpenQueryOnly(string path) => Open(path, 0);

    public static SafeFileHandle? OpenReadWrite(string path) => Open(path, GENERIC_READ | GENERIC_WRITE);

    /// <summary>Read access only: receives a copy of every input report, cannot send anything.</summary>
    public static SafeFileHandle? OpenReadOnly(string path) => Open(path, GENERIC_READ);

    /// <summary>Like <see cref="OpenReadOnly"/>, for asynchronous (cancellable) reads.</summary>
    public static SafeFileHandle? OpenReadOnlyOverlapped(string path) => Open(path, GENERIC_READ, FILE_FLAG_OVERLAPPED);

    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    private static SafeFileHandle? Open(string path, uint access, uint flags = 0)
    {
        var handle = CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, flags, 0);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return handle;
    }

    public static HIDP_CAPS? GetCaps(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out nint preparsed))
            return null;
        try
        {
            return HidP_GetCaps(preparsed, out HIDP_CAPS caps) == HIDP_STATUS_SUCCESS ? caps : null;
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    public static string? GetProductString(SafeFileHandle handle)
    {
        var buffer = new byte[256];
        return HidD_GetProductString(handle, buffer, buffer.Length) ? DecodeString(buffer) : null;
    }

    public static string? GetManufacturerString(SafeFileHandle handle)
    {
        var buffer = new byte[256];
        return HidD_GetManufacturerString(handle, buffer, buffer.Length) ? DecodeString(buffer) : null;
    }

    private static string DecodeString(byte[] buffer)
    {
        string text = System.Text.Encoding.Unicode.GetString(buffer);
        int end = text.IndexOf('\0');
        return (end >= 0 ? text[..end] : text).Trim();
    }
}
