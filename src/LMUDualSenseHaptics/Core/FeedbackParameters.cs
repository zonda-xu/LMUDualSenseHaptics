namespace LMUDualSenseHaptics.Core;

// Explicit starting values, not a claim of measured vehicle calibration.
public sealed record FeedbackParameters
{
    public string Name {get;init;}="LMU 初始预设 v0.5.0";
    public RoadResponse RoadResponse {get;init;}=new();
    public double V0 {get;init;}=.025;
    public double V1 {get;init;}=.30;
    public double F0 {get;init;}=.04;
    public double F1 {get;init;}=.35;
    public double L0 {get;init;}=.04;
    public double L1 {get;init;}=.35;
    public double Fmin {get;init;}=100;
    public double Lmin {get;init;}=100;
    public double ContactMinLoad {get;init;}=100;
    public double AngleOffset0 {get;init;}=.06;
    public double AngleOffset1 {get;init;}=.18;
    public double RatioThreshold0 {get;init;}=.08;
    public double RatioThreshold1 {get;init;}=.25;
    public double LoadGate0 {get;init;}=100;
    public double LoadGate1 {get;init;}=600;
    public double LateralWeight {get;init;}=.65;
    public double JerkThreshold {get;init;}=150;
    public double DeltaVThreshold {get;init;}=.75;
    public double LockRatio {get;init;}=.30;
    public double LockFullRatio {get;init;}=.60;
    public double SpinRatio {get;init;}=.10;
    public double SpinFullRatio {get;init;}=.30;
    public double InputThreshold {get;init;}=.02;
    public double LearningMaxThrottle {get;init;}=.60;
    public double LearningMaxLongAccel {get;init;}=2.5;
    public double LearningMaxLatAccel {get;init;}=1.5;
    public double RadiusTrustedSeconds {get;init;}=1;
    public double AxleRadiusTolerance {get;init;}=.04;
    public double RadiusDriftTolerance {get;init;}=.04;
    public double RoadLearningSeconds {get;init;}=5;
    public double NoiseMultiplier {get;init;}=3;
    public void Validate()
    {
        if(RoadResponse is null)throw new InvalidDataException("缺少路面响应参数");
        RoadResponse.Validate();
        double[] values=[V0,V1,F0,F1,L0,L1,Fmin,Lmin,ContactMinLoad,AngleOffset0,AngleOffset1,
            RatioThreshold0,RatioThreshold1,LoadGate0,LoadGate1,LateralWeight,JerkThreshold,DeltaVThreshold,
            LockRatio,LockFullRatio,SpinRatio,SpinFullRatio,InputThreshold,LearningMaxThrottle,
            LearningMaxLongAccel,LearningMaxLatAccel,RadiusTrustedSeconds,AxleRadiusTolerance,
            RadiusDriftTolerance,RoadLearningSeconds,NoiseMultiplier];
        if(string.IsNullOrWhiteSpace(Name)||values.Any(x=>!double.IsFinite(x)||x<0)||
            V0<=0||V1<=V0||F0<=0||F1<=F0||L0<=0||L1<=L0||Fmin<=0||Lmin<=0||ContactMinLoad<=0||
            AngleOffset1<=AngleOffset0||RatioThreshold1<=RatioThreshold0||LoadGate1<=LoadGate0||
            LateralWeight>1||JerkThreshold<=0||DeltaVThreshold<=0||LockRatio<=0||LockFullRatio<=LockRatio||
            SpinRatio<=0||SpinFullRatio<=SpinRatio||InputThreshold>=1||LearningMaxThrottle>1||
            LearningMaxLongAccel<=0||LearningMaxLatAccel<=0||RadiusTrustedSeconds<.25||
            AxleRadiusTolerance is <=0 or >.15||RadiusDriftTolerance is <=0 or >.15||
            RoadLearningSeconds<1||NoiseMultiplier is <1 or >6)
            throw new InvalidDataException("反馈参数非法：检查有限值、单位与阈值顺序");
    }
}

// Verification here concerns this parameter set only. Telemetry validity and
// device writes remain runtime checks, independent of this optional evidence.
public sealed record VehiclePreset(int GameVersion,string VehicleModel,string VehicleClass,
    bool Verified,string EvidenceFile,FeedbackParameters Parameters)
{
    public bool Matches(TelemetryFrame f)=>Verified&&GameVersion==f.GameVersion&&
        VehicleModel==f.VehicleModel&&VehicleClass==f.VehicleClass;
    public void Validate()
    {
        if(GameVersion<=0||string.IsNullOrWhiteSpace(VehicleModel)||string.IsNullOrWhiteSpace(VehicleClass)||
            Parameters is null||(Verified&&string.IsNullOrWhiteSpace(EvidenceFile)))
            throw new InvalidDataException("车型预设缺少身份、参数或验证证据");
        Parameters.Validate();
    }
}
