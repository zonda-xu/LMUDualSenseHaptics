using System.Windows;
namespace LMUDualSenseHaptics;
public partial class App:Application
{
    private static Action? emergencyStop;
    internal static void RegisterEmergencyStop(Action action)=>Volatile.Write(ref emergencyStop,action);
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException+=(_,args)=>{args.Handled=true;if(MainWindow is MainWindow window)window.FatalShutdown("应用异常："+args.Exception.Message);else Shutdown(1);};
        AppDomain.CurrentDomain.ProcessExit+=(_,_)=>{Volatile.Read(ref emergencyStop)?.Invoke();};
        AppDomain.CurrentDomain.UnhandledException+=(_,_)=>{Volatile.Read(ref emergencyStop)?.Invoke();};
        base.OnStartup(e);
    }
}
