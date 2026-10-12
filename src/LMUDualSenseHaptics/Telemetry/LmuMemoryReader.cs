using System.Diagnostics;
using System.Runtime.InteropServices;
namespace LMUDualSenseHaptics.Telemetry;
// The two int32 cells are waiters,busy. No Rust implementation is copied.
internal sealed unsafe class LmuMemoryReader:IDisposable
{
    private IntPtr dataMap,data,lockMap,lockData,lockEvent,updateEvent;
    private Process? process;
    public void Open()
    {
        try
        {
            process=Process.GetProcessesByName("Le Mans Ultimate").FirstOrDefault()??throw new IOException("等待 LMU 进程");
            dataMap=Check(OpenFileMapping(4,false,"LMU_Data"));data=Check(MapViewOfFile(dataMap,4,0,0,(nuint)LmuLayout.SnapshotSize));
            // Open existing official objects only: never synthesize another lock namespace.
            lockMap=Check(OpenFileMapping(0xF001F,false,"LMU_SharedMemoryLockData"));lockData=Check(MapViewOfFile(lockMap,0xF001F,0,0,8));
            lockEvent=Check(OpenEvent(0x100002,false,"LMU_SharedMemoryLockEvent"));updateEvent=Check(OpenEvent(0x100000,false,"LMU_Data_Event"));
        }
        catch{Dispose();throw;}
    }
    public bool IsAlive=>process is not null&&!process.HasExited;
    public void WaitForUpdate(){uint r=WaitForSingleObject(updateEvent,5);if(r is not (0 or 258))throw new IOException("LMU 更新事件等待失败");}
    public void CopySnapshot(byte[] target)
    {
        if(target.Length!=LmuLayout.SnapshotSize)throw new ArgumentException("快照长度不匹配");
        ref int waiters=ref *(int*)lockData;ref int busy=ref *((int*)lockData+1);
        if(Volatile.Read(ref waiters)<0 || Volatile.Read(ref busy) is <0 or >1)throw new InvalidDataException("共享锁布局状态非法");
        bool acquired=false,registered=false;
        try
        {
            for(int i=0;i<4000;i++){if(Interlocked.CompareExchange(ref busy,1,0)==0){acquired=true;break;}Thread.SpinWait(1);}
            if(!acquired)
            {
                Interlocked.Increment(ref waiters);registered=true;double end=Clock.Now+.025;
                while(!acquired)
                {if(Interlocked.CompareExchange(ref busy,1,0)==0){acquired=true;break;}if(Clock.Now>=end)throw new TimeoutException("LMU 共享锁超时");uint result=WaitForSingleObject(lockEvent,1);if(result is not (0 or 258))throw new IOException("LMU 锁等待失败");}
            }
            Marshal.Copy(data,target,0,target.Length);
        }
        finally
        {
            if(registered)Interlocked.Decrement(ref waiters);
            if(acquired){Volatile.Write(ref busy,0);if(Volatile.Read(ref waiters)>0)SetEvent(lockEvent);}
        }
    }
    public void Dispose()
    {
        foreach(var p in new[]{data,lockData})if(p!=IntPtr.Zero)UnmapViewOfFile(p);
        foreach(var h in new[]{dataMap,lockMap,lockEvent,updateEvent})if(h!=IntPtr.Zero)CloseHandle(h);
        data=dataMap=lockData=lockMap=lockEvent=updateEvent=IntPtr.Zero;process?.Dispose();process=null;
    }
    private static IntPtr Check(IntPtr p)=>p!=IntPtr.Zero?p:throw new IOException("LMU 共享对象打开失败，Win32="+Marshal.GetLastWin32Error());
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr OpenFileMapping(uint access,bool inherit,string name);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern IntPtr MapViewOfFile(IntPtr map,uint access,uint high,uint low,nuint length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr OpenEvent(uint access,bool inherit,string name);
    [DllImport("kernel32.dll")]private static extern uint WaitForSingleObject(IntPtr h,uint ms);
    [DllImport("kernel32.dll")]private static extern bool SetEvent(IntPtr h);
    [DllImport("kernel32.dll")]private static extern bool UnmapViewOfFile(IntPtr h);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr h);
}
internal static class Clock {public static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;}
