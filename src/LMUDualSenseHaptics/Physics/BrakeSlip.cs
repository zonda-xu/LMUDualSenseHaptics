using LMUDualSenseHaptics.Core;

namespace LMUDualSenseHaptics.Physics;

public static class BrakeSlip
{
    // LMU signed wheel-local velocities: patch/ground is positive
    // under braking, near zero in rolling, and approaches one on a locked wheel.
    // Check the rolling identity ground - patch = omega * effective radius.
    // No assumed radius or waiting for global straight-line calibration.
    public static bool TryRead(WheelFrame w,double minimumLoad,out double slip)
    {
        slip=0;
        if(!WheelContact.IsGrounded(w,minimumLoad)||!float.IsFinite(w.GroundLongitudinalMps)||
            !float.IsFinite(w.OmegaRadPerSec)||Math.Abs(w.GroundLongitudinalMps)<5||
            w.LongitudinalPatchMps is not float patch||!float.IsFinite(patch))return false;
        double ground=w.GroundLongitudinalMps,omega=w.OmegaRadPerSec;
        double ratio=patch/ground;
        if(ratio is < -2 or > 1.05)return false;
        // Validate in velocity space: dividing two almost-zero quantities at
        // lock makes an otherwise consistent wheel fail the radius check.
        // The 0.5 m/s tolerance covers small residuals, not a percentage of speed.
        // A missing/zero patch on a stopped wheel still fails at driving speed.
        double low=Math.Min(.15*omega,.60*omega)-.5;
        double high=Math.Max(.15*omega,.60*omega)+.5;
        double rolling=ground-patch;
        if(rolling<low||rolling>high)return false;
        slip=Math.Clamp(ratio,0,1);
        return true;
    }
}
