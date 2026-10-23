using System.Text.Json;
using System.Threading.Channels;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Config;
namespace LMUDualSenseHaptics.Diagnostics;
internal sealed class TelemetryRecorder:IAsyncDisposable
{
    private readonly Channel<object> queue=Channel.CreateBounded<object>(new BoundedChannelOptions(256){SingleReader=true,FullMode=BoundedChannelFullMode.Wait});
    private readonly Task worker;public long Dropped;public string? Failure;
    private int stopped;
    public bool Stopped => Volatile.Read(ref stopped) != 0;
    public const long RecordingLimitBytes = 128L * 1024 * 1024;
    public const long DebugLimitBytes = 16L * 1024 * 1024;
    public TelemetryRecorder(string path, long maximumBytes = RecordingLimitBytes)
    {
        if(maximumBytes < 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        worker=Task.Run(async()=>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var output=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read,65536,true);
                long written=0;
                byte[] end=System.Text.Encoding.UTF8.GetBytes("{\"Kind\":\"recording-stop\",\"Reason\":\"size-limit\"}\n");
                await foreach(var item in queue.Reader.ReadAllAsync())
                {
                    var bytes=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item,ProfileStore.JsonCompact)+"\n");
                    if(written+bytes.Length+end.Length>maximumBytes)
                    {
                        Failure=$"达到日志大小上限（{maximumBytes/1048576} MiB），已停止写入";
                        Interlocked.Exchange(ref stopped,1);queue.Writer.TryComplete();
                        await output.WriteAsync(end);break;
                    }
                    await output.WriteAsync(bytes);written+=bytes.Length;
                }
            }
            catch(Exception e){Failure=e.Message;Interlocked.Exchange(ref stopped,1);queue.Writer.TryComplete();}
            finally{while(queue.Reader.TryRead(out _)){} }
        });
    }
    public void Record(object item){if(!Stopped&&!queue.Writer.TryWrite(item))Interlocked.Increment(ref Dropped);}
    public async ValueTask DisposeAsync(){queue.Writer.TryComplete();await worker;}
}
internal sealed class LogSampling
{
    private double telemetry=double.NegativeInfinity,output=double.NegativeInfinity,status=double.NegativeInfinity;
    private double errorAt=double.NegativeInfinity;private string error="";private readonly object errorGate=new();
    private static bool Allow(ref double previous,double now,double interval)
    {if(now-previous<interval-1e-9)return false;previous=now;return true;}
    public bool Telemetry(double now)=>Allow(ref telemetry,now,.020);
    public bool Output(double now)=>Allow(ref output,now,.100);
    public bool Status(double now)=>Allow(ref status,now,.500);
    public bool Error(string message,double now)
    {lock(errorGate){if(message==error&&now-errorAt<10)return false;error=message;errorAt=now;return true;}}
}
internal sealed record OutputRecord(string Kind,double At,long Sequence,double SourceCapturedAt,double WriteMilliseconds,bool Success,FeedbackFrame Frame,
    double Abs,double Lock,double Tc,double Spin,double Shift,double Redline,GripLayers Layers,double PreLeft,double PreRight,double EngineGain,double ContinuousGain,double OppositeGain,long ClippedFrames,string ParameterSource,ChannelLevels Channels,string LeftTriggerSource,string RightTriggerSource,VoiceCoilCommand? AudioCommand=null,bool AudioReady=false,BrakeDiagnostic? Brake=null);
internal sealed record BrakeDiagnostic(double Prompt,double EstimatedLimit,double Confidence,int IndependentStops,double LoadFactor,double DemandRate,string Context,string Evidence,double Slip,double Risk,int ValidSlipWheels);
