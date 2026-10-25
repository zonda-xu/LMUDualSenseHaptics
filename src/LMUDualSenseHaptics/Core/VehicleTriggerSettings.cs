using System.Collections.Immutable;
namespace LMUDualSenseHaptics.Core;

// A trigger-only snapshot. Audio, road, grip and device settings stay global.
public sealed record TriggerSettings
{
    public BrakeFeedbackMode BrakeMode {get;init;} = BrakeFeedbackMode.Auto;
    public ThrottleFeedbackMode ThrottleMode {get;init;} = ThrottleFeedbackMode.TcOnly;
    public Feature BrakeBase {get;init;} = new(true,50);
    public Feature ThrottleBase {get;init;} = new(false,50);
    public Feature Abs {get;init;} = new(true,50);
    public Feature Lock {get;init;} = new(true,50);
    public Feature Tc {get;init;} = new(true,75);
    public Feature Wheelspin {get;init;} = new(true,50);
    public Feature Shift {get;init;} = new(true,45);
    public Feature Redline {get;init;} = new(true,35);
    public double RedlineStartRatio {get;init;} = .962;
    public BrakeResponse BrakeResponse {get;init;} = new();

    public static TriggerSettings From(Profile p) => new() {
        BrakeMode=p.BrakeMode,ThrottleMode=p.ThrottleMode,BrakeBase=p.BrakeBase,ThrottleBase=p.ThrottleBase,
        Abs=p.Abs,Lock=p.Lock,Tc=p.Tc,Wheelspin=p.Wheelspin,Shift=p.Shift,Redline=p.Redline,
        RedlineStartRatio=p.RedlineStartRatio,BrakeResponse=p.DefaultParameters.BrakeResponse
    };
    public Profile Apply(Profile p) => p with {
        BrakeMode=BrakeMode,ThrottleMode=ThrottleMode,BrakeBase=BrakeBase,ThrottleBase=ThrottleBase,
        Abs=Abs,Lock=Lock,Tc=Tc,Wheelspin=Wheelspin,Shift=Shift,Redline=Redline,RedlineStartRatio=RedlineStartRatio,
        DefaultParameters=p.DefaultParameters with{BrakeResponse=BrakeResponse}
    };
    public void Validate()
    {
        if(BrakeResponse is null)throw new InvalidDataException("扳机制动参数为空");
        // Validate through the existing v1.3 contract without recursively validating the collection.
        Apply(new Profile()).Validate();
    }
}

public sealed record VehicleTriggerSettings(string VehicleModel,string VehicleClass,TriggerSettings Settings)
{
    public static bool HasIdentity(TelemetryFrame? f) => f is {Connected:true} &&
        !string.IsNullOrWhiteSpace(f.VehicleModel) && !string.IsNullOrWhiteSpace(f.VehicleClass);
    public bool Matches(string model,string category) =>
        string.Equals(VehicleModel.Trim(),model.Trim(),StringComparison.OrdinalIgnoreCase) &&
        string.Equals(VehicleClass.Trim(),category.Trim(),StringComparison.OrdinalIgnoreCase);
    public bool Matches(TelemetryFrame f) => HasIdentity(f) && Matches(f.VehicleModel,f.VehicleClass);
    public void Validate()
    {
        if(string.IsNullOrWhiteSpace(VehicleModel)||string.IsNullOrWhiteSpace(VehicleClass)||Settings is null)
            throw new InvalidDataException("车辆扳机配置缺少车型、类别或设置");
        Settings.Validate();
    }
}

public static class TriggerProfiles
{
    public static TriggerSettings Common(Profile p) => p.TriggerDefaults ?? TriggerSettings.From(p);
    public static VehicleTriggerSettings? Find(Profile p,TelemetryFrame? f) =>
        VehicleTriggerSettings.HasIdentity(f)?p.VehicleTriggers.FirstOrDefault(v=>v.Matches(f!)):null;
    public static Profile Resolve(Profile p,TelemetryFrame? f) =>
        (Find(p,f)?.Settings ?? p.TriggerDefaults)?.Apply(p) ?? p;
    public static Profile SaveVehicle(Profile p,string model,string category,TriggerSettings settings)
    {
        var entry=new VehicleTriggerSettings(model.Trim(),category.Trim(),settings);entry.Validate();
        return p with{VehicleTriggers=p.VehicleTriggers.Where(v=>!v.Matches(model,category)).Append(entry).ToImmutableArray()};
    }
}
