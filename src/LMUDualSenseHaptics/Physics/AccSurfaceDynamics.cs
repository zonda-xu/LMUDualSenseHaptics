using LMUDualSenseHaptics.Core;
using static LMUDualSenseHaptics.Physics.Maths;
namespace LMUDualSenseHaptics.Physics;

// Adapted from Adudumax/ACC-DualSense-Feedback, MIT, commit
// 0c84c1901842fa1b864a3445226ddd64f991ffad. See THIRD_PARTY_NOTICES.md.
// LMU has no ACC native rumble, G-vibration or global SlipVibration channel.
// The surface and slip estimates below use only LMU's wheel telemetry.
public sealed class AccSurfaceDynamics
{
    public double[] Shock {get;}=new double[4];
    public double[] Slip {get;}=new double[4];
    private readonly double[] loadRef=new double[4],susp=new double[4],previousLoad=new double[4];
    private readonly double[] ratioBase=[.030,.030,.040,.040],angleBase=[.100,.100,.060,.060];
    private bool primed,adaptive;
    private readonly bool[] ratioPrimed=new bool[4];
    public double Side {get;private set;}
    public double Axle {get;private set;}
    public RearDragState RearDrag {get;}=new();
    public void Reset()
    {
        Array.Clear(Shock);Array.Clear(Slip);Array.Clear(loadRef);Array.Clear(susp);Array.Clear(previousLoad);
        double[] r=[.030,.030,.040,.040],a=[.100,.100,.060,.060];
        r.CopyTo(ratioBase,0);a.CopyTo(angleBase,0);Array.Clear(ratioPrimed);
        primed=adaptive=false;Side=Axle=0;RearDrag.Reset();
    }
    public void Update(TelemetryFrame f,WheelState[] wheels,double dt,double sensitivity,double contactMin,bool kerbActive,bool gripEnabled)
    {
        bool hadPrevious=primed;
        double speed=Smooth(4/3.6,28/3.6,f.SpeedMps);
        double loadSum=0;
        for(int i=0;i<4;i++)loadSum+=f.Wheels[i].TireLoadN;
        double mean=loadSum>1?loadSum/4:0;
        bool trusted=!kerbActive&&speed>.70&&f.Brake01<.20&&f.Throttle01<.82&&!f.AbsIntervening&&!f.TcIntervening;
        for(int i=0;i<4;i++)
        {
            var w=f.Wheels[i];var z=wheels[i];
            trusted &= WheelContact.IsGrounded(w,contactMin)&&!w.IsKerb&&
                Math.Abs(z.Alpha)<.24&&(!z.Kappa.HasValue||Math.Abs(z.Kappa.Value)<.16);
        }
        for(int i=0;i<4;i++)
        {
            var w=f.Wheels[i];var z=wheels[i];bool contact=WheelContact.IsGrounded(w,contactMin);
            double load=mean>1?Math.Clamp(w.TireLoadN/mean,0,2.5):1;
            if(!primed){loadRef[i]=load;susp[i]=w.SuspDeflectionM;previousLoad[i]=w.TireLoadN;}
            // ACC uses displacement per sample. Normalize to a 20 ms physics frame
            // so output is not accidentally multiplied by LMU's telemetry rate.
            // A change in the four-wheel average alone must not create phantom
            // impulses on stationary wheels when only one load changes.
            double loadShock=mean>1&&Math.Abs(w.TireLoadN-previousLoad[i])>1?Smooth(.018,.20,Math.Abs(load-loadRef[i])*sensitivity):0;
            double delta=Math.Abs(w.SuspDeflectionM-susp[i])*.020/dt*sensitivity;
            double suspensionShock=Smooth(.00035,.0045,delta);
            double target=Math.Max(loadShock,.84*suspensionShock)*speed*(contact?1:0);
            Shock[i]=contact?Env(Shock[i],target,dt,.004,.070):0;
            loadRef[i]+=(load-loadRef[i])*Math.Clamp(dt*36,0,1);susp[i]=w.SuspDeflectionM;previousLoad[i]=w.TireLoadN;
            // The first setup frame precedes calculation of wheel angles. Prime
            // each signal only when its first trustworthy measurement exists.
            if(trusted&&hadPrevious)
            {
                double at=Math.Clamp(Math.Abs(z.Alpha),i<2?.100:.060,i<2?.220:.180);
                if(!adaptive)angleBase[i]=at;
                else angleBase[i]=Ema(angleBase[i],at,dt,at>angleBase[i]?5:18);
                if(z.Kappa is double baselineK)
                {
                    double rt=Math.Clamp(Math.Abs(baselineK),i<2?.030:.040,i<2?.120:.140);
                    if(!ratioPrimed[i]){ratioBase[i]=rt;ratioPrimed[i]=true;}
                    else ratioBase[i]=Ema(ratioBase[i],rt,dt,rt>ratioBase[i]?5:18);
                }
            }
            double rs=Math.Max(i<2?.085:.110,ratioBase[i]*(i<2?1.35:1.30)+(i<2?.045:.055));
            double ast=Math.Max(i<2?.160:.100,angleBase[i]*(i<2?1.12:1.20)+(i<2?.035:.030));
            double longitudinal=z.Kappa is double k?Smooth(rs,rs+(i<2?.295:.290),Math.Abs(k))*(i<2?.88:.92):0;
            // ACC's SlipVibration gate is unavailable in LMU. Only valid ground
            // velocities and contact can contribute; no fabricated ACC signal.
            double lateral=Smooth(ast,ast+(i<2?.180:.170),Math.Abs(z.Alpha))*(i<2?.86:.92);
            double normalized=mean>1?C(w.TireLoadN/mean):.75;
            double loadFactor=.52+.48*normalized;
            Slip[i]=contact?Env(Slip[i],Math.Max(longitudinal,lateral)*.88*loadFactor*speed,dt,.008,.065):0;
        }
        primed=true;if(trusted&&hadPrevious)adaptive=true;
        double left=Math.Max(Shock[0],Shock[2])+.38*Math.Min(Shock[0],Shock[2]);
        double right=Math.Max(Shock[1],Shock[3])+.38*Math.Min(Shock[1],Shock[3]);
        double total=left+right,raw=total>.001?(right-left)/total:0;
        double requested=Math.Sign(raw)*Smooth(.025,.18,Math.Abs(raw))*Smooth(.035,.28,Math.Max(left,right));
        Side=Ema(Side,requested,dt,Math.Abs(requested)>.08?.006:.090);
        double front=Side>.20?Shock[1]:Side<-.20?Shock[0]:Math.Max(Shock[0],Shock[1]);
        double rear=Side>.20?Shock[3]:Side<-.20?Shock[2]:Math.Max(Shock[2],Shock[3]);
        double axle=front+rear>.001?(front-rear)/(front+rear):0;
        Axle=Ema(Axle,Math.Sign(axle)*Smooth(.06,.32,Math.Abs(axle)),dt,.030);
        if(!gripEnabled)RearDrag.Reset();
        else if(hadPrevious)RearDrag.Update(f,wheels,dt);
    }
}
