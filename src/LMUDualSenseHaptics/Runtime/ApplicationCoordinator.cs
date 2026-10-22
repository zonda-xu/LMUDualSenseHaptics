using System.Diagnostics;
using System.Runtime.InteropServices;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Telemetry;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Diagnostics;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Config;
namespace LMUDualSenseHaptics.Runtime;
internal sealed record Status(string State,string Device,string Telemetry,string Error,TelemetryFrame? Frame,FeedbackFrame Output,
    double Abs,double Lock,double Tc,double Spin,double Shift,double Redline,GripLayers Layers,double TelemetryHz,double OutputHz,double CpuPercent,long WorkingSet,
    double IntervalP50,double IntervalP95,double IntervalP99,double LatencyP50,double LatencyP95,long Dropped,string WheelDiagnostics,double AllocatedMBPerSecond=0,bool DeviceConnected=false,bool AdvancedFeedbackEnabled=false,string ParameterSource="等待驾驶",ChannelLevels Channels=default,string LeftTriggerSource="OFF",string RightTriggerSource="OFF",string LearningState="",VoiceCoilOutputStatus? Audio=null);
internal sealed class ApplicationCoordinator:IAsyncDisposable
{
    private sealed record DeviceTest(string Name,double EndsAt);
    private DeviceTest? deviceTest;
    public void RequestDeviceTest(string name)
    {
        if(name is not ("OFF" or "L2" or "R2" or "LEFT" or "RIGHT" or "PULSE_L2" or "PULSE_R2" or "AUDIO_LEFT" or "AUDIO_RIGHT" or "AUDIO_KERB_LEFT" or "AUDIO_KERB_RIGHT"))throw new ArgumentException("未知手柄检查项目");
        if(Volatile.Read(ref latest) is {Driving:true})throw new InvalidOperationException("请先离开驾驶状态，再检查手柄");
        if (name.StartsWith("AUDIO_",StringComparison.Ordinal) && !Profile.AudioHaptics.Enabled)
            throw new InvalidOperationException("请先启用 USB 音圈模式");
        if ((name is "LEFT" or "RIGHT") && Profile.AudioHaptics.Enabled)
            throw new InvalidOperationException("检查普通握把震动前，请先关闭 USB 音圈；音圈请使用“左握把震动”或“右握把震动”按钮");
        Volatile.Write(ref deviceTest,new(name,Clock.Now+1));
    }
    private readonly VoiceCoilOutput voiceCoils=new();
    public AudioEndpointInfo[] AudioEndpoints=>voiceCoils.Endpoints;
    public void RefreshAudioDevices()=>voiceCoils.RefreshDevices();
    private Profile profile=new();private TelemetryFrame? latest;private Status status=new("Disconnected","未连接","等待 LMU","",null,FeedbackFrame.Off,0,0,0,0,0,0,default,0,0,0,0,0,0,0,0,0,0,"");
    private string telemetryStatus="等待 LMU",telemetryError="";
    private readonly CancellationTokenSource stop=new();private Task? telemetryTask,outputTask;private readonly FeedbackLoop pipeline=new();
    private TelemetryRecorder? debugLog;
    private readonly LogSampling logSampling=new();
    private TelemetryRecorder? recorder;private string? recordingPath;private readonly object recordGate=new();
    private readonly Queue<double> intervals=new(),latencies=new();private double lastWrite,lastCount;private long writes,frames;private double lastCpu;private long lastAllocated=GC.GetTotalAllocatedBytes();
    public Status Current=>Volatile.Read(ref status);public Profile Profile=>Volatile.Read(ref profile);
    public string? RecordingPath=>Volatile.Read(ref recordingPath);
    public void SetProfile(Profile p){p.Validate();if(RecordingPath is not null)throw new InvalidOperationException("请先停止录制，再更改参数");Volatile.Write(ref profile,p);}
    public void Start(){if(outputTask is not null)return;voiceCoils.Start();debugLog=new(Path.Combine(ProfileStore.DirectoryPath,"logs","debug-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".jsonl"),TelemetryRecorder.DebugLimitBytes);debugLog.Record(new{Kind="session",Version="1.2-preview",DebugHz=2,TelemetryHz=50,OutputHz=10});telemetryTask=Task.Run(TelemetryLoop);outputTask=Task.Run(OutputLoop);}
    public void StartRecording()
    {
        lock(recordGate){if(recorder is not null)return;recordingPath=Path.Combine(ProfileStore.DirectoryPath,"logs","telemetry-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".jsonl");recorder=new(recordingPath);recorder.Record(new{Kind="profile",Version="1.2-preview",Profile,TelemetryHz=50,OutputHz=10,RawSnapshots=false});}
    }
    public async Task StopRecording(){TelemetryRecorder? r;lock(recordGate){r=recorder;recorder=null;recordingPath=null;}if(r is not null)await r.DisposeAsync();}
    private void RecordError(string message)
    {
        double now=Clock.Now;if(!logSampling.Error(message,now))return;
        var item=new{Kind="error",At=now,Message=message};
        lock(recordGate){recorder?.Record(item);}debugLog?.Record(item);
    }
    private void TelemetryLoop()
    {
        while(!stop.IsCancellationRequested)
        {
            using var reader=new LmuMemoryReader();
            try
            {
                reader.Open();var parser=new LmuLayout();byte[] bytes=new byte[LmuLayout.SnapshotSize];telemetryError="";
                while(!stop.IsCancellationRequested && reader.IsAlive)
                {
                    reader.WaitForUpdate();
                    try{reader.CopySnapshot(bytes);}catch(TimeoutException){telemetryStatus="共享锁暂时忙";continue;}
                    var frame=parser.Parse(bytes,Clock.Now);
                    if(frame is not null){Volatile.Write(ref latest,frame);Interlocked.Increment(ref frames);lock(recordGate){if(recorder is {Stopped:false}&&logSampling.Telemetry(frame.CapturedAtSec))recorder.Record(new{Kind="telemetry",Frame=frame});}}
                    else if(!bytes[LmuLayout.TelemetryOffset+2].Equals((byte)1))Volatile.Write(ref latest,null);
                    telemetryStatus=$"LMU 车辆数据已连接 · 游戏接口版本 {parser.Version}";
                }
                Volatile.Write(ref latest,null);telemetryStatus="等待 LMU";
            }
            catch(Exception ex){Volatile.Write(ref latest,null);telemetryError=ex.Message;telemetryStatus="遥测连接/解析错误";RecordError(ex.Message);}
            stop.Token.WaitHandle.WaitOne(500);
        }
    }
    private void OutputLoop()
    {
        using var device=new HidOutputSession(DualSenseHidDevice.TryOpenFirst);double next=Clock.Now,nextUi=0;var process=Process.GetCurrentProcess();
        using var timer=new TimerResolution();
        try
        {
            while(!stop.IsCancellationRequested)
            {
                double now=Clock.Now;
                if(now<next){stop.Token.WaitHandle.WaitOne(Math.Max(1,(int)((next-now)*1000)));continue;}
                next+=.004;if(now-next>.008)next=now+.004;
                device.EnsureConnected(now);
                var f=Volatile.Read(ref latest);var p=Profile;
                var feedback=!device.Connected?pipeline.Off("Disconnected"):pipeline.Tick(f,now,p);
                if(telemetryError.Length>0)feedback=pipeline.Off("Error："+telemetryError);
                var audioCommand=pipeline.AdvancedFeedbackEnabled && f is not null
                    ? VoiceCoilCommand.Create(pipeline.Signals.RoadSignals,pipeline.Layers,f,now,p,
                        pipeline.Mixer.ContinuousGain,pipeline.Mixer.EngineGain) : VoiceCoilCommand.Off;
                var test=Volatile.Read(ref deviceTest);
                if(test is not null && now<test.EndsAt && f is not {Driving:true})
                {
                    pipeline.Off("DeviceReady：手柄检查 "+test.Name);
                    audioCommand=VoiceCoilCommand.Test(test.Name,now,test.EndsAt,p.AudioHaptics);
                    feedback=test.Name switch
                    {
                        "L2"=>new(0,0,TriggerEffect.FeedbackZones(new byte[]{1,1,1,1,2,2,2,2,3,3}),TriggerEffect.Off),
                        "R2"=>new(0,0,TriggerEffect.Off,TriggerEffect.FeedbackZones(new byte[]{1,1,1,1,2,2,2,2,3,3})),
                        "LEFT"=>new(38,0,TriggerEffect.Off,TriggerEffect.Off),
                        "RIGHT"=>new(0,38,TriggerEffect.Off,TriggerEffect.Off),
                        "PULSE_L2"=>new(0,0,TriggerEffect.Vibration(0,2,15),TriggerEffect.Off),
                        "PULSE_R2"=>new(0,0,TriggerEffect.Off,TriggerEffect.Vibration(0,2,12)),
                        _=>FeedbackFrame.Off
                    };
                }
                else if(test is not null)Volatile.Write(ref deviceTest,null);
                feedback=HapticsOutputRouting.ForTransport(feedback,p.AudioHaptics.Enabled);
                double before=Clock.Now;bool success=device.TryWrite(feedback,now);double after=Clock.Now;
                voiceCoils.Submit(new(p.AudioHaptics.Enabled && device.Connected,p.AudioHaptics.EndpointId,
                    device.ContainerId,device.Generation),success?audioCommand:VoiceCoilCommand.Off);
                var audioStatus=voiceCoils.Current;
                if(!device.Connected)pipeline.Off("Disconnected");
                if(success)
                {
                    if(lastWrite>0){intervals.Enqueue((after-lastWrite)*1000);if(intervals.Count>4096)intervals.Dequeue();}lastWrite=after;writes++;
                    if(f is not null&&pipeline.AdvancedFeedbackEnabled&&f.Sequence!=lastLatencySequence){latencies.Enqueue((after-f.CapturedAtSec)*1000);if(latencies.Count>4096)latencies.Dequeue();lastLatencySequence=f.Sequence;}
                }
                var e=pipeline.Signals;var layers=pipeline.Layers;
                string deviceLabel=device.Label;

                lock(recordGate)
                {
                    if(recorder is {Stopped:false} && logSampling.Output(after))recorder.Record(new OutputRecord("output",after,f?.Sequence??0,f?.CapturedAtSec??0,(after-before)*1000,success,feedback,e.Abs,e.Lock,e.Tc,e.Spin,e.Shift,e.Redline,layers,pipeline.Mixer.PreLeft,pipeline.Mixer.PreRight,pipeline.Mixer.EngineGain,pipeline.Mixer.ContinuousGain,pipeline.Mixer.OppositeGain,pipeline.Mixer.ClippedFrames,pipeline.ParameterSource,pipeline.Channels,pipeline.LeftTriggerSource,pipeline.RightTriggerSource,audioCommand,audioStatus.Ready));
                }
                if(now>=nextUi)
                {
                    nextUi=now+.1;double dt=now-lastCount;
                    double cpu=process.TotalProcessorTime.TotalSeconds;long allocated=GC.GetTotalAllocatedBytes();
                    double outputHz=dt>0?writes/dt:0,teleHz=dt>0?Interlocked.Exchange(ref frames,0)/dt:0;
                    string wd=$"刹车 {e.Braking.Mode} · {e.Braking.Evidence} · 前/后风险 {e.Braking.FrontRisk:F2}/{e.Braking.RearRisk:F2} · slip={e.Braking.MaxSlip:F3} rate={e.Braking.MaxSlipRate:F2}/s · ABS墙={e.Braking.AbsOnsetBrake?.ToString("F2")??"无"}\n"+string.Join("\n",e.Wheels.Select((w,i)=>$"{new[]{"FL","FR","RL","RR"}[i]}  R={w.Radius:F4}m n={w.RadiusSamples} t={w.RadiusTime:F1}s  κ={(w.Kappa?.ToString("F3")??"未知")}  α={w.Alpha:F3}rad  shock={w.Shock:F3} slip={w.Slip:F3}  noiseV={w.NoiseVelocity:F4}m/s noiseF/L={w.NoiseForce:F3}/{w.NoiseLoad:F3} learn={w.RoadLearnTime:F1}s"));
                    wd+="\n"+string.Join("\n",e.RoadSignals.Wheels.Select((w,i)=>
                    {
                        var raw=f is not null && f.Wheels.Length>i?f.Wheels[i]:null;
                        return $"{new[]{"FL","FR","RL","RR"}[i]} {w.Source} contact={w.Contact} via={w.ContactEvidence} type={raw?.SurfaceType?.ToString()??"未知"} material={raw?.TerrainName??""} texture={w.Texture:F3} bump={w.Bump:F3} kerb={w.Kerb:F3} inferred={w.InferredKerb:F3} susV={w.SuspensionVelocity:F4}m/s loadRate={w.LoadRate:F2}/s";
                    }));
                    string state=pipeline.Reason;string err=device.LastError.Length>0?device.LastError:telemetryError;
                    if(p.AudioHaptics.Enabled && device.Connected && audioStatus.Error.Length>0)
                        err=string.Join(" | ",new[]{err,audioStatus.Error}.Where(x=>x.Length>0));
                    if(recorder?.Failure is string fail)err="录制："+fail;
                    if(debugLog?.Failure is string debugFail)err=string.Join(" | ",new[]{err,"诊断日志："+debugFail}.Where(x=>x.Length>0));
                    Volatile.Write(ref status,new(state,deviceLabel,telemetryStatus,err,f,device.Connected?feedback:FeedbackFrame.Off,e.Abs,e.Lock,e.Tc,e.Spin,e.Shift,e.Redline,layers,teleHz,outputHz,dt>0?(cpu-lastCpu)/dt/Environment.ProcessorCount*100:0,process.WorkingSet64,
                    Percentile(intervals,.5),Percentile(intervals,.95),Percentile(intervals,.99),Percentile(latencies,.5),Percentile(latencies,.95),recorder?.Dropped??0,wd,dt>0?(allocated-lastAllocated)/dt/1048576.0:0,device.Connected,pipeline.AdvancedFeedbackEnabled,pipeline.ParameterSource,pipeline.Channels,pipeline.LeftTriggerSource,pipeline.RightTriggerSource,e.LearningState,audioStatus));
                    if(logSampling.Status(now))debugLog?.Record(new{Kind="status",At=now,Status=Current});lastCount=now;lastCpu=cpu;lastAllocated=allocated;writes=0;
                }
            }
        }
        catch(Exception ex){Volatile.Write(ref status,Current with{State="Error",Error=ex.Message,Output=FeedbackFrame.Off});RecordError(ex.ToString());}
        finally{voiceCoils.Submit(new(false,"",Guid.Empty,0),VoiceCoilCommand.Off);process.Dispose();}
    }
    private long lastLatencySequence=-1;
    private static double Percentile(Queue<double> q,double p){if(q.Count==0)return 0;var a=q.Order().ToArray();return a[(int)Math.Round((a.Length-1)*p)];}
    public void EmergencyStop(){try{voiceCoils.RequestStop();stop.Cancel();outputTask?.Wait(1500);}catch(Exception){/* Best effort only on process teardown. */}}
    public async ValueTask DisposeAsync(){voiceCoils.RequestStop();stop.Cancel();if(telemetryTask is not null && outputTask is not null)await Task.WhenAll(telemetryTask,outputTask);voiceCoils.Dispose();await StopRecording();if(debugLog is not null)await debugLog.DisposeAsync();stop.Dispose();}
    private sealed class TimerResolution:IDisposable
    {private readonly uint result=timeBeginPeriod(1);public void Dispose(){if(result==0)timeEndPeriod(1);}[DllImport("winmm.dll")]private static extern uint timeBeginPeriod(uint period);[DllImport("winmm.dll")]private static extern uint timeEndPeriod(uint period);}
}
