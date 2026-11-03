using LMUDualSenseHaptics.Core;
namespace LMUDualSenseHaptics.Runtime;

// Used only by the sole output thread. The interface permits lifecycle tests
// without invoking Windows HID on a test machine.
internal interface IHidOutputDevice:IDisposable
{
    string Label {get;}
    Guid ContainerId => Guid.Empty;
    int LastWriteError {get;}
    bool TryWrite(in FeedbackFrame frame);
}
internal sealed class HidOutputSession(Func<IHidOutputDevice?> open):IDisposable
{
    private IHidOutputDevice? device;
    private double retry;
    public bool Connected=>device is not null;
    public Guid ContainerId=>device?.ContainerId??Guid.Empty;
    public long Generation {get;private set;}
    public UiText Label=>device?.Label??L.M("Text_0BAC633C43");
    public UiText LastError {get;private set;}="";
    public void EnsureConnected(double now)
    {
        if(device is not null||now<retry)return;
        device=open();if(device is not null)Generation++;retry=now+1;
    }
    public bool TryWrite(in FeedbackFrame frame,double now)
    {
        if(device is null)return false;
        if(device.TryWrite(frame)){LastError="";return true;}
        LastError=L.M("Text_C26C1DD8A6", device.LastWriteError);
        device.Dispose();device=null;retry=now+1;
        return false;
    }
    // OFF is a valid report, never a reason to close a connected device.
    public void Dispose(){device?.Dispose();device=null;}
}
