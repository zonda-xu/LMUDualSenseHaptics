// Adapted from ACC-DualSense-Feedback (MIT); see THIRD_PARTY_NOTICES.md.
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Runtime;

namespace LMUDualSenseHaptics.Hardware;

internal enum DualSenseTransport
{
    Usb,
    Bluetooth
}

internal sealed record DualSenseDeviceInfo(
    string Path,
    ushort ProductId,
    DualSenseTransport Transport,
    ushort InputReportLength,
    ushort OutputReportLength, Guid ContainerId);

internal sealed class DualSenseHidDevice : IHidOutputDevice
{
    private const ushort SonyVendorId = 0x054C;
    private const ushort DualSenseProductId = 0x0CE6;
    private const ushort DualSenseEdgeProductId = 0x0DF2;

    private bool _audioMode;
    private readonly SafeFileHandle _handle;
    private readonly byte[] _reportBuffer = new byte[78];

    public DualSenseDeviceInfo Info { get; }
    public string Label => $"USB PID {Info.ProductId:X4}";
    public int LastWriteError { get; private set; }
    public Guid ContainerId => Info.ContainerId;

    private DualSenseHidDevice(DualSenseDeviceInfo info, SafeFileHandle handle)
    {
        Info = info;
        _handle = handle;
    }

    public static List<DualSenseDeviceInfo> Enumerate()
    {
        var result = new List<DualSenseDeviceInfo>();
        NativeMethods.HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = NativeMethods.SetupDiGetClassDevs(
            ref hidGuid,
            null,
            IntPtr.Zero,
            NativeMethods.DigcfPresent | NativeMethods.DigcfDeviceInterface);

        if (set == NativeMethods.InvalidHandleValue)
            return result;

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new NativeMethods.SpDeviceInterfaceData
                {
                    CbSize = (uint)Marshal.SizeOf<NativeMethods.SpDeviceInterfaceData>()
                };

                if (!NativeMethods.SetupDiEnumDeviceInterfaces(
                        set, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                    break;

                NativeMethods.SetupDiGetDeviceInterfaceDetail(
                    set, ref interfaceData, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
                if (required == 0)
                    continue;

                IntPtr detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    var devInfo = new NativeMethods.SpDevInfoData { CbSize = (uint)Marshal.SizeOf<NativeMethods.SpDevInfoData>() };
                    if (!NativeMethods.SetupDiGetDeviceInterfaceDetailWithInfo(
                            set, ref interfaceData, detail, required, out _, ref devInfo))
                        continue;

                    // cbSize is 8 on x64 because the native structure's total
                    // size includes tail padding, but WCHAR DevicePath itself
                    // still begins immediately after the 4-byte DWORD.
                    const int pathOffset = sizeof(uint);
                    string? path = Marshal.PtrToStringUni(detail + pathOffset);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    using SafeFileHandle probe = OpenShared(path);
                    if (probe.IsInvalid)
                        continue;

                    var attributes = new NativeMethods.HiddAttributes
                    {
                        Size = Marshal.SizeOf<NativeMethods.HiddAttributes>()
                    };
                    if (!NativeMethods.HidD_GetAttributes(probe, ref attributes) ||
                        attributes.VendorId != SonyVendorId ||
                        (attributes.ProductId != DualSenseProductId && attributes.ProductId != DualSenseEdgeProductId))
                        continue;

                    if (!TryGetCapabilities(probe, out NativeMethods.HidpCaps caps) ||
                        caps.UsagePage != 0x01 || caps.Usage != 0x05)
                        continue;

                    ushort outputLength = caps.OutputReportByteLength;
                    bool bluetooth = outputLength >= 78 ||
                                     path.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
                                     path.Contains("BLUETOOTH", StringComparison.OrdinalIgnoreCase);
                    Guid containerId = Guid.Empty;
                    var containerKey = new NativeMethods.DevicePropertyKey
                        { Format = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), Id = 2 };
                    byte[] containerBytes = new byte[16];
                    if (NativeMethods.SetupDiGetDeviceProperty(set, ref devInfo, ref containerKey,
                        out uint propertyType, containerBytes, 16, out uint actual, 0) && actual == 16 && propertyType == 13)
                        containerId = new Guid(containerBytes);
                    result.Add(new DualSenseDeviceInfo(
                        path,
                        attributes.ProductId,
                        bluetooth ? DualSenseTransport.Bluetooth : DualSenseTransport.Usb,
                        caps.InputReportByteLength,
                        outputLength, containerId));
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    public static DualSenseHidDevice? TryOpenFirst()
    {
        // Prefer USB for deterministic competition latency.
        foreach (DualSenseDeviceInfo info in Enumerate().Where(static d => d.Transport == DualSenseTransport.Usb && d.OutputReportLength == 48))
        {
            SafeFileHandle handle = OpenShared(info.Path);
            if (!handle.IsInvalid)
                return new DualSenseHidDevice(info, handle);
            handle.Dispose();
        }

        return null;
    }

    public bool TryWrite(in FeedbackFrame frame)
    {
        try
        {
            _audioMode = frame.UseAudioHaptics;
            int length = DualSenseReportBuilder.Build(
                frame,
                _reportBuffer);
            bool success = NativeMethods.WriteFile(
                _handle,
                _reportBuffer,
                (uint)length,
                out uint written,
                IntPtr.Zero) && written == length;
            LastWriteError = success ? 0 : Marshal.GetLastWin32Error();
            return success;
        }
        catch (IOException ex)
        {
            LastWriteError = ex.HResult;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            LastWriteError = ex.HResult;
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            TryWrite(FeedbackFrame.Off with { UseAudioHaptics = _audioMode });
        }
        catch
        {
        }

        _handle.Dispose();
    }

    private static SafeFileHandle OpenShared(string path)
        => NativeMethods.CreateFile(
            path,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            0,
            IntPtr.Zero);

    private static bool TryGetCapabilities(SafeFileHandle handle, out NativeMethods.HidpCaps caps)
    {
        caps = default;
        if (!NativeMethods.HidD_GetPreparsedData(handle, out IntPtr data))
            return false;
        try
        {
            return NativeMethods.HidP_GetCaps(data, out caps) >= 0;
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(data);
        }
    }
}
