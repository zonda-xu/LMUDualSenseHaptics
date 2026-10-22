using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Physics;

namespace LMUDualSenseHaptics.Haptics;

public static class BrakeTrigger
{
    private static readonly byte[] Base = [3,4,4,5,5,6,6,7,7,8];

    public static (TriggerEffect Effect, string Source) Render(BrakeSignalEngine e, TelemetryFrame f, double now, Profile p)
    {
        Span<byte> zones = stackalloc byte[10];
        for (int i = 0; i < 10; i++) zones[i] = (byte)Math.Clamp((int)Math.Round(Base[i] * p.BrakeBase.Gain), 0, 8);
        double age = Math.Max(0, now - f.CapturedAtSec + f.SourceAgeSec);
        double fade = Math.Exp(-age / .040);
        var mode = BrakeSignalEngine.ResolveMode(f, p);
        bool braking = f.Brake01 > 0 && f.SpeedMps >= 5 && f.Gear >= 0;
        double demand = !braking ? 0 : mode == BrakeFeedbackMode.Abs ? e.AbsWall * p.Abs.Gain :
            mode == BrakeFeedbackMode.NoAbs ? e.Risk * p.Lock.Gain : 0;
        demand *= fade;
        string source = "BrakeBase";
        if (demand > .025)
        {
            source = mode == BrakeFeedbackMode.Abs ? "AbsWall" : e.LockSeverity > .5 ? "Lock" : "BrakeLimit";
            // One zone before the estimated pedal position puts the force on the
            // finger immediately. Pedal-to-travel alignment assumes linear game input.
            double position = mode == BrakeFeedbackMode.Abs ? e.AbsOnsetBrake ?? f.Brake01 : f.Brake01;
            int start = Math.Clamp((int)Math.Floor(position * 10) - 1, 0, 8);
            int added = Math.Clamp((int)Math.Ceiling(6 * demand), 1, 6);
            // Severe lock adds a shallow 18 Hz force modulation. Keep the wall and
            // base curve throughout; never replace them with a low-amplitude motor mode.
            int accent = mode == BrakeFeedbackMode.NoAbs && e.LockSeverity * p.Lock.Gain * fade > .2 &&
                Math.Sin(now * 2 * Math.PI * 18) > 0 ? 1 : 0;
            for (int i = start; i < 10; i++) zones[i] = (byte)Math.Min(8, zones[i] + added + accent);
        }
        bool any = false; foreach (byte z in zones) any |= z > 0;
        return (any ? TriggerEffect.FeedbackZones(zones) : TriggerEffect.Off, source);
    }
}
