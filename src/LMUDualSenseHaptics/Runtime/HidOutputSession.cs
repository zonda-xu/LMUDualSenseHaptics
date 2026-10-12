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
    public string Label=>device?.Label??"未连接 USB DualSense";
    public string LastError {get;private set;}="";
    public void EnsureConnected(double now)
    {
        if(device is not null||now<retry)return;
        device=open();if(device is not null)Generation++;retry=now+1;
    }
    public bool TryWrite(in FeedbackFrame frame,double now)
    {
        if(device is null)return false;
        if(device.TryWrite(frame)){LastError="";return true;}
        LastError="USB HID 完整报文写入失败，Win32="+device.LastWriteError;
        device.Dispose();device=null;retry=now+1;
        return false;
    }
    // OFF is a valid report, never a reason to close a connected device.
    public void Dispose(){device?.Dispose();device=null;}
}
