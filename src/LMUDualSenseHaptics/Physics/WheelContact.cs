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
    public static UiText Evidence(WheelFrame wheel, double minimumLoadN) =>
        !IsGrounded(wheel, minimumLoadN) ? L.M("Text_C7C6F8A23E") : wheel.TireLoadN > minimumLoadN ? L.M("Text_959494EC5B") : L.M("Text_2DF868A4C6");
}
