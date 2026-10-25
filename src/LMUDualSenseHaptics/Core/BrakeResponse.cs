namespace LMUDualSenseHaptics.Core;

// Preview starting points, not measured tyre peak-friction slip ratios.
public sealed record BrakeResponse
{
    public double WarningSlip { get; init; } = .06;
    public double CriticalSlip { get; init; } = .16;
    public double LockSlip { get; init; } = .25;
    public double PredictionSeconds { get; init; } = .04;
    public double AttackSeconds { get; init; } = .008;
    public double ReleaseSeconds { get; init; } = .040;
    // Minimum cue tail after the last supported threshold sample; lift and
    // invalid contact always cancel it. Uses source time, never output ticks.
    public double CueHoldSeconds { get; init; } = .160;

    // Engineering prior, not a measured optimal brake input. Learning only uses
    // independently observed slip onset, never an uneventful pedal application.
    public double InitialLimit { get; init; } = .78;
    public double UncertaintyMargin { get; init; } = .12;
    public double DemandLeadSeconds { get; init; } = .06;
    public double LateralReduction { get; init; } = .25;
    public double MinimumPrompt { get; init; } = .20;
    // Game B = clamp((travel - deadzone)/(end - deadzone)) ^ exponent.
    public double TravelDeadzone { get; init; } = 0;
    public double TravelEnd { get; init; } = 1;
    public double InputExponent { get; init; } = 1;
    public double TravelFor(double brake) => TravelDeadzone + (TravelEnd-TravelDeadzone)*Math.Pow(Math.Clamp(brake,0,1),1/InputExponent);

    public void Validate()
    {
        double[] values = [WarningSlip, CriticalSlip, LockSlip, PredictionSeconds, AttackSeconds, ReleaseSeconds,CueHoldSeconds,InitialLimit,UncertaintyMargin,DemandLeadSeconds,LateralReduction,MinimumPrompt,TravelDeadzone,TravelEnd,InputExponent];
        if (values.Any(v => !double.IsFinite(v)) || WarningSlip < .02 || CriticalSlip <= WarningSlip ||
            LockSlip <= CriticalSlip || LockSlip > .8 || PredictionSeconds is < 0 or > .08 ||
            AttackSeconds is < .004 or > .03 || ReleaseSeconds is < .01 or > .15 || CueHoldSeconds is < .08 or > .3 || InitialLimit is < .4 or > .95 ||
            UncertaintyMargin is < .03 or > .25 || DemandLeadSeconds is < 0 or > .15 ||
            LateralReduction is < 0 or > .5 || MinimumPrompt is < .1 or > .4 ||
            TravelDeadzone is < 0 or > .3 || TravelEnd is < .5 or > 1 ||
            InputExponent is < .25 or > 4)
            throw new InvalidDataException("刹车预警参数无效：预警、临界、抱死阈值须依次递增。");
    }
}
