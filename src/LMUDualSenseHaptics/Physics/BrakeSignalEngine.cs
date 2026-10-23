using LMUDualSenseHaptics.Core;
using static LMUDualSenseHaptics.Physics.Maths;

namespace LMUDualSenseHaptics.Physics;

// Per-wheel longitudinal evidence. Lateral slip alone never means brake lock.
public sealed class BrakeSignalEngine
{
    public BrakeBudget Budget { get; } = new();
    public BrakeResponse Response { get; private set; } = new();
    private readonly double[] rollingRatio = new double[4], reference = new double[4], loss = new double[4], rate = new double[4];
    private readonly bool[] contact = new bool[4];
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
        Array.Clear(rollingRatio); Array.Clear(reference); Array.Clear(loss); Array.Clear(rate); Array.Clear(contact);
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
        bool absoluteEvidence = false, trendEvidence = false;
        for (int i = 0; i < 4; i++)
        {
            var w = f.Wheels[i];
            double ground = Math.Abs(w.GroundLongitudinalMps), omega = Math.Abs(w.OmegaRadPerSec);
            bool grounded = WheelContact.IsGrounded(w, parameters.ContactMinLoad) && ground >= 5;
            double ratio = grounded ? omega / ground : 0;
            bool pair = freshPair && grounded && contact[i] && previous is not null &&
                w.GroundLongitudinalMps * previous.Wheels[i].GroundLongitudinalMps > 0 &&
                w.OmegaRadPerSec * previous.Wheels[i].OmegaRadPerSec >= 0;
            double change = pair && rollingRatio[i] > 1e-6 ? ratio / rollingRatio[i] : 1;
            // Accumulate wheel-speed loss relative to its own local ground speed.
            // This catches collapse before radius learning finishes, and retains
            // the loss while the wheel stays slow. It resets at brake release.
            if (!braking || !grounded || !pair) { loss[i] = 0; rate[i] = 0; reference[i] = ratio; }
            else
            {
                if (reference[i] <= 1e-6) reference[i] = rollingRatio[i];
                loss[i] = reference[i] > 1e-6 ? C(1 - ratio / reference[i]) : 0;
                rate[i] = Ema(rate[i], Math.Clamp((1 - change) / dt, -4, 4), dt, .010);
            }
            if (grounded && braking)
            {
                double slip = wheels[i].Kappa is double k ? C(-k) : loss[i];
                // Anchor cumulative loss whenever the independently learned radius is trusted.
                if (wheels[i].Kappa is not null)
                {
                    loss[i] = slip; absoluteEvidence = true;
                    if (slip < .95 && ratio > 1e-6) reference[i] = ratio / (1 - slip);
                }
                else if (pair) trendEvidence = true;
                double rising = Math.Max(0, rate[i]) * (1 - slip);
                double predicted = Math.Min(1, slip + Math.Min(.12, rising * c.PredictionSeconds));
                double risk = Smooth(c.WarningSlip, c.CriticalSlip, predicted);
                // Pressure and sliding fraction only reinforce longitudinal evidence.
                // Neither is interpreted as a friction limit or a standalone lock event.
                if (previous is not null && pair && w.BrakePressure01 is float pressure &&
                    previous.Wheels[i].BrakePressure01 is float oldPressure && pressure > oldPressure + .01)
                    risk = C(risk * 1.1);
                if (w.SlidingFraction is float fraction) risk = C(risk * (1 + .15 * fraction));
                if (i < 2) front = Math.Max(front, risk); else rear = Math.Max(rear, risk);
                MaxSlip = Math.Max(MaxSlip, slip); MaxSlipRate = Math.Max(MaxSlipRate, rising);
                lockTarget = Math.Max(lockTarget, Smooth(c.CriticalSlip, c.LockSlip, slip));
            }
            rollingRatio[i] = ratio; contact[i] = grounded;
        }
        if(Mode==BrakeFeedbackMode.NoAbs)Budget.Update(f,previous,wheels,MaxSlip,c,parameters.ContactMinLoad);
        bool abs = Mode == BrakeFeedbackMode.Abs && f.AbsIntervening && braking;
        if (!braking)
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
            (absoluteEvidence ? "逐轮滑移" : trendEvidence ? "轮速趋势" : "等待轮速校正")+" · "+Budget.Evidence;
    }
}
