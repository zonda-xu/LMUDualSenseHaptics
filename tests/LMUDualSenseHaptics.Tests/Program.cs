using System.Collections.Immutable;
using System.Text.Json;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Config;
using LMUDualSenseHaptics.Physics;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Runtime;
using LMUDualSenseHaptics.Telemetry;
using static LMUDualSenseHaptics.Physics.Maths;

if(args.Length==3 && args[0]=="--replay-debug"){Revision052Tests.ReplayDebug(args[1],args[2]);return;}

if(args.Length>0 && args[0] is "--replay" or "--replay-raw")
{
    if(args.Length!=3)throw new ArgumentException("--replay recording.jsonl result.jsonl");
    var loop=new FeedbackLoop();var parser=new LmuLayout();bool raw=args[0]=="--replay-raw";Profile? profile=null;
    using var result=new StreamWriter(args[2]);int n=0;TelemetryFrame? current=null;double next=double.NaN;
    var synth=new VoiceCoilSynthesizer(48000);var pcm=new float[192*4];var report=new byte[48];
    void RenderUntil(double end)
    {
        if(current is null||profile is null)return;
        while(next<end)
        {
            var output=loop.Tick(current,next,profile);
            var audio=loop.AdvancedFeedbackEnabled?VoiceCoilCommand.Create(loop.Signals.RoadSignals,loop.Layers,current,next,profile,loop.Mixer.ContinuousGain,loop.Mixer.EngineGain):VoiceCoilCommand.Off;
            var transport=HapticsOutputRouting.ForTransport(output,profile.AudioHaptics.Enabled);
            DualSenseReportBuilder.Build(transport,report);synth.Render(audio,pcm,next);
            bool speakerSilence=Enumerable.Range(0,192).All(i=>pcm[i*4]==0&&pcm[i*4+1]==0);
            result.WriteLine(JsonSerializer.Serialize(new{Sequence=current.Sequence,At=next,RawTc=current.TcIntervening,Output=output,Transport=transport,Audio=audio,AudioLevels=synth.Levels,SpeakerChannelsSilent=speakerSilence,HidFlag0=report[1],HidFlag2=report[39],State=loop.Reason,loop.Signals.Abs,loop.Signals.Lock,loop.Signals.Tc,loop.Signals.Spin,loop.Signals.LeftRoad,loop.Signals.RightRoad,loop.Signals.Impact,loop.AdvancedFeedbackEnabled,loop.ParameterSource,loop.Channels,loop.LeftTriggerSource,loop.RightTriggerSource,Layers=loop.Layers},ProfileStore.JsonCompact));
            next+=.004;n++;
        }
    }
    foreach(string line in File.ReadLines(args[1]))
    {
        using var doc=JsonDocument.Parse(line);string? kind=doc.RootElement.GetProperty("Kind").GetString();
        if(kind=="profile")profile=doc.RootElement.GetProperty("Profile").Deserialize<Profile>(ProfileStore.Json);
        if(kind!="telemetry")continue;
        if(profile is null)throw new InvalidDataException("回放需要录制时使用的配置");
        var frame=doc.RootElement.GetProperty("Frame").Deserialize<TelemetryFrame>(ProfileStore.Json)!;
        if(raw)
        {
            if(!doc.RootElement.TryGetProperty("Raw",out var bytes))throw new InvalidDataException("原始快照回放缺少 Raw");
            frame=parser.Parse(Convert.FromBase64String(bytes.GetString()!),frame.CapturedAtSec)!;
            if(frame is null)continue;
        }
        if(current is not null && frame.CapturedAtSec<current.CapturedAtSec)throw new InvalidDataException("录制时钟逆序");
        if(double.IsNaN(next))next=frame.CapturedAtSec;
        RenderUntil(frame.CapturedAtSec);current=frame;
    }
    if(current is not null)RenderUntil(current.CapturedAtSec+.26);

    Console.WriteLine($"Replay {n} output ticks at 250 Hz; no device access.");return;
}
int passed=0,failed=0;
void Check(string name,Action test){try{test();Console.WriteLine("PASS "+name);passed++;}catch(Exception e){Console.WriteLine("FAIL "+name+": "+e.Message);failed++;}}
void Assert(bool b,string message="assertion"){if(!b)throw new Exception(message);}
void Throws(Action f){try{f();}catch(Exception){return;}throw new Exception("Expected rejection");}
FeedbackParameters synthetic=new(){Name="SYNTHETIC TEST PARAMETERS — NOT REAL CALIBRATION"};
Profile p=new(){DefaultParameters=synthetic};
TelemetryFrame Frame(long n,double t,float omega=60,float brake=0,float throttle=0,bool abs=false,bool tc=false,int gear=3,float speed=20)
{
    var w=Enumerable.Range(0,4).Select(_=>new WheelFrame(omega,0,3000,3000,0,0,speed,0,null,true)).ToImmutableArray();
    return new(n,t,0,true,true,false,"synthetic","GT3",speed,0,0,-speed,0,0,0,brake,throttle,0,gear,5000,8000,abs,tc,abs,true,w,t,1,VehicleModel:"synthetic");
}
if(args.Length>0 && args[0]=="--export-defaults")
{
    string dir=args.Length>1?args[1]:"examples";Directory.CreateDirectory(dir);var profile=new Profile();
    ProfileStore.Save(profile,Path.Combine(dir,"profile.default.json"));
    File.WriteAllText(Path.Combine(dir,"default-parameters.json"),JsonSerializer.Serialize(profile.DefaultParameters,ProfileStore.Json));
    var template=new VehiclePreset(14200,"Porsche 911 GT3 R LMGT3","GT3",false,"",profile.DefaultParameters);
    File.WriteAllText(Path.Combine(dir,"vehicle-preset.template.json"),JsonSerializer.Serialize(template,ProfileStore.Json));
    Console.WriteLine("Default parameters and an unverified vehicle template exported.");return;
}
if(args.Length>0 && args[0]=="--make-fixtures")
{
    string dir=args.Length>1?args[1]:"fixtures";Directory.CreateDirectory(dir);
    foreach(string scenario in new[]{"clean","left-kerb","right-kerb","heavy-brake","corner","landing","collision"})
    {
        using var output=new StreamWriter(Path.Combine(dir,scenario+".synthetic.jsonl"));
        output.WriteLine(JsonSerializer.Serialize(new{Kind="profile",Profile=p},ProfileStore.JsonCompact));
        for(int n=0;n<300;n++)
        {
            var f=Frame(n,n*.01);bool eventWindow=n is >150 and <170;
            if(scenario=="heavy-brake"&&eventWindow)f=f with{Brake01=1,AbsIntervening=true};
            if(scenario=="corner")f=f with{Steering=.1f,Wheels=f.Wheels.Select(w=>w with{GroundLateralMps=1}).ToImmutableArray()};
            if((scenario=="left-kerb"||scenario=="right-kerb")&&eventWindow)f=f with{Wheels=f.Wheels.Select((w,i)=>i==(scenario=="left-kerb"?0:1)?w with{SuspDeflectionM=n%2==0?.025f:0,SuspForceN=6000,TireLoadN=6000}:w).ToImmutableArray()};
            if(scenario=="landing"&&n>150&&n<165)f=f with{Wheels=f.Wheels.Select(w=>w with{TireLoadN=0,SuspForceN=0}).ToImmutableArray()};
            if(scenario=="landing"&&n==165)f=f with{Wheels=f.Wheels.Select(w=>w with{TireLoadN=7000,SuspForceN=7000,SuspDeflectionM=.04f}).ToImmutableArray()};
            if(scenario=="collision"&&n==165)f=f with{AccelX=40,LastImpactTime=1.65f,LastImpactMagnitude=10};
            output.WriteLine(JsonSerializer.Serialize(new{Kind="telemetry",Frame=f},ProfileStore.JsonCompact));
        }
    }
    Console.WriteLine("7 synthetic fixtures generated; not recorded LMU data.");return;
}
void Warm(SignalEngine e){for(int n=0;n<151;n++)e.Update(Frame(n,n*.01),p);}
Check("smoothstep limits, midpoint, invalid interval",()=>{Assert(Smooth(0,1,-1)==0&&Smooth(0,1,2)==1&&Smooth(0,1,.5)==.5);Throws(()=>Smooth(1,1,0));Throws(()=>C(double.NaN));});
Check("EMA invariance 60/100/250 Hz",()=>{var a=new List<double>();foreach(int hz in new[]{60,100,250}){double v=0;for(int i=0;i<hz;i++)v=Ema(v,1,1.0/hz,.1);a.Add(v);}Assert(a.Max()-a.Min()<1e-10);});
Check("OFF and merged 48-byte USB layout",()=>{byte[] b=new byte[48];DualSenseReportBuilder.Build(FeedbackFrame.Off,b);Assert(b[0]==2&&b[1]==15&&b[2]==85&&b[3]==0&&b[4]==0&&b[11]==5&&b[22]==5&&b[39]==6);var f=new FeedbackFrame(42,91,TriggerEffect.Vibration(0,4,15),TriggerEffect.FeedbackZones(new byte[]{1,2,3,4,5,6,7,8,8,8}));DualSenseReportBuilder.Build(f,b);Assert(b[3]==91&&b[4]==42&&b[11]==0x21&&b[22]==0x26&&b[31]==15);});
Check("trigger bitmap and packed strength",()=>{byte[] b=new byte[11];TriggerEffect.Vibration(9,8,28).WriteTo(b);Assert(b[1]==0&&b[2]==2&&b[9]==28);TriggerEffect.FeedbackZones(new byte[10]).WriteTo(b);Assert(b[1]==0&&b[2]==0);});
Check("radius unknown until trusted calibration",()=>{var e=new SignalEngine();e.Update(Frame(0,0),p);e.Update(Frame(1,.01,brake:1),p);Assert(e.Wheels.All(w=>w.Kappa is null&&w.RadiusSamples==0));});
Check("negative lock, positive spin, calibration frozen during events",()=>{var e=new SignalEngine();Warm(e);double radius=e.Wheels[0].Radius;e.Update(Frame(152,1.52,omega:30,brake:1),p);Assert(e.Wheels[0].Kappa<-.45&&e.Lock>0&&e.Wheels[0].Radius==radius);e.Update(Frame(153,1.53,omega:80,throttle:1),p);Assert(e.Wheels[0].Kappa>.3&&e.Spin>0&&e.Wheels[0].Radius==radius);});
Check("low speed slip unknown",()=>{var e=new SignalEngine();Warm(e);e.Update(Frame(152,1.52,omega:0,brake:1,speed:2),p);Assert(e.Wheels.All(w=>w.Kappa is null));});
Check("turning individual wheel speeds do not cause lock",()=>{var e=new SignalEngine();Warm(e);var f=Frame(152,1.52,brake:.5f);var w=f.Wheels.ToArray();w[0]=w[0] with{OmegaRadPerSec=54,GroundLongitudinalMps=18};w[1]=w[1] with{OmegaRadPerSec=66,GroundLongitudinalMps=22};e.Update(f with{Wheels=w.ToImmutableArray()},p);Assert(e.Wheels.All(q=>Math.Abs(q.Kappa??0)<.001));});
Check("ABS/TC intervention suppresses mechanical targets",()=>{var e=new SignalEngine();Warm(e);e.Update(Frame(152,1.52,omega:20,brake:1,abs:true),p);Assert(e.Abs>0&&e.Lock==0);e.Update(Frame(153,1.53,omega:90,throttle:1,tc:true),p);Assert(e.Tc>0&&e.Spin==0);});
Check("atan2 units and normal sustained corner",()=>{var e=new SignalEngine();for(int n=0;n<600;n++){var f=Frame(n,n*.01);f=f with{Wheels=f.Wheels.Select(w=>w with{GroundLateralMps=1}).ToImmutableArray(),Steering=.1f};e.Update(f,p);}Assert(Math.Abs(e.Wheels[0].Alpha-Math.Atan2(1,20))<1e-6);Assert(e.Wheels[0].Slip<.005);});
Check("single right suspension impact pans right",()=>{var e=new SignalEngine();Warm(e);var f=Frame(152,1.52);f=f with{Wheels=f.Wheels.Select((w,i)=>i==1?w with{SuspDeflectionM=.03f,SuspForceN=6000,TireLoadN=6000}:w).ToImmutableArray()};e.Update(f,p);Assert(e.RightRoad>e.LeftRoad*3&&e.Impact==0);});
Check("slow pitch does not resemble sharp kerb",()=>{var e=new SignalEngine();for(int n=0;n<200;n++){var f=Frame(n,n*.01,brake:.5f);f=f with{Wheels=f.Wheels.Select(w=>w with{SuspDeflectionM=(float)(.005*Math.Sin(n*.01))}).ToImmutableArray()};e.Update(f,p);}Assert(e.Wheels.Max(w=>w.Shock)<.001&&e.Impact==0);});
Check("initial gear, reset, car change avoid false shifts",()=>{var e=new SignalEngine();e.Update(Frame(0,0,gear:6,throttle:1),p);Assert(e.Shift==0);e.Update(Frame(1,.01,gear:7,throttle:1),p);Assert(e.Shift>0);e.Update(Frame(2,.02,gear:8,throttle:1) with{CarId="other"},p);Assert(e.Shift==0&&e.Wheels.All(w=>w.RadiusSamples==0));});
Check("legacy R2 shift priority over TC",()=>{var e=new SignalEngine{Shift=1,Tc=1};var t=new TriggerEngine();var r=t.Render(e,Frame(0,0),0,p with{ThrottleMode=ThrottleFeedbackMode.Legacy});byte[] b=new byte[11];r.Right.WriteTo(b);Assert(b[0]==0x26&&b[9]==28);});
Check("paused, stale, missing, invalid and not driving output OFF",()=>{var loop=new FeedbackLoop();var f=Frame(0,0);Assert(loop.Tick(f with{Paused=true},0,p)==FeedbackFrame.Off);Assert(loop.Tick(f,.251,p)==FeedbackFrame.Off);Assert(loop.Tick(null,0,p)==FeedbackFrame.Off);Assert(loop.Tick(f with{Brake01=float.NaN},0,p)==FeedbackFrame.Off);Assert(loop.Tick(f with{Driving=false},0,p)==FeedbackFrame.Off);Assert(loop.Layers==default&&loop.Mixer.PreLeft==0);});
Check("duplicate sequence does not integrate events",()=>{var e=new SignalEngine();Warm(e);var f=Frame(152,1.52,brake:1,abs:true);e.Update(f,p);double a=e.Abs;for(int n=0;n<200;n++)e.Update(f,p);Assert(a==e.Abs);});
Check("reverse time resets derivatives",()=>{var e=new SignalEngine();Warm(e);e.Update(Frame(152,.8,gear:8,brake:1),p);Assert(e.Lock==0&&e.Shift==0&&e.Wheels.All(w=>w.Shock==0));});
Check("collision needs second evidence",()=>{var e=new SignalEngine();Warm(e);e.Update(Frame(152,1.52) with{AccelY=30},p);Assert(e.Impact==0);e.Update(Frame(153,1.53,speed:15) with{AccelX=40,LastImpactTime=1.53f,LastImpactMagnitude=10},p);Assert(e.Impact>0);});
Check("random mixer never overflows",()=>{var rng=new Random(77);var mixer=new FrameMixer();for(int i=0;i<10000;i++){double R()=>rng.NextDouble();var g=new GripLayers(R(),R(),R(),R(),R(),R(),R(),R(),R(),R(),R());mixer.Mix((TriggerEffect.Off,TriggerEffect.Off),g,p with{RightGain=1.42});}Assert(mixer.ClippedFrames>0);});
Check("JSON atomic roundtrip and invalid profile rejection",()=>{string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try{ProfileStore.Save(p,path);Assert(JsonSerializer.Serialize(ProfileStore.Load(path),ProfileStore.JsonCompact)==JsonSerializer.Serialize(p,ProfileStore.JsonCompact));Throws(()=>ProfileStore.Save(p with{Road=new(true,101)},path));}finally{File.Delete(path);}});
// Synthetic binary ABI fixture: independent construction, not real LMU data.
byte[] Snapshot()
{
    byte[] b=new byte[LmuLayout.SnapshotSize];void I(int o,int v)=>BitConverter.GetBytes(v).CopyTo(b,o);void D(int o,double v)=>BitConverter.GetBytes(v).CopyTo(b,o);
    I(64,1);b[96]=3;b[128464]=1;b[128466]=1;int v=128468;I(v,7);D(v+12,1);I(v+352,3);D(v+532,8000);D(v+200,-20);I(1632+104,1);b[1632+115]=1;int sc=1632+560;I(sc,7);b[sc+196]=1;for(int i=0;i<4;i++){int q=v+848+i*260;D(q+104,3000);D(q+16,3000);D(q+40,60);D(q+72,20);}return b;
}
Check("binary parser player ID, freshness and field offsets",()=>{var parser=new LmuLayout();var b=Snapshot();var f=parser.Parse(b,1)!;Assert(f.Driving&&f.LocalVzMps==-20&&f.Wheels[0].OmegaRadPerSec==60&&f.Wheels[0].TireLoadN==3000);Assert(parser.Parse(b,2) is null);b[1632+108]=9;Assert(parser.Parse(b,3)!.Paused);});
Check("binary rejects wrong player, NaN, bool and length",()=>{var b=Snapshot();b[1632+560+196]=0;Throws(()=>new LmuLayout().Parse(b,1));b=Snapshot();BitConverter.GetBytes(double.NaN).CopyTo(b,128468+848);Throws(()=>new LmuLayout().Parse(b,1));b=Snapshot();b[128466]=2;Throws(()=>new LmuLayout().Parse(b,1));Throws(()=>new LmuLayout().Parse(new byte[1],1));});
Check("ABS event invariance at 60/100/250 Hz",()=>{var levels=new List<double>();foreach(int hz in new[]{60,100,250}){var e=new SignalEngine();e.Update(Frame(0,0),p);for(int n=1;n<=hz;n++)e.Update(Frame(n,n/(double)hz,brake:1,abs:true),p);levels.Add(e.Abs);}Assert(levels.Max()-levels.Min()<1e-8);});
Check("landing restores contact and creates suspension shock",()=>{var e=new SignalEngine();Warm(e);var f=Frame(152,1.52);e.Update(f with{Wheels=f.Wheels.Select(w=>w with{TireLoadN=0,SuspForceN=0}).ToImmutableArray()},p);f=Frame(153,1.53);e.Update(f with{Wheels=f.Wheels.Select(w=>w with{SuspDeflectionM=.04f,TireLoadN=7000,SuspForceN=7000}).ToImmutableArray()},p);Assert(e.Wheels.All(w=>w.Shock>.2));});
Check("alternating kerbs have finite smooth direction",()=>{var e=new SignalEngine();Warm(e);for(int n=152;n<252;n++){var f=Frame(n,n*.01);f=f with{Wheels=f.Wheels.Select((w,i)=>w with{SuspDeflectionM=i==n%2?.02f:0,TireLoadN=i==n%2?6000:3000,SuspForceN=i==n%2?6000:3000}).ToImmutableArray()};e.Update(f,p);Assert(double.IsFinite(e.SidePan)&&Math.Abs(e.SidePan)<=1);}});
Check("ABS wall responds on first intervention frame",()=>{var e=new SignalEngine();var f=Frame(0,0,brake:.7f,abs:true);e.Update(f,p);var output=new TriggerEngine().Render(e,f,0,p);Assert(output.Left.Mode==0x21&&e.Braking.AbsWall==1);});
Check("zero enabled strength restores OFF base",()=>{var e=new SignalEngine();var t=new TriggerEngine();var output=t.Render(e,Frame(0,0),0,p with{BrakeBase=new(true,0),ThrottleBase=new(false,50)});Assert(output.Left==TriggerEffect.Off&&output.Right==TriggerEffect.Off);});
Check("resume after stale resets event history",()=>{var loop=new FeedbackLoop();for(int i=0;i<100;i++)loop.Tick(Frame(i,i*.01,brake:1,abs:true),i*.01,p);Assert(loop.Signals.Abs>.9);Assert(loop.Tick(Frame(99,.99),2,p)==FeedbackFrame.Off);loop.Tick(Frame(101,2.01,gear:7,throttle:1),2.01,p);Assert(loop.Signals.Abs==0&&loop.Signals.Shift==0);});
Check("deterministic telemetry replay identical reports",()=>{var a=new FeedbackLoop();var b=new FeedbackLoop();for(int i=0;i<1000;i++){var f=Frame(i,i*.01,brake:i%100<30?1:0,abs:i%100<30,throttle:i%100>60?1:0);Assert(a.Tick(f,f.CapturedAtSec,p)==b.Tick(f,f.CapturedAtSec,p));}});
Check("signed longitudinal conventions yield principal slip angle",()=>{Assert(Math.Abs(SignalEngine.SlipAngle(1,-20)+Math.Atan(1.0/20))<1e-12);Assert(Math.Abs(SignalEngine.SlipAngle(1,20)-Math.Atan(1.0/20))<1e-12);});
Check("LMU 14200 OptionsLocation zero is realtime driving",()=>
{
    var b=Snapshot();BitConverter.GetBytes(14200).CopyTo(b,64);b[96]=0;b[1632+108]=5;
    int sc=1632+560;b[sc+198]=0;b[sc+507]=0;b[sc+532]=5;
    var f=new LmuLayout().Parse(b,1)!;Assert(f.Driving&&!f.Paused&&f.GameVersion==14200);
});
Check("driving rejects monitor, AI, remote, replay, garage and pause",()=>
{
    foreach(int condition in new[]{0,1,2,3,4,5})
    {
        var b=Snapshot();b[96]=0;int sc=1632+560;
        if(condition==0)b[1632+115]=0;
        if(condition is >=1 and <=3)b[sc+197]=(byte)condition;
        if(condition==4)b[sc+507]=1;
        if(condition==5)b[1632+108]=9;
        Assert(!new LmuLayout().Parse(b,1)!.Driving);
    }
    var invalid=Snapshot();invalid[1632+560+507]=2;Throws(()=>new LmuLayout().Parse(invalid,1));
});
Check("frozen physics cannot resume feedback through UI state change",()=>
{
    var b=Snapshot();var parser=new LmuLayout();Assert(parser.Parse(b,1)!.Driving);
    b[1632+108]=9;Assert(parser.Parse(b,2)!.Paused);
    b[1632+108]=5;Assert(!parser.Parse(b,3)!.Driving);
    BitConverter.GetBytes(1.01).CopyTo(b,128468+12);Assert(parser.Parse(b,3.01)!.Driving);
});
Check("default parameters enable L2 and engine while R2 is free",()=>
{
    var loop=new FeedbackLoop();var output=loop.Tick(Frame(0,0),0,new Profile());
    Assert(output.LeftTrigger.Mode==0x21&&output.RightTrigger.Mode==5);
    Assert(output.LeftActuator>0||output.RightActuator>0);
    Assert(loop.AdvancedFeedbackEnabled&&loop.ParameterSource.Contains("默认初始参数"));
    Assert(loop.Layers.RoadL==0&&loop.Layers.SlipL==0&&loop.Layers.ImpactL==0);
});
Check("legacy parameters keep redline and upshift events",()=>
{
    var loop=new FeedbackLoop();var basic=new Profile{ThrottleMode=ThrottleFeedbackMode.Legacy};
    loop.Tick(Frame(0,0,throttle:1) with{Rpm=7950},0,basic);
    var redline=loop.Tick(Frame(1,.05,throttle:1) with{Rpm=7950},.05,basic);
    byte[] report=new byte[11];redline.RightTrigger.WriteTo(report);Assert(report[0]==0x26&&report[9]==18);
    var shift=loop.Tick(Frame(2,.1,throttle:1,gear:4),.1,basic);
    shift.RightTrigger.WriteTo(report);Assert(report[0]==0x26&&report[9]==28&&loop.Signals.Shift>0);
});
VehiclePreset preset=new(1,"synthetic","GT3",true,"SYNTHETIC TEST ONLY",synthetic with{V0=5,V1=10,RoadResponse=new(){SuspensionNoiseMps=5,SuspensionFullMps=10,TireNoiseMps=5,TireFullMps=10,LoadNoisePerSec=1000,LoadFullPerSec=2000,BumpNoiseMps=5,BumpFullMps=10,BumpLoadNoisePerSec=1000,BumpLoadFullPerSec=2000}});
Check("unverified or mismatched preset selects working default parameters",()=>
{
    foreach(var candidate in new[]{preset with{GameVersion=2},preset with{VehicleModel="other"},preset with{VehicleClass="GTE"},preset with{Verified=false}})
    {
        var loop=new FeedbackLoop();var profile=p with{VehiclePresets=[candidate]};
        for(int n=0;n<151;n++)loop.Tick(Frame(n,n*.01),n*.01,profile);
        var f=Frame(152,1.52) with{Wheels=Frame(152,1.52).Wheels.Select((w,i)=>i==1?w with{SuspDeflectionM=.03f,SuspForceN=7000,TireLoadN=7000}:w).ToImmutableArray()};
        var output=loop.Tick(f,1.52,profile);
        Assert(loop.AdvancedFeedbackEnabled&&loop.ParameterSource.Contains("默认初始参数")&&loop.Layers.RoadR>0&&output.RightActuator>output.LeftActuator);
    }
});
Check("matching vehicle preset has priority and switching clears history",()=>
{
    var loop=new FeedbackLoop();var profile=p with{VehiclePresets=[preset]};
    loop.Tick(Frame(0,0),0,profile);
    var f=Frame(1,.01) with{Wheels=Frame(1,.01).Wheels.Select(w=>w with{SuspDeflectionM=.03f,SuspForceN=7000,TireLoadN=7000}).ToImmutableArray()};
    loop.Tick(f,.01,profile);Assert(loop.ParameterSource.Contains("已验证车型预设")&&loop.Layers.RoadL==0);
    loop.Tick(f,.014,p);Assert(loop.ParameterSource.Contains("默认初始参数")&&loop.Layers.RoadL==0);
    f=Frame(2,.02) with{AccelX=40,LastImpactTime=.02f,LastImpactMagnitude=10};
    loop.Tick(f,.02,p);Assert(loop.Signals.Impact>0);
    loop.Tick(f,.024,profile);Assert(loop.Signals.Impact==0&&loop.Layers.ImpactL==0);
});
Check("USB handle survives 250 OFF writes then basic driving then pause",()=>
{
    int opens=0;var fake=new FakeHidDevice();var session=new HidOutputSession(()=>{opens++;return fake;});var loop=new FeedbackLoop();
    for(int i=0;i<250;i++){double now=i*.004;session.EnsureConnected(now);Assert(session.TryWrite(loop.Tick(null,now,new Profile()),now));}
    var frame=Frame(1,1);Assert(session.TryWrite(loop.Tick(frame,1,new Profile()),1));
    Assert(session.TryWrite(loop.Tick(frame with{Paused=true},1.004,new Profile()),1.004));
    Assert(opens==1&&fake.Disposals==0&&fake.Writes.Count==252&&session.Connected&&session.Label=="USB PID 0CE6");
    Assert(fake.Writes.Take(250).All(f=>f==FeedbackFrame.Off)&&fake.Writes[250]!=FeedbackFrame.Off&&fake.Writes[251]==FeedbackFrame.Off);
    session.Dispose();session.Dispose();Assert(fake.Disposals==1&&!session.Connected);
});
Check("USB write failure alone closes and schedules reconnect",()=>
{
    int opens=0;var first=new FakeHidDevice{Fail=true};var second=new FakeHidDevice();
    using var session=new HidOutputSession(()=>++opens==1?first:second);
    session.EnsureConnected(0);Assert(!session.TryWrite(FeedbackFrame.Off,0));
    Assert(!session.Connected&&first.Disposals==1&&session.LastError.Contains("1167"));
    session.EnsureConnected(.999);Assert(opens==1);session.EnsureConnected(1);Assert(opens==2&&session.Connected);
    Assert(session.TryWrite(FeedbackFrame.Off,1)&&second.Disposals==0&&session.LastError=="");
});
Check("missing USB retry does not depend on feedback mode",()=>
{
    int opens=0;using var session=new HidOutputSession(()=>{opens++;return null;});
    session.EnsureConnected(0);for(int i=1;i<250;i++)session.EnsureConnected(i*.004);
    Assert(opens==1&&!session.Connected&&!session.TryWrite(FeedbackFrame.Off,.5));
    session.EnsureConnected(1);Assert(opens==2);
});
Check("sanitized LMU 14200 recording reproduces basic output",()=>
{
    var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","lmu14200-options0.snapshot.bin"));
    var f=new LmuLayout().Parse(bytes,0)!;Assert(f.Driving&&f.GameVersion==14200&&f.VehicleModel=="Porsche 911 GT3 R LMGT3");
    var loop=new FeedbackLoop();var output=loop.Tick(f,0,new Profile());
    Assert(output.LeftTrigger.Mode==0x21&&output.RightTrigger.Mode==(f.TcIntervening?0x21:5)&&loop.AdvancedFeedbackEnabled);
    Assert(loop.Layers.EngineR>0&&loop.Layers.RoadL==0&&loop.Layers.SlipR==0&&loop.Layers.ImpactL==0);
});
Check("default road event reaches grip mixer and merged USB report",()=>
{
    var loop=new FeedbackLoop();var profile=new Profile{Engine=new(false,0)};
    loop.Tick(Frame(0,0),0,profile);
    var f=Frame(1,.01) with{Wheels=Frame(1,.01).Wheels.Select((w,i)=>i==0?w with{SuspDeflectionM=.04f,SuspForceN=7000,TireLoadN=7000}:w).ToImmutableArray()};
    var output=loop.Tick(f,.01,profile);byte[] report=new byte[48];DualSenseReportBuilder.Build(output,report);
    Assert(loop.Signals.LeftRoad>0&&loop.Layers.RoadL>0&&loop.Channels.Road>0&&output.LeftActuator>output.RightActuator);
    Assert(report[4]==output.LeftActuator&&report[3]==output.RightActuator&&report[11]==5&&report[22]==0x21);
});
Check("default lateral slip works before radius learning",()=>
{
    var loop=new FeedbackLoop();var profile=new Profile{Engine=new(false,0)};loop.Tick(Frame(0,0),0,profile);
    var f=Frame(1,.01) with{Wheels=Frame(1,.01).Wheels.Select((w,i)=>i==3?w with{GroundLateralMps=8}:w).ToImmutableArray()};
    var output=loop.Tick(f,.01,profile);
    Assert(loop.Signals.Wheels.All(w=>w.Kappa is null)&&loop.Layers.SlipR>0&&loop.Channels.GripSlip>0&&output.RightActuator>output.LeftActuator);
});
Check("moderate steady throttle learns radius without coasting",()=>
{
    var e=new SignalEngine();for(int n=0;n<180;n++)e.Update(Frame(n,n*.01,throttle:.4f),new Profile());
    Assert(e.Wheels.All(w=>w.RadiusTime>=1&&w.Kappa is not null&&Math.Abs(w.Kappa.Value)<.001&&w.RoadLearnTime>1));
});
Check("normal road noise floor learns and remains bounded",()=>
{
    var e=new SignalEngine();var profile=new Profile();
    for(int n=0;n<1000;n++)
    {
        var f=Frame(n,n*.01,throttle:.3f) with{Wheels=Frame(n,n*.01).Wheels.Select(w=>w with{SuspDeflectionM=(float)(.0002*Math.Sin(n*.1)),SuspForceN=(float)(3000+20*Math.Sin(n*.1)),TireLoadN=(float)(3000+20*Math.Sin(n*.1))}).ToImmutableArray()};
        e.Update(f,profile);
    }
    Assert(e.Wheels.All(w=>w.NoiseVelocity>0&&w.NoiseVelocity<=profile.DefaultParameters.V0&&w.NoiseForce>0&&w.NoiseLoad>0&&w.Shock<.01));
});
Check("learning excludes braking turning intervention kerbs and wheel disagreement",()=>
{
    for(int condition=0;condition<8;condition++)
    {
        var e=new SignalEngine();Warm(e);var before=e.Wheels.Select(w=>(w.RadiusTime,w.RoadLearnTime,w.NoiseVelocity,w.NoiseForce,w.NoiseLoad)).ToArray();
        var f=Frame(152,1.52,throttle:.4f);
        f=condition switch
        {
            0=>f with{Brake01=.5f},1=>f with{Steering=.3f},2=>f with{AbsIntervening=true},3=>f with{TcIntervening=true},
            4=>f with{Wheels=f.Wheels.Select((w,i)=>i==0?w with{SuspDeflectionM=.04f,TireLoadN=7000,SuspForceN=7000}:w).ToImmutableArray()},
            5=>f with{Wheels=f.Wheels.Select((w,i)=>i==0?w with{OmegaRadPerSec=90}:w).ToImmutableArray()},
            6=>f with{AccelX=30,LastImpactTime=1.52f,LastImpactMagnitude=2},_=>f with{Throttle01=1}
        };
        e.Update(f,p);Assert(e.Wheels.Select(w=>(w.RadiusTime,w.RoadLearnTime,w.NoiseVelocity,w.NoiseForce,w.NoiseLoad)).SequenceEqual(before));
    }
});
Check("airborne wheel has no kappa and cannot teach radius",()=>
{
    var e=new SignalEngine();Warm(e);var f=Frame(152,1.52) with{Wheels=Frame(152,1.52).Wheels.Select(w=>w with{TireLoadN=0,OmegaRadPerSec=90}).ToImmutableArray()};
    double time=e.Wheels[0].RadiusTime;e.Update(f,p);Assert(e.Wheels.All(w=>w.Kappa is null&&w.RadiusTime==time));
});
Check("steady high wheel slip cannot update an established radius",()=>
{
    var e=new SignalEngine();Warm(e);double radius=e.Wheels[0].Radius,time=e.Wheels[0].RadiusTime;
    for(int n=152;n<220;n++)e.Update(Frame(n,n*.01,omega:90,throttle:.4f),p);
    Assert(e.Wheels.All(w=>w.Radius==radius&&w.RadiusTime==time)&&e.Spin>0);
});
Check("lock and spin use specified input and slip thresholds",()=>
{
    foreach(var test in new (float omega,float brake,float throttle,bool lockOn,bool spinOn)[]{(51.6f,.021f,0f,false,false),(48.6f,.021f,0f,true,false),(42f,.019f,0f,false,false),(65.4f,0f,.021f,false,false),(67.2f,0f,.021f,false,true),(70f,0f,.019f,false,false)})
    {
        var e=new SignalEngine();Warm(e);e.Update(Frame(152,1.52,omega:test.omega,brake:test.brake,throttle:test.throttle),p);
        Assert((e.Lock>0)==test.lockOn&&(e.Spin>0)==test.spinOn);
    }
});
Check("vertical kerb even with contact event is not a collision",()=>
{
    var loop=new FeedbackLoop();loop.Tick(Frame(0,0),0,new Profile());
    var f=Frame(1,.01) with{AccelY=50,LastImpactTime=.01f,LastImpactMagnitude=5,Wheels=Frame(1,.01).Wheels.Select(w=>w with{SuspDeflectionM=.04f,TireLoadN=7000,SuspForceN=7000}).ToImmutableArray()};
    loop.Tick(f,.01,new Profile());Assert(loop.Signals.LeftRoad>0&&loop.Signals.Impact==0&&loop.Signals.Scrape==0&&loop.Channels.Impact==0);
    loop.Tick(Frame(2,.11) with{AccelX=40,LastImpactTime=.01f,LastImpactMagnitude=5},.11,new Profile());Assert(loop.Signals.Impact==0);
});
Check("large deceleration without a collision event stays out of impact channel",()=>
{
    var e=new SignalEngine();Warm(e);e.Update(Frame(152,1.52,speed:10,brake:1) with{AccelZ=50},p);Assert(e.Impact==0&&e.Scrape==0);
});
Check("reported collision reaches directional grips and diagnostics",()=>
{
    var loop=new FeedbackLoop();var profile=new Profile{Engine=new(false,0)};loop.Tick(Frame(0,0),0,profile);
    var output=loop.Tick(Frame(1,.01) with{AccelX=40,LastImpactTime=.01f,LastImpactMagnitude=5},.01,profile);
    Assert(loop.Layers.ImpactR>loop.Layers.ImpactL&&loop.Channels.Impact>0&&output.RightActuator>output.LeftActuator);
});
Check("collision event can precede dynamics by one telemetry frame",()=>
{
    var e=new SignalEngine();e.Update(Frame(0,0),p);e.Update(Frame(1,.01) with{LastImpactTime=.01f,LastImpactMagnitude=1},p);Assert(e.Impact==0);
    e.Update(Frame(2,.04) with{AccelX=20,LastImpactTime=.01f,LastImpactMagnitude=1},p);Assert(e.Impact>0);
});
Check("old impact timestamp does not retrigger and learning resumes",()=>
{
    var e=new SignalEngine();Warm(e);double oldLearn=e.Wheels[0].RadiusTime;
    e.Update(Frame(152,1.52) with{AccelX=40,LastImpactTime=1.52f,LastImpactMagnitude=5},p);double first=e.ImpactStart;
    for(int n=153;n<300;n++)e.Update(Frame(n,n*.01,throttle:.3f) with{LastImpactTime=1.52f,LastImpactMagnitude=5},p);
    Assert(e.ImpactStart==first&&e.Impact==0&&e.Wheels[0].RadiusTime>oldLearn);
});
Check("disabled advanced features remove their requests and grip output",()=>
{
    var loop=new FeedbackLoop();var profile=new Profile{Road=new(false,55),GripSlip=new(true,0),Impact=new(false,60),Engine=new(false,15)};
    loop.Tick(Frame(0,0),0,profile);
    var f=Frame(1,.01) with{AccelX=40,LastImpactTime=.01f,LastImpactMagnitude=5,Wheels=Frame(1,.01).Wheels.Select(w=>w with{SuspDeflectionM=.04f,SuspForceN=7000,TireLoadN=7000,GroundLateralMps=8}).ToImmutableArray()};
    var output=loop.Tick(f,.01,profile);Assert(loop.Signals.Impact>0&&loop.Signals.LeftRoad>0&&loop.Signals.Wheels[0].Slip>0);
    Assert(output.LeftActuator==0&&output.RightActuator==0&&loop.Channels.Road==0&&loop.Channels.GripSlip==0&&loop.Channels.Impact==0&&loop.Channels.Engine==0);
});
Check("all channels become zero on pause stale disconnect and invalid telemetry",()=>
{
    foreach(int condition in new[]{0,1,2,3})
    {
        var loop=new FeedbackLoop();var profile=new Profile();loop.Tick(Frame(0,0),0,profile);
        var f=Frame(1,.01) with{AccelX=30,LastImpactTime=.01f,LastImpactMagnitude=1};loop.Tick(f,.01,profile);Assert(loop.Channels.Impact>0);
        f=condition switch{0=>f with{Paused=true},2=>f with{Connected=false},3=>f with{Rpm=float.NaN},_=>f};
        Assert(loop.Tick(f,condition==1?1:.02,profile)==FeedbackFrame.Off&&loop.Channels==default&&loop.LeftTriggerSource=="OFF"&&loop.RightTriggerSource=="OFF");
    }
});
Check("parameter validation rejects NaN invalid order and duplicate identity",()=>
{
    Throws(()=>new Profile{DefaultParameters=new(){V0=double.NaN}}.Validate());
    Throws(()=>new Profile{DefaultParameters=new(){BrakeResponse=new(){WarningSlip=.2,CriticalSlip=.1}}}.Validate());
    Throws(()=>new Profile{VehiclePresets=[preset,preset]}.Validate());
    Throws(()=>(preset with{EvidenceFile=""}).Validate());
});
Check("vehicle preset JSON roundtrip preserves numeric parameters and evidence",()=>
{
    string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
    try
    {
        File.WriteAllText(path,JsonSerializer.Serialize(preset,ProfileStore.Json));Assert(ProfileStore.LoadPreset(path)==preset);
        var profile=p with{VehiclePresets=[preset]};ProfileStore.Save(profile,path);var loaded=ProfileStore.Load(path);
        Assert(loaded.VehiclePresets.SequenceEqual(profile.VehiclePresets)&&loaded.DefaultParameters==profile.DefaultParameters);
    }
    finally{File.Delete(path);}
});
Check("car switch discards learned radius and normal-road history",()=>
{
    var e=new SignalEngine();Warm(e);Assert(e.Wheels[0].RadiusTime>1);
    e.Update(Frame(152,1.52) with{CarId="another car",VehicleModel="other"},p);
    Assert(e.Wheels.All(w=>w.RadiusTime==0&&w.RoadLearnTime==0&&w.Kappa is null)&&e.Shift==0&&e.Impact==0);
});
Check("default learning stable across 60 100 and 250 Hz",()=>
{
    foreach(int hz in new[]{60,100,250})
    {
        var e=new SignalEngine();for(int n=0;n<=hz*2;n++)e.Update(Frame(n,n/(double)hz,throttle:.4f),new Profile());
        Assert(e.Wheels.All(w=>Math.Abs(w.Radius-1.0/3)<1e-6&&w.Kappa is not null&&Math.Abs(w.RoadLearnTime-2)<.001));
    }
});
Revision04Tests.Run(Check);
Revision05Tests.Run(Check);
Revision051Tests.Run(Check);
Revision052Tests.Run(Check);
BrakePreviewTests.Run(Check);
Console.WriteLine($"RESULT {passed} passed, {failed} failed (synthetic, sanitized recorded fields and fake HID; no hardware claims)");
Environment.ExitCode=failed==0?0:1;

internal sealed class FakeHidDevice:IHidOutputDevice
{
    public string Label=>"USB PID 0CE6";
    public int LastWriteError=>Fail?1167:0;
    public bool Fail;
    public int Disposals;
    public List<FeedbackFrame> Writes {get;}=new();
    public bool TryWrite(in FeedbackFrame frame){if(Fail)return false;Writes.Add(frame);return true;}
    public void Dispose(){Disposals++;}
}
