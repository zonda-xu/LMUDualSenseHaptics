using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Physics;

namespace LMUDualSenseHaptics.Haptics;

public static class BrakeTrigger
{
    private static readonly byte[] Base = [0,1,1,2,2,3,3,4,4,5];
    private static readonly byte[] AbsBase = [5,5,6,6,7,7,8,8,8,8];

    public static (TriggerEffect Effect, string Source) Render(BrakeSignalEngine e, TelemetryFrame f, double now, Profile p)
    {
        Span<byte> zones = stackalloc byte[10];
        var mode = BrakeSignalEngine.ResolveMode(f, p);
        var baseline=mode==BrakeFeedbackMode.Abs?AbsBase:Base;
        for (int i = 0; i < 10; i++) zones[i] = (byte)Math.Clamp((int)(mode==BrakeFeedbackMode.Abs?Math.Ceiling(baseline[i]*p.BrakeBase.Gain):Math.Round(baseline[i]*p.BrakeBase.Gain)), 0, 8);
        double age = Math.Max(0, now - f.CapturedAtSec + f.SourceAgeSec);
        double fade = Math.Exp(-age / .040);
        bool braking = f.Brake01 > 0 && f.SpeedMps >= 5 && f.Gear >= 0;
        double demand = !braking ? 0 : mode == BrakeFeedbackMode.Abs ? e.AbsWall * p.Abs.Gain :
            mode == BrakeFeedbackMode.NoAbs ? e.Risk * p.Lock.Gain : 0;
        demand *= fade;
        string source = "BrakeBase";
        // Intervention gate depends on measured severity, not the gain slider.
        // Hardware generates the pulse; no one-level host-side modulation.
        if(mode==BrakeFeedbackMode.NoAbs && braking && p.Lock.Gain>0 && e.LockSeverity*fade>.25)
        {
            byte amplitude=(byte)Math.Clamp((int)Math.Ceiling(8*p.Lock.Gain*Math.Max(.5,e.LockSeverity*fade)),1,8);
            return (TriggerEffect.Vibration(0,amplitude,24),"LockPulse");
        }
        if(mode==BrakeFeedbackMode.NoAbs && e.ValidSlipWheels==4 && e.Budget.Confidence>=.3 && e.Budget.Prompt>0 && p.Lock.Gain>0 && fade>.025)
        {
            // Wall is installed ahead of the finger, even at zero slip/input.
            int wall=Math.Clamp((int)Math.Floor(e.Response.TravelFor(e.Budget.Prompt)*10),0,9);
            int force=Math.Clamp((int)Math.Round(2*p.Lock.Gain*fade),0,2);
            for(int i=wall;i<10;i++)zones[i]=(byte)Math.Min(8,zones[i]+force);
            source="BrakeReference";
        }
        bool warning=mode==BrakeFeedbackMode.NoAbs && braking && p.Lock.Gain>0 && fade>.025 &&
            (e.MaxSlip>=e.Response.WarningSlip || e.Risk>.025);
        if (mode==BrakeFeedbackMode.Abs ? demand>.025 : warning)
        {
            source = mode == BrakeFeedbackMode.Abs ? "AbsWall" : e.LockSeverity > .5 ? "Lock" : "BrakeLimit";
            // One zone before the estimated pedal position puts the force on the
            // finger immediately. Pedal-to-travel alignment assumes linear game input.
            double position = mode == BrakeFeedbackMode.Abs ? e.AbsOnsetBrake ?? f.Brake01 : f.Brake01;
            int start = Math.Clamp((int)Math.Floor(e.Response.TravelFor(position) * 10) - 1, 0, 8);
            int added = Math.Clamp((int)Math.Ceiling((mode==BrakeFeedbackMode.Abs?6:8) * demand), 1, 8);
            for (int i = start; i < 10; i++) zones[i] = (byte)Math.Min(8, zones[i] + added);
        }
        bool any = false; foreach (byte z in zones) any |= z > 0;
        return (any ? TriggerEffect.FeedbackZones(zones) : TriggerEffect.Off, source);
    }
}
