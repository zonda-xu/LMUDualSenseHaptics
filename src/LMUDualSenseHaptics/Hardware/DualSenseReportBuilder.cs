// USB branch adapted from ACC-DualSense-Feedback (MIT); see THIRD_PARTY_NOTICES.md.
using LMUDualSenseHaptics.Core;
namespace LMUDualSenseHaptics.Hardware;
public static class DualSenseReportBuilder
{
    public const int UsbReportLength = 48;
    public static int Build(in FeedbackFrame frame, Span<byte> destination)
    {
        if(destination.Length<48) throw new ArgumentException("USB report requires 48 bytes");
        var report=destination[..48]; report.Clear();
        report[0]=0x02;
        // Bit 1 selects classic rumble, bit 0 enables compatible vibration.
        // Both must be clear for audio haptics; retain L2/R2 valid bits 2 and 3.
        report[1]=frame.UseAudioHaptics?(byte)0x0C:(byte)0x0F;report[2]=0x55;
        report[3]=frame.UseAudioHaptics?(byte)0:frame.RightActuator;
        report[4]=frame.UseAudioHaptics?(byte)0:frame.LeftActuator;
        frame.RightTrigger.WriteTo(report.Slice(11,11));
        frame.LeftTrigger.WriteTo(report.Slice(22,11));
        // Also clear compatible-vibration-v2 (bit 2) in audio mode.
        report[39]=frame.UseAudioHaptics?(byte)0x02:(byte)0x06;report[42]=0x02;report[43]=0x02;
        return 48;
    }
}
