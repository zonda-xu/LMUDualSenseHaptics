using LMUDualSenseHaptics.Core;
using static LMUDualSenseHaptics.Physics.Maths;

namespace LMUDualSenseHaptics.Physics;

// An empirical pedal reference, NOT remaining tyre grip or a brake controller.
public sealed class BrakeBudget
{
    private sealed class Sample { public double Mean; public int Stops; }
    private readonly Dictionary<string, Sample> samples = new();
    private readonly double[] referenceLoad = new double[4], minLoad = new double[4], maxLoad = new double[4];
    private readonly int[] loadFrames = new int[4];
    private bool sampledStop;
    private double previousSlip, lastTime = double.NaN;
    private long intervals;
    private double intervalSum, intervalMin = double.PositiveInfinity, intervalMax;
    public double Prompt { get; private set; }
    public double EstimatedLimit { get; private set; }
    public double Confidence { get; private set; }
    public double LoadFactor { get; private set; } = 1;
    public double DemandRate { get; private set; }
    public int Stops { get; private set; }
    public int AcceptedStops { get; private set; }
    public bool SampleAccepted { get; private set; }
    public double? ObservedOnsetBrake { get; private set; }
    public int ContextCount => samples.Count;
    public string Context { get; private set; } = "";
    public string Evidence { get; private set; } = "初始参考，未标定";
    public string FrameTiming => intervals == 0 ? "等待有效帧间隔" :
        $"物理帧间隔 min/avg/max={intervalMin*1000:F1}/{intervalSum/intervals*1000:F1}/{intervalMax*1000:F1} ms";

    public void Reset()
    {
        samples.Clear(); Array.Clear(referenceLoad); Array.Clear(minLoad); Array.Clear(maxLoad); Array.Clear(loadFrames);
        AcceptedStops=0;SampleAccepted=false;ObservedOnsetBrake=null;
        sampledStop=false;previousSlip=0;lastTime=double.NaN;intervals=0;intervalSum=intervalMax=0;intervalMin=double.PositiveInfinity;
        Prompt=EstimatedLimit=Confidence=DemandRate=0;Stops=0;LoadFactor=1;Context="";Evidence="初始参考，未标定";
    }

    private static int Bin(float? value, double width) => value is float v ? (int)Math.Floor(v/width) : -999;
    private static string Key(TelemetryFrame f) =>
        $"{(int)(f.SpeedMps/10)}:{(int)(Math.Abs(f.AccelX)/2.5)}:{Bin(f.RearBrakeBias,.02)}:{f.BrakeMigration}:{f.MotorState}:{Bin(f.MotorTorqueNm,50)}:" +
        string.Join("/",f.Wheels.Select(w=>$"{w.CompoundIndex},{w.SurfaceType},{Bin(w.TireTemperatureK,20)},{Bin(w.Wear01,.1)},{Bin(w.TirePressureKpa,20)}"));

    public void Update(TelemetryFrame f, TelemetryFrame? previous, double slip, BrakeResponse c, double contactMin)
    {
        double dt = previous is null ? 0 : f.SourceTimeSec-previous.SourceTimeSec;
        bool pair = dt is >0 and <=.1;
        if(f.SourceTimeSec==lastTime)return;
        lastTime=f.SourceTimeSec;SampleAccepted=false;ObservedOnsetBrake=null;
        if(pair){intervals++;intervalSum+=dt;intervalMin=Math.Min(intervalMin,dt);intervalMax=Math.Max(intervalMax,dt);}
        bool driving=f.Driving&&!f.Paused&&f.SpeedMps>=5&&f.Gear>=0;
        bool braking=f.Brake01>.02;
        if(!braking)sampledStop=false;
        LoadFactor=1; int validLoads=0;
        for(int i=0;i<4;i++)
        {
            var w=f.Wheels[i];
            // Zero, implausible, constant and airborne channels do not establish
            // capacity. Suspension force is deliberately not substituted for load.
            bool valid=w.TireLoadN is >100 and <50000 && WheelContact.IsGrounded(w,contactMin);
            if(!valid){loadFrames[i]=0;minLoad[i]=maxLoad[i]=referenceLoad[i]=0;continue;}
            if(loadFrames[i]==0)minLoad[i]=maxLoad[i]=w.TireLoadN;
            minLoad[i]=Math.Min(minLoad[i],w.TireLoadN);maxLoad[i]=Math.Max(maxLoad[i],w.TireLoadN);loadFrames[i]++;
            if(!braking && Math.Abs(f.AccelX)<2 && Math.Abs(f.Steering)<.1)
                referenceLoad[i]=referenceLoad[i]==0?w.TireLoadN:Ema(referenceLoad[i],w.TireLoadN,pair?dt:.01,.5);
            bool varying=loadFrames[i]>=8 && maxLoad[i]-minLoad[i]>Math.Max(25,.01*maxLoad[i]);
            if(varying && referenceLoad[i]>100)
            {
                validLoads++;
                // Only reduce on unloading. Never assume extra load grants an
                // equal percentage of extra friction or increase the pedal limit.
                LoadFactor=Math.Min(LoadFactor,Math.Clamp(w.TireLoadN/referenceLoad[i],.6,1));
            }
        }
        double lateral=1-c.LateralReduction*C(Math.Abs(f.AccelX)/12);
        double stateFactor=LoadFactor*lateral;
        Context=Key(f);
        samples.TryGetValue(Context,out var sample);
        // At most one observation per braking manoeuvre. Require independently
        // validated patch velocities and an onset crossing; safe braking is not a limit sample.
        bool onset=slip>=c.WarningSlip && previousSlip<c.WarningSlip;
        bool clean=driving&&braking&&pair&&f.Brake01>.15&&!f.AbsIntervening&&!f.TcIntervening&&
            (f.FilteredBrake01 is not float filtered || filtered<=.02 || Math.Abs(filtered-f.Brake01)<.1)&&
            previous!.Brake01>.15&&Math.Abs(f.Brake01-previous.Brake01)<.15&&
            f.LastImpactTime==previous.LastImpactTime&&f.Wheels.All(w=>BrakeSlip.TryRead(w,contactMin,out _))&&
            f.Wheels.All(w=>WheelContact.IsGrounded(w,contactMin)&&!w.IsKerb);
        if(onset&&clean&&!sampledStop)
        {
            if(sample is null)
            {
                if(samples.Count>=128)samples.Remove(samples.Keys.First());
                samples[Context]=sample=new();
            }
            double observed=Math.Clamp(Math.Min(f.Brake01,Math.Max(.15,previous!.Brake01))/stateFactor,.25,1);
            sample.Mean=sample.Stops==0?observed:sample.Mean+.25*(observed-sample.Mean);
            sample.Stops=Math.Min(20,sample.Stops+1);sampledStop=true;
            AcceptedStops++;SampleAccepted=true;ObservedOnsetBrake=Math.Min(f.Brake01,Math.Max(.15,previous!.Brake01));
        }
        previousSlip=braking?slip:0;
        Stops=sample?.Stops??0;
        Confidence=Stops>=3?Math.Min(.8,Stops/10.0):0;
        // Unconfirmed samples may lower a reference, never raise it. Raising a
        // reference requires several independent stops in the same state bin.
        double learned=sample is null?c.InitialLimit:Stops<3?Math.Min(c.InitialLimit,sample.Mean):sample.Mean;
        EstimatedLimit=Math.Clamp(learned*stateFactor,.25,.95);
        DemandRate=pair?Math.Clamp((f.Brake01-previous!.Brake01)/dt,0,10):0;
        if(pair&&braking)
        {
            for(int i=0;i<4;i++)
            {
                // Convert the *change* of a live normalised pressure channel to
                // pedal units using its current ratio, not pressure/load = grip.
                if(f.Wheels[i].BrakePressure01 is float pressure && pressure>.02 &&
                   previous!.Wheels[i].BrakePressure01 is float old && old>.02)
                {
                    double ratio=pressure/Math.Max(f.Brake01,.1);
                    if(ratio is >=.25 and <=2)
                        DemandRate=Math.Max(DemandRate,Math.Clamp((pressure-old)/dt/ratio,0,10));
                }
            }
        }
        double margin=c.UncertaintyMargin*(1-.5*Confidence)+(validLoads<4?.02:0);
        Prompt=driving?Math.Clamp(EstimatedLimit-margin-Math.Min(.15,c.DemandLeadSeconds*DemandRate),c.MinimumPrompt,.9):0;
        Evidence=$"{(Stops>=3?"多次滑移起点参考":Stops>0?"单次起点，低置信度":"初始参考，未标定")} · 有效载荷 {validLoads}/4 · {FrameTiming}";
    }
}
