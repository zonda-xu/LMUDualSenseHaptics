using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Config;
using LMUDualSenseHaptics.Runtime;
using LMUDualSenseHaptics.Hardware;

internal static class LocalizationTests
{
    private static void Need(bool value,string message="assertion") {if(!value)throw new Exception(message);}
    public static void Run(Action<string,Action> check)
    {
        check("1.6 all six embedded catalogs format every resource",()=>
        {
            var keys=L.Resources("zh-CN").Keys.Order().ToArray();
            Need(L.Languages.Select(x=>x.Code).SequenceEqual(new[]{"zh-CN","en","fr","es","ru","ar"}));
            foreach(var language in L.Languages)
            {
                Need(keys.SequenceEqual(L.Resources(language.Code).Keys.Order()));
                foreach(var key in keys)
                {
                    string value=L.M(key,Enumerable.Repeat<object?>(1,20).ToArray()).Render(language.Code);
                    Need(!string.IsNullOrWhiteSpace(value),language.Code+" "+key);
                }
            }
        });
        check("1.6 cached nested diagnostics and validation errors switch without recreation",()=>
        {
            var cached=L.M("Text_51318070A7",L.M("Text_BE1462586F"),"DualSense",L.M("Text_18A9D61DEE"),"Test");
            var error=L.InvalidData(L.M("Text_07F89BFD62"));
            var endpoint=new AudioEndpointInfo("test","DualSense",Guid.Empty,2,48000,L.Error(error));
            string stableLog=JsonSerializer.Serialize(cached);
            try
            {
                foreach(var language in L.Languages)
                {
                    L.SetLanguage(language.Code);
                    Need(cached.ToString().Contains(L.T("Text_BE1462586F")));
                    Need(L.Error(error).ToString()==L.T("Text_07F89BFD62"));
                    Need(endpoint.DisplayName.Contains(L.T("Text_07F89BFD62")),"Cached endpoint error did not switch language");
                    Need(JsonSerializer.Serialize(cached)==stableLog,"UI language changed logs");
                    Need(error is InvalidDataException);
                }
            }
            finally {L.SetLanguage("zh-CN");}
        });
        check("1.6 language preference persists separately without modifying profile or share",()=>
        {
            string folder=Path.Combine(Path.GetTempPath(),"lmu-language-"+Guid.NewGuid());Directory.CreateDirectory(folder);
            string preference=Path.Combine(folder,"ui-language.txt"),profile=Path.Combine(folder,"profile.json"),share=Path.Combine(folder,"car.json");
            try
            {
                ProfileStore.Save(new(),profile);VehicleTriggerShare.Export(new("Test","Hyper",new()),share);
                byte[] profileBytes=File.ReadAllBytes(profile),shareBytes=File.ReadAllBytes(share);
                Need(UiLanguageStore.Load(preference)=="zh-CN");
                foreach(var language in L.Languages)
                {
                    UiLanguageStore.Save(language.Code,preference);Need(UiLanguageStore.Load(preference)==language.Code);
                    Need(File.ReadAllBytes(profile).SequenceEqual(profileBytes));Need(File.ReadAllBytes(share).SequenceEqual(shareBytes));
                }
                try{UiLanguageStore.Save("unsupported",preference);throw new Exception("accepted invalid language");}
                catch(ArgumentOutOfRangeException){}
                Need(UiLanguageStore.Load(preference)=="ar");Need(!Directory.GetFiles(folder,"*.tmp").Any());
            }
            finally{Directory.Delete(folder,true);}
        });
        check("1.6 repeated language switches leave final HID bytes and brake learning identical",()=>
        {
            var baseline=new FeedbackLoop();var switched=new FeedbackLoop();var profile=new Profile();
            var priorCulture=CultureInfo.CurrentUICulture;
            try
            {
                for(int n=1;n<=360;n++)
                {
                    string category=n%120<60?"Hyper":"GT3";float brake=(n%60)/59f;
                    var f=new TelemetryFrame(n,n*.02,0,true,true,false,"slot-1",category,40,0,0,-40,0,0,0,.5f,brake,0,4,6000,8000,false,true,false,true,
                        Enumerable.Range(0,4).Select(_=>new WheelFrame(-40/.35f*(1-brake*.2f),0,3000,3000,0,.01f,-40,0,null,true,LongitudinalPatchMps:brake*8)).ToImmutableArray(),n*.02,VehicleModel:category);
                    L.SetLanguage("zh-CN");var expected=baseline.Tick(f,f.CapturedAtSec,profile);
                    string language=L.Languages[n%6].Code;L.SetLanguage(language);CultureInfo.CurrentUICulture=L.Culture(language);
                    var actual=switched.Tick(f,f.CapturedAtSec,profile);
                    byte[] a=new byte[48],b=new byte[48];DualSenseReportBuilder.Build(expected,a);DualSenseReportBuilder.Build(actual,b);
                    Need(a.SequenceEqual(b),"HID changed at "+n);
                    Need(baseline.Signals.Braking.Budget.Prompt==switched.Signals.Braking.Budget.Prompt);
                    Need(baseline.Signals.Braking.Budget.Confidence==switched.Signals.Braking.Budget.Confidence);
                }
            }
            finally{L.SetLanguage("zh-CN");CultureInfo.CurrentUICulture=priorCulture;}
        });
    }
}
