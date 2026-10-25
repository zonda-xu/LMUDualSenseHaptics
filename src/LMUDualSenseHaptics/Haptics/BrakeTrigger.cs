using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Physics;

namespace LMUDualSenseHaptics.Haptics;

public static class BrakeTrigger
{
    private static readonly byte[] AbsBase = [5,5,6,6,7,7,8,8,8,8];

    public static (TriggerEffect Effect, string Source, bool ReferenceApplied) Render(BrakeSignalEngine e, TelemetryFrame f, double now, Profile p)
    {
        Span<byte> zones = stackalloc byte[10];
        var mode = BrakeSignalEngine.ResolveMode(f, p);
        var c=e.Response;
        bool noAbs=mode==BrakeFeedbackMode.NoAbs;
        for(int i=0;i<10;i++)
        {
            double level=noAbs?c.PedalStartLevel+(c.PedalEndLevel-c.PedalStartLevel)*Math.Pow(i/9.0,c.PedalExponent):AbsBase[i];
            // Enabled nonzero base always supplies the lowest physical level.
            // Low settings share levels because firmware has only eight steps.
            zones[i]=p.BrakeBase.Gain<=0?(byte)0:(byte)Math.Clamp((int)Math.Ceiling(level*p.BrakeBase.Gain),1,8);
        }
        double age=Math.Max(0,now-f.CapturedAtSec+f.SourceAgeSec);
        bool braking=f.Brake01>0&&f.SpeedMps>=5&&f.Gear>=0;
        string source="BrakeBase";
        bool referenceApplied=false;
        if(noAbs)
        {
            // Stable stroke curve + bounded slip-dependent increase. No moving
            // wall at the estimated finger position and no warning vibration.
            double risk=braking&&e.WarningActive?Math.Max(.12,e.Risk):0;
            int wall=e.ValidSlipWheels==4&&e.Budget.Confidence>=.3&&e.Budget.Prompt>0?
                Math.Clamp((int)Math.Floor(c.TravelFor(e.Budget.Prompt)*10),0,9):10;
            int reference=(int)Math.Round(2*p.Lock.Gain);
            for(int i=0;i<10;i++)
            {
                int control=(int)Math.Ceiling(3*Math.Sqrt(p.Lock.Gain)*risk*(.6+.4*i/9.0));
                int learned=i>=wall?reference:0;
                int normal=Math.Min(8,zones[i]+control);
                int combined=Math.Min(8,zones[i]+Math.Max(control,learned));
                referenceApplied|=combined>normal;
                zones[i]=(byte)combined;
            }
            if(risk>0&&p.Lock.Gain>0)source="BrakeLimit";
            else if(referenceApplied)source="BrakeReference";
            double elapsed=f.SourceTimeSec+age-e.LockPulseStarted;
            if(braking&&e.LockActive&&p.Lock.Gain>0&&elapsed>=0&&elapsed<c.SeverePulseSeconds)
            {
                int finger=Math.Clamp((int)Math.Floor(c.TravelFor(f.Brake01)*10),0,9);
                int steady=zones[finger];
                int peak=Math.Max(steady,(int)Math.Ceiling(8*Math.Sqrt(p.Lock.Gain)));
                // Enter/exit the brief event at the current resistance code.
                // Vibration amplitude and static force are different physical
                // modes, so equal codes still require a controller feel test.
                double edge=Math.Min(1,Math.Min(elapsed,c.SeverePulseSeconds-elapsed)/.020);
                byte amplitude=(byte)Math.Clamp((int)Math.Round(steady+(peak-steady)*edge),1,8);
                return (TriggerEffect.Vibration(0,amplitude,24),"LockPulse",false);
            }
        }
        else
        {
            // Manual ABS wall is unchanged; automatic GT3 uses the v1.0.1 path.
            double demand=braking?e.AbsWall*p.Abs.Gain*Math.Exp(-age/.040):0;
            if(demand>.025)
            {
                source="AbsWall";
                int start=Math.Clamp((int)Math.Floor(c.TravelFor(e.AbsOnsetBrake??f.Brake01)*10)-1,0,8);
                int added=Math.Clamp((int)Math.Ceiling(6*demand),1,8);
                for(int i=start;i<10;i++)zones[i]=(byte)Math.Min(8,zones[i]+added);
            }
        }
        bool any=false;foreach(byte z in zones)any|=z>0;
        return (any?TriggerEffect.FeedbackZones(zones):TriggerEffect.Off,source,referenceApplied);
    }
}
