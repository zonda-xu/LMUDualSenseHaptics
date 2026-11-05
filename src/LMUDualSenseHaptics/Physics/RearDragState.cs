using LMUDualSenseHaptics.Core;
using static LMUDualSenseHaptics.Physics.Maths;
namespace LMUDualSenseHaptics.Physics;

// Adapted from RearDragFeedbackState.cs, ACC-DualSense-Feedback, MIT.
// Vehicle lateral speed and yaw are independently required; no synthetic yaw.
public sealed class RearDragState
{
    public double Strength {get;private set;}
    public bool Active {get;private set;}
    public bool Recovery {get;private set;}
    private bool motionPrimed;
    private double target,evidence,previousBody,previousRear,previousYaw,previousSteer,confirmedAt;
    public void Reset(){Strength=target=evidence=previousBody=previousRear=previousYaw=previousSteer=confirmedAt=0;Active=Recovery=motionPrimed=false;}
    public void Update(TelemetryFrame f,WheelState[] wheels,double dt)
    {
        double forward=Math.Abs(f.LocalVzMps);
        if(f.LocalYawRate is not float yaw || !float.IsFinite(yaw)||forward<=2||f.Gear<0||f.SpeedMps<14/3.6) {Reset();return;}
        // Inspect fixed wheel arrays directly: avoid allocating a boxed immutable
        // array enumerator in the physics loop (ACC's update is allocation-free).
        for(int i=0;i<4;i++)
            if(!double.IsFinite(wheels[i].Alpha)||!WheelContact.IsGrounded(f.Wheels[i],50)){Reset();return;}
        double rl=Math.Abs(wheels[2].Alpha),rr=Math.Abs(wheels[3].Alpha);
        double front=(Math.Abs(wheels[0].Alpha)+Math.Abs(wheels[1].Alpha))*.5,rear=(rl+rr)*.5;
        double body=Math.Abs(f.LocalVxMps)/Math.Max(forward,4),steer=Math.Abs(f.Steering);
        double speed=Smooth(14,35,f.SpeedMps*3.6);
        // LMU exposes normalized steering input, not ACC's steering angle.
        double turn=Math.Max(Smooth(.035,.14,steer),Math.Max(Smooth(.06,.28,Math.Abs(yaw)),Smooth(.025,.08,body)));
        double qualification=Smooth(.12,.29,rear)*turn*speed*Math.Max(Smooth(.025,.075,rear-front),Math.Max(Smooth(.070,.17,body),Smooth(.20,.34,rear)));
        bool confirmed=qualification>.055&&(Math.Min(rl,rr)>=.075||(body>=.075&&rear>=.14));
        evidence=confirmed?evidence+dt:0;
        if(!Active&&evidence>=.012){Active=true;Recovery=false;confirmedAt=f.SourceTimeSec;}
        if(Active)
        {
            double bodyRate=(body-previousBody)/dt,rearRate=(rear-previousRear)/dt,steerRate=(steer-previousSteer)/dt;
            if(motionPrimed&&((bodyRate<-.12&&rearRate<0)||(yaw*previousYaw<0&&body>.045)||(steerRate< -2&&body>.045)))Recovery=true;
            if(motionPrimed&&bodyRate>1.5&&rearRate>2)Recovery=false;
            if(confirmed)confirmedAt=f.SourceTimeSec;
            double age=Math.Max(0,f.SourceTimeSec-confirmedAt);
            double rearStrength=Smooth(.10,.27,rear)*Math.Max(Smooth(.018,.065,rear-front),Math.Max(Smooth(.045,.12,body),Smooth(.18,.32,rear)))*turn*speed;
            double recovery=Smooth(.055,.17,rear)*Smooth(.025,.075,body)*.68*speed*(1-Smooth(.040,.180,age));
            target=Math.Max(rearStrength,recovery);
            if(!confirmed&&age>=.180){Active=Recovery=false;target=0;}
        }
        else target=0;
        Strength=Env(Strength,target,dt,.012,.100);
        if(Strength<.003&&target==0)Strength=0;
        previousBody=body;previousRear=rear;previousYaw=yaw;previousSteer=steer;motionPrimed=true;
    }
}
