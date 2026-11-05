using System.Collections.Immutable;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Physics;
using LMUDualSenseHaptics.Runtime;

// Scenario designs adapted from ACC-DualSense-Feedback tests/GripLossFeedbackChecks.cs,
// commit 0c84c1901842fa1b864a3445226ddd64f991ffad (MIT, Adudumax).
// Inputs are synthetic LMU frames; they are not recorded ACC/LMU driving data.
internal static class AccReferenceRegressionTests
{
    private const double Step=.004;
    private const float Speed=100f/3.6f;
    private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static TelemetryFrame Frame(int n,double front=.04,double rearLeft=.04,double rearRight=.04,
        double body=0,double yaw=.25,double steering=.2,double? source=null)
    {
        var wheels=Enumerable.Range(0,4).Select(i=>new WheelFrame(-Speed/.35f,0,3000,3000,0,.01f,-Speed,
            (float)(Math.Tan(i<2?front:i==2?rearLeft:rearRight)*Speed),null,true)).ToImmutableArray();
        double t=n*Step;
        return new(n,t,0,true,true,false,"ref-car","Hypercar",Speed,(float)(body*Speed),0,-Speed,
            0,0,0,0,0,(float)steering,4,5000,8000,false,false,false,false,wheels,source??t,14200,
            VehicleModel:"Reference",LocalYawRate:(float)yaw);
    }
    private static bool Active(FeedbackLoop loop)=>loop.Signals.SurfaceDynamics.RearDrag.Active||loop.Signals.SurfaceDynamics.RearDrag.Strength>0;
    private static VoiceCoilCommand Command(FeedbackLoop loop,TelemetryFrame f,Profile p)=>
        VoiceCoilCommand.Create(loop.Signals.RoadSignals,loop.Layers,f,f.CapturedAtSec,p,loop.Mixer.ContinuousGain,loop.Mixer.EngineGain);
    public static void Run(Action<string,Action> check)
    {
        check("ACC reference: normal, loaded corner and front understeer do not start rear drag",()=>
        {
            foreach(int mode in new[]{0,1,2})
            {
                var loop=new FeedbackLoop();var noYaw=new FeedbackLoop();var p=new Profile();
                for(int n=0;n<750;n++)
                {
                    var f=mode switch {1=>Frame(n,.13,.09,.09,.06),2=>Frame(n,.30,.06,.06,.05),_=>Frame(n)};
                    f=f with{Throttle01=.5f};var a=loop.Tick(f,f.CapturedAtSec,p);var b=noYaw.Tick(f with{LocalYawRate=null},f.CapturedAtSec,p);
                    Need(!Active(loop),$"mode {mode}, frame {n}: false rear event");
                    Need(a.LeftTrigger==b.LeftTrigger&&a.RightTrigger==b.RightTrigger,$"mode {mode}, frame {n}: rear state altered triggers");
                }
            }
        });
        check("ACC reference: first trustworthy wheel-angle and available-ratio samples prime separate baselines",()=>
        {
            var f=Frame(1,.01,.01,.01,0,.02,.01);var surface=new AccSurfaceDynamics();
            var wheels=Enumerable.Range(0,4).Select(_=>new WheelState()).ToArray();
            surface.Update(f,wheels,.01,1,100,false); // setup: wheel angles are not yet calculated
            for(int i=0;i<4;i++)wheels[i].Alpha=.19;
            surface.Update(f,wheels,.01,1,100,false);
            Need(surface.Slip.All(x=>x==0),$"first trustworthy angle was treated as excessive: {string.Join(",",surface.Slip)}");
            surface.Reset();foreach(var w in wheels){w.Alpha=.01;w.Kappa=null;}
            surface.Update(f,wheels,.01,1,100,false);surface.Update(f,wheels,.01,1,100,false);
            foreach(var w in wheels)w.Kappa=.14;
            surface.Update(f,wheels,.01,1,100,false);
            Need(surface.Slip.All(x=>x==0),$"first available ratio was treated as excessive: {string.Join(",",surface.Slip)}");
        });
        check("ACC reference: each single rear wheel spike with normal body motion stays quiet",()=>
        {
            foreach(bool right in new[]{false,true})
            {
                var loop=new FeedbackLoop();var p=new Profile();
                for(int n=0;n<450;n++)
                {
                    var f=Frame(n,.05,right?.04:.40,right?.40:.04,.06);loop.Tick(f,f.CapturedAtSec,p);
                    Need(!Active(loop),$"{(right?"right":"left")} wheel spike confirmed at {n}");
                    Need(Command(loop,f,p).Left.RearDrag==0,"one-wheel spike reached PCM drag voice");
                }
            }
        });
        check("ACC reference: unilateral kerb and grass do not create false rear drag or opposite-side PCM",()=>
        {
            foreach(bool right in new[]{false,true})
            foreach(bool grass in new[]{false,true})
            {
                var loop=new FeedbackLoop();var p=new Profile{SpatialSeparation=100,Engine=new(false,0),Impact=new(false,0),Shift=new(false,0),Abs=new(false,0)};
                for(int n=0;n<500;n++)
                {
                    var f=Frame(n,.05,right?.04:.40,right?.40:.04,.06);
                    int wheel=right?3:2;
                    f=f with{Wheels=f.Wheels.Select((w,i)=>i==wheel?w with{SurfaceType=(byte)(grass?2:5),SuspDeflectionM=(float)(.002*Math.Sin(2*Math.PI*15*n*Step))}:w).ToImmutableArray()};
                    loop.Tick(f,f.CapturedAtSec,p);Need(!Active(loop),$"{(right?"right":"left")} {(grass?"grass":"kerb")} started rear event");
                    if(n>=30)
                    {
                        var c=Command(loop,f,p);var same=right?c.Right:c.Left;var other=right?c.Left:c.Right;
                        double signal=grass?same.Road+same.Bump+same.RearSlip:same.Kerb+same.Road+same.Bump+same.RearSlip;
                        double opposite=other.Kerb+other.Road+other.Bump+other.RearSlip;
                        Need(signal>0&&opposite<signal*.08,$"opposite side leaked {opposite:F4} vs {signal:F4}");
                    }
                }
            }
        });
        check("ACC reference: pure TC and learned longitudinal wheelspin cannot create lateral rear drag",()=>
        {
            foreach(bool tc in new[]{false,true})
            {
                var loop=new FeedbackLoop();var noYaw=new FeedbackLoop();var p=new Profile();
                for(int n=0;n<320;n++)
                {
                    var f=Frame(n,.01,.01,.01,0,.02,.01);loop.Tick(f,f.CapturedAtSec,p);noYaw.Tick(f with{LocalYawRate=null},f.CapturedAtSec,p);
                }
                Need(loop.Signals.Wheels[2].Kappa.HasValue,"radius did not become valid");
                for(int n=320;n<500;n++)
                {
                    var f=Frame(n,.01,.01,.01,0,.02,.01) with{Throttle01=1,TcIntervening=tc};
                    f=f with{Wheels=f.Wheels.Select((w,i)=>i>=2?w with{OmegaRadPerSec=w.OmegaRadPerSec*1.8f}:w).ToImmutableArray()};
                    var a=loop.Tick(f,f.CapturedAtSec,p);var b=noYaw.Tick(f with{LocalYawRate=null},f.CapturedAtSec,p);
                    Need(loop.Signals.Wheels[2].Kappa>.3,"rear longitudinal slip not established");
                    Need(!Active(loop),$"pure {(tc?"TC":"wheelspin")} started lateral drag");
                    Need(a.LeftTrigger==b.LeftTrigger&&a.RightTrigger==b.RightTrigger,"drag logic changed trigger output");
                }
            }
        });
        check("ACC reference: paired rear slip or unilateral slip with body motion opens the rear event",()=>
        {
            foreach(int mode in new[]{0,1,2})
            {
                var loop=new FeedbackLoop();var p=new Profile();TelemetryFrame? last=null;
                for(int n=0;n<200;n++)
                {
                    last=mode switch {0=>Frame(n,.05,.40,.40,0),1=>Frame(n,.05,.04,.40,.30),_=>Frame(n,.05,.40,.04,.30)};
                    loop.Tick(last,last.CapturedAtSec,p);
                }
                var d=loop.Signals.SurfaceDynamics.RearDrag;var c=Command(loop,last!,p);
                Need(d.Active&&d.Strength>.5,$"mode {mode}: supported slide not confirmed ({d.Strength:F3})");
                Need(c.Left.RearDrag>c.Right.RearDrag&&c.Right.RearDrag>0,$"mode {mode}: rear PCM signature missing");
            }
        });
        check("ACC reference: braking, full throttle and real TC do not gate lateral rear detection",()=>
        {
            foreach(int mode in new[]{0,1,2,3})
            {
                var loop=new FeedbackLoop();var noYaw=new FeedbackLoop();var p=new Profile();TelemetryFrame? last=null;
                for(int n=0;n<200;n++)
                {
                    var f=Frame(n,.08,.35,.35,.32) with{Throttle01=mode==1?1:0,Brake01=mode==2?1:0,TcIntervening=mode==3};last=f;
                    var a=loop.Tick(f,f.CapturedAtSec,p);var b=noYaw.Tick(f with{LocalYawRate=null},f.CapturedAtSec,p);
                    Need(a.LeftTrigger==b.LeftTrigger&&a.RightTrigger==b.RightTrigger,$"mode {mode}, frame {n}: trigger output changed");
                }
                var c=Command(loop,last!,p);Need(loop.Signals.SurfaceDynamics.RearDrag.Strength>.9&&c.Left.RearDrag>0&&c.Right.RearDrag>0,$"mode {mode}: rear cue gated");
            }
        });
        check("ACC reference: yaw reversal and countersteer preserve a bounded recovery voice",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile();
            for(int n=0;n<100;n++){var f=Frame(n,.07,.35,.35,.32);loop.Tick(f,f.CapturedAtSec,p);}
            bool recovery=false;
            for(int n=100;n<135;n++)
            {
                var f=Frame(n,.26,.14,.14,.216,-.2,-.4);loop.Tick(f,f.CapturedAtSec,p);
                var d=loop.Signals.SurfaceDynamics.RearDrag;recovery|=d.Recovery;
                Need(d.Strength>.1,$"recovery cut at frame {n}: {d.Strength:F3}");
                var c=Command(loop,f,p);Need(c.Left.RearDrag>c.Right.RearDrag&&c.Right.RearDrag>0,"rear voice changed side signature");
            }
            Need(recovery,"yaw reversal never entered recovery");
        });
        check("ACC reference: settled corner cannot renew drag and its release follows 100 ms",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile();
            for(int n=0;n<100;n++){var f=Frame(n,.07,.35,.35,.32);loop.Tick(f,f.CapturedAtSec,p);}
            bool measured=false;double previous=0;
            for(int n=100;n<650;n++)
            {
                var f=Frame(n,.08,.09,.09,.06);loop.Tick(f,f.CapturedAtSec,p);
                double x=loop.Signals.SurfaceDynamics.RearDrag.Strength;
                if(n>=180&&x>.0031&&previous>0){double expected=previous*Math.Exp(-Step/.100);Need(Math.Abs(x-expected)<1e-9,$"release differs at {n}");measured=true;}
                previous=x;
            }
            Need(measured&&previous==0,"rear drag failed to expire after the corner");
        });
        check("ACC reference: stationary, reverse and missing yaw immediately clear the rear event",()=>
        {
            foreach(int mode in new[]{0,1,2})
            {
                var loop=new FeedbackLoop();var p=new Profile();
                for(int n=0;n<80;n++){var f=Frame(n,.07,.35,.35,.32);loop.Tick(f,f.CapturedAtSec,p);}
                Need(Active(loop),"setup failed");var stop=Frame(80,.07,.35,.35,.32);
                stop=mode switch {0=>stop with{SpeedMps=0,LocalVzMps=0},1=>stop with{Gear=-1},_=>stop with{LocalYawRate=null}};
                loop.Tick(stop,stop.CapturedAtSec,p);Need(!Active(loop),$"mode {mode} retained rear state");
            }
        });
        check("ACC reference: duplicate mutations do not add evidence and frozen frames reach OFF",()=>
        {
            var loop=new FeedbackLoop();var p=new Profile();var initial=Frame(0);loop.Tick(initial,0,p);
            for(int n=1;n<50;n++)
            {
                var forged=Frame(n,.07,.35,.35,.32) with{Sequence=initial.Sequence};loop.Tick(forged,forged.CapturedAtSec,p);
                Need(!Active(loop),"duplicate sequence opened rear event");
            }
            for(int n=50;n<100;n++){var f=Frame(n,.07,.35,.35,.32);loop.Tick(f,f.CapturedAtSec,p);}
            var last=Frame(99,.07,.35,.35,.32);Need(Active(loop),"fresh frames did not start event");
            Need(loop.Tick(last,last.CapturedAtSec+p.TelemetryStaleMs/1000.0+.001,p)==FeedbackFrame.Off,"stale telemetry did not turn output off");
            Need(!Active(loop),"stale telemetry retained rear state");
        });
        check("ACC reference: clock reversal, packet rollback and source gaps reset then admit new frames",()=>
        {
            foreach(int mode in new[]{0,1,2})
            {
                var loop=new FeedbackLoop();var p=new Profile();
                for(int n=0;n<100;n++){var f=Frame(n,.07,.35,.35,.32);loop.Tick(f,f.CapturedAtSec,p);}
                Need(Active(loop),"setup failed");
                var jump=mode switch {0=>Frame(60,.07,.35,.35,.32),1=>Frame(100,.07,.35,.35,.32) with{Sequence=2},_=>Frame(100,.07,.35,.35,.32,source:1.0)};
                loop.Tick(jump,jump.CapturedAtSec,p);Need(!Active(loop),$"mode {mode}: discontinuity retained rear state");
                for(int i=1;i<=50;i++)
                {
                    var f=Frame(100+i,.07,.35,.35,.32) with{Sequence=jump.Sequence+i,CapturedAtSec=jump.CapturedAtSec+i*Step,SourceTimeSec=jump.SourceTimeSec+i*Step};
                    loop.Tick(f,f.CapturedAtSec,p);
                }
                Need(loop.Signals.SurfaceDynamics.RearDrag.Strength>.5,$"mode {mode}: fresh frames did not recover");
            }
        });
        check("ACC reference: nonfinite motion and yaw do not poison the next valid event",()=>
        {
            foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            foreach(bool yaw in new[]{true,false})
            {
                var loop=new FeedbackLoop();var p=new Profile();var invalid=Frame(0) with{LocalYawRate=yaw?bad:.2f,LocalVxMps=yaw?0:bad};
                loop.Tick(invalid,0,p);Need(!Active(loop),"invalid sample started an event");
                for(int n=1;n<150;n++){var f=Frame(n,.07,.35,.35,.32);loop.Tick(f,f.CapturedAtSec,p);}
                Need(loop.Signals.SurfaceDynamics.RearDrag.Strength>.5,"valid samples failed to recover");
            }
        });
        check("ACC reference: disabled grip yields no drag PCM while leaving both triggers unchanged",()=>
        {
            var p=new Profile{GripSlip=new(false,0)};var loop=new FeedbackLoop();var noYaw=new FeedbackLoop();
            for(int n=0;n<300;n++)
            {
                var f=Frame(n,.09,.35,.35,.32) with{Throttle01=n<150?1:0,Brake01=n>=150?.7f:0,TcIntervening=n is >=50 and <120};
                var a=loop.Tick(f,f.CapturedAtSec,p);var b=noYaw.Tick(f with{LocalYawRate=null},f.CapturedAtSec,p);var c=Command(loop,f,p);
                Need(a.LeftTrigger==b.LeftTrigger&&a.RightTrigger==b.RightTrigger,$"triggers changed at {n}");
                Need(c.Left.RearDrag==0&&c.Right.RearDrag==0&&c.Left.FrontSlip==0&&c.Right.RearSlip==0,"disabled grip leaked into PCM");
            }
        });
        check("ACC reference: rear event update avoids per-frame managed allocations",()=>
        {
            var state=new RearDragState();var f=Frame(0,.07,.35,.35,.32);var wheels=new[]{new WheelState{Alpha=.07},new WheelState{Alpha=.07},new WheelState{Alpha=.35},new WheelState{Alpha=.35}};
            for(int i=0;i<10000;i++)state.Update(f,wheels,Step);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<100000;i++)state.Update(f,wheels,Step);
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Need(allocated==0,$"rear state allocated {allocated} bytes / 100000 updates");
        });
    }
}
