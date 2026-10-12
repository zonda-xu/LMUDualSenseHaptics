using LMUDualSenseHaptics.Core;
using static LMUDualSenseHaptics.Physics.Maths;
namespace LMUDualSenseHaptics.Physics;

public sealed class RoadWheelState
{
    public double Texture, Bump, Kerb, KerbPhase, KerbHz;
    public double SuspensionVelocity, WheelVelocity, TireVelocity, LoadRate;
    public double InferredKerb { get; internal set; }
    public bool Contact { get; internal set; }
    public string ContactEvidence { get; internal set; } = "等待遥测";
    public string Source { get; internal set; } = "等待遥测";
    public double Combined => Math.Max(Math.Max(Texture, Bump), Kerb);
    internal double SlowSuspension, SlowWheel, SlowTire, SlowLoad;
    internal double Reversals, LastDirectionTime;
    internal int Direction;
}

// Independent road filters preserve the original L2/ABS/TC timing domain.
public sealed class RoadSignalEngine
{
    public RoadWheelState[] Wheels { get; private set; } = CreateWheels();
    public RoadResponse Response { get; private set; } = new();
    private TelemetryFrame? previous;
    private static RoadWheelState[] CreateWheels() => Enumerable.Range(0, 4).Select(_ => new RoadWheelState()).ToArray();
    public void Reset() { previous = null; Wheels = CreateWheels(); }

    public void Update(TelemetryFrame f, RoadResponse c, double sensitivity = 1, bool dynamicKerbFallback = true)
    {
        Response = c;
        if (previous?.Sequence == f.Sequence) return;
        double dt = previous is null ? 0 : f.SourceTimeSec - previous.SourceTimeSec;
        if (previous is null || previous.CarId != f.CarId || previous.GameVersion != f.GameVersion ||
            previous.VehicleModel != f.VehicleModel || previous.VehicleClass != f.VehicleClass || dt <= 0 || dt > .25)
        { Reset(); previous = f; return; }
        double moving = Smooth(.6, 6, f.SpeedMps);
        Span<double> evidence = stackalloc double[4];
        Span<bool> contacts = stackalloc bool[4];
        for (int i = 0; i < 4; i++)
        {
            var w = f.Wheels[i]; var old = previous.Wheels[i]; var z = Wheels[i];
            double sv = (w.SuspDeflectionM - old.SuspDeflectionM) / dt;
            double wv = (w.WheelYLocationM - old.WheelYLocationM) / dt;
            double tv = (w.TireVerticalDeflectionM - old.TireVerticalDeflectionM) / dt;
            // Keep suspension force distinct from tyre load; both can carry road dynamics.
            double tireRate = (w.TireLoadN - old.TireLoadN) / (dt * Math.Max((w.TireLoadN + old.TireLoadN) * .5, 500));
            double forceRate = (w.SuspForceN - old.SuspForceN) / (dt * Math.Max((Math.Abs(w.SuspForceN) + Math.Abs(old.SuspForceN)) * .5, 500));
            double lr = Math.Abs(tireRate) >= Math.Abs(forceRate) ? tireRate : forceRate;
            z.SlowSuspension = Ema(z.SlowSuspension, sv, dt, .18);
            z.SlowWheel = Ema(z.SlowWheel, wv, dt, .18);
            z.SlowTire = Ema(z.SlowTire, tv, dt, .18);
            z.SlowLoad = Ema(z.SlowLoad, lr, dt, .10);
            z.SuspensionVelocity = Math.Abs(sv - z.SlowSuspension);
            z.WheelVelocity = Math.Abs(wv - z.SlowWheel);
            z.TireVelocity = Math.Abs(tv - z.SlowTire);
            z.LoadRate = Math.Abs(lr - z.SlowLoad);
            double sus = Smooth(c.SuspensionNoiseMps, c.SuspensionFullMps, z.SuspensionVelocity * sensitivity);
            double wheel = Smooth(c.SuspensionNoiseMps, c.SuspensionFullMps, z.WheelVelocity * sensitivity);
            double tire = Smooth(c.TireNoiseMps, c.TireFullMps, z.TireVelocity * sensitivity);
            double load = Smooth(c.LoadNoisePerSec, c.LoadFullPerSec, z.LoadRate * sensitivity);
            bool contact = WheelContact.IsGrounded(w, c.ContactMinLoadN); contacts[i] = contact;
            z.Contact = contact; z.ContactEvidence = WheelContact.Evidence(w, c.ContactMinLoadN);
            evidence[i] = Math.Max(Math.Max(sus, .8 * wheel), Math.Max(.65 * tire, .55 * load));
            double texture = evidence[i] * moving * (contact ? 1 : 0);
            double bump = Math.Max(Smooth(c.BumpNoiseMps, c.BumpFullMps, z.SuspensionVelocity * sensitivity),
                .6 * Smooth(c.BumpLoadNoisePerSec, c.BumpLoadFullPerSec, z.LoadRate * sensitivity)) * moving * (contact ? 1 : 0);
            z.Texture = Env(z.Texture, texture, dt, c.AttackSeconds, c.ReleaseSeconds);
            z.Bump = Env(z.Bump, bump, dt, .004, .105);
            // Repeated fast sign reversals, not one load transfer or a single impact.
            double signed = sv - z.SlowSuspension;
            if (Math.Abs(wv - z.SlowWheel) > Math.Abs(signed)) signed = wv - z.SlowWheel;
            if (Math.Abs((tv - z.SlowTire) * 2) > Math.Abs(signed)) signed = (tv - z.SlowTire) * 2;
            int direction = Math.Abs(signed) > c.SuspensionNoiseMps * 1.5 ? Math.Sign(signed) : 0;
            z.Reversals *= Math.Exp(-dt / .16);
            if (direction != 0 && direction != z.Direction)
            {
                double interval = f.SourceTimeSec - z.LastDirectionTime;
                if (z.Direction != 0 && interval is >= .008 and <= .09) z.Reversals = Math.Min(4, z.Reversals + 1);
                z.Direction = direction; z.LastDirectionTime = f.SourceTimeSec;
            }
            if (!contact || moving == 0)
            { z.Texture = z.Bump = z.Kerb = z.Reversals = 0; z.Direction = 0; }
        }
        for (int i = 0; i < 4; i++)
        {
            var w = f.Wheels[i]; var z = Wheels[i];
            // Numeric kerb or explicit material remains authoritative. Dynamic fallback
            // is deliberately labelled an inference, limited to dry/unknown surfaces.
            double opposite = Math.Max(Wheels[(i % 2 == 0) ? 1 : 0].Texture, Wheels[(i % 2 == 0) ? 3 : 2].Texture);
            z.InferredKerb = dynamicKerbFallback && !w.IsKerb && (w.SurfaceType is null or 0) && contacts[i]
                ? .55 * moving * Smooth(1.6, 3, z.Reversals) * Smooth(.06, .35, z.Texture) *
                  Smooth(.06, .20, z.Texture - opposite * 1.2) : 0;
            double kerb = w.IsKerb && contacts[i] ? moving * (c.KerbBaseLevel + (1 - c.KerbBaseLevel) * evidence[i]) : z.InferredKerb;
            z.Kerb = Env(z.Kerb, kerb, dt, .008, .065);
            z.KerbHz = Math.Clamp(f.SpeedMps / c.KerbSpacingM, 12, 48);
            z.KerbPhase = (z.KerbPhase + z.KerbHz * dt) % 1;
            if (!contacts[i] || moving == 0) z.Texture = z.Bump = z.Kerb = z.InferredKerb = 0;
            z.Source = !contacts[i] ? "离地 / 无接触" : moving == 0 ? "静止" :
                w.IsKerb ? "路肩标记" : z.InferredKerb > .02 ? "路肩样动态（推断）" :
                z.Texture > .01 ? "悬架 / 轮胎动态" : w.SurfaceType is null ? "类型未知 / 当前平顺" : "普通路面 / 当前平顺";
        }
        previous = f;
    }
}
