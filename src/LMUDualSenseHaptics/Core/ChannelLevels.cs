namespace LMUDualSenseHaptics.Core;
// Normalized requests after feature gain/priority/ducking, before nonlinear
// mixing and USB quantization. They are not independently measured motor output.
public readonly record struct ChannelLevels(double BrakeBase,double ThrottleBase,double Abs,double Lock,
    double Tc,double Wheelspin,double Shift,double Redline,double Road,double GripSlip,double Engine,double Impact,double Kerb=0);
