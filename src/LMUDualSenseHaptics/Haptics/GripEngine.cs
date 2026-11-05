using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Physics;
using static LMUDualSenseHaptics.Physics.Maths;
namespace LMUDualSenseHaptics.Haptics;
public readonly record struct GripLayers(double RoadL,double RoadR,double SlipL,double SlipR,double ImpactL,double ImpactR,double Centered,double EngineL,double EngineR,double SpatialL,double SpatialR,double KerbL=0,double KerbR=0, double RoadBumpL=0,double RoadBumpR=0,double FrontSlipL=0,double FrontSlipR=0,double RearSlipL=0,double RearSlipR=0,double RearDragL=0,double RearDragR=0,double FrontShare=.5);
public static class GripEngine
{
    public static GripLayers Render(SignalEngine e,TelemetryFrame f,double now,Profile p)
    {
        // USB rumble commands carry amplitudes, not an audio waveform. Preserve a
        // broad envelope instead of chopping every road event into narrow pulses.
        var roads=RoadHaptics.Render(e.RoadSignals,f,now,p);
        double A(double x,double tau)=>e.At(x,now,f,tau);
        var dynamics=e.SurfaceDynamics;
        Span<double> shock=stackalloc double[4],slip=stackalloc double[4];
        for(int i=0;i<4;i++){shock[i]=A(dynamics.Shock[i],.070);slip[i]=A(dynamics.Slip[i],.065)*p.GripSlip.Gain;}
        double side=dynamics.Side;
        // ACC's short shock window carries side ownership; the v1.5 kerb path
        // remains unchanged. Front/rear are encoded as different cadence.
        double strongest=Math.Max(Math.Max(shock[0],shock[1]),Math.Max(shock[2],shock[3]))*.08*p.Road.Gain;
        double roadL=strongest*(side>0?1-.985*side:1);
        double roadR=strongest*(side<0?1+.985*side:1);
        double frontShare=.5+.5*dynamics.Axle;
        double surfaceWave=frontShare*Pulse(now,48,.18,1.45)+(1-frontShare)*Pulse(now,13,.08,3.2);
        double frontWave=Pulse(now,76,.14,1.35),rearWave=Pulse(now,16,.06,3.6);
        double drag=A(dynamics.RearDrag.Strength,.100)*p.GripSlip.Gain;
        double dragWave=(.64+.36*(.5+.5*Math.Cos(now*36*Math.Tau)))*(.88+.12*(.5+.5*Math.Cos(now*4.5*Math.Tau)));
        double sl=Math.Max(Math.Max(slip[0]*frontWave*.58,slip[2]*rearWave*.68),drag*.52*dragWave);
        double sr=Math.Max(Math.Max(slip[1]*frontWave*.58,slip[3]*rearWave*.68),drag*.31*dragWave);
        double spatialL=Math.Max(Math.Max(roadL,roads.KerbL),Math.Max(slip[0],slip[2]));
        double spatialR=Math.Max(Math.Max(roadR,roads.KerbR),Math.Max(slip[1],slip[3]));
        double age=now-e.ImpactStart;
        double impact=age is >=0 and <.18?e.Impact*Math.Exp(-age/.060)*(.45*Pulse(now,50,.05,2)+.55*Pulse(now,12,.1,2))*p.Impact.Gain:0;
        impact=Soft(impact,A(e.Scrape,.08)*p.Impact.Gain);
        double centered=Math.Max(A(e.Abs,.105)*p.Abs.Gain*.12,A(e.Shift,.052)*p.Shift.Gain*.22);
        double engine=f.MaxRpm>0?C(f.Rpm/f.MaxRpm)*p.Engine.Gain*.25*Pulse(now,22,.25,1.3):0;
        return new(roadL*surfaceWave,roadR*surfaceWave,sl,sr,impact*(e.ImpactPan>0?.25:1),impact*(e.ImpactPan<0?.25:1),centered,engine*.8,engine,spatialL,spatialR,roads.KerbL,roads.KerbR, roadL,roadR,slip[0]*.58,slip[1]*.58,slip[2]*.68,slip[3]*.68,drag*.52,drag*.31,frontShare);
    }
}
