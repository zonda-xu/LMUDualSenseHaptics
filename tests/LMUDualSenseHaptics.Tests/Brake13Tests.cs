using System.Collections.Immutable;
using System.Text.Json;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Config;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Physics;
using LMUDualSenseHaptics.Runtime;

// Entirely synthetic scenarios, including 250–360 km/h panic stops.
internal static class Brake13Tests
{
    private static void Need(bool value,string reason="assertion"){if(!value)throw new Exception(reason);}
    private static readonly Profile P=new(){BrakeBase=new(true,5),Lock=new(true,35)};
    private static TelemetryFrame F(int n,double t,float slip=0,float brake=.8f,float speed=90) =>
        new(n,t,0,true,true,false,"synthetic-13","Hyper",speed,0,0,-speed,0,0,0,brake,0,0,6,6000,8000,false,false,false,true,
            Enumerable.Range(0,4).Select(_=>new WheelFrame(-speed/.35f*(1-slip),0,3000,3000,0,.01f,-speed,0,null,true,LongitudinalPatchMps:-speed*slip)).ToImmutableArray(),t);
    private static int Amplitude(TriggerEffect e)=>1+(e.P2&7);
    private static void Pulse(FeedbackFrame o,int hz)
    {
        Need(o.LeftTrigger.Mode==0x26&&o.LeftTrigger.P8==hz,$"expected {hz} Hz, got {o.LeftTrigger.Mode}/{o.LeftTrigger.P8}");
        byte[] hid=new byte[48];DualSenseReportBuilder.Build(o,hid);
        Need(hid[22]==0x26&&hid[31]==hz&&hid[23]==255&&(hid[24]&3)==3,"whole-travel HID pulse");
    }
    public static void Run(Action<string,Action> check)
    {
        check("1.3 full-travel high-speed lock acts on the first sample at all source rates",()=>
        {
            foreach(float speed in new[]{250/3.6f,300/3.6f,360/3.6f})
            foreach(double dt in new[]{.004,.01,1.0/60,.02,1.0/30,.05})
            foreach(float residualOmega in new[]{0f,-.51f,-1f,-2f})
            {
                var l=new FeedbackLoop();l.Tick(F(0,0,0,0,speed),0,P);
                var f=F(1,dt,1,1,speed);f=f with{Wheels=f.Wheels.Select(w=>w with{OmegaRadPerSec=residualOmega,LongitudinalPatchMps=-speed+.04f}).ToImmutableArray()};
                var o=l.Tick(f,dt,P);Pulse(o,24);
                Need(l.Signals.Braking.ValidSlipWheels==4&&l.Signals.Wheels.All(w=>w.Kappa is null));
                for(double age=0;age<dt;age+=.004)Need(l.Tick(f,dt+age,P).LeftTrigger==o.LeftTrigger,"packet spacing must not fade a pulse");
            }
        });
        check("1.3 a steady full brake without slip is not a fabricated lock",()=>
        {
            var l=new FeedbackLoop();
            for(int n=0;n<100;n++){var o=l.Tick(F(n,n*.01,0,n==0?0:1),n*.01,P);Need(o.LeftTrigger.Mode==5&&!l.Signals.Braking.WarningActive&&!l.Signals.Braking.LockActive);}
        });
        check("1.3 near-lock velocity validation is symmetric and bounded",()=>
        {
            var w=F(0,0,1).Wheels[0] with{OmegaRadPerSec=-1,LongitudinalPatchMps=-89.99f};
            Need(BrakeSlip.TryRead(w,100,out double a)&&a>.99);
            Need(BrakeSlip.TryRead(w with{OmegaRadPerSec=1,GroundLongitudinalMps=90,LongitudinalPatchMps=89.99f},100,out double b)&&Math.Abs(a-b)<1e-9);
            foreach(var bad in new[]{w with{LongitudinalPatchMps=0},w with{LongitudinalPatchMps=null},w with{LongitudinalPatchMps=-80},w with{OmegaRadPerSec=-100},w with{GroundLongitudinalMps=float.NaN},w with{OmegaRadPerSec=float.NaN}})
                Need(!BrakeSlip.TryRead(bad,100,out _));
        });
        check("1.3 a brief warning has a bounded 160 ms tail",()=>
        {
            var l=new FeedbackLoop();Pulse(l.Tick(F(0,0,.08f),0,P),12);
            for(int n=1;n<16;n++)Pulse(l.Tick(F(n,n*.01),n*.01,P),12);
            Need(l.Tick(F(17,.17),.17,P).LeftTrigger.Mode==5);
        });
        check("1.3 warning hysteresis sustains the cue through threshold chatter",()=>
        {
            var l=new FeedbackLoop();Pulse(l.Tick(F(0,0,.07f),0,P),12);
            for(int n=1;n<100;n++)Pulse(l.Tick(F(n,n*.01,n%2==0?.055f:.045f),n*.01,P),12);
            for(int n=100;n<130;n++)l.Tick(F(n,n*.01),n*.01,P);
            Need(!l.Signals.Braking.WarningActive);
        });
        check("1.5 warning and lock retain distinct rates at gentler moderate gain",()=>
        {
            var l=new FeedbackLoop();var warning=l.Tick(F(0,0,.0601f),0,P);Pulse(warning,12);
            Need(Amplitude(warning.LeftTrigger)==2);
            var locked=l.Tick(F(1,.01,1,1),.01,P);Pulse(locked,24);
            Need(Amplitude(locked.LeftTrigger)==3&&Amplitude(locked.LeftTrigger)>Amplitude(warning.LeftTrigger));
        });
        check("1.3 severe cue holds briefly then returns to the warning stage",()=>
        {
            var l=new FeedbackLoop();Pulse(l.Tick(F(0,0,1),0,P),24);
            for(int n=1;n<16;n++)Pulse(l.Tick(F(n,n*.01,.1f),n*.01,P),24);
            Pulse(l.Tick(F(17,.17,.1f),.17,P),12);
        });
        check("1.3 cancelling conditions override cue hold immediately",()=>
        {
            var f=F(1,.01,1);
            foreach(var stop in new[]{f with{Brake01=0},f with{Brake01=.01f},f with{SpeedMps=2},f with{Gear=-1},f with{Paused=true},f with{Driving=false},f with{Connected=false},f with{Wheels=f.Wheels.Select(w=>w with{TireLoadN=0,TireVerticalDeflectionM=0}).ToImmutableArray()},f with{Wheels=f.Wheels.Select(w=>w with{LongitudinalPatchMps=null}).ToImmutableArray()}})
            {var l=new FeedbackLoop();Pulse(l.Tick(F(0,0,1),0,P),24);Need(l.Tick(stop,.01,P).LeftTrigger.Mode==5&&!l.Signals.Braking.LockActive);}
        });
        check("1.3 one invalid locked wheel cannot latch using other rolling wheels",()=>
        {
            var l=new FeedbackLoop();var f=F(0,0);f=f with{Wheels=f.Wheels.Select((w,i)=>i==0?w with{OmegaRadPerSec=0,LongitudinalPatchMps=-90}:w).ToImmutableArray()};
            Pulse(l.Tick(f,0,P),24);
            var next=F(1,.01);next=next with{Wheels=next.Wheels.Select((w,i)=>i==0?w with{LongitudinalPatchMps=null}:w).ToImmutableArray()};
            Need(l.Tick(next,.01,P).LeftTrigger.Mode==5&&!l.Signals.Braking.LockActive&&l.Signals.Braking.ValidSlipWheels==3);
        });
        check("1.3 timeout uses source age and clears held state",()=>
        {
            var l=new FeedbackLoop();var f=F(0,0,1) with{SourceAgeSec=.2};Pulse(l.Tick(f,0,P),24);
            Need(l.Tick(f,.051,P)==FeedbackFrame.Off&&!l.Signals.Braking.LockActive);
            Need(l.Tick(F(1,.06),.06,P).LeftTrigger.Mode==5);
        });
        check("1.3 duplicate packets do not extend a cue deadline",()=>
        {
            var l=new FeedbackLoop();var f=F(0,0,.08f);Pulse(l.Tick(f,0,P),12);
            for(int n=1;n<40;n++)Pulse(l.Tick(f,n*.004,P),12);
            Need(l.Tick(F(1,.17),.17,P).LeftTrigger.Mode==5);
        });
        check("1.3 repeated GT3 and Hyper switches clear pulse state in both directions",()=>
        {
            var l=new FeedbackLoop();
            for(int n=0;n<20;n++)
            {
                var hyper=F(n*2,n*.04,1);Pulse(l.Tick(hyper,hyper.CapturedAtSec,P),24);
                var gt=F(n*2+1,n*.04+.02) with{VehicleClass="LMGT3",AbsAvailable=true};
                var o=l.Tick(gt,gt.CapturedAtSec,P);
                Need(BrakeRouting.Legacy(gt,P)&&!l.Signals.Braking.WarningActive&&!l.Signals.Braking.LockActive&&o.LeftTrigger.P8!=24);
            }
        });
        check("1.3 gains remain monotonic with disabled and zero producing no pulse",()=>
        {
            foreach(float slip in new[]{.08f,1f})
            {
                int last=0;
                foreach(int gain in new[]{0,1,5,15,35,70,100})
                {var l=new FeedbackLoop();var o=l.Tick(F(0,0,slip),0,P with{Lock=new(true,gain)});if(gain==0)Need(o.LeftTrigger.Mode==5);else{Need(Amplitude(o.LeftTrigger)>=last);last=Amplitude(o.LeftTrigger);}}
                Need(new FeedbackLoop().Tick(F(0,0,slip),0,P with{Lock=new(false,35)}).LeftTrigger.Mode==5);
            }
        });
        check("1.3 cue timing setting validates and survives profile serialization",()=>
        {
            var p=P with{DefaultParameters=new(){BrakeResponse=new(){CueHoldSeconds=.24}}};
            var copy=JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(p,ProfileStore.Json),ProfileStore.Json)!;
            copy.Validate();Need(copy.DefaultParameters.BrakeResponse.CueHoldSeconds==.24);
            foreach(double bad in new[]{double.NaN,double.PositiveInfinity,-1,0,.5})
            {bool rejected=false;try{new BrakeResponse{CueHoldSeconds=bad}.Validate();}catch(InvalidDataException){rejected=true;}Need(rejected);}
        });
    }
}
