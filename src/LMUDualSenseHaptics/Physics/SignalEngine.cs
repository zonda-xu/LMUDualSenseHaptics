using System.Numerics;
using LMUDualSenseHaptics.Core;
using static LMUDualSenseHaptics.Physics.Maths;
namespace LMUDualSenseHaptics.Physics;
public sealed class WheelState
{
    public double Radius,RadiusTime,PreviousSusp,SlowVelocity,ForceReference,LoadReference,Shock,Slip,AngleBaseline,RatioBaseline;
    public double NoiseVelocity,NoiseForce,NoiseLoad,RoadLearnTime;
    public int RadiusSamples;
    public double? Kappa;
    public double Alpha;
    public bool Learning;
}
public sealed class SignalEngine
{
    public RoadSignalEngine RoadSignals {get;}=new();
    public WheelState[] Wheels {get;private set;}=Enumerable.Range(0,4).Select(_=>new WheelState()).ToArray();
    public double Abs,Lock,Tc,Spin,Shift,Redline,SidePan,FrontShare,LeftRoad,RightRoad,Impact,ImpactPan,Scrape;
    public double ShiftStart=double.NegativeInfinity,ImpactStart=double.NegativeInfinity;
    public string LearningState {get;private set;}="等待新物理帧";
    private double cooldown,lastImpactTime,pendingImpactUntil=double.NegativeInfinity,lastImpactObserved=double.NegativeInfinity;
    private TelemetryFrame? previous;
    private readonly Queue<(double T,Vector2 V)> velocityHistory=new();
    public void Reset()
    {
        RoadSignals.Reset();
        Wheels=Enumerable.Range(0,4).Select(_=>new WheelState()).ToArray();
        Abs=Lock=Tc=Spin=Shift=Redline=SidePan=FrontShare=LeftRoad=RightRoad=Impact=ImpactPan=Scrape=0;
        ShiftStart=ImpactStart=pendingImpactUntil=lastImpactObserved=double.NegativeInfinity;
        cooldown=lastImpactTime=0;previous=null;velocityHistory.Clear();LearningState="等待新物理帧";
    }
    public void Update(TelemetryFrame f,Profile p,FeedbackParameters? parameters=null)
    {
        f.Validate();var c=parameters??p.DefaultParameters;
        if(previous?.Sequence==f.Sequence)return;
        double dt=previous is null?0:f.CapturedAtSec-previous.CapturedAtSec;
        if(previous is null||previous.CarId!=f.CarId||previous.GameVersion!=f.GameVersion||
            previous.VehicleModel!=f.VehicleModel||previous.VehicleClass!=f.VehicleClass||dt<=0||dt>.25||f.SourceTimeSec<=previous.SourceTimeSec)
        {
            Reset();previous=f;lastImpactTime=f.LastImpactTime;
            RoadSignals.Update(f,c.RoadResponse,p.RoadSensitivity/100.0,p.AudioHaptics.DynamicKerbFallback);
            for(int i=0;i<4;i++)
            {
                Wheels[i].PreviousSusp=f.Wheels[i].SuspDeflectionM;
                Wheels[i].ForceReference=f.Wheels[i].SuspForceN;Wheels[i].LoadReference=f.Wheels[i].TireLoadN;
            }
            velocityHistory.Enqueue((f.CapturedAtSec,new(f.LocalVxMps,f.LocalVzMps)));return;
        }
        RoadSignals.Update(f,c.RoadResponse,p.RoadSensitivity/100.0,p.AudioHaptics.DynamicKerbFallback);
        // Completed impacts must stop inhibiting learning after the envelope ends.
        if(f.CapturedAtSec-ImpactStart>.22){Impact=ImpactPan=0;}
        bool newImpact=f.LastImpactTime>lastImpactTime&&f.LastImpactMagnitude>0;
        if(newImpact){pendingImpactUntil=f.CapturedAtSec+.08;lastImpactObserved=f.CapturedAtSec;}
        double horizontalJerk=new Vector2(f.AccelX-previous.AccelX,f.AccelZ-previous.AccelZ).Length()/dt;
        Span<double> candidates=stackalloc double[4],hp=stackalloc double[4],forceChange=stackalloc double[4],loadChange=stackalloc double[4];
        bool stable=f.SpeedMps>=8&&f.Brake01<.03&&f.Throttle01<=c.LearningMaxThrottle&&Math.Abs(f.Steering)<.08&&
            !f.AbsIntervening&&!f.TcIntervening&&Math.Abs(f.AccelX)<c.LearningMaxLatAccel&&Math.Abs(f.AccelZ)<c.LearningMaxLongAccel&&
            horizontalJerk<c.JerkThreshold*.5&&f.CapturedAtSec-lastImpactObserved>.5&&f.Gear==previous.Gear&&f.CapturedAtSec-ShiftStart>.25;
        for(int i=0;i<4;i++)
        {
            var q=f.Wheels[i];var z=Wheels[i];double g=Math.Abs(q.GroundLongitudinalMps),o=Math.Abs(q.OmegaRadPerSec);
            z.Alpha=SlipAngle(q.GroundLateralMps,q.GroundLongitudinalMps);candidates[i]=g/Math.Max(o,1);
            double vel=(q.SuspDeflectionM-z.PreviousSusp)/dt;
            z.SlowVelocity=Ema(z.SlowVelocity,vel,dt,.18);hp[i]=Math.Abs(vel-z.SlowVelocity);
            // Short filters measure changes. They are not the learned normal-road baseline.
            z.ForceReference=Ema(z.ForceReference,q.SuspForceN,dt,.20);z.LoadReference=Ema(z.LoadReference,q.TireLoadN,dt,.20);
            forceChange[i]=Math.Abs(q.SuspForceN-z.ForceReference)/Math.Max(Math.Abs(z.ForceReference),c.Fmin);
            loadChange[i]=Math.Abs(q.TireLoadN-z.LoadReference)/Math.Max(z.LoadReference,c.Lmin);
            stable &= g>=8&&o>10&&WheelContact.IsGrounded(q,c.ContactMinLoad)&&!q.IsKerb&&Math.Abs(z.Alpha)<.035&&
                Math.Abs(g-f.SpeedMps)/Math.Max(f.SpeedMps,1)<.08&&candidates[i] is >.15 and <.60&&
                hp[i]<c.V1*.5&&forceChange[i]<c.F1*.5&&loadChange[i]<c.L1*.5&&z.Shock<.1&&z.Slip<.1;
        }
        // Compare each axle's effective radius, not raw RPM across different tyre sizes.
        bool consistent=true;
        for(int i=0;i<4;i+=2)
            consistent &= Math.Abs(candidates[i]-candidates[i+1])/Math.Max((candidates[i]+candidates[i+1])*.5,.01)<=c.AxleRadiusTolerance;
        for(int i=0;i<4;i++)
        {
            var z=Wheels[i];
            if(z.RadiusTime>=c.RadiusTrustedSeconds&&Math.Abs(candidates[i]/z.Radius-1)>c.RadiusDriftTolerance)consistent=false;
        }
        bool learn=stable&&consistent;
        LearningState=learn?"可信直线滚动：正在学习半径与正常路面":"学习冻结：低速、操控/动力变化、轮速不一致或异常路面";
        for(int i=0;i<4;i++)
        {
            var q=f.Wheels[i];var z=Wheels[i];z.Learning=learn;
            if(learn)
            {
                // Before trust is established, inconsistent candidates restart the sample duration.
                if(z.RadiusSamples>0&&Math.Abs(candidates[i]/z.Radius-1)>c.RadiusDriftTolerance)
                {z.RadiusSamples=0;z.RadiusTime=0;}
                z.Radius=z.RadiusSamples==0?candidates[i]:Ema(z.Radius,candidates[i],dt,2);
                z.RadiusSamples++;z.RadiusTime+=dt;
                // Bounded noise floors cannot swallow kerbs, high slip or collision samples.
                z.NoiseVelocity=Ema(z.NoiseVelocity,Math.Min(hp[i],c.V0),dt,c.RoadLearningSeconds);
                z.NoiseForce=Ema(z.NoiseForce,Math.Min(forceChange[i],c.F0),dt,c.RoadLearningSeconds);
                z.NoiseLoad=Ema(z.NoiseLoad,Math.Min(loadChange[i],c.L0),dt,c.RoadLearningSeconds);
                z.RoadLearnTime+=dt;
                z.AngleBaseline=Ema(z.AngleBaseline,Math.Abs(z.Alpha),dt,3);
            }
            double g=Math.Abs(q.GroundLongitudinalMps),o=Math.Abs(q.OmegaRadPerSec);
            z.Kappa=z.RadiusTime>=c.RadiusTrustedSeconds&&f.SpeedMps>=5&&g>=5&&WheelContact.IsGrounded(q,c.ContactMinLoad)?
                (o*z.Radius-g)/Math.Max(g,1):null;
            if(learn&&z.Kappa is double baselineK)z.RatioBaseline=Ema(z.RatioBaseline,Math.Abs(baselineK),dt,3);
            // Do not multiply suspension evidence by force/load evidence: either
            // may legitimately stay almost constant while a tyre crosses a kerb.
            z.Shock=RoadSignals.Wheels[i].Combined;
            double lateral=Smooth(z.AngleBaseline+c.AngleOffset0,z.AngleBaseline+c.AngleOffset1,Math.Abs(z.Alpha));
            double longitudinal=z.Kappa is double k?Smooth(z.RatioBaseline+c.RatioThreshold0,z.RatioBaseline+c.RatioThreshold1,Math.Abs(k)):0;
            z.Slip=WheelContact.IsGrounded(q,c.ContactMinLoad)
                ? Env(z.Slip,Math.Max(longitudinal,lateral*c.LateralWeight)*Smooth(5,15,f.SpeedMps),dt,.008,.065) : 0;
            z.PreviousSusp=q.SuspDeflectionM;
        }
        double bg=Smooth(.08,.28,f.Brake01),tg=Smooth(.08,.32,f.Throttle01),sg=Smooth(4,16,f.SpeedMps);
        double ar=f.AbsIntervening?1:0,tr=f.TcIntervening?1:0;
        Abs=Env(Abs,ar*bg,dt,.032,.105);Tc=Env(Tc,tr*tg,dt,.060,.135);
        static double Cue(double ratio,double start,double full)=>ratio>=start?.25+.75*Smooth(start,full,ratio):0;
        Lock=Env(Lock,(1-ar)*(f.Brake01>c.InputThreshold?1:0)*Wheels.Max(q=>q.Kappa is double k?Cue(Math.Max(0,-k),c.LockRatio,c.LockFullRatio):0)*sg,dt,.050,.135);
        Spin=Env(Spin,(1-tr)*(f.Throttle01>c.InputThreshold?1:0)*Wheels.Max(q=>q.Kappa is double k?Cue(Math.Max(0,k),c.SpinRatio,c.SpinFullRatio):0)*sg,dt,.085,.175);
        if(previous.Gear>0&&f.Gear>previous.Gear&&f.SpeedMps>8/3.6&&f.Throttle01>.18)ShiftStart=f.CapturedAtSec;
        Shift=Env(Shift,f.CapturedAtSec-ShiftStart<.115?1:0,dt,.010,.052);
        Redline=Env(Redline,f.MaxRpm>0?Smooth(p.RedlineStartRatio,.99,f.Rpm/f.MaxRpm)*tg:0,dt,.03,.10);
        double le=Math.Max(Wheels[0].Shock,Wheels[2].Shock)+.35*Math.Min(Wheels[0].Shock,Wheels[2].Shock);
        double re=Math.Max(Wheels[1].Shock,Wheels[3].Shock)+.35*Math.Min(Wheels[1].Shock,Wheels[3].Shock);
        double side=(re-le)/(re+le+1e-6)*Smooth(.05,.28,Math.Max(le,re));
        SidePan=Ema(SidePan,side,dt,Math.Abs(side)>Math.Abs(SidePan)?.010:.080);
        double front=Math.Max(Wheels[0].Shock,Wheels[1].Shock),rear=Math.Max(Wheels[2].Shock,Wheels[3].Shock);
        FrontShare=front/(front+rear+1e-6);
        double opp=1-.88*Smooth(.25,.80,Math.Abs(SidePan))*p.SpatialSeparation/100.0;
        LeftRoad=Math.Max(Wheels[0].Shock,Wheels[2].Shock);RightRoad=Math.Max(Wheels[1].Shock,Wheels[3].Shock);
        if(SidePan>0)LeftRoad=Math.Max(LeftRoad*opp,RightRoad*.12);else if(SidePan<0)RightRoad=Math.Max(RightRoad*opp,LeftRoad*.12);
        while(velocityHistory.Count>0&&f.CapturedAtSec-velocityHistory.Peek().T>.14)velocityHistory.Dequeue();
        var old=velocityHistory.OrderBy(v=>Math.Abs(v.T-(f.CapturedAtSec-.1))).FirstOrDefault();
        var velocity=new Vector2(f.LocalVxMps,f.LocalVzMps);
        double dv=velocityHistory.Count>0&&f.CapturedAtSec-old.T>=.07?(velocity-old.V).Length():0;
        double horizontalAccel=new Vector2(f.AccelX,f.AccelZ).Length();
        // An event is mandatory. Vertical suspension motion alone never confirms a collision.
        bool confirmed=f.CapturedAtSec<=pendingImpactUntil&&(horizontalAccel>2||dv>.15);
        if(confirmed&&f.CapturedAtSec>cooldown)
        {
            Impact=.2+.8*Math.Max(Smooth(c.JerkThreshold,c.JerkThreshold*4,horizontalJerk),Smooth(c.DeltaVThreshold,c.DeltaVThreshold*3,dv));
            ImpactStart=f.CapturedAtSec;cooldown=f.CapturedAtSec+.22;pendingImpactUntil=double.NegativeInfinity;
            ImpactPan=Math.Clamp(f.AccelX/Math.Max(horizontalAccel,1),-1,1);
        }
        Scrape=Env(Scrape,newImpact&&Math.Abs(f.AccelX)>5?.3:0,dt,.03,.08);
        lastImpactTime=f.LastImpactTime;velocityHistory.Enqueue((f.CapturedAtSec,velocity));previous=f;
    }
    public static double SlipAngle(double lateral,double longitudinal)
    {
        double a=Math.Atan2(lateral,longitudinal);
        return a>Math.PI/2?a-Math.PI:a<-Math.PI/2?a+Math.PI:a;
    }
    public double At(double value,double now,TelemetryFrame f,double release)=>value*Math.Exp(-Math.Max(0,now-f.CapturedAtSec)/release);
}
