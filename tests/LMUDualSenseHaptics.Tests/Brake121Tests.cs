using System.Collections.Immutable;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Physics;
using LMUDualSenseHaptics.Runtime;
using LMUDualSenseHaptics.Telemetry;

internal static class Brake121Tests
{
    private static void Need(bool value,string reason="assertion"){if(!value)throw new Exception(reason);}
    private static TelemetryFrame F(int n,float brake=0,string cls="Hypercar") =>
        new(n,n*.01,0,true,true,false,"same-slot",cls,30,0,0,-30,0,0,0,brake,0,0,3,5000,8000,false,false,null,null,
            Enumerable.Range(0,4).Select(_=>new WheelFrame(90,0,3000,3000,0,.01f,30,0,null,true,BrakePressure01:brake,LongitudinalPatchMps:0)).ToImmutableArray(),
            n*.01,14200,VehicleModel:"same-model");
    private static WheelState[] Trusted()=>Enumerable.Range(0,4).Select(_=>new WheelState{Kappa=0}).ToArray();
    private static int[] Zones(TriggerEffect effect)
    {
        byte[] b=new byte[11];effect.WriteTo(b);int active=b[1]|b[2]<<8;
        uint packed=(uint)(b[3]|b[4]<<8|b[5]<<16|b[6]<<24);
        return Enumerable.Range(0,10).Select(i=>(active&(1<<i))==0?0:1+(int)((packed>>(3*i))&7)).ToArray();
    }
    public static void Run(Action<string,Action> check)
    {
        check("1.2.1 repeated GT3 Hyper GT3 switching changes actual HID output without restart",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile();int n=0;
            for(int cycle=0;cycle<4;cycle++)
            {
                foreach(string cls in new[]{"LMGT3","Hypercar","GT3","LMP2"})
                {
                    var a=F(n++,.8f,cls) with{AbsIntervening=true,AbsAvailable=true,AbsSetting=5};loop.Tick(a,a.CapturedAtSec,p);
                    var b=F(n++,.8f,cls) with{AbsIntervening=true,AbsAvailable=true,AbsSetting=5};var output=loop.Tick(b,b.CapturedAtSec,p);
                    bool gt3=cls.Contains("GT3");Need(output.LeftTrigger.Mode==(gt3?0x26:0x21),cls);
                    Need(loop.LeftTriggerSource==(gt3?"Abs":"BrakeBase"),cls);
                    Need(gt3||loop.Signals.Braking.Budget.Prompt>0);
                }
            }
        });
        check("1.2.1 authoritative class and missing-class model detection",()=>
        {
            Need(BrakeRouting.IsGt3(F(0) with{VehicleClass="",VehicleModel="Porsche 911 GT3 R"}));
            Need(!BrakeRouting.IsGt3(F(0) with{VehicleClass="Hypercar",VehicleModel="GT3 stale model"}));
            Need(BrakeRouting.IsGt3(F(0) with{VehicleClass="lmgt3"}));
            Need(!BrakeRouting.IsGt3(F(0) with{VehicleClass="",VehicleModel=""}));
        });
        check("1.2.1 GT3 legacy base and ABS/lock priority match old output bytes",()=>
        {
            var p=new Profile();var f=F(0,.8f,"GT3");var e=new SignalEngine();var t=new TriggerEngine();
            Need(t.Render(e,f,0,p).Left==TriggerEffect.FeedbackZones(new byte[]{2,2,2,2,2,3,3,4,4,4}));
            e.Abs=1;e.LegacyLock=1;var expected=TriggerEffect.Vibration(0,4,15);
            Need(t.Render(e,f,0,p).Left==expected);
            e.Abs=0;Need(t.Render(e,f,.03,p).Left.P8==8);
        });
        check("1.2.1 class change with repeated sequence resets state",()=>
        {
            var e=new SignalEngine();e.Update(F(0,.8f,"GT3"),new());
            e.Update(F(0,0,"Hypercar"),new());Need(e.Braking.Mode==BrakeFeedbackMode.NoAbs&&e.Braking.Budget.Prompt>0);
        });
        check("1.2.1 manual mode can return to auto repeatedly on same frame",()=>
        {
            var loop=new FeedbackLoop();var f=F(0,0,"GT3");
            for(int i=0;i<3;i++)
            {
                loop.Tick(f,0,new(){BrakeMode=BrakeFeedbackMode.NoAbs});Need(loop.LeftTriggerSource=="BrakeBase"&&loop.Signals.Braking.Mode==BrakeFeedbackMode.NoAbs);
                loop.Tick(f,0,new());Need(loop.LeftTriggerSource=="BrakeBase"&&loop.Signals.Braking.Budget.Prompt==0);
            }
        });
        check("1.2.1 unlearned reference is diagnostic only and does not resist the finger",()=>
        {
            var loop=new FeedbackLoop();var output=loop.Tick(F(0),0,new());var z=Zones(output.LeftTrigger);
            Need(loop.Signals.Braking.Risk==0&&loop.Signals.Braking.Budget.Prompt is >.5 and <.8);
            Need(loop.LeftTriggerSource=="BrakeBase"&&z[0]==1&&z.Max()==3);
        });
        check("1.2.1 baseline alone works and all brake features can be disabled",()=>
        {
            var p=new Profile{Lock=new(false,50),Abs=new(false,50)};var loop=new FeedbackLoop();
            var o=loop.Tick(F(0),0,p);Need(loop.LeftTriggerSource=="BrakeBase"&&Zones(o.LeftTrigger)[0]==1&&Zones(o.LeftTrigger).Max()>0);
            Need(loop.Tick(F(1),.01,p with{BrakeBase=new(false,50)}).LeftTrigger==TriggerEffect.Off);
        });
        check("1.2.1 rising demand advances prompt without any slip",()=>
        {
            var a=new BrakeBudget();var b=new BrakeBudget();var c=new BrakeResponse();
            a.Update(F(0,.3f),null,0,c,100);b.Update(F(0,.6f),null,0,c,100);
            a.Update(F(1,.6f),F(0,.3f),0,c,100);b.Update(F(1,.6f),F(0,.6f),0,c,100);
            Need(a.Prompt<b.Prompt&&a.Confidence==0);
        });
        check("1.2.1 live pressure rise advances demand at steady pedal without slip",()=>
        {
            var a=new BrakeBudget();var f=F(0,.6f);var g=F(1,.6f) with{Wheels=F(1,.6f).Wheels.Select(w=>w with{BrakePressure01=.65f}).ToImmutableArray()};
            a.Update(f,null,0,new(),100);double old=a.Prompt;a.Update(g,f,0,new(),100);
            Need(a.DemandRate>0&&a.Prompt<old);
        });
        check("1.2.1 lateral demand lowers reference but is never lock evidence",()=>
        {
            var loop=new FeedbackLoop();loop.Tick(F(0),0,new());double before=loop.Signals.Braking.Budget.Prompt;
            loop.Tick(F(1,.5f) with{AccelX=8},.01,new());Need(loop.Signals.Braking.Budget.Prompt<before&&loop.Signals.Braking.Risk==0&&loop.Signals.Lock==0);
        });
        check("1.2.1 varying valid load reduces reference before slip",()=>
        {
            var b=new BrakeBudget();TelemetryFrame? previous=null;
            for(int n=0;n<12;n++){var f=F(n) with{Wheels=F(n).Wheels.Select(w=>w with{TireLoadN=3000+n*10}).ToImmutableArray()};b.Update(f,previous,0,new(),100);previous=f;}
            double before=b.Prompt;var unload=F(12,.4f) with{Wheels=F(12).Wheels.Select(w=>w with{TireLoadN=2000}).ToImmutableArray()};
            b.Update(unload,previous,0,new(),100);Need(b.LoadFactor<.8&&b.Prompt<before);
        });
        check("1.2.1 zero or constant loads cannot masquerade as learned capacity",()=>
        {
            foreach(float load in new[]{0f,3000f})
            {var b=new BrakeBudget();TelemetryFrame? prev=null;for(int n=0;n<50;n++){var f=F(n) with{Wheels=F(n).Wheels.Select(w=>w with{TireLoadN=load}).ToImmutableArray()};b.Update(f,prev,0,new(),100);prev=f;}Need(b.LoadFactor==1&&b.Evidence.Contains("有效载荷 0/4"));}
        });
        check("1.2.1 safe braking never teaches a true limit",()=>
        {
            var b=new BrakeBudget();for(int n=0;n<100;n++)b.Update(F(n,.9f),n==0?null:F(n-1,.9f),0,new(),100);
            Need(b.Stops==0&&b.Confidence==0&&b.ContextCount==0);
        });
        check("1.2.1 confidence needs independent stops and current setup context",()=>
        {
            var b=new BrakeBudget();TelemetryFrame? prev=null;int n=0;
            for(int stop=0;stop<3;stop++)
            {
                foreach(var pair in new[]{(0f,0d),(.6f,0d),(.65f,.08d),(.65f,0d),(.65f,.08d)})
                {var f=F(n++,pair.Item1);b.Update(f,prev,pair.Item2,new(),100);prev=f;}
                Need(b.Stops==stop+1);if(stop==0)Need(b.Confidence==0);
            }
            Need(b.Confidence>0);
            foreach(var changed in new[]{F(n,.5f) with{RearBrakeBias=.55f},F(n,.5f) with{BrakeMigration=2},F(n,.5f) with{MotorState=3,MotorTorqueNm=-100},F(n,.5f) with{SpeedMps=70}})
            {b.Update(changed with{SourceTimeSec=n++*.01},prev,0,new(),100);Need(b.Stops==0&&b.Confidence==0);}
        });
        check("1.2.1 stale and car changes erase learned and preset walls",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile();loop.Tick(F(0),0,p);Need(loop.Signals.Braking.Budget.Prompt>0);
            Need(loop.Tick(F(0),.3,p)==FeedbackFrame.Off&&loop.Signals.Braking.Budget.Prompt==0);
            loop.Tick(F(30) with{CarId="new"},.3,p);Need(loop.Signals.Braking.Budget.Stops==0&&loop.Signals.Braking.Budget.Confidence==0);
        });
        check("1.2.1 physics frame intervals ignore repeated output ticks",()=>
        {
            var loop=new FeedbackLoop();loop.Tick(F(0),0,new());loop.Tick(F(1),.01,new());var timing=loop.Signals.Braking.Budget.FrameTiming;
            for(int i=0;i<10;i++)loop.Tick(F(1),.01+i*.004,new());Need(loop.Signals.Braking.Budget.FrameTiming==timing&&timing.Contains("10.0/10.0/10.0"));
        });
        check("1.2.1 game curve calibration maps brake reference back to trigger travel",()=>
        {
            var c=new BrakeResponse{TravelDeadzone=.1,TravelEnd=.9,InputExponent=2};c.Validate();
            Need(Math.Abs(c.TravelFor(.25)-.5)<1e-9&&c.TravelFor(0)==.1&&c.TravelFor(1)==.9);
            foreach(var bad in new[]{c with{InitialLimit=double.NaN},c with{InputExponent=0},c with{DemandLeadSeconds=1}})
            {bool rejected=false;try{bad.Validate();}catch(InvalidDataException){rejected=true;}Need(rejected);}
        });
        check("1.2.1 new ABI offsets and optional invalid field isolation",()=>
        {
            var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","lmu14200-options0.snapshot.bin"));
            int v=LmuLayout.TelemetryOffset+4+bytes[LmuLayout.TelemetryOffset+1]*LmuLayout.VehicleSize;
            void D(int offset,double value)=>BitConverter.GetBytes(value).CopyTo(bytes,v+offset);
            D(428,.45);D(664,.53);D(712,-120);bytes[v+744]=3;bytes[v+760]=4;
            D(848+56,-.5);D(848+96,123);D(848+88,456);D(848+120,180);D(848+136,330);D(848+152,.8);bytes[v+848+240]=2;
            var f=new LmuLayout().Parse(bytes,0)!;Need(Math.Abs(f.FilteredBrake01!.Value-.45)<1e-6&&Math.Abs(f.RearBrakeBias!.Value-.53)<1e-6);
            Need(f.MotorTorqueNm==-120&&f.MotorState==3&&f.BrakeMigration==4);
            var w=f.Wheels[0];Need(w.LongitudinalPatchMps==-.5f&&w.LongitudinalForceN==123&&w.LateralForceN==456&&w.TirePressureKpa==180&&w.TireTemperatureK==330&&w.CompoundIndex==2);
            D(712,double.NaN);D(848+96,double.NaN);D(848+32,8);
            f=new LmuLayout().Parse(bytes,0)!;Need(f.MotorTorqueNm is null&&f.Wheels[0].LongitudinalForceN is null&&f.Wheels[0].BrakePressure01 is null);
        });
    }
}
