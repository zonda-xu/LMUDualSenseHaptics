using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using LMUDualSenseHaptics.Config;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Runtime;

internal static class Revision15Tests
{
    private static void Need(bool value,string message="assertion") {if(!value)throw new Exception(message);}
    private static void Reject(Action action) {try{action();}catch(Exception ex) when(ex is JsonException or InvalidDataException){return;}throw new Exception("invalid share accepted");}
    private sealed class Files:IDisposable
    {
        public string Directory {get;}=Path.Combine(Path.GetTempPath(),"lmu-share-"+Guid.NewGuid());
        public string Share=>Path.Combine(Directory,"分享.json");
        public string Profile=>Path.Combine(Directory,"profile.json");
        public Files()=>System.IO.Directory.CreateDirectory(Directory);
        public void Dispose()=>System.IO.Directory.Delete(Directory,true);
    }
    private static TelemetryFrame F(int n,string model="Car A",string category="Hyper",float slip=.0601f)=>
        new(n,n*.02,0,true,true,false,"slot",category,90,0,0,-90,0,0,0,.8f,.7f,0,6,6000,8000,false,true,false,true,
            Enumerable.Range(0,4).Select(_=>new WheelFrame(-90/.35f*(1-slip),0,3000,3000,0,.01f,-90,0,null,true,LongitudinalPatchMps:-90*slip)).ToImmutableArray(),n*.02,VehicleModel:model);
    private static int Amplitude(TriggerEffect e)=>1+(e.P2&7);
    public static void Run(Action<string,Action> check)
    {
        check("1.5 complete single-car share roundtrips every trigger parameter",()=>
        {
            using var files=new Files();
            var settings=new TriggerSettings {BrakeMode=BrakeFeedbackMode.NoAbs,ThrottleMode=ThrottleFeedbackMode.Legacy,
                BrakeBase=new(true,23),ThrottleBase=new(true,31),Abs=new(false,42),Lock=new(true,25),Tc=new(true,37),
                Wheelspin=new(false,18),Shift=new(true,44),Redline=new(false,29),RedlineStartRatio=.95,
                BrakeResponse=new(){TravelDeadzone=.12,TravelEnd=.92,InputExponent=1.5,CueHoldSeconds=.2}};
            var vehicle=new VehicleTriggerSettings("中文车 A","Hyper",settings);
            VehicleTriggerShare.Export(vehicle,files.Share);
            Need(VehicleTriggerShare.Import(files.Share)==vehicle);
            var doc=JsonNode.Parse(File.ReadAllText(files.Share))!.AsObject();
            Need(doc.Count==5&&doc["Format"]!.GetValue<string>()=="LMUDualSenseHaptics.VehicleTriggers");
            Need(!File.ReadAllText(files.Share).Contains("AudioHaptics")&&!File.ReadAllText(files.Share).Contains("EndpointId"));
            Need(System.IO.Directory.GetFiles(files.Directory).Length==1,"temporary file leaked");
        });
        check("1.5 share import save reload drives A-B-A automatic output switching",()=>
        {
            using var files=new Files();
            var vehicle=new VehicleTriggerSettings("Car A","Hyper",new(){Lock=new(false,0),BrakeBase=new(false,0),Tc=new(false,0)});
            VehicleTriggerShare.Export(vehicle,files.Share);
            var original=new Profile();var merged=VehicleTriggerShare.Apply(original,VehicleTriggerShare.Import(files.Share));
            ProfileStore.Save(merged,files.Profile);var loaded=ProfileStore.Load(files.Profile);var loop=new FeedbackLoop();
            for(int n=0;n<6;n++)
            {
                bool a=n%2==0;var f=F(n,a?"Car A":"Car B");var output=loop.Tick(f,f.CapturedAtSec,loaded);
                Need(loop.AdvancedFeedbackEnabled);
                Need((output.LeftTrigger==TriggerEffect.Off)==a&&(output.RightTrigger==TriggerEffect.Off)==a,"wrong car used imported settings");
            }
            Need(original.VehicleTriggers.Length==0);
        });
        check("1.5 import replaces only matching identity and preserves global and other cars",()=>
        {
            var root=TriggerProfiles.SaveVehicle(new(){AudioHaptics=new(){EndpointId="private-device"}},"Car A","Hyper",new());
            root=TriggerProfiles.SaveVehicle(root,"Car B","Hyper",new(){Tc=new(true,13)});
            var updated=VehicleTriggerShare.Apply(root,new(" car a "," HYPER ",new(){Lock=new(true,20)}));
            Need(updated.VehicleTriggers.Length==2&&TriggerProfiles.Resolve(updated,F(0)).Lock.Strength==20);
            Need(TriggerProfiles.Resolve(updated,F(0,"Car B")).Tc.Strength==13);
            Need(updated.AudioHaptics==root.AudioHaptics&&updated.DefaultParameters==root.DefaultParameters&&updated.TriggerDefaults==root.TriggerDefaults);
            Need(updated.BrakeBase==root.BrakeBase&&updated.Lock==root.Lock&&updated.Shift==root.Shift&&updated.VehiclePresets==root.VehiclePresets);
        });
        check("1.5 shares reject wrong format version foreign fields and invalid parameters",()=>
        {
            using var files=new Files();VehicleTriggerShare.Export(new("Car A","Hyper",new()),files.Share);
            string valid=File.ReadAllText(files.Share);
            Action<JsonObject>[] corrupt=[d=>d["Format"]="other",d=>d["Version"]=2,d=>d["VehicleModel"]=" ",
                d=>d["Settings"]!["Lock"]!["Strength"]=101,d=>d["Settings"]!["BrakeResponse"]!["TravelEnd"]=0,
                d=>d["AudioHaptics"]=new JsonObject(),d=>d["Settings"]=null];
            foreach(var change in corrupt)
            {var doc=JsonNode.Parse(valid)!.AsObject();change(doc);File.WriteAllText(files.Share,doc.ToJsonString());Reject(()=>VehicleTriggerShare.Import(files.Share));}
            File.WriteAllText(files.Share,JsonSerializer.Serialize(new Profile(),ProfileStore.Json));Reject(()=>VehicleTriggerShare.Import(files.Share));
            File.WriteAllText(files.Share,"{broken");Reject(()=>VehicleTriggerShare.Import(files.Share));
        });
        check("1.5 incomplete shares cannot silently fill missing settings with defaults",()=>
        {
            using var files=new Files();VehicleTriggerShare.Export(new("Car A","Hyper",new()),files.Share);
            string valid=File.ReadAllText(files.Share);
            foreach(string path in new[]{"VehicleModel","VehicleClass","Settings","Settings.Lock","Settings.Lock.Enabled","Settings.BrakeResponse.TravelEnd","Settings.ThrottleMode"})
            {
                var doc=JsonNode.Parse(valid)!;var parent=doc;var pieces=path.Split('.');
                foreach(string part in pieces[..^1])parent=parent![part];
                parent!.AsObject().Remove(pieces[^1]);File.WriteAllText(files.Share,doc.ToJsonString());Reject(()=>VehicleTriggerShare.Import(files.Share));
            }
        });
        check("1.5 invalid import or export leaves the existing saved files unchanged",()=>
        {
            using var files=new Files();var root=new Profile();ProfileStore.Save(root,files.Profile);
            VehicleTriggerShare.Export(new("Car A","Hyper",new()),files.Share);string saved=File.ReadAllText(files.Share);
            Reject(()=>VehicleTriggerShare.Export(new("","Hyper",new()),files.Share));Need(File.ReadAllText(files.Share)==saved);
            string before=File.ReadAllText(files.Profile);File.WriteAllText(files.Share,"{}");
            Reject(()=>VehicleTriggerShare.Apply(root,VehicleTriggerShare.Import(files.Share)));Need(File.ReadAllText(files.Profile)==before);
        });
        check("1.5 shared and directly configured cars produce identical final HID bytes",()=>
        {
            using var files=new Files();var vehicle=new VehicleTriggerSettings("Car A","Hyper",new(){Lock=new(true,28),Tc=new(true,42)});
            VehicleTriggerShare.Export(vehicle,files.Share);
            var direct=VehicleTriggerShare.Apply(new(),vehicle);var imported=VehicleTriggerShare.Apply(new(),VehicleTriggerShare.Import(files.Share));
            var a=new FeedbackLoop();var b=new FeedbackLoop();var bytesA=new byte[48];var bytesB=new byte[48];
            for(int n=0;n<50;n++)
            {
                var f=F(n,slip:n<10?0:n<30?.08f:1);
                DualSenseReportBuilder.Build(a.Tick(f,f.CapturedAtSec,direct),bytesA);
                DualSenseReportBuilder.Build(b.Tick(f,f.CapturedAtSec,imported),bytesB);Need(bytesA.SequenceEqual(bytesB));
            }
        });
        check("1.5 default no-ABS warning and lock amplitudes are reduced in hardware bytes",()=>
        {
            foreach(float slip in new[]{.0601f,.12f,1f})
            {
                var loop=new FeedbackLoop();var f=F(0,slip:slip);var output=loop.Tick(f,0,new());var e=loop.Signals.Braking;
                double strength=e.LockActive?.85+.15*e.LockSeverity:.55+.35*e.Risk;
                int old=(int)Math.Ceiling(8*Math.Sqrt(.5)*strength),expected=(int)Math.Ceiling(8*.5*strength);
                Need(output.LeftTrigger.Mode==0x26&&Amplitude(output.LeftTrigger)==expected&&expected<old);
                Need(output.LeftTrigger.P8==(e.LockActive?24:12));
                var hid=new byte[48];DualSenseReportBuilder.Build(output,hid);Need((hid[25]&7)+1==expected);
            }
        });
        check("1.5 no-ABS gain remains monotonic and full strength endpoint is retained",()=>
        {
            foreach(float slip in new[]{.0601f,1f})
            {
                int last=0;
                for(int gain=0;gain<=100;gain++)
                {
                    var p=new Profile(){BrakeBase=new(false,0),Lock=new(true,gain)};var loop=new FeedbackLoop();var output=loop.Tick(F(0,slip:slip),0,p);
                    if(gain==0){Need(output.LeftTrigger==TriggerEffect.Off);continue;}
                    int amplitude=Amplitude(output.LeftTrigger);Need(amplitude>=last);last=amplitude;
                    if(gain==100){var e=loop.Signals.Braking;Need(amplitude==(int)Math.Ceiling(8*(e.LockActive?.85+.15*e.LockSeverity:.55+.35*e.Risk)));}
                }
            }
        });
    }
}
