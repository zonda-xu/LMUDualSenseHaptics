namespace LMUDualSenseHaptics.Haptics;

public readonly record struct VoiceCoilLevels(double LeftRms, double RightRms, double LeftPeak, double RightPeak);

// Sample-rate DSP, not 250 Hz HID amplitude modulation. The first two channels
// always remain digital silence; channels 3 and 4 drive left/right voice coils.
public sealed class VoiceCoilSynthesizer
{
    public const double PeakLimit = .85;
    private readonly int sampleRate;
    private readonly Oscillator left = new(0), right = new(.31);
    public VoiceCoilLevels Levels { get; private set; }
    public VoiceCoilSynthesizer(int sampleRate)
    {
        if (sampleRate is < 8000 or > 192000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        this.sampleRate = sampleRate; left.Reset(); right.Reset();
    }
    public void Reset() { left.Reset(); right.Reset(); Levels = default; }
    public void Render(VoiceCoilCommand command, Span<float> interleavedQuad, double playbackStart)
    {
        if (interleavedQuad.Length % 4 != 0 || !double.IsFinite(playbackStart))
            throw new ArgumentException("Expected whole quadraphonic PCM frames and a finite clock");
        interleavedQuad.Clear();
        int count = interleavedQuad.Length / 4;
        if (!command.Enabled || playbackStart >= command.ValidUntil || !double.IsFinite(command.ValidUntil))
        { Reset(); return; }
        double l2 = 0, r2 = 0, lp = 0, rp = 0;
        double dt = 1.0 / sampleRate, ramp = 1 - Math.Exp(-dt / .005);
        for (int n = 0; n < count; n++)
        {
            if (playbackStart + n * dt >= command.ValidUntil)
            { left.Reset(); right.Reset(); break; }
            double l = left.Next(command.Left, command, dt, ramp);
            double r = right.Next(command.Right, command, dt, ramp) * Safe(command.RightGain, 2);
            l = Limit(l * Safe(command.MasterGain, 1));
            r = Limit(r * Safe(command.MasterGain, 1));
            if (command.SwapSides) (l, r) = (r, l);
            interleavedQuad[4 * n + 2] = (float)l;
            interleavedQuad[4 * n + 3] = (float)r;
            l2 += l * l; r2 += r * r; lp = Math.Max(lp, Math.Abs(l)); rp = Math.Max(rp, Math.Abs(r));
        }
        Levels = count == 0 ? default : new(Math.Sqrt(l2 / count), Math.Sqrt(r2 / count), lp, rp);
    }
    private static double Safe(double x, double max = 1) => double.IsFinite(x) ? Math.Clamp(x, 0, max) : 0;
    private static double Hz(double x, double fallback) => double.IsFinite(x) ? Math.Clamp(x, 8, 250) : fallback;
    private static double Limit(double x) => double.IsFinite(x) ? PeakLimit * Math.Tanh(x / PeakLimit) : 0;
    private sealed class Oscillator(double phaseOffset)
    {
        private double roadPhase, detailPhase, bumpPhase, kerbPhase, cadencePhase, slipPhase, enginePhase, impactPhase;
        private double road, bump, kerb, slip, engine, impact, cue;
        private double dcInput, dcOutput;
        public void Reset()
        {
            road = bump = kerb = slip = engine = impact = cue = dcInput = dcOutput = 0;
            roadPhase = detailPhase = bumpPhase = kerbPhase = cadencePhase = slipPhase = enginePhase = impactPhase = phaseOffset;
        }
        private static double Sine(ref double phase, double hz, double dt)
        {
            double result = Math.Sin(2 * Math.PI * phase);
            phase += hz * dt; phase -= Math.Floor(phase);
            return result;
        }
        public double Next(VoiceCoilSide s, VoiceCoilCommand c, double dt, double ramp)
        {
            road += (Safe(s.Road) - road) * ramp; bump += (Safe(s.Bump) - bump) * ramp;
            kerb += (Safe(s.Kerb) - kerb) * ramp; slip += (Safe(s.Slip) - slip) * ramp;
            impact += (Safe(s.Impact) - impact) * ramp; engine += (Safe(s.Engine) - engine) * ramp;
            cue += (Safe(s.Cue) - cue) * ramp;
            double texture = .78 * Sine(ref roadPhase, Hz(c.RoadHz, 75), dt) +
                             .22 * Sine(ref detailPhase, Hz(c.RoadHz * 1.617, 121), dt);
            double cadence = .5 + .5 * Sine(ref cadencePhase, Hz(s.KerbRateHz, 24), dt);
            double serration = .18 + .82 * cadence * cadence * cadence;
            double k = Sine(ref kerbPhase, Hz(c.KerbHz, 110), dt) * serration;
            double b = Sine(ref bumpPhase, Hz(c.BumpHz, 45), dt);
            double loss = Sine(ref slipPhase, 155, dt);
            double motor = Sine(ref enginePhase, Hz(c.EngineHz, 60), dt);
            double hit = Sine(ref impactPhase, 52, dt);
            // Road/kerb dominate background layers. A kerb does not erase road dynamics.
            double priority = Math.Max(Math.Max(road, kerb), Math.Max(bump, impact));
            double background = 1 - Math.Clamp(priority * 1.8, 0, .95);
            double x = road * texture * (1 - .45 * bump) * (1 - .30 * kerb) +
                       .85 * bump * b + 1.15 * kerb * k + .55 * slip * loss +
                       .85 * impact * hit + .45 * cue * hit + .65 * engine * motor * background;
            // Remove sub-audible/DC energy before limiting; no nonzero "minimum" at rest.
            double dc = x - dcInput + Math.Exp(-2 * Math.PI * 12 * dt) * dcOutput;
            dcInput = x; dcOutput = dc;
            return dc;
        }
    }
}
