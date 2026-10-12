using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Physics;
using static LMUDualSenseHaptics.Physics.Maths;
namespace LMUDualSenseHaptics.Haptics;

public readonly record struct RoadLayers(double RoadL,double RoadR,double KerbL,double KerbR);
public static class RoadHaptics
{
    // A soft dead band followed by a response curve and minimum-output lift.
    // Zero input always remains zero; no unconditional motor floor is added.
    public static double Response(double level,RoadResponse c)
    {
        double x=C((level-.015)/.985);
        return c.MinimumOutput*Smooth(0,.08,x)+(1-c.MinimumOutput)*Math.Pow(x,c.ResponseGamma);
    }
    public static RoadLayers Render(RoadSignalEngine e,TelemetryFrame f,double now,Profile p)
    {
        Span<double> road=stackalloc double[4],kerb=stackalloc double[4];
        double age=Math.Max(0,now-f.CapturedAtSec);
        for(int i=0;i<4;i++)
        {
            var z=e.Wheels[i];
            // Keep the suspension-driven envelope. No fixed 14/40 Hz chopping.
            double envelope=Math.Max(z.Texture*Math.Exp(-age/e.Response.ReleaseSeconds),
                                     z.Bump*Math.Exp(-age/.105));
            road[i]=Response(envelope,e.Response)*p.Road.Gain;
            // A surface label provides contact evidence, not a measured waveform.
            // This speed-linked amplitude modulation is an explicitly synthesized cue.
            double phase=z.KerbPhase+z.KerbHz*Math.Min(age,.05);
            double carrier=.65+.35*(.5+.5*Math.Sin(2*Math.PI*phase));
            kerb[i]=Response(z.Kerb*Math.Exp(-age/.065),e.Response)*carrier*p.Kerb.Gain;
        }
        (double L,double R) Map(Span<double> wheels)
        {
            double l=Math.Max(wheels[0],wheels[2]),r=Math.Max(wheels[1],wheels[3]);
            // Preserve the strong-side request. Separation only controls crossfeed.
            double crossfeed=.28*(1-p.SpatialSeparation/100.0);
            return (Math.Max(l,r*crossfeed),Math.Max(r,l*crossfeed));
        }
        var roads=Map(road);var kerbs=Map(kerb);
        return new(roads.L,roads.R,kerbs.L,kerbs.R);
    }
}
