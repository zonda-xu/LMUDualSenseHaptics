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
        // No-ABS effects are firmware oscillators: hold the latest valid physics
        // state between packets. FeedbackLoop owns the telemetry timeout/reset.
        // Preserve the existing manual ABS renderer.
        bool braking = f.Brake01 > 0 && f.SpeedMps >= 5 && f.Gear >= 0;
        double demand = mode==BrakeFeedbackMode.Abs && braking ? e.AbsWall*p.Abs.Gain*Math.Exp(-age/.040) : 0;
        string source = "BrakeBase";
        // Intervention gate depends on measured severity, not the gain slider.
        // Hardware generates the pulse; no one-level host-side modulation.
        if(mode==BrakeFeedbackMode.NoAbs && braking && p.Lock.Gain>0 && (e.LockActive||e.WarningActive))
        {
            bool locked=e.LockActive;
            // Linear gain softens moderate/default settings without changing detection,
            // cue timing or the full-strength endpoint. Zero still disables cues.
            double strength=locked?.85+.15*e.LockSeverity:.55+.35*e.Risk;
            byte amplitude=(byte)Math.Clamp((int)Math.Ceiling(8*p.Lock.Gain*strength),1,8);
            // Starts at zero so even a fully depressed trigger receives the pulse.
            return (TriggerEffect.Vibration(0,amplitude,(byte)(locked?24:12)),locked?"LockPulse":"BrakeLimit");
        }
        if(mode==BrakeFeedbackMode.NoAbs && e.ValidSlipWheels==4 && e.Budget.Confidence>=.3 && e.Budget.Prompt>0 && p.Lock.Gain>0)
        {
            // Wall is installed ahead of the finger, even at zero slip/input.
            int wall=Math.Clamp((int)Math.Floor(e.Response.TravelFor(e.Budget.Prompt)*10),0,9);
            int force=Math.Clamp((int)Math.Round(2*p.Lock.Gain),0,2);
            for(int i=wall;i<10;i++)zones[i]=(byte)Math.Min(8,zones[i]+force);
            source="BrakeReference";
        }
        if (mode==BrakeFeedbackMode.Abs && demand>.025)
        {
            source = "AbsWall";
            // One zone before the estimated pedal position puts the force on the
            // finger immediately. Pedal-to-travel alignment assumes linear game input.
            double position = e.AbsOnsetBrake ?? f.Brake01;
            int start = Math.Clamp((int)Math.Floor(e.Response.TravelFor(position) * 10) - 1, 0, 8);
            int added = Math.Clamp((int)Math.Ceiling(6 * demand), 1, 8);
            for (int i = start; i < 10; i++) zones[i] = (byte)Math.Min(8, zones[i] + added);
        }
        bool any = false; foreach (byte z in zones) any |= z > 0;
        return (any ? TriggerEffect.FeedbackZones(zones) : TriggerEffect.Off, source);
    }
}
