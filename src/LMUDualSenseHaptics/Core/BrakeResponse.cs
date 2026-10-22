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

    public void Validate()
    {
        double[] values = [WarningSlip, CriticalSlip, LockSlip, PredictionSeconds, AttackSeconds, ReleaseSeconds];
        if (values.Any(v => !double.IsFinite(v)) || WarningSlip < .02 || CriticalSlip <= WarningSlip ||
            LockSlip <= CriticalSlip || LockSlip > .8 || PredictionSeconds is < 0 or > .08 ||
            AttackSeconds is < .004 or > .03 || ReleaseSeconds is < .01 or > .15)
            throw new InvalidDataException("刹车预警参数无效：预警、临界、抱死阈值须依次递增。");
    }
}
