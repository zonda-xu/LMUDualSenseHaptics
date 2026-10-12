using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Hardware;
using static LMUDualSenseHaptics.Physics.Maths;
namespace LMUDualSenseHaptics.Haptics;
public sealed class FrameMixer
{
    public long ClippedFrames {get;private set;}
    public double PreRight {get;private set;}
    public double PreLeft {get;private set;}
    public double EngineGain {get;private set;}
    public double ContinuousGain {get;private set;}
    public double OppositeGain {get;private set;}
    public void ResetDiagnostics(){PreLeft=PreRight=EngineGain=ContinuousGain=OppositeGain=0;}
    public FeedbackFrame Mix((TriggerEffect Left,TriggerEffect Right) t,GripLayers g,Profile p)
    {
        double slipL=g.SlipL,slipR=g.SlipR;
        double pan=(g.SpatialR-g.SpatialL)/(g.SpatialR+g.SpatialL+1e-6);
        double opp=1-.88*Smooth(.25,.80,Math.Abs(pan))*p.SpatialSeparation/100.0;
        OppositeGain=opp;
        if(pan>0)slipL*=opp;else slipR*=opp;
        // RoadHaptics already maps left/right contact; do not spatially attenuate twice.
        double l=Soft(Math.Max(g.RoadL,g.KerbL),slipL),r=Soft(Math.Max(g.RoadR,g.KerbR),slipR);
        // Collision ducks continuous textures, but preserves spatial impact information.
        double collision=Math.Max(g.ImpactL,g.ImpactR);ContinuousGain=1-.7*collision;l*=ContinuousGain;r*=ContinuousGain;
        l=Soft(Soft(l,g.ImpactL),.8*g.Centered);r=Soft(Soft(r,g.ImpactR),g.Centered);
        double priority=new[]{g.RoadL,g.RoadR,g.KerbL,g.KerbR,g.SlipL,g.SlipR,g.ImpactL,g.ImpactR,g.Centered}.Max();
        double engineGain=1-Smooth(.05,.35,priority);EngineGain=engineGain;
        l=Soft(l,g.EngineL*engineGain);r=Soft(r,g.EngineR*engineGain);PreLeft=l;PreRight=r*p.RightGain;
        if(PreRight>1||l>=.999)ClippedFrames++;
        return new(Byte(l),Byte(PreRight),t.Left,t.Right);
    }
}
