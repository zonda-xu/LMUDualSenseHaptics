using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using LMUDualSenseHaptics.Core;
namespace LMUDualSenseHaptics.Telemetry;
// Independently authored parser of public ABI facts; see docs/FIELDS.md and REFERENCES.md.
public sealed class LmuLayout
{
    public const int SnapshotSize=324820,VehicleSize=1888,WheelSize=260,TelemetryOffset=128464,ScoringOffset=1632;
    private long sequence;private double previousTime=double.NaN;private string previousCar="";private bool? previousDriving,previousPaused;
    public int Version {get;private set;}
    public TelemetryFrame? Parse(byte[] b,double now)
    {
        if(b.Length!=SnapshotSize)throw new InvalidDataException("共享内存结构长度不匹配");
        Version=I(b,64);if(Version<=0)throw new InvalidDataException("LMU gameVersion 无效");
        int count=b[TelemetryOffset],index=b[TelemetryOffset+1];
        if(count>104)throw new InvalidDataException("车辆数超出布局");
        if(!B(b,TelemetryOffset+2)){previousTime=double.NaN;previousCar="";previousDriving=previousPaused=null;return null;}
        if(index>=count)throw new InvalidDataException("玩家遥测索引无效");
        int v=TelemetryOffset+4+index*VehicleSize,slot=I(b,v),scount=I(b,ScoringOffset+104);
        if(scount is < 0 or >104)throw new InvalidDataException("Scoring 车辆数非法");
        int scoring=-1;
        for(int i=0;i<scount;i++){int q=ScoringOffset+560+i*584;if(I(b,q)==slot&&B(b,q+196)){if(scoring>=0)throw new InvalidDataException("多个玩家车辆记录");scoring=q;}}
        if(scoring<0)throw new InvalidDataException("无法在 scoring 中核对玩家 slot ID");
        bool paused=b[ScoringOffset+108]==9;
        // LMU 14200 may keep mOptionsLocation at 0 during a realtime session.
        // Use the matched player's scoring state; the UI location is not a driving gate.
        bool inRealtime=B(b,ScoringOffset+115);
        bool playerControlled=(sbyte)b[scoring+197]==0;
        bool inGarageStall=B(b,scoring+507);
        bool driving=inRealtime&&playerControlled&&!inGarageStall&&!paused;
        double source=D(b,v+12);string model=S(b,v+796,30);if(model.Length==0)model=S(b,v+32,64);
        string vehicleClass=S(b,scoring+200,32);
        string car=slot+":"+model+":"+Version+":"+vehicleClass;
        // mElapsedTime is physics freshness; SME_UPDATE_TELEMETRY is not assumed to be a counter.
        if(source==previousTime && car==previousCar && driving==previousDriving && paused==previousPaused)return null;
        if(source==previousTime && car==previousCar && driving)driving=false; // status changes never manufacture fresh physics
        var wheels=ImmutableArray.CreateBuilder<WheelFrame>(4);
        for(int i=0;i<4;i++)
        {int q=v+848+i*WheelSize;wheels.Add(new(F(b,q+40),F(b,q),F(b,q+16),F(b,q+104),F(b,q+188),F(b,q+180),F(b,q+72),F(b,q+64),null,true,b[q+176]<=6?b[q+176]:null,S(b,q+160,16),Unit(b,q+32),Unit(b,q+112),Optional(b,q+56),Optional(b,q+96),Optional(b,q+88),Positive(b,q+120),Positive(b,q+136),Unit(b,q+152),b[q+240]));}
        float vx=F(b,v+184),vy=F(b,v+192),vz=F(b,v+200);
        var f=new TelemetryFrame(++sequence,now,0,true,driving,paused,car,vehicleClass,
            (float)Math.Sqrt((double)vx*vx+(double)vy*vy+(double)vz*vz),vx,vy,vz,F(b,v+208),F(b,v+216),F(b,v+224),
            F(b,v+396),F(b,v+388),F(b,v+404),I(b,v+352),F(b,v+356),F(b,v+532),B(b,v+746),B(b,v+747),
            b[v+757]>0,b[v+751]>0,wheels.MoveToImmutable(),source,Version,F(b,v+552),F(b,v+560),model,b[v+756],Unit(b,v+428),Unit(b,v+664),b[v+760],Optional(b,v+712),b[v+744]<=3?b[v+744]:null);
        f.Validate();previousTime=source;previousCar=car;previousDriving=driving;previousPaused=paused;return f;
    }
    // Optional model inputs must not take down the entire haptics stream.
    private static float? Optional(byte[] b,int o)
    {double x=BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(o,8)));return double.IsFinite(x)&&Math.Abs(x)<=float.MaxValue?(float)x:null;}
    private static float? Unit(byte[] b,int o) => Optional(b,o) is float x && x is >=0 and <=1 ? x : null;
    private static float? Positive(byte[] b,int o) => Optional(b,o) is float x && x>0 ? x : null;
    private static int I(byte[] b,int o)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o,4));
    private static double D(byte[] b,int o){double x=BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(o,8)));if(!double.IsFinite(x))throw new InvalidDataException($"非有限字段 @{o}");return x;}
    private static float F(byte[] b,int o){double d=D(b,o);if(d>float.MaxValue||d<-float.MaxValue)throw new InvalidDataException("字段 float 溢出");return (float)d;}
    private static bool B(byte[] b,int o)=>b[o] switch{0=>false,1=>true,_=>throw new InvalidDataException($"bool 非法 @{o}")};
    private static string S(byte[] b,int o,int n){var bytes=b.AsSpan(o,n);int end=bytes.IndexOf((byte)0);return Encoding.UTF8.GetString(end<0?bytes:bytes[..end]);}
}
