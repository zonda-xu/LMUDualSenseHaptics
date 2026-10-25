using System.Collections.Immutable;
using System.Text.Json;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Config;
using LMUDualSenseHaptics.Runtime;
using LMUDualSenseHaptics.Hardware;

internal static class VehicleTriggerTests
{
    private static void Need(bool value,string message="assertion") {if(!value)throw new Exception(message);}
    private static void Invalid(Action action) {try{action();}catch(InvalidDataException){return;}throw new Exception("expected invalid configuration");}
    private static TelemetryFrame F(int n,string model="Car A",string category="Hyper") =>
        new(n,n*.02,0,true,true,false,"slot-1",category,40,0,0,-40,0,0,0,.5f,.7f,0,4,6000,8000,false,true,false,true,
            Enumerable.Range(0,4).Select(_=>new WheelFrame(-40/.35f,0,3000,3000,0,.01f,-40,0,null,true,LongitudinalPatchMps:0)).ToImmutableArray(),n*.02,VehicleModel:model);
    private static readonly TriggerSettings Quiet=new(){BrakeBase=new(false,0),Abs=new(false,0),Lock=new(false,0),Tc=new(false,0),Shift=new(false,0)};
    public static void Run(Action<string,Action> check)
    {
        check("1.3.9 repeated A-B-unknown-A switches both triggers on the next tick",()=>
        {
            var root=TriggerProfiles.SaveVehicle(new(),"Car A","Hyper",Quiet);
            root=TriggerProfiles.SaveVehicle(root,"Car B","GT3",new(){BrakeBase=new(true,100),Tc=new(true,100)});
            var loop=new FeedbackLoop();int n=0;
            foreach(var model in new[]{"Car A","Car B","Unknown","Car A","Car B","Car A"})
            {
                var f=F(++n,model,model=="Car B"?"GT3":"Hyper");var o=loop.Tick(f,f.CapturedAtSec,root);
                Need(loop.AdvancedFeedbackEnabled,loop.Reason);
                Need((o.LeftTrigger==TriggerEffect.Off)==(model=="Car A"),"left switch "+model);
                Need((o.RightTrigger==TriggerEffect.Off)==(model=="Car A"),"right switch "+model);
                Need(loop.TriggerProfileSource.Contains(model=="Unknown"?"通用":model));
            }
        });
        check("1.3.9 identity ignores casing whitespace session slot and game version",()=>
        {
            var root=TriggerProfiles.SaveVehicle(new()," Car A "," Hyper ",Quiet);
            Need(TriggerProfiles.Find(root,F(1,"car a"," HYPER ") with{CarId="another-slot",GameVersion=99999}) is not null);
            Need(TriggerProfiles.Find(root,F(2,"Car A","GT3")) is null);
            Need(TriggerProfiles.Find(root,F(3,"Car AB")) is null);
            Need(TriggerProfiles.Find(root,F(4,"")) is null);
            Need(TriggerProfiles.Find(root,F(5) with{Connected=false}) is null);
        });
        check("1.3.9 common edits and other-car edits cannot overwrite a dedicated car",()=>
        {
            var root=TriggerProfiles.SaveVehicle(new(),"Car A","Hyper",Quiet);
            root=root with{TriggerDefaults=new(){Tc=new(true,10)}};
            var a=TriggerProfiles.Resolve(root,F(1));
            Need(a.Tc.Gain==0&&TriggerProfiles.Resolve(root,F(2,"New")).Tc.Gain==.1);
            root=TriggerProfiles.SaveVehicle(root,"Car B","Hyper",new(){Tc=new(true,100)});
            Need(TriggerProfiles.Resolve(root,F(3)).Tc.Gain==0);
            root=TriggerProfiles.SaveVehicle(root,"car a","hyper",Quiet with{BrakeBase=new(true,20)});
            Need(root.VehicleTriggers.Length==2&&TriggerProfiles.Resolve(root,F(4)).BrakeBase.Gain==.2);
        });
        check("1.3.9 vehicle trigger settings survive actual ProfileStore disk roundtrip",()=>
        {
            string path=Path.Combine(Path.GetTempPath(),"lmu-vehicle-"+Guid.NewGuid()+".json");
            try
            {
                var root=TriggerProfiles.SaveVehicle(new(){TriggerDefaults=new(){Tc=new(true,42)}},"中文车 A","Hyper",Quiet with{BrakeResponse=new(){TravelDeadzone=.1,TravelEnd=.9,InputExponent=2}});
                ProfileStore.Save(root,path);var loaded=ProfileStore.Load(path);var a=TriggerProfiles.Resolve(loaded,F(1,"中文车 A"));
                Need(a.Tc.Gain==0&&a.DefaultParameters.BrakeResponse.TravelDeadzone==.1&&a.DefaultParameters.BrakeResponse.InputExponent==2);
                Need(TriggerProfiles.Resolve(loaded,F(2,"Other")).Tc.Strength==42);
            }
            finally{File.Delete(path);}
        });
        check("1.3.9 legacy config retains v1.3 inputs and outputs when no overrides exist",()=>
        {
            var old=JsonSerializer.Deserialize<Profile>("{\"BrakeBase\":{\"Enabled\":true,\"Strength\":23},\"Tc\":{\"Enabled\":true,\"Strength\":56}}",ProfileStore.Json)!;
            Need(old.VehicleTriggers.Length==0&&old.TriggerDefaults is null);
            Need(ReferenceEquals(TriggerProfiles.Resolve(old,F(1)),old));
            var baseline=new FeedbackLoop();var explicitSnapshot=new FeedbackLoop();
            var copied=TriggerProfiles.SaveVehicle(old,"Car A","Hyper",TriggerSettings.From(old));
            for(int n=1;n<80;n++)
            {
                var f=F(n) with{Brake01=n%2==0?.8f:.3f,TcIntervening=n%3==0};
                Need(baseline.Tick(f,f.CapturedAtSec,old)==explicitSnapshot.Tick(f,f.CapturedAtSec,copied),"snapshot must preserve original feedback");
            }
        });
        check("1.3.9 per-car trigger gains do not change grip/audio settings or grip output",()=>
        {
            var root=new Profile(){Shift=new(true,83),Abs=new(true,71)};
            var saved=TriggerProfiles.SaveVehicle(root,"Car A","Hyper",Quiet);
            var resolved=TriggerProfiles.Resolve(saved,F(1));
            Need(resolved.AudioHaptics==root.AudioHaptics&&resolved.Road==root.Road&&resolved.SpatialSeparation==root.SpatialSeparation);
            var a=new FeedbackLoop();var b=new FeedbackLoop();
            for(int n=1;n<60;n++)
            {
                var f=F(n) with{Gear=n>10?5:4,AbsIntervening=n>30};
                var oa=a.Tick(f,f.CapturedAtSec,root);var ob=b.Tick(f,f.CapturedAtSec,saved);
                Need(a.Layers==b.Layers&&oa.LeftActuator==ob.LeftActuator&&oa.RightActuator==ob.RightActuator,"grip gains leaked");
            }
        });
        check("1.3.9 invalid duplicate or incomplete vehicle configurations are rejected",()=>
        {
            Invalid(()=>new Profile(){VehicleTriggers=[new("A","Hyper",Quiet),new(" a ","HYPER",Quiet)]}.Validate());
            Invalid(()=>new Profile(){VehicleTriggers=[new("","Hyper",Quiet)]}.Validate());
            Invalid(()=>new Profile(){VehicleTriggers=[new("A","Hyper",Quiet with{Tc=new(true,101)})]}.Validate());
            Invalid(()=>new Profile(){TriggerDefaults=Quiet with{BrakeResponse=new(){TravelDeadzone=.9}}}.Validate());
            Invalid(()=>new Profile(){VehicleTriggers=[new("A","Hyper",null!)]}.Validate());
        });
        check("1.3.9 pause disconnect stale and config deletion cannot retain prior-car output",()=>
        {
            var root=TriggerProfiles.SaveVehicle(new(),"Car A","Hyper",Quiet);var loop=new FeedbackLoop();
            var f=F(1);Need(loop.Tick(f,f.CapturedAtSec,root).RightTrigger==TriggerEffect.Off);
            Need(loop.Tick(f with{Paused=true},f.CapturedAtSec,root)==FeedbackFrame.Off);
            f=F(2,"Other");Need(loop.Tick(f,f.CapturedAtSec,root).RightTrigger!=TriggerEffect.Off);
            Need(loop.Tick(f,f.CapturedAtSec+1,root)==FeedbackFrame.Off);
            Need(loop.Tick(f with{Connected=false},f.CapturedAtSec,root)==FeedbackFrame.Off);
            f=F(3);Need(loop.Tick(f,f.CapturedAtSec,root).RightTrigger==TriggerEffect.Off);
            var deleted=root with{VehicleTriggers=[]};Need(loop.Tick(f,f.CapturedAtSec,deleted).RightTrigger!=TriggerEffect.Off);
        });
    }
}
