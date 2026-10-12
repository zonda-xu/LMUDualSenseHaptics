using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Physics;
using static LMUDualSenseHaptics.Physics.Maths;

namespace LMUDualSenseHaptics.Haptics;

public readonly record struct VoiceCoilSide(double Road, double Bump, double Kerb,
    double Slip, double Impact, double Engine, double Cue, double KerbRateHz)
{
    public static VoiceCoilSide Zero => default;
}

// Immutable snapshots only. No audio thread reads the mutable telemetry filters.
public sealed record VoiceCoilCommand(bool Enabled, double ValidUntil,
    VoiceCoilSide Left, VoiceCoilSide Right, double RoadHz, double KerbHz,
    double BumpHz, double EngineHz, double MasterGain, double RightGain, bool SwapSides)
{
    public static VoiceCoilCommand Off { get; } = new(false, 0, default, default, 75, 110, 45, 60, 0, 1, false);
    public static VoiceCoilCommand Create(RoadSignalEngine engine, GripLayers layers,
        TelemetryFrame frame, double now, Profile profile, double continuousGain, double engineGain)
    {
        var c = profile.AudioHaptics;
        if (!c.Enabled) return Off;
        double age = Math.Max(0, now - frame.CapturedAtSec);
        double crossfeed = .28 * (1 - profile.SpatialSeparation / 100.0);
        double Road(int i) => RoadHaptics.Response(engine.Wheels[i].Texture *
            Math.Exp(-age / engine.Response.ReleaseSeconds), engine.Response) * profile.Road.Gain;
        double Bump(int i) => RoadHaptics.Response(engine.Wheels[i].Bump *
            Math.Exp(-age / .105), engine.Response) * profile.Road.Gain;
        double Kerb(int i) => RoadHaptics.Response(engine.Wheels[i].Kerb *
            Math.Exp(-age / .065), engine.Response) * profile.Kerb.Gain;
        (double L, double R) Map(Func<int, double> value)
        {
            double l = Math.Max(value(0), value(2)), r = Math.Max(value(1), value(3));
            return (Math.Max(l, r * crossfeed), Math.Max(r, l * crossfeed));
        }
        var road = Map(Road); var bump = Map(Bump); var kerb = Map(Kerb);
        // Spacing is a tunable synthetic cue; telemetry does not provide a kerb waveform.
        double cadence = Math.Clamp(frame.SpeedMps / engine.Response.KerbSpacingM, 8, 65);
        VoiceCoilSide Side(bool left) => new(
            C((left ? road.L : road.R) * continuousGain),
            C((left ? bump.L : bump.R) * continuousGain),
            C((left ? kerb.L : kerb.R) * continuousGain),
            C((left ? layers.SlipL : layers.SlipR) * continuousGain),
            C(left ? layers.ImpactL : layers.ImpactR),
            C((left ? layers.EngineL : layers.EngineR) * engineGain), C(layers.Centered), cadence);
        // A separate 75 ms watchdog also protects against a stopped HID/output thread.
        double deadline = Math.Min(now + .075,
            frame.CapturedAtSec - frame.SourceAgeSec + profile.TelemetryStaleMs / 1000.0);
        return new(true, deadline, Side(true), Side(false), c.RoadFrequencyHz,
            c.KerbFrequencyHz, c.BumpFrequencyHz, Math.Clamp(frame.Rpm / 60.0, 30, 160),
            c.Gain, profile.RightGain, c.SwapSides);
    }

    public static VoiceCoilCommand Test(string name, double now, double endsAt, AudioHapticsSettings settings)
    {
        bool left = name is "AUDIO_LEFT" or "AUDIO_KERB_LEFT";
        bool kerb = name is "AUDIO_KERB_LEFT" or "AUDIO_KERB_RIGHT";
        if (name is not ("AUDIO_LEFT" or "AUDIO_RIGHT" or "AUDIO_KERB_LEFT" or "AUDIO_KERB_RIGHT")) return Off;
        var side = new VoiceCoilSide(kerb ? 0 : .32, 0, kerb ? .42 : 0, 0, 0, 0, 0, 24);
        return new(true, Math.Min(now + .075, endsAt), left ? side : default, left ? default : side,
            settings.RoadFrequencyHz, settings.KerbFrequencyHz, settings.BumpFrequencyHz,
            60, settings.Gain, 1, settings.SwapSides);
    }
}

public static class HapticsOutputRouting
{
    // Audio owns the two voice coils; HID still owns L2/R2. Never run both rumble paths.
    public static FeedbackFrame ForTransport(in FeedbackFrame frame, bool audioMode) => audioMode
        ? frame with { LeftActuator = 0, RightActuator = 0, UseAudioHaptics = true }
        : frame with { UseAudioHaptics = false };
}
