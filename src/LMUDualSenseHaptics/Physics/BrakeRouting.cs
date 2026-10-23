using LMUDualSenseHaptics.Core;

namespace LMUDualSenseHaptics.Physics;

public static class BrakeRouting
{
    // Re-evaluate each frame. Class is authoritative; model is used only when
    // scoring has not supplied a class. Never latch the first ABS observation.
    public static bool IsGt3(TelemetryFrame f)
    {
        string value = string.IsNullOrWhiteSpace(f.VehicleClass) ? f.VehicleModel : f.VehicleClass;
        return value.Contains("GT3", StringComparison.OrdinalIgnoreCase);
    }
    public static bool Legacy(TelemetryFrame f, Profile p) => p.BrakeMode == BrakeFeedbackMode.Auto && IsGt3(f);
}
