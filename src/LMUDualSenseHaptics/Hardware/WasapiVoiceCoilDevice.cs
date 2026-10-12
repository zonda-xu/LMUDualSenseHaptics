using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LMUDualSenseHaptics.Haptics;
using static LMUDualSenseHaptics.Hardware.WasapiNative;
namespace LMUDualSenseHaptics.Hardware;

[SupportedOSPlatform("windows")]
internal sealed class WasapiVoiceCoilDevice : IDisposable
{
    private IAudioClient? client;
    private IAudioRenderClient? renderer;
    private readonly AutoResetEvent audioEvent = new(false);
    private readonly float[] pcm;
    private readonly byte[] bytes;
    private readonly uint capacity;
    private readonly VoiceCoilSynthesizer synth;
    public AudioPcmFormat Format { get; }
    public AudioEndpointInfo Endpoint { get; }
    public WaitHandle Event => audioEvent;
    public VoiceCoilLevels Levels => synth.Levels;
    public long SubmittedFrames { get; private set; }
    public double BufferMilliseconds => capacity * 1000.0 / Format.SampleRate;

    public static AudioEndpointInfo[] Enumerate()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDeviceCollection? devices = null;
        try
        {
            Check(enumerator.EnumAudioEndpoints(0, 1, out devices)); Check(devices.GetCount(out uint count));
            var result = new List<AudioEndpointInfo>();
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    Check(devices.Item(i, out device)); Check(device.GetId(out string id));
                    string name = ReadName(device); Guid container = ReadContainer(device);
                    int channels = 0, rate = 0; string problem = "";
                    IAudioClient? probe = null; IntPtr format = IntPtr.Zero;
                    try
                    {
                        var guid = AudioClientId; Check(device.Activate(ref guid, 23, IntPtr.Zero, out object obj));
                        probe = (IAudioClient)obj; Check(probe.GetMixFormat(out format));
                        channels = (ushort)Marshal.ReadInt16(format, 2); rate = Marshal.ReadInt32(format, 4);
                        _ = ReadFormat(format); // Distinguishes stereo/invalid masks from supported quad.
                    }
                    catch (Exception ex) when (ex is COMException or InvalidDataException) { problem = ex.Message; }
                    finally { if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format); Release(probe); }
                    result.Add(new(id, name, container, channels, rate, problem));
                }
                catch (COMException) { /* Device may disappear during enumeration; skip that endpoint. */ }
                finally { Release(device); }
            }
            return result.ToArray();
        }
        finally { Release(devices); Release(enumerator); }
    }
    private static string ReadName(IMMDevice device)
    {
        IPropertyStore? props = null; PropVariant value = default;
        try
        {
            Check(device.OpenPropertyStore(0, out props)); var key = FriendlyName; Check(props.GetValue(ref key, out value));
            return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "Unnamed audio endpoint" : "Unnamed audio endpoint";
        }
        finally { PropVariantClear(ref value); Release(props); }
    }
    private static Guid ReadContainer(IMMDevice device)
    {
        IPropertyStore? props = null; PropVariant value = default;
        try
        {
            Check(device.OpenPropertyStore(0, out props)); var key = ContainerId; Check(props.GetValue(ref key, out value));
            if (value.Type == 72 && value.Pointer != IntPtr.Zero) return Marshal.PtrToStructure<Guid>(value.Pointer);
            return value.Type == 31 && Guid.TryParse(Marshal.PtrToStringUni(value.Pointer), out Guid id) ? id : Guid.Empty;
        }
        catch (COMException) { return Guid.Empty; }
        finally { PropVariantClear(ref value); Release(props); }
    }
    private static AudioPcmFormat ReadFormat(IntPtr pointer)
    {
        int extra = (ushort)Marshal.ReadInt16(pointer, 16);
        if (extra > 1024) throw new InvalidDataException("音频格式扩展长度异常");
        var bytes = new byte[18 + extra]; Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return AudioPcmFormat.Parse(bytes);
    }
    public WasapiVoiceCoilDevice(AudioEndpointInfo endpoint)
    {
        Endpoint = endpoint;
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator(); IMMDevice? device = null;
        IntPtr format = IntPtr.Zero;
        try
        {
            Check(enumerator.GetDevice(endpoint.Id, out device));
            var guid = AudioClientId; Check(device.Activate(ref guid, 23, IntPtr.Zero, out object obj));
            client = (IAudioClient)obj; Check(client.GetMixFormat(out format)); Format = ReadFormat(format);
            // Exact mix format, shared/event-driven. No automatic channel conversion.
            // Shared/event-driven streams require both durations to be zero.
            // GetBufferSize below supplies the engine's actual allocation.
            Check(client.Initialize(0, EventCallback, 0, 0, format, IntPtr.Zero));
            Check(client.SetEventHandle(audioEvent.SafeWaitHandle.DangerousGetHandle()));
            Check(client.GetBufferSize(out capacity));
            if (capacity == 0 || capacity > Format.SampleRate) throw new InvalidDataException("WASAPI 缓冲大小异常");
            guid = RenderClientId; Check(client.GetService(ref guid, out obj)); renderer = (IAudioRenderClient)obj;
            pcm = new float[checked((int)capacity * 4)]; bytes = new byte[checked((int)capacity * Format.BlockAlign)];
            synth = new(Format.SampleRate);
            Check(renderer.GetBuffer(capacity, out _)); Check(renderer.ReleaseBuffer(capacity, Silent));
            Check(client.Start());
        }
        catch
        {
            // Constructors that throw do not receive Dispose; release partial COM ownership here.
            Release(renderer); renderer = null; Release(client); client = null; audioEvent.Dispose(); throw;
        }
        finally { if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format); Release(device); Release(enumerator); }
    }
    public void Pump(VoiceCoilCommand command, double now)
    {
        var c = client ?? throw new ObjectDisposedException(nameof(WasapiVoiceCoilDevice));
        var r = renderer ?? throw new ObjectDisposedException(nameof(WasapiVoiceCoilDevice));
        Check(c.GetCurrentPadding(out uint padding));
        if (padding > capacity) throw new InvalidDataException("WASAPI padding 超界");
        uint frames = capacity - padding; if (frames == 0) return;
        int samples = checked((int)frames * 4), size = checked((int)frames * Format.BlockAlign);
        // Include already queued samples when enforcing the command expiry deadline.
        synth.Render(command, pcm.AsSpan(0, samples), now + padding / (double)Format.SampleRate);
        Format.Encode(pcm.AsSpan(0, samples), bytes.AsSpan(0, size));
        Check(r.GetBuffer(frames, out IntPtr data));
        bool copied = false;
        try { Marshal.Copy(bytes, 0, data, size); copied = true; }
        finally { Check(r.ReleaseBuffer(frames, copied ? 0u : Silent)); }
        SubmittedFrames += frames;
    }
    public void Dispose()
    {
        // Stop and reset discard pending haptic samples, including on hot-unplug/error.
        if (client is not null) { _ = client.Stop(); _ = client.Reset(); }
        Release(renderer); renderer = null; Release(client); client = null; audioEvent.Dispose();
    }
}
