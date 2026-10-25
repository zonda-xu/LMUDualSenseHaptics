using System.Collections.Immutable;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Physics;
using LMUDualSenseHaptics.Runtime;

internal static class Brake135Tests
{
    private static void Need(bool b,string why="assertion"){if(!b)throw new Exception(why);}
    private static TelemetryFrame F(int n,float slip=0,float brake=.6f) => new(n,n*.01,0,true,true,false,"test135","Hyper",30,0,0,-30,0,0,0,brake,0,0,3,5000,8000,false,false,false,true,
        Enumerable.Range(0,4).Select(_=>new WheelFrame(-30/.35f*(1-slip),0,3000,3000,0,.01f,-30,0,null,true,LongitudinalPatchMps:-30*slip)).ToImmutableArray(),n*.01);
    private static int[] Levels(TriggerEffect e)
    {
        byte[] b=new byte[11];e.WriteTo(b);uint bits=(uint)(b[3]|b[4]<<8|b[5]<<16|b[6]<<24);int mask=b[1]|b[2]<<8;
        return Enumerable.Range(0,10).Select(i=>(mask&(1<<i))==0?0:1+(int)(bits>>(3*i)&7)).ToArray();
    }
    public static void Run(Action<string,Action> check)
    {
        check("1.3.5 automatic GT3 matches v1.0.1 source oracle through full telemetry pipeline",()=>
        {
            foreach(var c in new[]{new FeedbackParameters(),new FeedbackParameters{LockRatio=.2,LockFullRatio=.45}})
            {
                var p=new Profile{DefaultParameters=c};var now=new SignalEngine();var old=new LMUDualSenseHaptics.Legacy101.SignalEngine();
                var a=new TriggerEngine();var b=new LMUDualSenseHaptics.Legacy101.TriggerEngine();
                for(int n=0;n<650;n++)
                {
                    float slip=n is >420 and <490?.7f:0;
                    var f=F(n,slip,n<400?0:.8f) with{VehicleClass="LMGT3",AbsAvailable=true,AbsIntervening=n is >405 and <430};
                    now.Update(f,p);old.Update(f,p);
                    Need(Math.Abs(now.Abs-old.Abs)<1e-12&&Math.Abs(now.LegacyLock-old.Lock)<1e-12,"v1.0.1 signal mismatch");
                    for(int tick=0;tick<3;tick++)
                    {double t=f.CapturedAtSec+tick*.003;Need(a.Render(now,f,t,p).Left==b.Render(old,f,t,p).Left,"v1.0.1 trigger bytes mismatch");}
                }
                Need(now.Wheels.All(w=>w.RadiusSamples>0),"oracle must exercise learned lock path");
            }
        });
        check("1.3.5 base is stable, progressive and independent of brake risk",()=>
        {
            var p=new Profile{Lock=new(false,50)};var l=new FeedbackLoop();var normal=l.Tick(F(0,0,0),0,p).LeftTrigger;
            var z=Levels(normal);Need(normal.Mode==0x21&&z[0]==1&&z[9]==3);
            Need(z.Zip(z.Skip(1)).All(x=>x.First<=x.Second));
            for(int n=1;n<100;n++)Need(l.Tick(F(n,n%2==0?0:1,n/100f),n*.01,p).LeftTrigger==normal);
            foreach(int gain in new[]{1,5,10,50,100})Need(Levels(new FeedbackLoop().Tick(F(0),0,p with{BrakeBase=new(true,gain)}).LeftTrigger).Min()>=1);
            Need(new FeedbackLoop().Tick(F(0),0,p with{BrakeBase=new(true,0)}).LeftTrigger.Mode==5);
        });
        check("1.3.5 curve controls change shape without changing detection",()=>
        {
            var p=new Profile{BrakeBase=new(true,100),Lock=new(false,50)};
            var soft=new FeedbackLoop().Tick(F(0),0,p with{DefaultParameters=new(){BrakeResponse=new(){PedalStartLevel=1,PedalEndLevel=4,PedalExponent=3}}}).LeftTrigger;
            var firm=new FeedbackLoop().Tick(F(0),0,p with{DefaultParameters=new(){BrakeResponse=new(){PedalStartLevel=4,PedalEndLevel=8,PedalExponent=.5}}}).LeftTrigger;
            Need(Levels(soft)[0]==1&&Levels(soft)[9]==4&&Levels(firm)[0]==4&&Levels(firm)[9]==8);
        });
        check("1.3.5 sustained sub-lock slip adds resistance monotonically without vibration",()=>
        {
            int[] previous=new int[10];
            foreach(float slip in new[]{0f,.07f,.10f,.15f,.20f,.24f})
            {
                var l=new FeedbackLoop();FeedbackFrame o=default;
                for(int n=0;n<100;n++){o=l.Tick(F(n,slip),n*.01,new());Need(o.LeftTrigger.Mode==0x21);}
                var z=Levels(o.LeftTrigger);Need(z.Zip(previous).All(x=>x.First>=x.Second));previous=z;
            }
        });
        check("1.3.5 persistent severe lock emits one burst then stable resistance",()=>
        {
            var l=new FeedbackLoop();var p=new Profile();TriggerEffect steady=default;
            for(int n=0;n<100;n++)
            {
                var o=l.Tick(F(n,1,1),n*.01,p);
                Need(n<12?o.LeftTrigger.Mode==0x26&&o.LeftTrigger.P8==24:o.LeftTrigger.Mode==0x21);
                if(n==50)steady=o.LeftTrigger;if(n>50)Need(o.LeftTrigger==steady);
                Need(Levels(o.LeftTrigger).All(v=>v is >=1 and <=8));
            }
            Need(Levels(steady)[9]>3);
        });
        check("1.3.5 burst rearms only after recovery, not chatter",()=>
        {
            var l=new FeedbackLoop();var p=new Profile();l.Tick(F(0,1),0,p);
            for(int n=1;n<40;n++)l.Tick(F(n,n%3==0?.1f:1),n*.01,p);
            Need(l.Tick(F(40,1),.4,p).LeftTrigger.Mode==0x21);
            for(int n=41;n<65;n++)l.Tick(F(n,0),n*.01,p);
            Need(l.Tick(F(65,1),.65,p).LeftTrigger.Mode==0x26);
        });
        check("1.3.5 event edges start at the current steady resistance code",()=>
        {
            var l=new FeedbackLoop();var p=new Profile();var f=F(0,1,1);
            var entry=l.Tick(f,0,p);var peak=l.Tick(f,.03,p);var end=l.Tick(f,.116,p);var after=l.Tick(f,.124,p);
            Need(entry.LeftTrigger.Mode==0x26&&after.LeftTrigger.Mode==0x21);
            Need(Levels(entry.LeftTrigger)[9]==Levels(after.LeftTrigger)[9]);
            Need(Levels(peak.LeftTrigger)[9]>=Levels(entry.LeftTrigger)[9]&&Levels(end.LeftTrigger)[9]<=Levels(peak.LeftTrigger)[9]);
        });
        check("1.3.5 learner counts independent accepted stops and actual wall visibility",()=>
        {
            var l=new FeedbackLoop();var p=new Profile{Lock=new(true,100)};int n=0;
            for(int stop=0;stop<4;stop++)
            {
                foreach(var pair in new[]{(0f,0f),(.6f,0f),(.65f,.08f)})
                {var f=F(n++,pair.Item2,pair.Item1);l.Tick(f,f.CapturedAtSec,p);}
                Need(l.Signals.Braking.Budget.AcceptedStops==stop+1&&l.Signals.Braking.Budget.Stops==stop+1);
            }
            var safe=F(n++,0,0);var o=l.Tick(safe,safe.CapturedAtSec,p);
            Need(l.LeftReferenceApplied&&o.LeftTrigger.Mode==0x21&&l.LeftTriggerSource=="BrakeReference");
            Need(!l.Tick(F(n++,1),n*.01,p).Equals(FeedbackFrame.Off)&&!l.LeftReferenceApplied);
            Need(l.Signals.Braking.Budget.ContextCount==1);
        });
        check("1.3.5 saturated or risk-covered reference is not counted as applied",()=>
        {
            var p=new Profile{BrakeBase=new(true,100),Lock=new(true,100),DefaultParameters=new(){BrakeResponse=new(){PedalStartLevel=4,PedalEndLevel=8,PedalExponent=.5,UncertaintyMargin=.03}}};
            var l=new FeedbackLoop();int n=0;
            for(int stop=0;stop<4;stop++)
                foreach(var pair in new[]{(0f,0f),(.95f,0f),(.97f,.08f)})
                {var f=F(n++,pair.Item2,pair.Item1);l.Tick(f,f.CapturedAtSec,p);}
            var safe=F(n++,0,0);l.Tick(safe,safe.CapturedAtSec,p);
            Need(l.Signals.Braking.Budget.Confidence>=.3&&l.Signals.Braking.Budget.Prompt>.8);
            Need(!l.LeftReferenceApplied,"saturated base has no extra reference contribution");
            var locked=F(n,1);l.Tick(locked,locked.CapturedAtSec,p);
            var sustained=l.Tick(locked,locked.CapturedAtSec+.13,p);
            Need(sustained.LeftTrigger.Mode==0x21&&!l.LeftReferenceApplied,"risk-covered wall is not independently visible");
        });
        check("1.3.5 invalid pedal curve parameters are rejected",()=>
        {
            foreach(var c in new[]{new BrakeResponse{PedalStartLevel=0},new BrakeResponse{PedalEndLevel=9},new BrakeResponse{PedalExponent=double.NaN},new BrakeResponse{SeverePulseSeconds=1}})
            {bool rejected=false;try{c.Validate();}catch(InvalidDataException){rejected=true;}Need(rejected);}
        });
    }
}
