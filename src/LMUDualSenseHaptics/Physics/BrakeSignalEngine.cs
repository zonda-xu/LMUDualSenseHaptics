using LMUDualSenseHaptics.Core;
using static LMUDualSenseHaptics.Physics.Maths;

namespace LMUDualSenseHaptics.Physics;

// Per-wheel longitudinal evidence. Lateral slip alone never means brake lock.
public sealed class BrakeSignalEngine
{
    public BrakeBudget Budget { get; } = new();
    public BrakeResponse Response { get; private set; } = new();
    private readonly double[] lastSlip = new double[4], rate = new double[4];
    private readonly bool[] valid = new bool[4];
    public int ValidSlipWheels { get; private set; }
    private BrakeFeedbackMode? previousMode;
    public BrakeFeedbackMode Mode { get; private set; }
    public double Risk { get; private set; }
    public double LockSeverity { get; private set; }
    public double FrontRisk { get; private set; }
    public double RearRisk { get; private set; }
    public double MaxSlip { get; private set; }
    public double MaxSlipRate { get; private set; }
    public double? AbsOnsetBrake { get; private set; }
    public double AbsWall { get; private set; }
    public string Evidence { get; private set; } = "等待驾驶";

    public static BrakeFeedbackMode ResolveMode(TelemetryFrame f, Profile p) =>
        p.BrakeMode != BrakeFeedbackMode.Auto ? p.BrakeMode :
        BrakeRouting.IsGt3(f) ? BrakeFeedbackMode.Abs : BrakeFeedbackMode.NoAbs;

    public void Reset()
    {
        Budget.Reset();
        Array.Clear(lastSlip); Array.Clear(rate); Array.Clear(valid); ValidSlipWheels=0;
        Risk = LockSeverity = FrontRisk = RearRisk = MaxSlip = MaxSlipRate = AbsWall = 0;
        AbsOnsetBrake = null; previousMode = null;
        Mode = BrakeFeedbackMode.Auto; Evidence = "等待驾驶";
    }

    public void Update(TelemetryFrame f, TelemetryFrame? previous, WheelState[] wheels,
        Profile p, FeedbackParameters parameters)
    {
        var mode = ResolveMode(f, p);
        if (previousMode != mode) Reset();
        Mode = mode; previousMode = mode;
        var c = parameters.BrakeResponse; Response=c;
        // Derivatives use physics time, not host scheduling jitter.
        double dt = previous is null ? 0 : f.SourceTimeSec - previous.SourceTimeSec;
        bool freshPair = dt is > 0 and <= .1;
        bool braking = f.Brake01 > parameters.InputThreshold && f.SpeedMps >= 5 && f.Gear >= 0;
        double front = 0, rear = 0, lockTarget = 0;
        MaxSlip = MaxSlipRate = 0;
        ValidSlipWheels=0;
        for(int i=0;i<4;i++)
        {
            var w=f.Wheels[i];
            bool usable=BrakeSlip.TryRead(w,parameters.ContactMinLoad,out double slip);
            bool pair=freshPair&&usable&&valid[i]&&previous is not null&&
                w.GroundLongitudinalMps*previous.Wheels[i].GroundLongitudinalMps>0;
            rate[i]=pair?Ema(rate[i],Math.Clamp((slip-lastSlip[i])/dt,-4,4),dt,.010):0;
            if(usable)ValidSlipWheels++;
            if(usable&&braking)
            {
                double rising=Math.Max(0,rate[i]);
                double predicted=Math.Min(1,slip+Math.Min(.12,rising*c.PredictionSeconds));
                double risk=Smooth(c.WarningSlip,c.CriticalSlip,predicted);
                if(i<2)front=Math.Max(front,risk);else rear=Math.Max(rear,risk);
                MaxSlip=Math.Max(MaxSlip,slip);MaxSlipRate=Math.Max(MaxSlipRate,rising);
                lockTarget=Math.Max(lockTarget,Smooth(c.CriticalSlip,c.LockSlip,slip));
            }
            lastSlip[i]=slip;valid[i]=usable;
        }
        if(Mode==BrakeFeedbackMode.NoAbs)Budget.Update(f,previous,MaxSlip,c,parameters.ContactMinLoad);
        bool abs = Mode == BrakeFeedbackMode.Abs && f.AbsIntervening && braking;
        if (!braking || ValidSlipWheels==0)
        {
            AbsOnsetBrake = null; AbsWall = Risk = LockSeverity = FrontRisk = RearRisk = 0;
        }
        else
        {
            if (abs)
                AbsOnsetBrake = Math.Min(AbsOnsetBrake ?? 1, f.Brake01);
            // Only remember onset inside this braking manoeuvre. A deliberate lift
            // below the wall releases it; never carry a fixed pedal limit into another corner.
            if (!abs && AbsOnsetBrake is double onset && f.Brake01 < onset - .06)
                AbsOnsetBrake = null;
            AbsWall = abs ? 1 : AbsOnsetBrake is double threshold ? Smooth(threshold - .04, threshold, f.Brake01) : 0;
            double step = freshPair ? dt : .004;
            FrontRisk = Env(FrontRisk, front, step, c.AttackSeconds, c.ReleaseSeconds);
            RearRisk = Env(RearRisk, rear, step, c.AttackSeconds, c.ReleaseSeconds);
            Risk = Mode == BrakeFeedbackMode.NoAbs ? Math.Max(FrontRisk, RearRisk) : 0;
            LockSeverity = Mode == BrakeFeedbackMode.NoAbs ? Env(LockSeverity, lockTarget, step, c.AttackSeconds, c.ReleaseSeconds) : 0;
        }
        Evidence = Mode == BrakeFeedbackMode.Auto ? "ABS 状态未知，请选择刹车模式" :
            Mode == BrakeFeedbackMode.Abs ? "GT3 旧版 / 手动 ABS" :
            ($"接触区滑移有效 {ValidSlipWheels}/4")+" · "+Budget.Evidence;
    }
}
