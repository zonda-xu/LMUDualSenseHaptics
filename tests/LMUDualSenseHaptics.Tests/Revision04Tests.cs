using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Config;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Physics;
using LMUDualSenseHaptics.Runtime;
using LMUDualSenseHaptics.Telemetry;
using static LMUDualSenseHaptics.Physics.Maths;

internal static class Revision04Tests
{
    private static void Assert(bool value,string message="assertion")
    {if(!value)throw new Exception(message);}
    private static TelemetryFrame Frame(int n,double t,float speed=20,bool tc=false)
    {
        var wheels=Enumerable.Range(0,4).Select(_=>new WheelFrame(60,0,3000,3000,0,0,speed,0,null,true)).ToImmutableArray();
        return new(n,t,0,true,true,false,"test","GT3",speed,0,0,-speed,0,0,0,0,1,0,3,5000,8000,false,tc,true,true,wheels,t,14200,VehicleModel:"test");
    }
    private static TelemetryFrame Wheel(TelemetryFrame f,int index,Func<WheelFrame,WheelFrame> update)=>
        f with{Wheels=f.Wheels.Select((w,i)=>i==index?update(w):w).ToImmutableArray()};
    private static int Peak(TriggerEffect effect)
    {
        if(effect.Mode==5)return 0;
        uint packed=(uint)(effect.P2|(effect.P3<<8)|(effect.P4<<16)|(effect.P5<<24));int mask=effect.P0|(effect.P1<<8);
        return Enumerable.Range(0,10).Max(i=>(mask&(1<<i))==0?0:(int)((packed>>(3*i))&7)+1);
    }
    private static Profile RoadOnly()=>new(){Engine=new(false,15),GripSlip=new(false,50),Impact=new(false,60),Shift=new(false,45),Abs=new(false,50)};
    public static void Run(Action<string,Action> check)
    {
        check("v0.4 default R2 free even with full throttle / shift / spin / redline",()=>
        {
            var triggers=new TriggerEngine();var signals=new SignalEngine{Shift=1,Tc=1,Spin=1,Redline=1};
            for(int n=0;n<100;n++)Assert(triggers.Render(signals,Frame(n,n*.004),n*.004,new Profile()).Right==TriggerEffect.Off);
        });
        check("v0.4 real TC has steady significant resistance on first frame",()=>
        {
            var triggers=new TriggerEngine();var result=triggers.Render(new(),Frame(0,0,tc:true),0,new Profile());
            Assert(result.Right.Mode==0x21&&Peak(result.Right)==6&&triggers.RightSource=="Tc");
        });
        check("v0.4 TC OFF releases immediately without an envelope tail",()=>
        {
            var triggers=new TriggerEngine();var signals=new SignalEngine{Tc=1};var profile=new Profile();
            Assert(triggers.Render(signals,Frame(0,0,tc:true),0,profile).Right.Mode==0x21);
            Assert(triggers.Render(signals,Frame(1,.004,tc:false),.004,profile).Right==TriggerEffect.Off);
        });
        check("v0.4 TC not replaced by shift and disabled TC emits OFF",()=>
        {
            var signals=new SignalEngine{Shift=1,Tc=1,Spin=1,Redline=1};var triggers=new TriggerEngine();
            Assert(triggers.Render(signals,Frame(0,0,tc:true),0,new Profile()).Right.Mode==0x21);
            Assert(triggers.Render(signals,Frame(1,.01,tc:true),.01,new Profile{Tc=new(true,0)}).Right==TriggerEffect.Off);
            Assert(triggers.Render(signals,Frame(2,.02,tc:true),.02,new Profile{Tc=new(false,75)}).Right==TriggerEffect.Off);
        });
        check("v0.4 all TC gain steps remain bounded and zero gain is OFF",()=>
        {
            int old=0;for(int gain=0;gain<=100;gain++)
            {
                var r=new TriggerEngine().Render(new(),Frame(0,0,tc:true),0,new Profile{Tc=new(true,gain)}).Right;
                int peak=Peak(r);Assert(peak>=old&&peak<=8&&(gain>0||r.Mode==5));old=peak;
            }
        });
        check("v0.4 L2 identical across TC-only and legacy modes",()=>
        {
            var a=new TriggerEngine();var b=new TriggerEngine();var p=new Profile();
            for(int n=0;n<200;n++)
            {
                var signals=new SignalEngine{Abs=n%40<20?.8:0,Lock=n%30<15?.6:0};var f=Frame(n,n*.01) with{Brake01=.7f};
                Assert(a.Render(signals,f,f.CapturedAtSec,p).Left==b.Render(signals,f,f.CapturedAtSec,p with{ThrottleMode=ThrottleFeedbackMode.Legacy}).Left);
            }
        });
        check("v0.4 road parser reads surface byte and bounded material string",()=>
        {
            byte[] raw=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","lmu14200-options0.snapshot.bin"));
            int v=LmuLayout.TelemetryOffset+4+raw[LmuLayout.TelemetryOffset+1]*LmuLayout.VehicleSize;
            int q=v+848+2*LmuLayout.WheelSize;raw[q+176]=5;
            raw.AsSpan(q+160,16).Clear();Encoding.ASCII.GetBytes("RMBL_test").CopyTo(raw,q+160);
            var frame=new LmuLayout().Parse(raw,0)!;
            Assert(frame.Wheels[2].SurfaceType==5&&frame.Wheels[2].TerrainName=="RMBL_test"&&frame.Wheels[2].IsKerb);
            raw[q+176]=255;raw.AsSpan(q+160,16).Clear();
            Assert(new LmuLayout().Parse(raw,0)!.Wheels[2].SurfaceType is null);
        });
        check("v0.4 dry-surface kerb material fallback and grass not a kerb",()=>
        {
            var wheel=Frame(0,0).Wheels[0];
            foreach(string material in new[]{"RMBL01","rumble","KERB_A","curb01"})Assert((wheel with{SurfaceType=0,TerrainName=material}).IsKerb);
            foreach(byte surface in new byte[]{0,1,2,3,4,6})Assert(!(wheel with{SurfaceType=surface,TerrainName="GRASS"}).IsKerb);
        });
        check("v0.5 material fallback still rejects known grass gravel and wet surfaces",()=>
        {
            var wheel=Frame(0,0).Wheels[0];
            foreach(byte? surface in new byte?[]{1,2,3,4,6})
                foreach(string material in new[]{"RMBL01","rumble","KERB_A","curb01"})
                    Assert(!(wheel with{SurfaceType=surface,TerrainName=material}).IsKerb);
            Assert((wheel with{SurfaceType=5,TerrainName=""}).IsKerb);
        });
        check("v0.4 imported v0.3 TC gain is preserved",()=>
        {
            var p=JsonSerializer.Deserialize<Profile>("{\"Tc\":{\"Enabled\":true,\"Strength\":50}}",ProfileStore.Json)!;
            p.Validate();Assert(p.Tc.Strength==50&&p.ThrottleMode==ThrottleFeedbackMode.TcOnly);
            Assert(Peak(new TriggerEngine().Render(new(),Frame(0,0,tc:true),0,p).Right)==4);
        });
        check("v0.4 tagged flat left kerb works without suspension or load changes",()=>
        {
            var loop=new FeedbackLoop();var profile=RoadOnly();loop.Tick(Frame(0,0),0,profile);
            var f=Wheel(Frame(1,.01),0,w=>w with{SurfaceType=5});var output=loop.Tick(f,.01,profile);
            Assert(loop.Layers.KerbL>.15&&output.LeftActuator>30&&output.LeftActuator>output.RightActuator*3);
            Assert(loop.Signals.Impact==0&&loop.Channels.Kerb>0);
        });
        check("v0.4 tagged right rear kerb localizes to right grip",()=>
        {
            var loop=new FeedbackLoop();var profile=RoadOnly();loop.Tick(Frame(0,0),0,profile);
            var f=Wheel(Frame(1,.01),3,w=>w with{SurfaceType=5});var output=loop.Tick(f,.01,profile);
            Assert(output.RightActuator>30&&output.RightActuator>output.LeftActuator*3&&loop.Layers.KerbR>loop.Layers.KerbL);
        });
        check("v0.4 tiny suspension-only kerb oscillation is not force-gated",()=>
        {
            var loop=new FeedbackLoop();var profile=RoadOnly();int peak=0;
            for(int n=0;n<100;n++)
            {
                var f=Wheel(Frame(n,n*.01),0,w=>w with{SuspDeflectionM=(float)(.0008*Math.Sin(2*Math.PI*15*n*.01))});
                var output=loop.Tick(f,f.CapturedAtSec,profile);peak=Math.Max(peak,output.LeftActuator);
            }
            Assert(peak>20,$"peak {peak}");
        });
        check("v0.4 wheel vertical position is an independent road input",()=>
        {
            var loop=new FeedbackLoop();var profile=RoadOnly();loop.Tick(Frame(0,0),0,profile);
            var f=Wheel(Frame(1,.01),1,w=>w with{WheelYLocationM=.003f});var output=loop.Tick(f,.01,profile);
            Assert(output.RightActuator>20&&loop.Signals.RoadSignals.Wheels[1].Texture>0);
        });
        check("v0.4 tire deflection and load changes independently reach road channel",()=>
        {
            foreach(bool tire in new[]{false,true})
            {
                var loop=new FeedbackLoop();var profile=RoadOnly();loop.Tick(Frame(0,0),0,profile);
                var f=Wheel(Frame(1,.01),0,w=>tire?w with{TireVerticalDeflectionM=.001f}:w with{TireLoadN=3400});
                Assert(loop.Tick(f,.01,profile).LeftActuator>20);
            }
        });
        check("v0.4 stationary or airborne tagged kerb has no road/kerb output",()=>
        {
            foreach(bool airborne in new[]{false,true})
            {
                var loop=new FeedbackLoop();var profile=RoadOnly();loop.Tick(Frame(0,0),0,profile);
                var f=Frame(1,.01,speed:airborne?20:0);
                f=f with{Wheels=f.Wheels.Select(w=>w with{SurfaceType=5,TireLoadN=airborne?0:3000,SuspDeflectionM=.04f}).ToImmutableArray()};
                var output=loop.Tick(f,.01,profile);Assert(output.LeftActuator==0&&output.RightActuator==0);
            }
        });
        check("v0.4 smooth road and response minimum never create permanent rumble",()=>
        {
            var loop=new FeedbackLoop();var profile=RoadOnly();
            for(int n=0;n<200;n++)
            {
                var output=loop.Tick(Frame(n,n*.01),n*.01,profile);Assert(output.LeftActuator==0&&output.RightActuator==0);
            }
            Assert(RoadHaptics.Response(0,new RoadResponse())==0);
        });
        check("v0.4 kerb exits and decays without lingering output",()=>
        {
            var loop=new FeedbackLoop();var profile=RoadOnly();loop.Tick(Frame(0,0),0,profile);
            for(int n=1;n<20;n++){var f=Wheel(Frame(n,n*.01),0,w=>w with{SurfaceType=5});loop.Tick(f,f.CapturedAtSec,profile);}
            FeedbackFrame output=default;for(int n=20;n<100;n++)output=loop.Tick(Frame(n,n*.01),n*.01,profile);
            Assert(output.LeftActuator==0&&output.RightActuator==0);
        });
        check("v0.4 road output is invariant to capture scheduling jitter",()=>
        {
            var a=new RoadSignalEngine();var b=new RoadSignalEngine();var response=new RoadResponse();
            for(int n=0;n<100;n++)
            {
                var f=Wheel(Frame(n,n*.01),0,w=>w with{SuspDeflectionM=(float)(.001*Math.Sin(n*.8))});
                a.Update(f,response);b.Update(f with{CapturedAtSec=f.CapturedAtSec+(n%2)*.004},response);
                Assert(Math.Abs(a.Wheels[0].Texture-b.Wheels[0].Texture)<1e-12);
            }
        });
        check("v0.4 road clocks reset on duplicate source time and car switch",()=>
        {
            var e=new RoadSignalEngine();var c=new RoadResponse();e.Update(Frame(0,0),c);
            e.Update(Wheel(Frame(1,.01),0,w=>w with{SurfaceType=5}),c);Assert(e.Wheels[0].Kerb>0);
            e.Update(Frame(2,.02) with{SourceTimeSec=.01},c);Assert(e.Wheels.All(w=>w.Combined==0));
            e.Update(Wheel(Frame(3,.03),0,w=>w with{SurfaceType=5}),c);Assert(e.Wheels[0].Kerb>0);
            e.Update(Frame(4,.04) with{CarId="other"},c);Assert(e.Wheels.All(w=>w.Combined==0));
        });
        check("v0.4 tagged kerb keeps radius learning frozen",()=>
        {
            var signals=new SignalEngine();var profile=new Profile();
            for(int n=0;n<200;n++)
            {
                var f=Wheel(Frame(n,n*.01) with{Throttle01=.3f},0,w=>w with{SurfaceType=5});signals.Update(f,profile);
            }
            Assert(signals.Wheels.All(w=>w.RadiusTime==0));
        });
        check("v0.4 disabled road and kerb remove their grip and channel requests",()=>
        {
            var loop=new FeedbackLoop();var profile=RoadOnly() with{Road=new(false,55),Kerb=new(true,0)};
            loop.Tick(Frame(0,0),0,profile);
            var f=Wheel(Frame(1,.01),0,w=>w with{SurfaceType=5,SuspDeflectionM=.04f});var output=loop.Tick(f,.01,profile);
            Assert(output.LeftActuator==0&&output.RightActuator==0&&loop.Channels.Road==0&&loop.Channels.Kerb==0);
        });
        check("v0.4 engine layer numerically identical to v0.3 formula",()=>
        {
            var profile=new Profile();var signals=new SignalEngine();
            for(int n=0;n<1000;n++)
            {
                double t=n*.004;var f=Frame(n,t) with{Rpm=n*8};var layers=GripEngine.Render(signals,f,t,profile);
                double expected=C(f.Rpm/f.MaxRpm)*profile.Engine.Gain*.25*Pulse(t,22,.25,1.3);
                Assert(layers.EngineR==expected&&layers.EngineL==expected*.8);
            }
        });
        check("v0.4 engine-only mixed bytes preserved",()=>
        {
            var p=new Profile();var mixer=new FrameMixer();var signals=new SignalEngine();
            for(int n=0;n<250;n++)
            {
                double t=n*.004;var f=Frame(n,t);var layers=GripEngine.Render(signals,f,t,p);
                var output=mixer.Mix((TriggerEffect.Off,TriggerEffect.Off),layers,p);
                double engine=C(f.Rpm/f.MaxRpm)*p.Engine.Gain*.25*Pulse(t,22,.25,1.3);
                Assert(output.LeftActuator==Byte(Soft(0,engine*.8))&&output.RightActuator==Byte(Soft(0,engine)));
            }
        });
        check("v0.4 legacy profile JSON keeps L2 gain and receives new defaults",()=>
        {
            var p=JsonSerializer.Deserialize<Profile>("{\"BrakeBase\":{\"Enabled\":true,\"Strength\":67},\"ThrottleBase\":{\"Enabled\":true,\"Strength\":50}}",ProfileStore.Json)!;
            p.Validate();Assert(p.BrakeBase.Strength==67&&p.ThrottleMode==ThrottleFeedbackMode.TcOnly&&p.Kerb.Enabled);
            Assert(new TriggerEngine().Render(new(),Frame(0,0),0,p).Right==TriggerEffect.Off);
            string encoded=JsonSerializer.Serialize(p,ProfileStore.Json);Assert(JsonSerializer.Deserialize<Profile>(encoded,ProfileStore.Json)!.ThrottleMode==ThrottleFeedbackMode.TcOnly);
        });
        check("v0.4 old wheel recordings deserialize with unknown optional surface",()=>
        {
            const string json="{\"OmegaRadPerSec\":60,\"SuspDeflectionM\":0,\"SuspForceN\":3000,\"TireLoadN\":3000,\"WheelYLocationM\":0,\"TireVerticalDeflectionM\":0,\"GroundLongitudinalMps\":20,\"GroundLateralMps\":0,\"TireDirty\":null,\"Valid\":true}";
            var wheel=JsonSerializer.Deserialize<WheelFrame>(json,ProfileStore.Json)!;
            Assert(wheel.SurfaceType is null&&!wheel.IsKerb);
        });
        check("v0.4 road and theme parameters reject invalid configurations",()=>
        {
            foreach(var p in new Profile[]{new(){RoadSensitivity=0},new(){ThrottleMode=(ThrottleFeedbackMode)42},new(){UiTheme="other"},new(){Kerb=new(true,101)},new(){DefaultParameters=new(){RoadResponse=new(){SuspensionNoiseMps=double.NaN}}}})
            {
                bool rejected=false;try{p.Validate();}catch(InvalidDataException){rejected=true;}Assert(rejected);
            }
        });
        check("v0.4 pause stale disconnect release TC and both grip channels",()=>
        {
            var loop=new FeedbackLoop();var profile=new Profile();var f=Frame(0,0,tc:true);
            Assert(loop.Tick(f,0,profile).RightTrigger.Mode==0x21);
            Assert(loop.Tick(f with{Paused=true},.001,profile)==FeedbackFrame.Off&&loop.Channels==default);
            Assert(loop.Tick(f with{Connected=false},.001,profile)==FeedbackFrame.Off);
            Assert(loop.Tick(f,.251,profile)==FeedbackFrame.Off);
        });
    }
}
