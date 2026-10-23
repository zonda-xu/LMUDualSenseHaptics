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
        if(!WheelContact.IsGrounded(w,minimumLoad)||Math.Abs(w.GroundLongitudinalMps)<5||
            w.LongitudinalPatchMps is not float patch||!float.IsFinite(patch))return false;
        double ground=w.GroundLongitudinalMps,omega=w.OmegaRadPerSec;
        double ratio=patch/ground;
        if(ratio is < -2 or > 1.05)return false;
        if(Math.Abs(omega)>.5)
        {
            double radius=(ground-patch)/omega;
            if(radius is < .15 or > .60)return false;
        }
        else if(Math.Abs(1-ratio)>.05)return false;
        slip=Math.Clamp(ratio,0,1);
        return true;
    }
}
