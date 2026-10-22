using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Serialization;
using LMUDualSenseHaptics.Hardware;
namespace LMUDualSenseHaptics.Core;
public sealed record WheelFrame(float OmegaRadPerSec, float SuspDeflectionM, float SuspForceN,
    float TireLoadN, float WheelYLocationM, float TireVerticalDeflectionM,
    float GroundLongitudinalMps, float GroundLateralMps, float? TireDirty, bool Valid,
    byte? SurfaceType = null, string TerrainName = "", float? BrakePressure01 = null, float? SlidingFraction = null)
{
    private static readonly string[] KerbPrefixes=["RMBL","RUMBLE","KERB","CURB"];
    // Public LMU ABI: surface 5 is rumblestrip. Unknown surfaces remain unknown.
    // A small explicit material-prefix allow-list covers tracks that label kerbs
    // as ordinary dry surfaces or omit the numeric type. No assumption that every off-track surface is a kerb.
    [JsonIgnore]
    public bool IsKerb => SurfaceType == 5 ||
        (SurfaceType is null or 0) && KerbPrefixes.Any(prefix =>
            (TerrainName ?? "").TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
public sealed record TelemetryFrame(long Sequence, double CapturedAtSec, double SourceAgeSec,
    bool Connected, bool Driving, bool Paused, string CarId, string VehicleClass,
    float SpeedMps, float LocalVxMps, float LocalVyMps, float LocalVzMps,
    float AccelX, float AccelY, float AccelZ, float Brake01, float Throttle01, float Steering,
    int Gear, float Rpm, float MaxRpm, bool AbsIntervening, bool TcIntervening,
    bool? AbsAvailable, bool? TcAvailable, ImmutableArray<WheelFrame> Wheels,
    double SourceTimeSec = 0, int GameVersion = 0, float LastImpactTime = 0, float LastImpactMagnitude = 0, string VehicleModel = "", byte? AbsSetting = null)
{
    public Vector3 Velocity => new(LocalVxMps, LocalVyMps, LocalVzMps);
    public Vector3 Acceleration => new(AccelX, AccelY, AccelZ);
    public void Validate()
    {
        if (!double.IsFinite(CapturedAtSec) || !double.IsFinite(SourceTimeSec) || !double.IsFinite(SourceAgeSec) || SourceAgeSec < 0)
            throw new InvalidDataException("遥测时钟无效");
        float[] values = [SpeedMps, LocalVxMps, LocalVyMps, LocalVzMps, AccelX, AccelY, AccelZ, Brake01, Throttle01, Steering, Rpm, MaxRpm, LastImpactTime, LastImpactMagnitude];
        if (values.Any(v => !float.IsFinite(v)) || SpeedMps < 0 || Brake01 is < 0 or > 1 || Throttle01 is < 0 or > 1 || Math.Abs(Steering) > 1 || Gear is < -1 or > 20 || Rpm < 0 || MaxRpm < 0)
            throw new InvalidDataException("遥测物理字段非有限值或超界");
        if (Wheels.Length != 4) throw new InvalidDataException("需要 FL/FR/RL/RR 四轮");
        foreach (var q in Wheels)
            if (!q.Valid || q.TireLoadN < 0 || new[] {q.OmegaRadPerSec,q.SuspDeflectionM,q.SuspForceN,q.TireLoadN,q.WheelYLocationM,q.TireVerticalDeflectionM,q.GroundLongitudinalMps,q.GroundLateralMps}.Any(v=>!float.IsFinite(v)) || (q.TireDirty is float dirty && !float.IsFinite(dirty)) ||
                (q.BrakePressure01 is float pressure && (!float.IsFinite(pressure) || pressure is < 0 or > 1)) ||
                (q.SlidingFraction is float fraction && (!float.IsFinite(fraction) || fraction is < 0 or > 1)))
                throw new InvalidDataException("四轮数据无效");
    }
}
public readonly record struct FeedbackFrame(byte LeftActuator, byte RightActuator, TriggerEffect LeftTrigger, TriggerEffect RightTrigger, bool UseAudioHaptics = false)
{
    public static FeedbackFrame Off => new(0,0,TriggerEffect.Off,TriggerEffect.Off);
}
public sealed record Feature(bool Enabled, int Strength)
{
    public double Gain => Enabled ? Strength / 100.0 : 0;
}
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BrakeFeedbackMode { Auto, Abs, NoAbs }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThrottleFeedbackMode { TcOnly, Legacy }

public sealed record Profile
{
    public BrakeFeedbackMode BrakeMode { get; init; } = BrakeFeedbackMode.Auto;
    public AudioHapticsSettings AudioHaptics { get; init; } = new();
    // Missing members in v0.3 JSON receive these defaults without discarding L2 gains.
    public ThrottleFeedbackMode ThrottleMode { get; init; } = ThrottleFeedbackMode.TcOnly;
    public string UiTheme { get; init; } = "Light";
    public int RoadSensitivity { get; init; } = 100;
    public int HidTargetHz { get; init; } = 250;
    public int TelemetryStaleMs { get; init; } = 250;
    public Feature BrakeBase { get; init; } = new(true,50);
    public Feature ThrottleBase { get; init; } = new(false,50);
    public Feature Abs { get; init; } = new(true,50);
    public Feature Lock { get; init; } = new(true,50);
    public Feature Tc { get; init; } = new(true,75);
    public Feature Wheelspin { get; init; } = new(true,50);
    public Feature Shift { get; init; } = new(true,45);
    public Feature Redline { get; init; } = new(true,35);
    public Feature Road { get; init; } = new(true,55);
    public Feature Kerb { get; init; } = new(true,75);
    public Feature GripSlip { get; init; } = new(true,50);
    public Feature Engine { get; init; } = new(true,15);
    public Feature Impact { get; init; } = new(true,60);
    public int SpatialSeparation { get; init; } = 60;
    public double RightGain { get; init; } = 1;
    public double RedlineStartRatio { get; init; } = .962;
    public FeedbackParameters DefaultParameters { get; init; } = new();
    public ImmutableArray<VehiclePreset> VehiclePresets { get; init; } = [];
    public void Validate()
    {
        if (AudioHaptics is null) throw new InvalidDataException("缺少音圈配置");
        AudioHaptics.Validate();
        if (!Enum.IsDefined(BrakeMode) || !Enum.IsDefined(ThrottleMode) || UiTheme is not ("Light" or "Dark") || RoadSensitivity is < 25 or > 200)
            throw new InvalidDataException("油门模式、界面主题或路面灵敏度非法");
        if (HidTargetHz != 250 || TelemetryStaleMs is < 50 or > 1000 || SpatialSeparation is < 0 or > 100 || !double.IsFinite(RightGain) || RightGain is <= 0 or > 2 || !double.IsFinite(RedlineStartRatio) || RedlineStartRatio is < .90 or >= .99)
            throw new InvalidDataException("配置数值超界");
        foreach(var f in new[]{BrakeBase,ThrottleBase,Abs,Lock,Tc,Wheelspin,Shift,Redline,Road,Kerb,GripSlip,Engine,Impact})
            if(f is null || f.Strength is < 0 or > 100) throw new InvalidDataException("功能强度须在 0..100");
        if(DefaultParameters is null || VehiclePresets.IsDefault)throw new InvalidDataException("缺少默认参数或车型预设列表");
        DefaultParameters.Validate();
        var identities=new HashSet<(int,string,string)>();
        foreach(var preset in VehiclePresets)
        {
            if(preset is null)throw new InvalidDataException("车型预设为空");
            preset.Validate();
            if(!identities.Add((preset.GameVersion,preset.VehicleModel,preset.VehicleClass)))throw new InvalidDataException("车型预设身份重复");
        }
    }
}
