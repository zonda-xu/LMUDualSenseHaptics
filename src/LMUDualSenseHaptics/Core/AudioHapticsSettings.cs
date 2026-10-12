namespace LMUDualSenseHaptics.Core;

// Starting values for USB DualSense voice coils, not measured hardware calibration.
public sealed record AudioHapticsSettings
{
    public bool Enabled { get; init; } = true;
    public string EndpointId { get; init; } = ""; // Empty: match the active HID's container ID.
    public int MasterStrength { get; init; } = 75;
    public int RoadFrequencyHz { get; init; } = 75;
    public int KerbFrequencyHz { get; init; } = 110;
    public int BumpFrequencyHz { get; init; } = 45;
    public bool SwapSides { get; init; }
    public bool DynamicKerbFallback { get; init; } = true;
    public double Gain => MasterStrength / 100.0;

    public void Validate()
    {
        if (EndpointId is null || EndpointId.Length > 2048 || MasterStrength is < 0 or > 100 ||
            RoadFrequencyHz is < 25 or > 180 || KerbFrequencyHz is < 40 or > 220 ||
            BumpFrequencyHz is < 20 or > 100)
            throw new InvalidDataException("USB 音圈参数超界");
    }
}
