using System.Collections.Immutable;
using System.Text.Json;
using LMUDualSenseHaptics.Config;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Physics;
using LMUDualSenseHaptics.Runtime;
using LMUDualSenseHaptics.Telemetry;

internal static class BrakePreviewTests
{
    private static void Require(bool b, string reason = "assertion") { if (!b) throw new Exception(reason); }
    private static TelemetryFrame Frame(int n, float slip = 0, float brake = 0, bool abs = false) =>
        new(n,n*.01,0,true,true,false,"preview","Hypercar",20,0,0,-20,0,0,0,brake,0,0,3,5000,8000,
            abs,false,false,true,Enumerable.Range(0,4).Select(_=>new WheelFrame(60*(1-slip),0,3000,3000,0,.01f,20,0,null,true,
                BrakePressure01:brake,SlidingFraction:0,LongitudinalPatchMps:20*slip)).ToImmutableArray(),n*.01,14200,VehicleModel:"preview",AbsSetting:0);
    private static SignalEngine Warm(Profile? p = null)
    {
        var e=new SignalEngine();for(int n=0;n<=150;n++)e.Update(Frame(n),p??new());return e;
    }
    private static int[] Zones(TriggerEffect f)
    {
        byte[] b=new byte[11];f.WriteTo(b);int active=b[1]|b[2]<<8;
        uint packed=(uint)(b[3]|b[4]<<8|b[5]<<16|b[6]<<24);
        return Enumerable.Range(0,10).Select(i=>(active&(1<<i))==0?0:1+(int)((packed>>(3*i))&7)).ToArray();
    }
    public static void Run(Action<string,Action> check)
    {
        check("preview ABI: pressure, sliding fraction and ABS setting are separate fields",()=>
        {
            var b=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","lmu14200-options0.snapshot.bin"));
            int v=LmuLayout.TelemetryOffset+4+b[LmuLayout.TelemetryOffset+1]*LmuLayout.VehicleSize;
            for(int i=0;i<4;i++){int w=v+848+i*LmuLayout.WheelSize;BitConverter.GetBytes(.1*(i+1)).CopyTo(b,w+32);BitConverter.GetBytes(.2*(i+1)).CopyTo(b,w+112);}
            b[v+756]=3;b[v+757]=10;b[v+746]=0;
            var f=new LmuLayout().Parse(b,0)!;
            Require(f.AbsSetting==3&&f.AbsAvailable==true&&!f.AbsIntervening);
            for(int i=0;i<4;i++)Require(Math.Abs(f.Wheels[i].BrakePressure01!.Value-.1*(i+1))<1e-6&&Math.Abs(f.Wheels[i].SlidingFraction!.Value-.2*(i+1))<1e-6);
        });
        check("preview automatic mode routes by current class, not ABS metadata",()=>
        {
            var f=Frame(0);var p=new Profile();
            Require(BrakeSignalEngine.ResolveMode(f,p)==BrakeFeedbackMode.NoAbs);
            Require(BrakeSignalEngine.ResolveMode(f with{AbsAvailable=true,AbsSetting=4},p)==BrakeFeedbackMode.NoAbs);
            Require(BrakeSignalEngine.ResolveMode(f with{AbsAvailable=true,AbsSetting=0},p)==BrakeFeedbackMode.NoAbs);
            Require(BrakeSignalEngine.ResolveMode(f with{AbsAvailable=null,AbsSetting=null},p)==BrakeFeedbackMode.NoAbs);
            Require(BrakeSignalEngine.ResolveMode(f with{VehicleClass="LMGT3",AbsIntervening=false},p)==BrakeFeedbackMode.Abs);
            Require(BrakeSignalEngine.ResolveMode(f with{AbsIntervening=true},p with{BrakeMode=BrakeFeedbackMode.NoAbs})==BrakeFeedbackMode.NoAbs);
        });
        check("preview no-ABS warning precedes old 30 percent trigger",()=>
        {
            var p=new Profile();var e=Warm();var f=Frame(151,.08f,.7f);e.Update(f,p);
            var t=new TriggerEngine();var o=t.Render(e,f,f.CapturedAtSec,p);
            Require(e.Braking.MaxSlip<.1&&e.Braking.Risk>.2&&t.LeftSource=="BrakeLimit");
            Require(o.Left.Mode==0x21&&Zones(o.Left)[6]>Zones(new TriggerEngine().Render(new(),f,f.CapturedAtSec,p).Left)[6]);
        });
        check("preview trend prediction advances warning at equal measured slip",()=>
        {
            var a=Warm();var b=Warm();var f=Frame(151,.05f,.7f);a.Update(f,new());
            var noPrediction=new Profile{DefaultParameters=new(){BrakeResponse=new(){PredictionSeconds=0}}};b.Update(f,noPrediction);
            Require(a.Braking.Risk>b.Braking.Risk&&b.Braking.Risk==0);
        });
        check("preview rear-only risk is not averaged away",()=>
        {
            var e=Warm();var f=Frame(151,0,.7f);f=f with{Wheels=f.Wheels.Select((w,i)=>i==2?w with{OmegaRadPerSec=48,LongitudinalPatchMps=4}:w).ToImmutableArray()};
            e.Update(f,new());Require(e.Braking.RearRisk>.5&&e.Braking.FrontRisk==0&&e.Braking.Risk==e.Braking.RearRisk);
        });
        check("preview wheel collapse works before radius calibration",()=>
        {
            var e=new SignalEngine();e.Update(Frame(0),new());e.Update(Frame(1,.10f,.7f),new());
            Require(e.Wheels.All(w=>w.Kappa is null)&&e.Braking.Risk>.3);
        });
        check("preview uncalibrated complete lock persists and recovers",()=>
        {
            var e=new SignalEngine();e.Update(Frame(0),new());e.Update(Frame(1,1,.7f),new());
            for(int n=2;n<30;n++)e.Update(Frame(n,1,.7f),new());
            Require(e.Braking.MaxSlip>.99&&e.Braking.Risk>.9);
            for(int n=30;n<70;n++)e.Update(Frame(n,0,.7f),new());
            Require(e.Braking.MaxSlip==0&&e.Braking.Risk<.001);
        });
        check("preview ordinary car deceleration and local corner speeds do not warn",()=>
        {
            var e=Warm();var f=Frame(151,0,.7f);f=f with{SpeedMps=18,Wheels=f.Wheels.Select((w,i)=>w with{GroundLongitudinalMps=16+i,OmegaRadPerSec=3*(16+i),LongitudinalPatchMps=0}).ToImmutableArray()};
            e.Update(f,new());Require(e.Braking.Risk<1e-8);
        });
        check("preview lateral slip and sliding fraction alone cannot trigger brake warning",()=>
        {
            var e=Warm();var f=Frame(151,0,.7f);f=f with{Wheels=f.Wheels.Select(w=>w with{GroundLateralMps=5,SlidingFraction=1}).ToImmutableArray()};
            e.Update(f,new());Require(e.Braking.Risk==0);
        });
        check("preview airborne wheel and subsequent rolling landing do not create a derivative spike",()=>
        {
            var e=Warm();var f=Frame(151,.8f,.7f);f=f with{Wheels=f.Wheels.Select(w=>w with{TireLoadN=0,TireVerticalDeflectionM=0}).ToImmutableArray()};
            e.Update(f,new());Require(e.Braking.Risk==0);
            e.Update(Frame(152,0,.7f),new());Require(e.Braking.Risk==0);
        });
        check("preview brake lift and low speed immediately release all brake events",()=>
        {
            var e=Warm();e.Update(Frame(151,.4f,.7f),new());Require(e.Braking.Risk>0);
            e.Update(Frame(152,.4f,0),new());Require(e.Braking.Risk==0&&e.Lock==0);
            e.Update(Frame(153,.4f,.7f) with{SpeedMps=2},new());Require(e.Braking.Risk==0);
        });
        check("preview ABS wall acts on first physics frame and tracks lower onset in same stop",()=>
        {
            var e=new SignalEngine();var p=new Profile{BrakeMode=BrakeFeedbackMode.Abs};var f=Frame(0,0,.8f,true);
            e.Update(f,p);var t=new TriggerEngine();var o=t.Render(e,f,0,p);
            Require(e.Braking.AbsWall==1&&t.LeftSource=="AbsWall"&&o.Left.Mode==0x21);
            e.Update(Frame(1,0,.78f,false),p);Require(e.Braking.AbsWall>0);
            e.Update(Frame(2,0,.75f,true),p);Require(Math.Abs(e.Braking.AbsOnsetBrake!.Value-.75)<1e-6);
            e.Update(Frame(3,0,.6f,false),p);Require(e.Braking.AbsOnsetBrake is null&&e.Braking.AbsWall==0);
        });
        check("preview sustained ABS follows a lower active brake position",()=>
        {
            var e=new SignalEngine();var p=new Profile{BrakeMode=BrakeFeedbackMode.Abs};
            e.Update(Frame(0,0,.8f,true),p);e.Update(Frame(1,0,.5f,true),p);
            Require(Math.Abs(e.Braking.AbsOnsetBrake!.Value-.5)<1e-6&&e.Braking.AbsWall==1);
            var force=BrakeTrigger.Render(e.Braking,Frame(1,0,.5f,true),.01,p).Effect;
            var baseline=BrakeTrigger.Render(new(),Frame(1,0,.5f,true),.01,p).Effect;
            Require(Zones(force)[4]>Zones(baseline)[4]);
        });
        check("preview ABS threshold is not carried between braking manoeuvres or vehicle sessions",()=>
        {
            var p=new Profile{BrakeMode=BrakeFeedbackMode.Abs};var e=new SignalEngine();e.Update(Frame(0,0,.8f,true),p);
            e.Update(Frame(1),p);Require(e.Braking.AbsOnsetBrake is null);
            e.Update(Frame(2,0,.7f,true),p);e.Update(Frame(3) with{CarId="new-car"},p);Require(e.Braking.AbsOnsetBrake is null);
        });
        check("preview no-ABS lock uses a distinct hardware pulse; ABS wall remains independent",()=>
        {
            var p=new Profile();var e=Warm();var f=Frame(151,.6f,.8f);e.Update(f,p);
            var t=new TriggerEngine();var o=t.Render(e,f,f.CapturedAtSec,p).Left;
            var baseline=t.Render(new(),f,f.CapturedAtSec,p).Left;
            Require(o.Mode==0x26&&o.P8==24&&baseline.Mode==0x21);
            var abs=p with{BrakeMode=BrakeFeedbackMode.Abs};e.Update(Frame(152,.6f,.8f,true),abs);
            Require(e.Braking.Risk==0&&e.Lock==0&&e.Braking.AbsWall==1);
        });
        check("preview force gains are monotonic and disabling both brake features removes the wall",()=>
        {
            var e=Warm();var f=Frame(151,.4f,.7f);e.Update(f,new());int old=0;
            foreach(int gain in new[]{0,25,50,75,100})
            {var o=BrakeTrigger.Render(e.Braking,f,f.CapturedAtSec,new(){Lock=new(true,gain)});int peak=Zones(o.Effect).Max();Require(peak>=old&&peak<=8);old=peak;}
            var off=BrakeTrigger.Render(e.Braking,f,f.CapturedAtSec,new(){Lock=new(false,50),BrakeBase=new(false,50)});
            Require(off.Effect==TriggerEffect.Off);
        });
        check("preview duplicate telemetry cannot integrate wheel collapse",()=>
        {
            var e=Warm();var f=Frame(151,.1f,.7f);e.Update(f,new());double risk=e.Braking.Risk;
            for(int i=0;i<20;i++)e.Update(f,new());Require(e.Braking.Risk==risk);
        });
        check("preview physics derivative ignores host capture jitter",()=>
        {
            var a=Warm();var b=Warm();for(int n=151;n<158;n++)
            {var f=Frame(n,.01f*(n-150),.7f);a.Update(f,new());b.Update(f with{CapturedAtSec=f.CapturedAtSec+(n%2==0?.002:0)},new());}
            Require(Math.Abs(a.Braking.Risk-b.Braking.Risk)<1e-5);
        });
        check("preview stale telemetry turns off the wall and a fresh session resets it",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile{BrakeMode=BrakeFeedbackMode.Abs};var f=Frame(0,0,.8f,true);
            var active=loop.Tick(f,0,p);Require(active.LeftTrigger.Mode==0x21&&loop.LeftTriggerSource=="AbsWall");
            Require(loop.Tick(f,.251,p)==FeedbackFrame.Off&&loop.Signals.Braking.AbsOnsetBrake is null);
            loop.Tick(Frame(1) with{SourceTimeSec=1,CapturedAtSec=1},1,p);Require(loop.Signals.Braking.AbsWall==0);
        });
        check("preview configuration validates and round trips",()=>
        {
            var p=new Profile{BrakeMode=BrakeFeedbackMode.NoAbs};var copy=JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(p,ProfileStore.Json),ProfileStore.Json)!;
            copy.Validate();Require(copy.BrakeMode==p.BrakeMode&&copy.DefaultParameters.BrakeResponse==p.DefaultParameters.BrakeResponse);
            foreach(var c in new[]{new BrakeResponse{WarningSlip=double.NaN},new BrakeResponse{CriticalSlip=.01},new BrakeResponse{PredictionSeconds=.2}})
            {bool rejected=false;try{c.Validate();}catch(InvalidDataException){rejected=true;}Require(rejected);}
        });
        check("preview new wheel telemetry rejects nonfinite or out-of-range values",()=>
        {
            foreach(var value in new[]{float.NaN,float.PositiveInfinity,-1f,2f})
            {var f=Frame(0);f=f with{Wheels=f.Wheels.Select(w=>w with{BrakePressure01=value}).ToImmutableArray()};bool rejected=false;try{f.Validate();}catch(InvalidDataException){rejected=true;}Require(rejected);}
        });
    }
}
