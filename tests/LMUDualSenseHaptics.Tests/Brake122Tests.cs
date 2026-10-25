using System.Collections.Immutable;
using System.Text.Json;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Physics;
using LMUDualSenseHaptics.Runtime;

internal static class Brake122Tests
{
    private static void Need(bool value,string reason="assertion"){if(!value)throw new Exception(reason);}
    private static TelemetryFrame F(int n,float slip=0,float brake=.6f) => new(n,n*.01,0,true,true,false,"test","Hyper",30,0,0,-30,0,0,0,brake,0,0,3,5000,8000,false,false,false,true,
        Enumerable.Range(0,4).Select(_=>new WheelFrame(-90*(1-slip),0,3000,3000,0,.01f,-30,0,null,true,LongitudinalPatchMps:-30*slip)).ToImmutableArray(),n*.01);
    private static int Peak(TriggerEffect effect)
    {
        byte[] b=new byte[11];effect.WriteTo(b);int mask=b[1]|b[2]<<8;uint bits=(uint)(b[3]|b[4]<<8|b[5]<<16|b[6]<<24);
        return Enumerable.Range(0,10).Max(i=>(mask&(1<<i))==0?0:1+(int)(bits>>(3*i)&7));
    }
    public static void Run(Action<string,Action> check)
    {
        check("1.2.2 low base and moderate warning gains do not create an unlearned wall",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile{BrakeBase=new(true,5),Lock=new(true,35)};
            for(int n=0;n<100;n++)
            {var f=F(n,0,n%2==0?0:.6f);var o=loop.Tick(f,f.CapturedAtSec,p);Need(o.LeftTrigger.Mode==0x21&&Peak(o.LeftTrigger)==1&&loop.LeftTriggerSource=="BrakeBase"&&loop.Signals.Braking.Budget.Confidence==0);}
        });
        check("1.2.2 measured small slip warns without radius learning",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile{BrakeBase=new(true,5),Lock=new(true,35)};
            var f=F(0,.09f);var o=loop.Tick(f,0,p);
            Need(loop.Signals.Wheels.All(w=>w.Kappa is null)&&loop.Signals.Braking.ValidSlipWheels==4);
            Need(loop.LeftTriggerSource=="BrakeLimit"&&o.LeftTrigger.Mode==0x21&&Peak(o.LeftTrigger)>0);
        });
        check("1.2.2 measured warning threshold is independent of strength",()=>
        {
            foreach(int gain in new[]{1,5,15,35,100})
            {var l=new FeedbackLoop();var o=l.Tick(F(0,.0601f),0,new(){BrakeBase=new(false,50),Lock=new(true,gain)});Need(l.LeftTriggerSource=="BrakeLimit"&&Peak(o.LeftTrigger)>0);}
        });
        check("1.2.2 startup lock produces a short 24 Hz pulse then sustained resistance",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile{BrakeBase=new(true,5),Lock=new(true,35)};
            for(int n=0;n<50;n++)
            {
                var o=loop.Tick(F(n,1),n*.01,p);Need(n<12?o.LeftTrigger.Mode==0x26&&o.LeftTrigger.P8==24&&loop.LeftTriggerSource=="LockPulse":o.LeftTrigger.Mode==0x21&&loop.LeftTriggerSource=="BrakeLimit");
                byte[] report=new byte[48];DualSenseReportBuilder.Build(o,report);Need(report[22]==o.LeftTrigger.Mode&&report[31]==o.LeftTrigger.P8);
            }
        });
        check("1.2.2 warning gain controls amplitude without disabling the detection threshold",()=>
        {
            foreach(int gain in new[]{1,5,15,35,70,100})
            {var loop=new FeedbackLoop();var o=loop.Tick(F(0,1),0,new(){BrakeBase=new(false,50),Lock=new(true,gain)});Need(o.LeftTrigger.Mode==0x26&&Peak(o.LeftTrigger)>0);}
            var off=new FeedbackLoop().Tick(F(0,1),0,new(){BrakeBase=new(false,50),Lock=new(false,35)});Need(off.LeftTrigger.Mode==5);
        });
        check("1.2.2 lift, low speed, airborne and stale frames release lock pulse",()=>
        {
            foreach(var f in new[]{F(1,1,0),F(1,1) with{SpeedMps=2},F(1,1) with{Wheels=F(1,1).Wheels.Select(w=>w with{TireLoadN=0,TireVerticalDeflectionM=0}).ToImmutableArray()}})
            {var l=new FeedbackLoop();l.Tick(F(0,1),0,new());var o=l.Tick(f,.01,new());Need(o.LeftTrigger.Mode!=0x26);}
            var loop=new FeedbackLoop();loop.Tick(F(0,1),0,new());Need(loop.Tick(F(0,1),.251,new())==FeedbackFrame.Off);
        });
        check("1.2.2 negative drive slip and lateral slip cannot trigger brake lock",()=>
        {
            var loop=new FeedbackLoop();var f=F(0,-.2f) with{AccelX=12,Wheels=F(0,-.2f).Wheels.Select(w=>w with{GroundLateralMps=10,SlidingFraction=1}).ToImmutableArray()};
            loop.Tick(f,0,new());Need(loop.Signals.Braking.Risk==0&&loop.Signals.Braking.LockSeverity==0);
        });
        check("1.2.2 manual ABS intervention does not require non-ABS patch telemetry",()=>
        {
            var loop=new FeedbackLoop();var f=F(0) with{AbsIntervening=true,Wheels=F(0).Wheels.Select(w=>w with{LongitudinalPatchMps=null}).ToImmutableArray()};
            var output=loop.Tick(f,0,new(){BrakeMode=BrakeFeedbackMode.Abs});
            Need(loop.Signals.Braking.AbsWall==1&&loop.LeftTriggerSource=="AbsWall"&&output.LeftTrigger.Mode==0x21);
        });
        check("1.2.2 missing or inconsistent patch telemetry is explicit, never invented slip",()=>
        {
            foreach(float? patch in new float?[]{null,0,15,-60})
            {var w=F(0,1).Wheels[0] with{LongitudinalPatchMps=patch};Need(!BrakeSlip.TryRead(w,100,out _));}
            Need(BrakeSlip.TryRead(F(0,1).Wheels[0],100,out double slip)&&slip==1);
        });
        check("1.2.2 patch sign and rolling identity hold in either direction",()=>
        {
            var w=F(0,.1f).Wheels[0];Need(BrakeSlip.TryRead(w,100,out double a));
            var reverse=w with{OmegaRadPerSec=-w.OmegaRadPerSec,GroundLongitudinalMps=-w.GroundLongitudinalMps,LongitudinalPatchMps=-w.LongitudinalPatchMps};
            Need(BrakeSlip.TryRead(reverse,100,out double b)&&Math.Abs(a-b)<1e-9);
        });
        check("1.2.2 progressive single-wheel slip reaches warning then lock in final HID output",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile{BrakeBase=new(true,5),Lock=new(true,35)};
            bool warned=false,locked=false;
            for(int n=0;n<100;n++)
            {
                float slip=n<20?0:Math.Min(1,(n-20)*.015f);
                var f=F(n) with{Wheels=F(n).Wheels.Select((w,i)=>i==1?w with{OmegaRadPerSec=-90*(1-slip),LongitudinalPatchMps=-30*slip}:w).ToImmutableArray()};
                var o=loop.Tick(f,f.CapturedAtSec,p);
                warned|=loop.LeftTriggerSource=="BrakeLimit"&&slip<.16;
                locked|=loop.LeftTriggerSource=="LockPulse";
                if(slip>=.25)Need(o.LeftTrigger.Mode is 0x26 or 0x21 && loop.LeftTriggerSource is "LockPulse" or "BrakeLimit");
                for(int j=0;j<3;j++){var tick=loop.Tick(f,f.CapturedAtSec+j*.004,p);byte[] report=new byte[48];DualSenseReportBuilder.Build(tick,report);Need(report[22]==tick.LeftTrigger.Mode);}
            }
            Need(warned&&locked&&loop.Signals.Wheels.All(w=>w.Kappa is null));
        });
    }
}
