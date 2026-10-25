using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Physics;
namespace LMUDualSenseHaptics.Runtime;
// Pure pipeline shared by hardware loop and replay tests. Never owns a HID handle.
public sealed class FeedbackLoop
{
    public SignalEngine Signals {get;}=new();
    private FeedbackParameters? activeParameters;
    private BrakeFeedbackMode? activeBrakeMode;
    private VehiclePreset? activePreset;
    private readonly TriggerEngine triggers=new();public FrameMixer Mixer {get;}=new();
    public string Reason {get;private set;}="WaitingForLMU";
    public bool AdvancedFeedbackEnabled {get;private set;}
    public GripLayers Layers {get;private set;}
    public string ParameterSource {get;private set;}="等待驾驶";
    public ChannelLevels Channels {get;private set;}
    public bool LeftReferenceApplied => triggers.LeftReferenceApplied;
    public string LeftTriggerSource {get;private set;}="OFF";
    public string RightTriggerSource {get;private set;}="OFF";
    public FeedbackFrame Tick(TelemetryFrame? f,double now,Profile p)
    {
        try
        {
            p.Validate();
            if(f is null)return Off("WaitingForLMU");
            f.Validate();
            if(!f.Connected)return Off("Disconnected");
            if(f.Paused)return Off("Paused");
            if(!f.Driving)return Off("非驾驶状态");
            double age=now-f.CapturedAtSec+f.SourceAgeSec;
            if(age<0||age>p.TelemetryStaleMs/1000.0)return Off("Stale：遥测超时/时钟逆序");
            var preset=p.VehiclePresets.FirstOrDefault(v=>v.Matches(f));
            var parameters=preset?.Parameters??p.DefaultParameters;
            if(activeParameters!=parameters||activePreset!=preset||activeBrakeMode!=p.BrakeMode)
            {Signals.Reset();triggers.Reset();activeParameters=parameters;activePreset=preset;activeBrakeMode=p.BrakeMode;}
            Signals.Update(f,p,parameters);
            AdvancedFeedbackEnabled=true;
            ParameterSource=preset is null?"默认初始参数："+parameters.Name:"已验证车型预设："+parameters.Name;
            Reason="Driving："+ParameterSource;
            Layers=GripEngine.Render(Signals,f,now,p);
            var triggerFrame=triggers.Render(Signals,f,now,p);
            LeftTriggerSource=triggerFrame.Left.Mode==5?"OFF":triggers.LeftSource;
            RightTriggerSource=triggerFrame.Right.Mode==5?"OFF":triggers.RightSource;
            var output=Mixer.Mix(triggerFrame,Layers,p);
            double A(double value,double tau)=>Signals.At(value,now,f,tau);
            // Requested channel levels before nonlinear mixing, not physical acceleration.
            Channels=new(
                p.BrakeBase.Gain,
                RightTriggerSource=="ThrottleBase"?p.ThrottleBase.Gain:0,
                Math.Max(Signals.Braking.AbsWall*p.Abs.Gain,A(Signals.Abs,.105)*p.Abs.Gain*.12),
                LeftTriggerSource=="BrakeReference"?p.Lock.Gain:LeftTriggerSource is "Lock" or "LockPulse" or "BrakeLimit"?Math.Max(Signals.Lock,Signals.Braking.Risk)*p.Lock.Gain:0,
                RightTriggerSource=="Tc"?(p.ThrottleMode==ThrottleFeedbackMode.TcOnly?p.Tc.Gain:A(Signals.Tc,.135)*p.Tc.Gain):0,
                RightTriggerSource=="Wheelspin"?A(Signals.Spin,.175)*p.Wheelspin.Gain:0,
                A(Signals.Shift,.052)*p.Shift.Gain*(RightTriggerSource=="Shift"?1:.22),
                RightTriggerSource=="Redline"?A(Signals.Redline,.1)*p.Redline.Gain:0,
                Math.Max(Layers.RoadL,Layers.RoadR)*Mixer.ContinuousGain,
                Math.Max(Layers.SlipL,Layers.SlipR)*Mixer.ContinuousGain,
                Math.Max(Layers.EngineL,Layers.EngineR)*Mixer.EngineGain,
                Math.Max(Layers.ImpactL,Layers.ImpactR),
                Math.Max(Layers.KerbL,Layers.KerbR)*Mixer.ContinuousGain);
            return output;
        }
        catch(Exception ex) when(ex is InvalidDataException or ArgumentException){return Off("Error："+ex.Message);}
    }
    public FeedbackFrame Off(string reason)
    {
        Signals.Reset();triggers.Reset();activeParameters=null;activePreset=null;activeBrakeMode=null;AdvancedFeedbackEnabled=false;
        Layers=default;Channels=default;LeftTriggerSource=RightTriggerSource="OFF";ParameterSource="等待驾驶";Mixer.ResetDiagnostics();Reason=reason;return FeedbackFrame.Off;
    }
}
