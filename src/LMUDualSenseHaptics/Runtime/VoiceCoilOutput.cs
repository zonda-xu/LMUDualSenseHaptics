using System.Diagnostics;
using System.Runtime.Versioning;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Haptics;
using static LMUDualSenseHaptics.Hardware.WasapiNative;
namespace LMUDualSenseHaptics.Runtime;

internal sealed record AudioRoute(bool Enabled, string EndpointId, Guid HidContainer, long HidGeneration);
internal sealed record VoiceCoilOutputStatus(bool Ready, string Message, string Error, string EndpointId,
    string Format, double BufferMilliseconds, VoiceCoilLevels Levels, long SubmittedFrames)
{
    public static VoiceCoilOutputStatus Waiting { get; } = new(false, "等待 USB 音频端点", "", "", "", 0, default, 0);
}

[SupportedOSPlatform("windows")]
internal sealed class VoiceCoilOutput : IDisposable
{
    private sealed record Request(AudioRoute Route, VoiceCoilCommand Command);
    private readonly ManualResetEvent stop = new(false);
    private readonly AutoResetEvent wake = new(false);
    private readonly Thread thread;
    private Request request = new(new(false, "", Guid.Empty, 0), VoiceCoilCommand.Off);
    private VoiceCoilOutputStatus status = VoiceCoilOutputStatus.Waiting;
    private AudioEndpointInfo[] endpoints = [];
    private int refresh;
    private bool started, disposed;
    public VoiceCoilOutputStatus Current => Volatile.Read(ref status);
    public AudioEndpointInfo[] Endpoints => Volatile.Read(ref endpoints);
    public VoiceCoilOutput()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "DualSense WASAPI voice coils" };
        thread.SetApartmentState(ApartmentState.MTA);
    }
    public void Start() { if (started) return; started = true; thread.Start(); }
    public void RefreshDevices() { Interlocked.Increment(ref refresh); wake.Set(); }
    public void Submit(AudioRoute route, VoiceCoilCommand command)
    {
        var old = Volatile.Read(ref request); Volatile.Write(ref request, new(route, command));
        if (old.Route != route) wake.Set();
    }
    public void RequestStop() { stop.Set(); wake.Set(); }
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private void Run()
    {
        bool com = false; WasapiVoiceCoilDevice? device = null; AudioRoute? openedRoute = null;
        int seenRefresh = -1; double retryAt = 0, nextStats = 0; int timeouts = 0;
        try
        {
            Check(CoInitializeEx(IntPtr.Zero, 0)); com = true;
            while (!stop.WaitOne(0))
            {
                var input = Volatile.Read(ref request); var route = input.Route; double now = Now;
                bool force = seenRefresh != Volatile.Read(ref refresh) || openedRoute != route;
                if (force)
                {
                    device?.Dispose(); device = null; openedRoute = route; retryAt = 0;
                    Volatile.Write(ref status, VoiceCoilOutputStatus.Waiting);
                    seenRefresh = Volatile.Read(ref refresh);
                }
                if (device is null && (force || route.Enabled && now >= retryAt))
                {
                    try
                    {
                        var all = WasapiVoiceCoilDevice.Enumerate();
                        Volatile.Write(ref endpoints, all.Where(e => e.HasControllerName ||
                            route.HidContainer != Guid.Empty && e.ContainerId == route.HidContainer).ToArray());
                        if (route.Enabled)
                        {
                            var chosen = AudioEndpointSelection.Choose(all, route.EndpointId, route.HidContainer);
                            device = new(chosen); timeouts = 0;
                            Volatile.Write(ref status, new(true, "USB 音圈已启动 · " + chosen.Name, "", chosen.Id,
                                $"{device.Format.SampleRate} Hz / {device.Format.Bits}-bit / 4 ch · L=3 R=4",
                                device.BufferMilliseconds, default, 0));
                        }
                        else Volatile.Write(ref status, VoiceCoilOutputStatus.Waiting with { Message = "音圈未启用或 HID 未连接" });
                    }
                    catch (Exception ex)
                    {
                        device?.Dispose(); device = null; retryAt = now + 2;
                        Volatile.Write(ref status, VoiceCoilOutputStatus.Waiting with
                        { Message = "音圈未输出（扳机通路独立）", Error = $"{ex.Message} [0x{ex.HResult:X8}]" });
                    }
                }
                if (device is null) { WaitHandle.WaitAny([stop, wake], 200); continue; }
                try
                {
                    // Submission only happens after a successful audio-mode HID report.
                    var latest = Volatile.Read(ref request); now = Now;
                    device.Pump(latest.Route == route ? latest.Command : VoiceCoilCommand.Off, now);
                    if (now >= nextStats)
                    {
                        nextStats = now + .05;
                        Volatile.Write(ref status, Current with { Levels = device.Levels, SubmittedFrames = device.SubmittedFrames });
                    }
                    int signaled = WaitHandle.WaitAny([stop, wake, device.Event], 100);
                    timeouts = signaled == WaitHandle.WaitTimeout ? timeouts + 1 : 0;
                    if (timeouts >= 5) throw new TimeoutException("WASAPI 音频事件停止；请检查设备占用或 USB 连接");
                }
                catch (Exception ex)
                {
                    device.Dispose(); device = null; retryAt = Now + 2;
                    Volatile.Write(ref status, VoiceCoilOutputStatus.Waiting with
                    { Message = "音圈断开，等待重连", Error = $"{ex.Message} [0x{ex.HResult:X8}]" });
                }
            }
        }
        catch (Exception ex)
        { Volatile.Write(ref status, VoiceCoilOutputStatus.Waiting with { Message = "音频线程已停止", Error = ex.Message }); }
        finally { device?.Dispose(); if (com) CoUninitialize(); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; RequestStop();
        // Avoid disposing wait handles under a driver that is still unwinding COM.
        if (!started || thread.Join(2000)) { stop.Dispose(); wake.Dispose(); }
    }
}
