using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Physics;
using static LMUDualSenseHaptics.Physics.Maths;
namespace LMUDualSenseHaptics.Haptics;
public sealed class TriggerEngine
{
    private int leftSelected=-1,rightSelected=-1;private double leftUntil,rightUntil;
    private string brakeIdentity="";
    private static readonly byte[] Brake=[3,4,4,5,5,6,6,7,7,8];
    public string LeftSource {get;private set;}="BrakeBase";
    public string RightSource=>rightSelected switch{0=>"Shift",1=>"Tc",2=>"Wheelspin",3=>"Redline",_=>"ThrottleBase"};
    private static readonly byte[] Throttle=[2,2,2,3,3,3,4,4,5,5];
    public void Reset(){leftSelected=rightSelected=-1;leftUntil=rightUntil=0;brakeIdentity="";LeftSource="BrakeBase";}
    public (TriggerEffect Left,TriggerEffect Right) Render(SignalEngine e,TelemetryFrame f,double now,Profile p)
    {
        double A(double x,double tau)=>e.At(x,now,f,tau);
        string identity=$"{f.CarId}|{f.VehicleModel}|{f.VehicleClass}|{p.BrakeMode}";
        if(identity!=brakeIdentity){leftSelected=-1;leftUntil=0;brakeIdentity=identity;}
        TriggerEffect left;
        if(BrakeRouting.Legacy(f,p))
        {
            left=Map(Brake,p.BrakeBase.Gain,[A(e.Abs,.105)*p.Abs.Gain,A(e.LegacyLock,.135)*p.Lock.Gain],
                [15,8],now,ref leftSelected,ref leftUntil);
            LeftSource=leftSelected switch{0=>"Abs",1=>"Lock",_=>"BrakeBase"};
        }
        else
        {
            var brake=BrakeTrigger.Render(e.Braking,f,now,p);left=brake.Effect;LeftSource=brake.Source;
        }
        if(p.ThrottleMode == ThrottleFeedbackMode.TcOnly)
        {
            // A TC setting or wheel slip is NOT an intervention. Use the live flag,
            // not the smoothed event tail. Clearing it releases R2 on this tick.
            rightSelected = f.TcIntervening && p.Tc.Gain > 0 ? 1 : -1;
            rightUntil = 0;
            if(rightSelected < 0)return (left,TriggerEffect.Off);
            byte strength=(byte)Math.Clamp((int)Math.Round(8*p.Tc.Gain),1,8);
            Span<byte> zones=stackalloc byte[10];zones.Fill(strength);
            return (left,TriggerEffect.FeedbackZones(zones));
        }
        // Optional compatibility mode retains the original R2 event mapping.
        return (left,Map(Throttle,p.ThrottleBase.Gain,
            [A(e.Shift,.052)*p.Shift.Gain,A(e.Tc,.135)*p.Tc.Gain,
             A(e.Spin,.175)*p.Wheelspin.Gain,A(e.Redline,.1)*p.Redline.Gain],
            [28,12,7,18],now,ref rightSelected,ref rightUntil));
    }
    private static TriggerEffect Map(byte[] zones,double gain,double[] events,byte[] hz,double now,ref int selected,ref double until)
    {
        int chosen=Array.FindIndex(events,x=>x>.035);
        // Higher priority preempts immediately; 20 ms hold affects only exit/lower priority.
        if(selected>=0 && events[selected]>.025 && now<until && (chosen<0||chosen>selected))chosen=selected;
        if(chosen!=selected){selected=chosen;until=now+.020;}
        if(chosen>=0)return TriggerEffect.Vibration(0,(byte)Math.Clamp((int)Math.Round(1+Smooth(.035,.75,events[chosen])*4),1,8),hz[chosen]);
        if(gain<=0)return TriggerEffect.Off;
        Span<byte> scaled=stackalloc byte[10];for(int i=0;i<10;i++)scaled[i]=(byte)Math.Clamp((int)Math.Round(zones[i]*gain),0,8);
        return TriggerEffect.FeedbackZones(scaled);
    }
}
