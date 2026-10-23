using System.Collections.Immutable;
using System.Text.Json;
using LMUDualSenseHaptics.Config;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Diagnostics;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Physics;
using LMUDualSenseHaptics.Runtime;

internal static class Revision052Tests
{
    private static void Require(bool value,string message="assertion") { if(!value)throw new Exception(message); }
    private static TelemetryFrame Frame(int n) => new(n,n*.01,0,true,true,false,"test","Hypercar",20,0,0,-20,
        0,0,0,0,.4f,0,3,5000,8000,false,false,false,true,
        Enumerable.Range(0,4).Select(_=>new WheelFrame(60,0,3000,0,0,.01f,20,0,null,true)).ToImmutableArray(),n*.01,14200,VehicleModel:"test");
    public static void Run(Action<string,Action> check)
    {
        check("v0.5.2 zero tyre load with compression is grounded",()=>Require(WheelContact.IsGrounded(Frame(0).Wheels[0],100)));
        check("v0.5.2 suspension force alone does not establish contact",()=>Require(!WheelContact.IsGrounded(Frame(0).Wheels[0] with{TireVerticalDeflectionM=0,SuspForceN=9000},100)));
        check("v0.5.2 measured positive tyre load establishes contact",()=>Require(WheelContact.IsGrounded(Frame(0).Wheels[0] with{TireVerticalDeflectionM=0,TireLoadN=3000},100)));
        check("v0.5.2 invalid or negative contact inputs rejected",()=>
        {
            var w=Frame(0).Wheels[0];Require(!WheelContact.IsGrounded(w with{Valid=false},100));
            Require(!WheelContact.IsGrounded(w with{TireVerticalDeflectionM=float.NaN},100));Require(!WheelContact.IsGrounded(w with{TireLoadN=-1},100));
        });
        check("v0.5.2 zero-load normal rolling learns radius and longitudinal slip",()=>
        {
            var e=new SignalEngine();for(int n=0;n<250;n++)e.Update(Frame(n),new());
            Require(e.Wheels.All(w=>w.RadiusTime>1&&w.Kappa is not null&&Math.Abs(w.Radius-1.0/3)<1e-6));
            var f=Frame(250) with{Brake01=1,Wheels=Frame(250).Wheels.Select(w=>w with{OmegaRadPerSec=25}).ToImmutableArray()};
            for(int n=250;n<280;n++)e.Update(f with{Sequence=n,CapturedAtSec=n*.01,SourceTimeSec=n*.01},new());
            Require(e.Lock>.1&&e.Wheels.All(w=>w.Kappa<-.3));
        });
        check("v0.5.2 airborne clears slip and kerb despite suspension force",()=>
        {
            var e=new SignalEngine();for(int n=0;n<250;n++)e.Update(Frame(n),new());
            var f=Frame(250) with{Wheels=Frame(250).Wheels.Select(w=>w with{TireVerticalDeflectionM=0,SurfaceType=5,GroundLateralMps=8}).ToImmutableArray()};
            e.Update(f,new());Require(e.Wheels.All(w=>w.Slip==0&&w.Kappa is null));Require(e.RoadSignals.Wheels.All(w=>w.Combined==0));
        });
        check("v0.5.2 zero-load suspension force changes produce road texture",()=>
        {
            var e=new RoadSignalEngine();e.Update(Frame(0),new());
            e.Update(Frame(1) with{Wheels=Frame(1).Wheels.Select(w=>w with{SuspForceN=6000}).ToImmutableArray()},new());
            Require(e.Wheels.All(w=>w.Texture>0&&w.Contact));
        });
        check("v0.5.2 recorded right kerb reaches actual PCM channel four",()=>
        {
            var frames=JsonSerializer.Deserialize<TelemetryFrame[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures/zero-load-recorded.frames.json")),ProfileStore.Json)!;
            var first=frames.First(f=>f.Wheels[1].IsKerb&&!f.Wheels[0].IsKerb&&!f.Wheels[2].IsKerb);
            var loop=new FeedbackLoop();var p=new Profile{Engine=new(false,0),GripSlip=new(false,0),Impact=new(false,0),Abs=new(false,0),Shift=new(false,0),Redline=new(false,0),Road=new(false,0)};
            loop.Tick(first,first.CapturedAtSec,p);
            var f=first with{Sequence=first.Sequence+1,CapturedAtSec=first.CapturedAtSec+.01,SourceTimeSec=first.SourceTimeSec+.01};
            loop.Tick(f,f.CapturedAtSec,p);Require(loop.Channels.Kerb>0&&loop.Signals.RoadSignals.Wheels[1].Contact,$"kerb={loop.Channels.Kerb} contact={loop.Signals.RoadSignals.Wheels[1].Contact} state={loop.Reason}");
            var command=VoiceCoilCommand.Create(loop.Signals.RoadSignals,loop.Layers,f,f.CapturedAtSec,p,loop.Mixer.ContinuousGain,loop.Mixer.EngineGain);
            var synth=new VoiceCoilSynthesizer(48000);var pcm=new float[1920*4];synth.Render(command,pcm,f.CapturedAtSec);
            Require(synth.Levels.RightRms>0&&synth.Levels.RightRms>synth.Levels.LeftRms*3,$"PCM right={synth.Levels.RightRms} left={synth.Levels.LeftRms} command={command}");
            Require(Enumerable.Range(0,1920).All(n=>pcm[n*4]==0&&pcm[n*4+1]==0));
            loop.Tick(f with{Paused=true},f.CapturedAtSec+.004,p);Require(loop.Channels==default);
        });
        check("v0.5.2 logging sampling caps rates before allocation",()=>
        {
            var s=new LogSampling();int t=0,o=0,d=0;
            for(int n=0;n<2500;n++){double at=n*.004;if(s.Telemetry(at))t++;if(s.Output(at))o++;if(s.Status(at))d++;}
            Require(t==500&&o==100&&d==20);
            Require(s.Error("waiting",0)&&!s.Error("waiting",1)&&s.Error("different",1)&&s.Error("waiting",1));
        });
        check("v0.5.2 recorder bounds UTF8 bytes and emits valid stop marker",()=>
        {
            string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".jsonl");
            try
            {
                var r=new TelemetryRecorder(path,1024);for(int n=0;n<100;n++)r.Record(new{Kind="sample",Text=new string('路',100)});
                r.DisposeAsync().AsTask().GetAwaiter().GetResult();Require(new FileInfo(path).Length<=1024&&r.Stopped);
                var lines=File.ReadAllLines(path);foreach(var line in lines)using(JsonDocument.Parse(line)){}
                Require(JsonDocument.Parse(lines[^1]).RootElement.GetProperty("Reason").GetString()=="size-limit");
            }
            finally{File.Delete(path);}
        });
    }

    public static void ReplayDebug(string path,string destination)
    {
        var loop=new FeedbackLoop();var p=new Profile();var synth=new VoiceCoilSynthesizer(48000);var pcm=new float[192*4];
        int frames=0,driving=0,road=0,kerb=0,tagged=0,contact=0,radius=0;double maxLeft=0,maxRight=0;long last=-1;
        foreach(var line in File.ReadLines(path))
        {
            using var doc=JsonDocument.Parse(line);
            if(!doc.RootElement.TryGetProperty("Status",out var s)||!s.TryGetProperty("Frame",out var raw)||raw.ValueKind==JsonValueKind.Null)continue;
            var f=raw.Deserialize<TelemetryFrame>(ProfileStore.Json)!;if(f.Sequence==last)continue;last=f.Sequence;frames++;
            loop.Tick(f,f.CapturedAtSec,p);if(!loop.AdvancedFeedbackEnabled)continue;driving++;
            if(loop.Channels.Road>0)road++;if(loop.Channels.Kerb>0)kerb++;
            if(f.Wheels.Any(w=>w.IsKerb))tagged++;contact+=loop.Signals.RoadSignals.Wheels.Count(w=>w.Contact);
            if(loop.Signals.Wheels.Any(w=>w.RadiusTime>=p.DefaultParameters.RadiusTrustedSeconds))radius++;
            var c=VoiceCoilCommand.Create(loop.Signals.RoadSignals,loop.Layers,f,f.CapturedAtSec,p,loop.Mixer.ContinuousGain,loop.Mixer.EngineGain);
            synth.Render(c,pcm,f.CapturedAtSec);maxLeft=Math.Max(maxLeft,synth.Levels.LeftRms);maxRight=Math.Max(maxRight,synth.Levels.RightRms);
            Require(pcm.All(float.IsFinite)&&pcm.All(v=>Math.Abs(v)<=.850001));
            Require(Enumerable.Range(0,192).All(n=>pcm[4*n]==0&&pcm[4*n+1]==0));
        }
        var result=new{Source=Path.GetFileName(path),Profile="v0.5.2 defaults (original profile absent from debug log)",frames,driving,road,kerb,tagged,contact,radius,maxLeft,maxRight,Scope="parsed status samples; no original Raw snapshots or physical device"};
        File.WriteAllText(destination,JsonSerializer.Serialize(result,ProfileStore.Json));Console.WriteLine(JsonSerializer.Serialize(result));
        if(driving>0)Require(road>0&&kerb>0&&maxRight>0);
    }
}
