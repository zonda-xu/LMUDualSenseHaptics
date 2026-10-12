using LMUDualSenseHaptics.Core;

namespace LMUDualSenseHaptics.Physics;

public static class WheelContact
{
    // Vertical tyre compression is direct contact evidence when LMU reports
    // zero tyre load. Suspension force alone also exists in an airborne spring.
    public const double MinimumCompressionM = .001;
    public static bool IsGrounded(WheelFrame wheel, double minimumLoadN) =>
        wheel.Valid && float.IsFinite(wheel.TireLoadN) && float.IsFinite(wheel.TireVerticalDeflectionM) &&
        wheel.TireLoadN >= 0 && (wheel.TireLoadN > minimumLoadN || wheel.TireVerticalDeflectionM > MinimumCompressionM);
    public static string Evidence(WheelFrame wheel, double minimumLoadN) =>
        !IsGrounded(wheel, minimumLoadN) ? "无接触证据" : wheel.TireLoadN > minimumLoadN ? "轮胎载荷" : "轮胎压缩量";
}
