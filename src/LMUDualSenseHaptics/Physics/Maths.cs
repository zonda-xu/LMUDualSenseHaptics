namespace LMUDualSenseHaptics.Physics;
public static class Maths
{
    public static double C(double x) => double.IsFinite(x)?Math.Clamp(x,0,1):throw new InvalidDataException("Nonfinite strength");
    public static double Smooth(double a,double b,double x) { if(b<=a||!double.IsFinite(x))throw new ArgumentException("Invalid smoothstep"); var u=C((x-a)/(b-a));return u*u*(3-2*u); }
    public static double Ema(double prev,double x,double dt,double tau) { if(dt<=0||tau<=0||!double.IsFinite(dt)||!double.IsFinite(x)||!double.IsFinite(prev))throw new ArgumentException("Invalid EMA");return prev+(1-Math.Exp(-dt/tau))*(x-prev); }
    public static double Env(double prev,double target,double dt,double attack,double release)=>Ema(prev,C(target),dt,target>prev?attack:release);
    public static double Soft(double a,double b)=>1-(1-C(a))*(1-C(b));
    public static double Pulse(double now,double hz,double floor,double sharp)=>floor+(1-floor)*Math.Pow(.5+.5*Math.Sin(2*Math.PI*hz*now),sharp);
    public static byte Byte(double x)=>(byte)Math.Round(C(x)*255);
}
