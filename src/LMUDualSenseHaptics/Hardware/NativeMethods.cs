// Adapted from ACC-DualSense-Feedback (MIT); see THIRD_PARTY_NOTICES.md.
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LMUDualSenseHaptics.Hardware;

internal static unsafe partial class NativeMethods
{
    internal const uint DigcfPresent = 0x00000002;
    internal const uint DigcfDeviceInterface = 0x00000010;
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;
    internal static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDeviceInterfaceData
    {
        public uint CbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDevInfoData
    { public uint CbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DevicePropertyKey
    { public Guid Format; public uint Id; }

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetailWithInfo(IntPtr set,
        ref SpDeviceInterfaceData data, IntPtr detail, uint size, out uint required, ref SpDevInfoData deviceInfo);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SpDevInfoData deviceInfo,
        ref DevicePropertyKey key, out uint type, [Out] byte[] buffer, uint size, out uint required, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
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

    [LibraryImport("hid.dll")]
    internal static partial void HidD_GetHidGuid(out Guid hidGuid);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool HidD_GetAttributes(SafeFileHandle handle, ref HiddAttributes attributes);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsedData);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool HidD_FreePreparsedData(IntPtr preparsedData);

    [LibraryImport("hid.dll")]
    internal static partial int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr parent,
        uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr detailData,
        uint detailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [LibraryImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "WriteFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WriteFile(
        SafeFileHandle file,
        byte[] buffer,
        uint bytesToWrite,
        out uint bytesWritten,
        IntPtr overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "ReadFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReadFile(
        SafeFileHandle file,
        byte[] buffer,
        uint bytesToRead,
        out uint bytesRead,
        IntPtr overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "CancelIoEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
}
