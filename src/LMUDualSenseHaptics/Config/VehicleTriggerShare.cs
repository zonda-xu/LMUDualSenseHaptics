using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using LMUDualSenseHaptics.Core;
namespace LMUDualSenseHaptics.Config;

// A self-contained, single-vehicle exchange file. Never serializes a whole Profile.
public static class VehicleTriggerShare
{
    private const string Format="LMUDualSenseHaptics.VehicleTriggers";
    private sealed record Document(
        [property:JsonRequired] string Format,
        [property:JsonRequired] int Version,
        [property:JsonRequired] string VehicleModel,
        [property:JsonRequired] string VehicleClass,
        [property:JsonRequired] TriggerSettings Settings);
    private static readonly JsonSerializerOptions Json=CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var resolver=new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info=>
        {
            if(info.Type==typeof(TriggerSettings)||info.Type==typeof(BrakeResponse)||info.Type==typeof(Feature))
                foreach(var property in info.Properties)
                    if(property.Set is not null)property.IsRequired=true;
        });
        return new(){WriteIndented=true,IgnoreReadOnlyProperties=true,
            UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,TypeInfoResolver=resolver};
    }
    public static void Export(VehicleTriggerSettings vehicle,string path)
    {
        vehicle.Validate();
        var document=new Document(Format,1,vehicle.VehicleModel.Trim(),vehicle.VehicleClass.Trim(),vehicle.Settings);
        string full=Path.GetFullPath(path),temporary=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {JsonSerializer.Serialize(stream,document,Json);stream.Flush(true);}
            File.Move(temporary,full,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static VehicleTriggerSettings Import(string path)
    {
        using var stream=File.OpenRead(path);
        var document=JsonSerializer.Deserialize<Document>(stream,Json)??throw new InvalidDataException("车辆扳机分享文件为空");
        if(document.Format!=Format||document.Version!=1)throw new InvalidDataException("不是支持的车辆扳机分享文件");
        var vehicle=new VehicleTriggerSettings(document.VehicleModel,document.VehicleClass,document.Settings);
        vehicle.Validate();
        return vehicle with{VehicleModel=vehicle.VehicleModel.Trim(),VehicleClass=vehicle.VehicleClass.Trim()};
    }
    public static Profile Apply(Profile profile,VehicleTriggerSettings vehicle)
    {
        vehicle.Validate();
        var updated=TriggerProfiles.SaveVehicle(profile,vehicle.VehicleModel,vehicle.VehicleClass,vehicle.Settings);
        updated.Validate();return updated;
    }
}
