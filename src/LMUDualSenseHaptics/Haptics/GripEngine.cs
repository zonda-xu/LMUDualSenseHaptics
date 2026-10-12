using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Physics;
using static LMUDualSenseHaptics.Physics.Maths;
namespace LMUDualSenseHaptics.Haptics;
public readonly record struct GripLayers(double RoadL,double RoadR,double SlipL,double SlipR,double ImpactL,double ImpactR,double Centered,double EngineL,double EngineR,double SpatialL,double SpatialR,double KerbL=0,double KerbR=0);
public static class GripEngine
{
    public static GripLayers Render(SignalEngine e,TelemetryFrame f,double now,Profile p)
    {
        // USB rumble commands carry amplitudes, not an audio waveform. Preserve a
        // broad envelope instead of chopping every road event into narrow pulses.
        var roads=RoadHaptics.Render(e.RoadSignals,f,now,p);
        double A(double x,double tau)=>e.At(x,now,f,tau);
        double[] slip=e.Wheels.Select(z=>A(z.Slip,.065)).ToArray();
        double fl=Pulse(now,42,.14,1.3),rl=Pulse(now,17,.08,3);
        double sl=Math.Max(slip[0]*fl,slip[2]*rl)*p.GripSlip.Gain,sr=Math.Max(slip[1]*fl,slip[3]*rl)*p.GripSlip.Gain;
        double spatialL=Math.Max(Math.Max(roads.RoadL,roads.KerbL),Math.Max(slip[0]*p.GripSlip.Gain,slip[2]*p.GripSlip.Gain)),spatialR=Math.Max(Math.Max(roads.RoadR,roads.KerbR),Math.Max(slip[1]*p.GripSlip.Gain,slip[3]*p.GripSlip.Gain));
        double age=now-e.ImpactStart;
        double impact=age is >=0 and <.18?e.Impact*Math.Exp(-age/.060)*(.45*Pulse(now,50,.05,2)+.55*Pulse(now,12,.1,2))*p.Impact.Gain:0;
        impact=Soft(impact,A(e.Scrape,.08)*p.Impact.Gain);
        double centered=Math.Max(A(e.Abs,.105)*p.Abs.Gain*.12,A(e.Shift,.052)*p.Shift.Gain*.22);
        double engine=f.MaxRpm>0?C(f.Rpm/f.MaxRpm)*p.Engine.Gain*.25*Pulse(now,22,.25,1.3):0;
        return new(roads.RoadL,roads.RoadR,sl,sr,impact*(e.ImpactPan>0?.25:1),impact*(e.ImpactPan<0?.25:1),centered,engine*.8,engine,spatialL,spatialR,roads.KerbL,roads.KerbR);
    }
}
