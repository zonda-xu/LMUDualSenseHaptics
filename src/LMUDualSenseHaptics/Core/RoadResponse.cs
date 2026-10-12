namespace LMUDualSenseHaptics.Core;

// Engineering starting values, not measured per-car tuning or SimHub source code.
public sealed record RoadResponse
{
    public double SuspensionNoiseMps {get;init;}=.012;
    public double SuspensionFullMps {get;init;}=.16;
    public double TireNoiseMps {get;init;}=.004;
    public double TireFullMps {get;init;}=.065;
    public double LoadNoisePerSec {get;init;}=.5;
    public double LoadFullPerSec {get;init;}=8;
    public double BumpNoiseMps {get;init;}=.12;
    public double BumpFullMps {get;init;}=.50;
    public double BumpLoadNoisePerSec {get;init;}=2.5;
    public double BumpLoadFullPerSec {get;init;}=15;
    public double ContactMinLoadN {get;init;}=100;
    public double MinimumOutput {get;init;}=.12;
    public double ResponseGamma {get;init;}=.70;
    public double KerbBaseLevel {get;init;}=.65;
    public double KerbSpacingM {get;init;}=.45;
    public double AttackSeconds {get;init;}=.006;
    public double ReleaseSeconds {get;init;}=.090;

    public void Validate()
    {
        double[] values=[SuspensionNoiseMps,SuspensionFullMps,TireNoiseMps,TireFullMps,
            LoadNoisePerSec,LoadFullPerSec,BumpNoiseMps,BumpFullMps,BumpLoadNoisePerSec,BumpLoadFullPerSec,ContactMinLoadN,MinimumOutput,ResponseGamma,
            KerbBaseLevel,KerbSpacingM,AttackSeconds,ReleaseSeconds];
        if(values.Any(v=>!double.IsFinite(v)||v<0)||
            SuspensionNoiseMps<=0||SuspensionFullMps<=SuspensionNoiseMps||
            TireNoiseMps<=0||TireFullMps<=TireNoiseMps||
            LoadNoisePerSec<=0||LoadFullPerSec<=LoadNoisePerSec||
            BumpNoiseMps<=0||BumpFullMps<=BumpNoiseMps||
            BumpLoadNoisePerSec<=0||BumpLoadFullPerSec<=BumpLoadNoisePerSec||
            ContactMinLoadN<=0||MinimumOutput>.4||ResponseGamma is <.3 or >2||
            KerbBaseLevel is <=0 or >1||KerbSpacingM is <.1 or >2||
            AttackSeconds is <.002 or >.05||ReleaseSeconds is <.02 or >.25)
            throw new InvalidDataException("路面响应参数超界或阈值顺序错误");
    }
}
