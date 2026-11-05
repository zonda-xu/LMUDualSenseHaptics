using System.Buffers.Binary;
using System.Collections.Immutable;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Physics;
using LMUDualSenseHaptics.Runtime;
using LMUDualSenseHaptics.Telemetry;

internal static class AccSurface151Tests
{
    private static void Need(bool value,string message="assertion"){if(!value)throw new Exception(message);}
    private static TelemetryFrame Frame(int n,double rear=0,double body=0,double yaw=.2,double steer=.1,bool single=false)
    {
        const float speed=40;
        var wheels=Enumerable.Range(0,4).Select(i=>new WheelFrame(-speed/.35f,0,3000,3000,0,.01f,
            speed,(i==2||i==3&&!single)?(float)(Math.Tan(rear)*speed):0,null,true)).ToImmutableArray();
        return new(n,n*.01,0,true,true,false,"one","Hypercar",speed,(float)(body*speed),0,-speed,0,0,0,0,0,(float)steer,4,6000,8000,false,false,false,false,wheels,n*.01,14200,VehicleModel:"Test",LocalYawRate:(float)yaw);
    }
    public static void Run(Action<string,Action> check)
    {
        check("1.5.1 local yaw uses the verified LMU Y-component offset",()=>
        {
            var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","lmu14200-options0.snapshot.bin"));
            int v=LmuLayout.TelemetryOffset+4+bytes[LmuLayout.TelemetryOffset+1]*LmuLayout.VehicleSize;
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(v+312,8),BitConverter.DoubleToInt64Bits(.625));
            var parser=new LmuLayout();var f=parser.Parse(bytes,1)!;Need(f.LocalYawRate==.625f);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(v+312,8),BitConverter.DoubleToInt64Bits(double.NaN));
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(v+12,8),BitConverter.DoubleToInt64Bits(f.SourceTimeSec+.02));
            Need(parser.Parse(bytes,1.02)!.LocalYawRate is null);
        });
        check("1.5.1 road shock window localizes one-wheel impacts and uses independent audio",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile{GripSlip=new(false,0),Engine=new(false,0),Impact=new(false,0),Shift=new(false,0),Abs=new(false,0)};
            var f=Frame(0);loop.Tick(f,0,p);
            f=Frame(1) with{Wheels=Frame(1).Wheels.Select((w,i)=>i==1?w with{SuspDeflectionM=.003f,TireLoadN=5000}:w).ToImmutableArray()};
            var output=loop.Tick(f,.01,p);var a=loop.Signals.SurfaceDynamics;
            Need(a.Shock[1]>0&&a.Shock[0]==0&&a.Shock[2]==0&&a.Shock[3]==0);
            Need(loop.Layers.RoadR>loop.Layers.RoadL&&output.RightActuator>output.LeftActuator);
            var cmd=VoiceCoilCommand.Create(loop.Signals.RoadSignals,loop.Layers,f,.01,p,1,1);
            Need(cmd.Right.Road>cmd.Left.Road&&cmd.Right.Kerb==0);
            var synth=new VoiceCoilSynthesizer(48000);float[] pcm=new float[960];synth.Render(cmd,pcm,.01);
            Need(synth.Levels.RightPeak>synth.Levels.LeftPeak&&pcm.Where((_,i)=>i%4<2).All(x=>x==0));
        });
        check("1.5.1 rear oversteer requires paired wheels or body support and has a recovery tail",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile();
            for(int n=0;n<6;n++){var f=Frame(n,.25,.10);loop.Tick(f,f.CapturedAtSec,p);}
            Need(loop.Signals.SurfaceDynamics.RearDrag.Active&&loop.Signals.SurfaceDynamics.RearDrag.Strength>.1,"rear gate never activated");
            var active=Frame(6,.25,.10);loop.Tick(active,.06,p);
            var cmd=VoiceCoilCommand.Create(loop.Signals.RoadSignals,loop.Layers,active,.06,p,1,1);
            Need(cmd.Left.RearDrag>0&&cmd.Right.RearDrag>0&&cmd.Left.RearDrag>cmd.Right.RearDrag,"PCM rear cue missing");
            var recover=Frame(7,.12,.05);loop.Tick(recover,.07,p);
            Need(loop.Signals.SurfaceDynamics.RearDrag.Recovery&&loop.Signals.SurfaceDynamics.RearDrag.Strength>0,"recovery stage missing");
            for(int n=8;n<65;n++){var f=Frame(n);loop.Tick(f,f.CapturedAtSec,p);}
            Need(!loop.Signals.SurfaceDynamics.RearDrag.Active&&loop.Signals.SurfaceDynamics.RearDrag.Strength<.01,$"tail={loop.Signals.SurfaceDynamics.RearDrag.Strength:F3}");
            var single=new FeedbackLoop();for(int n=0;n<12;n++){var f=Frame(n,.30,0,single:true);single.Tick(f,f.CapturedAtSec,p);}
            Need(!single.Signals.SurfaceDynamics.RearDrag.Active,"single wheel falsely confirmed");
        });
        check("1.5.1 rear warning clears on stale frames, missing yaw and car changes",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile();
            for(int n=0;n<5;n++){var f=Frame(n,.25,.10);loop.Tick(f,f.CapturedAtSec,p);}
            Need(loop.Signals.SurfaceDynamics.RearDrag.Active);
            var invalid=Frame(5,.25,.10) with{LocalYawRate=null};loop.Tick(invalid,.05,p);Need(!loop.Signals.SurfaceDynamics.RearDrag.Active);
            for(int n=6;n<12;n++){var f=Frame(n,.25,.10);loop.Tick(f,f.CapturedAtSec,p);}
            Need(loop.Signals.SurfaceDynamics.RearDrag.Active);
            Need(loop.Tick(Frame(12,.25,.10),.5,p)==FeedbackFrame.Off&&!loop.Signals.SurfaceDynamics.RearDrag.Active);
            var changed=Frame(13,.25,.10) with{CarId="two",VehicleModel="Other"};loop.Tick(changed,.13,p);Need(!loop.Signals.SurfaceDynamics.RearDrag.Active);
            for(int n=14;n<18;n++){var f=Frame(n,.25,.10) with{CarId="two",VehicleModel="Other"};loop.Tick(f,f.CapturedAtSec,p);}
            Need(loop.Signals.SurfaceDynamics.RearDrag.Active);
            var gap=Frame(18,.25,.10) with{CarId="two",VehicleModel="Other",SourceTimeSec=.60};loop.Tick(gap,.18,p);
            Need(!loop.Signals.SurfaceDynamics.RearDrag.Active,"physics-time discontinuity kept stale event");
        });
        check("1.5.1 independent front, rear and drag voices generate distinct PCM",()=>
        {
            float[] Render(VoiceCoilSide side){var synth=new VoiceCoilSynthesizer(48000);var c=new VoiceCoilCommand(true,1,side,default,75,110,45,60,.6,1,false);float[] pcm=new float[9600];synth.Render(c,pcm,0);return pcm;}
            var front=Render(new(0,0,0,0,0,0,0,24,FrontSlip:.6));
            var rear=Render(new(0,0,0,0,0,0,0,24,RearSlip:.6));
            var drag=Render(new(0,0,0,0,0,0,0,24,RearDrag:.6));
            Need(front.Any(x=>x!=0)&&rear.Any(x=>x!=0)&&drag.Any(x=>x!=0));
            Need(!front.SequenceEqual(rear)&&!front.SequenceEqual(drag)&&!rear.SequenceEqual(drag));
            Need(front.Where((_,i)=>i%4!=2).All(x=>x==0));
        });
        check("1.5.1 unchanged kerb-only PCM matches the frozen v1.5 synthesizer sample by sample",()=>
        {
            var side=new VoiceCoilSide(0,0,.65,0,0,0,0,28);
            var command=new VoiceCoilCommand(true,1,side,default,75,110,45,60,.5,1,false);
            float[] actual=new float[9600],original=new float[9600];
            new VoiceCoilSynthesizer(48000).Render(command,actual,0);
            new FrozenVoiceCoilSynthesizer(48000).Render(command,original,0);
            Need(actual.SequenceEqual(original),"kerb waveform changed");
        });
    }
}
