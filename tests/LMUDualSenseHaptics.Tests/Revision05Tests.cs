using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text.Json;
using LMUDualSenseHaptics.Core;
using LMUDualSenseHaptics.Haptics;
using LMUDualSenseHaptics.Hardware;
using LMUDualSenseHaptics.Physics;
using LMUDualSenseHaptics.Runtime;

// Production C# unit tests. Adding tests is NOT evidence that they were executed.
internal static class Revision05Tests
{
    private static void Assert(bool value, string message = "assertion")
    { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected InvalidDataException"); }
    private static TelemetryFrame Frame(int n, double t, float speed = 20)
    {
        var wheels = Enumerable.Range(0, 4).Select(_ => new WheelFrame(60, 0, 3000, 3000, 0, 0, speed, 0, null, true)).ToImmutableArray();
        return new(n, t, 0, true, true, false, "test", "GT3", speed, 0, 0, -speed, 0, 0, 0, 0, 1, 0,
            3, 5000, 8000, false, false, true, true, wheels, t, 14200, VehicleModel: "test");
    }
    private static TelemetryFrame Wheel(TelemetryFrame f, int index, Func<WheelFrame, WheelFrame> transform) =>
        f with { Wheels = f.Wheels.Select((w, i) => i == index ? transform(w) : w).ToImmutableArray() };
    private static VoiceCoilCommand Command(VoiceCoilSide left, VoiceCoilSide right = default) =>
        new(true, 100, left, right, 75, 110, 45, 60, .75, 1, false);
    private static float[] Render(VoiceCoilCommand c, int frames = 4800, double now = 0)
    { var a = new float[frames * 4]; new VoiceCoilSynthesizer(48000).Render(c, a, now); return a; }
    private static double Energy(float[] a, int channel) => Enumerable.Range(0, a.Length / 4).Sum(n => (double)a[4*n+channel] * a[4*n+channel]);
    private static byte[] Format(int bits = 32, ushort tag = 3, int channels = 4, uint mask = 0, int validBits = 0)
    {
        var b = new byte[tag == 0xFFFE ? 40 : 18];
        BinaryPrimitives.WriteUInt16LittleEndian(b, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), (ushort)channels);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), 48000);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), 48000 * channels * bits / 8);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(12), (ushort)(channels * bits / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(14), (ushort)bits);
        if (tag == 0xFFFE)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(16), 22);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(18), (ushort)(validBits == 0 ? bits : validBits));
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20), mask);
            new Guid("00000001-0000-0010-8000-00aa00389b71").TryWriteBytes(b.AsSpan(24));
        }
        return b;
    }
    private static double Inference(bool both = false, bool enabled = true, byte? surface = null, bool single = false)
    {
        var road = new RoadSignalEngine(); double peak = 0;
        for (int n = 0; n < 200; n++)
        {
            double t = n * .01; float y = single ? (n == 20 ? .006f : 0) : (float)(.002 * Math.Sin(2 * Math.PI * 20 * t));
            var f = Wheel(Frame(n, t), 0, w => w with { SuspDeflectionM = y, SurfaceType = surface });
            if (both) f = Wheel(f, 1, w => w with { SuspDeflectionM = y, SurfaceType = surface });
            road.Update(f, new(), 1, enabled); peak = Math.Max(peak, road.Wheels[0].InferredKerb);
        }
        return peak;
    }
    public static void Run(Action<string, Action> check)
    {
        check("v0.5 simultaneous road PCM ABS and TC preserve independent transports", () =>
        {
            var loop=new FeedbackLoop();var p=new Profile{SpatialSeparation=100,Engine=new(false,15)};
            loop.Tick(Frame(0,0),0,p);
            var f=Wheel(Frame(1,.01) with{Brake01=1,TcIntervening=true,AbsIntervening=true},0,
                w=>w with{SuspDeflectionM=.004f});
            var mixed=loop.Tick(f,.01,p);var transport=HapticsOutputRouting.ForTransport(mixed,true);
            var report=new byte[48];DualSenseReportBuilder.Build(transport,report);
            var command=VoiceCoilCommand.Create(loop.Signals.RoadSignals,loop.Layers,f,.01,p,loop.Mixer.ContinuousGain,loop.Mixer.EngineGain);
            Assert(report[3]==0&&report[4]==0&&report[11]==0x21&&report[22]==0x26);
            Assert(transport.LeftTrigger==mixed.LeftTrigger&&transport.RightTrigger==mixed.RightTrigger);
            var pcm=Render(command,192,.01);Assert(Energy(pcm,2)>0&&Energy(pcm,0)==0&&Energy(pcm,1)==0);
        });
        check("v0.5 pause silences a running PCM synthesizer and HID triggers", () =>
        {
            var loop=new FeedbackLoop();var p=new Profile();loop.Tick(Frame(0,0),0,p);
            var f=Wheel(Frame(1,.01) with{TcIntervening=true},0,w=>w with{SurfaceType=5});
            loop.Tick(f,.01,p);var synth=new VoiceCoilSynthesizer(48000);var pcm=new float[768];
            synth.Render(VoiceCoilCommand.Create(loop.Signals.RoadSignals,loop.Layers,f,.01,p,1,1),pcm,.01);
            Assert(pcm.Any(x=>x!=0));var mixed=loop.Tick(f with{Paused=true},.014,p);
            synth.Render(loop.AdvancedFeedbackEnabled?throw new Exception("pause gate failed"):VoiceCoilCommand.Off,pcm,.014);
            Assert(pcm.All(x=>x==0));var transport=HapticsOutputRouting.ForTransport(mixed,true);
            Assert(transport.LeftTrigger==TriggerEffect.Off&&transport.RightTrigger==TriggerEffect.Off&&transport.LeftActuator==0&&transport.RightActuator==0);
        });
        check("v0.5 default profile enables USB audio without replacing old gains", () =>
        {
            var p = JsonSerializer.Deserialize<Profile>("{\"BrakeBase\":{\"Enabled\":true,\"Strength\":63},\"Kerb\":{\"Enabled\":false,\"Strength\":41}}")!;
            p.Validate(); Assert(p.AudioHaptics.Enabled && p.BrakeBase.Strength == 63 && !p.Kerb.Enabled && p.Kerb.Strength == 41);
        });
        check("v0.5 audio settings round trip", () =>
        {
            var c = new AudioHapticsSettings { EndpointId = "endpoint-test", SwapSides = true, KerbFrequencyHz = 145 };
            Assert(JsonSerializer.Deserialize<AudioHapticsSettings>(JsonSerializer.Serialize(c)) == c);
        });
        check("v0.5 audio settings bounds rejected", () =>
        {
            foreach (var c in new AudioHapticsSettings[] { new() { MasterStrength = 101 }, new() { RoadFrequencyHz = 0 },
                new() { KerbFrequencyHz = 221 }, new() { BumpFrequencyHz = 19 }, new() { EndpointId = null! } }) Reject(c.Validate);
            Reject((new Profile { AudioHaptics = null! }).Validate);
        });
        check("v0.5 HID audio mode clears both rumble selectors and keeps triggers", () =>
        {
            var f = new FeedbackFrame(77, 99, TriggerEffect.Vibration(0, 2, 15), TriggerEffect.Vibration(0, 3, 12));
            var legacy = new byte[48]; var audio = new byte[48];
            DualSenseReportBuilder.Build(f, legacy); DualSenseReportBuilder.Build(f with { UseAudioHaptics = true }, audio);
            Assert(legacy[1] == 15 && legacy[39] == 6 && legacy[3] == 99 && legacy[4] == 77);
            Assert(audio[1] == 12 && audio[39] == 2 && audio[3] == 0 && audio[4] == 0);
            Assert(legacy.AsSpan(11, 22).SequenceEqual(audio.AsSpan(11, 22)));
            Assert(Enumerable.Range(0, 48).Where(i => i is not (1 or 3 or 4 or 39)).All(i => legacy[i] == audio[i]));
        });
        check("v0.5 transport routes PCM and never mixes HID rumble", () =>
        {
            var f = new FeedbackFrame(90, 80, TriggerEffect.Vibration(0, 1, 12), TriggerEffect.Off);
            var a = HapticsOutputRouting.ForTransport(f, true);
            Assert(a.UseAudioHaptics && a.LeftActuator == 0 && a.RightActuator == 0 && a.LeftTrigger == f.LeftTrigger);
            Assert(HapticsOutputRouting.ForTransport(f, false) == f);
        });
        check("v0.5 audio OFF packet does not reselect classic rumble", () =>
        {
            var b = new byte[48]; DualSenseReportBuilder.Build(HapticsOutputRouting.ForTransport(FeedbackFrame.Off, true), b);
            Assert(b[1] == 12 && b[39] == 2 && b[11] == 5 && b[22] == 5);
        });
        check("v0.5 four-channel PCM left routing and speaker silence", () =>
        {
            var a = Render(Command(new(.4, 0, 0, 0, 0, 0, 0, 24)));
            Assert(Energy(a, 0) == 0 && Energy(a, 1) == 0 && Energy(a, 2) > 1 && Energy(a, 3) == 0);
            Assert(a.Any(x => x < 0) && a.Any(x => x > 0));
        });
        check("v0.5 four-channel PCM right routing", () =>
        {
            var a = Render(Command(default, new(.4, 0, 0, 0, 0, 0, 0, 24)));
            Assert(Energy(a, 0) == 0 && Energy(a, 1) == 0 && Energy(a, 2) == 0 && Energy(a, 3) > 1);
        });
        check("v0.5 swap channels preserves exact generated waveform", () =>
        {
            var c = Command(new(.4, 0, 0, 0, 0, 0, 0, 24)); var a = Render(c); var b = Render(c with { SwapSides = true });
            Assert(Enumerable.Range(0, a.Length/4).All(i => a[4*i+2] == b[4*i+3] && a[4*i+3] == b[4*i+2]));
        });
        check("v0.5 oscillator phase continues across audio buffers", () =>
        {
            var c = Command(new(.3, .2, .4, .1, 0, .1, 0, 24)); var expected = Render(c);
            var actual = new float[expected.Length]; var synth = new VoiceCoilSynthesizer(48000);
            for (int i = 0; i < 10; i++) synth.Render(c, actual.AsSpan(i * 1920, 1920), i * .01);
            Assert(actual.SequenceEqual(expected));
        });
        check("v0.5 OFF, zero command and master zero are digital silence", () =>
        {
            Assert(Render(VoiceCoilCommand.Off).All(x => x == 0)); Assert(Render(Command(default)).All(x => x == 0));
            Assert(Render(Command(new(.8, .8, .8, .8, .8, .8, .8, 24)) with { MasterGain = 0 }).All(x => x == 0));
        });
        check("v0.5 expired PCM command silences the remainder of a buffer", () =>
        {
            var a = Render(Command(new(.4, 0, 0, 0, 0, 0, 0, 24)) with { ValidUntil = .01 }, 960);
            Assert(a.Take(1920).Any(x => x != 0) && a.Skip(1920).All(x => x == 0));
        });
        check("v0.5 watchdog rejects stale and nonfinite deadlines", () =>
        {
            var c = Command(new(.4, 0, 0, 0, 0, 0, 0, 24));
            Assert(Render(c with { ValidUntil = .01 }, now: .02).All(x => x == 0));
            Assert(Render(c with { ValidUntil = double.NaN }).All(x => x == 0));
        });
        check("v0.5 PCM limiter finite and bounded for extreme requests", () =>
        {
            var c = Command(new(10, 10, 10, 10, 10, 10, 10, 24), new(10, 10, 10, 10, 10, 10, 10, 24)) with { MasterGain = 99, RightGain = 99 };
            Assert(Render(c).All(x => float.IsFinite(x) && Math.Abs(x) <= .850001));
            Assert(Render(c with { RoadHz = double.NaN, MasterGain = double.NaN }).All(x => x == 0));
        });
        check("v0.5 isolated kerb renders audible-rate bipolar PCM", () =>
        {
            var a = Render(Command(new(0, 0, .6, 0, 0, 0, 0, 24)));
            Assert(Energy(a, 2) > 1 && Energy(a, 3) == 0 && a.Any(x => x < 0));
        });
        check("v0.5 road carrier setting changes the waveform", () =>
        {
            var c = Command(new(.4, 0, 0, 0, 0, 0, 0, 24)); Assert(!Render(c).SequenceEqual(Render(c with { RoadHz = 100 })));
        });
        check("v0.5 generated RMS and peak meters match samples", () =>
        {
            var s = new VoiceCoilSynthesizer(48000); var a = new float[19200]; s.Render(Command(new(.3, 0, 0, 0, 0, 0, 0, 24)), a, 0);
            Assert(Math.Abs(s.Levels.LeftRms - Math.Sqrt(Energy(a, 2) / 4800)) < 1e-7 && s.Levels.RightPeak == 0);
        });
        check("v0.5 four one-second self-tests have explicit side and expiry", () =>
        {
            foreach (string name in new[] { "AUDIO_LEFT", "AUDIO_RIGHT", "AUDIO_KERB_LEFT", "AUDIO_KERB_RIGHT" })
            {
                var c = VoiceCoilCommand.Test(name, 5, 5.03, new()); Assert(c.Enabled && c.ValidUntil == 5.03);
                Assert((c.Left.Road + c.Left.Kerb > 0) == name.EndsWith("LEFT", StringComparison.Ordinal));
            }
            Assert(VoiceCoilCommand.Test("UNKNOWN", 0, 1, new()) == VoiceCoilCommand.Off);
        });
        check("v0.5 native float PCM encoder enforces channels 1/2 silence", () =>
        {
            var f = AudioPcmFormat.Parse(Format()); var bytes = new byte[16]; f.Encode(new float[] { 1, 1, .5f, -.25f }, bytes);
            Assert(bytes.Take(8).All(x => x == 0));
            Assert(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8))) == .5f);
            Assert(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12))) == -.25f);
        });
        check("v0.5 PCM16 PCM24 PCM32 and 24-in-32 support", () =>
        {
            foreach (int bits in new[] { 16, 24, 32 })
            {
                var f = AudioPcmFormat.Parse(Format(bits, 1)); var b = new byte[4 * bits / 8];
                f.Encode(new float[] { 1, -1, .5f, -.5f }, b); Assert(b.Take(2 * bits / 8).All(x => x == 0));
                Assert(b.Skip(2 * bits / 8).Any(x => x != 0));
            }
            var packed = AudioPcmFormat.Parse(Format(32, 0xFFFE, mask: 0x33, validBits: 24)); var bytes = new byte[16];
            packed.Encode(new float[] { 0, 0, .25f, -.25f }, bytes); Assert(bytes[8] == 0 && bytes[12] == 0);
        });
        check("v0.5 stereo wrong masks and malformed formats rejected", () =>
        {
            Reject(() => AudioPcmFormat.Parse(Format(channels: 2)));
            Reject(() => AudioPcmFormat.Parse(Format(tag: 0xFFFE, mask: 0x603)));
            Reject(() => AudioPcmFormat.Parse(new byte[17]));
            Reject(() => AudioPcmFormat.Parse(Format(24, 3)));
            Reject(() => AudioPcmFormat.Parse(Format(32, 0xFFFE, validBits: 33)));
        });
        check("v0.5 encoder replaces nonfinite input with silence", () =>
        {
            var f = AudioPcmFormat.Parse(Format()); var b = new byte[16]; f.Encode(new float[] { 1, 1, float.NaN, float.PositiveInfinity }, b);
            Assert(b.All(x => x == 0));
        });
        check("v0.5 auto endpoint matches HID container not first/default speaker", () =>
        {
            var id = Guid.NewGuid(); var a = new AudioEndpointInfo("speaker", "Speakers", Guid.NewGuid(), 4, 48000, "");
            var b = new AudioEndpointInfo("controller", "Localized controller", id, 4, 48000, "");
            Assert(AudioEndpointSelection.Choose(new[] { a, b }, "", id) == b);
            Reject(() => AudioEndpointSelection.Choose(new[] { a }, "", id));
            Reject(() => AudioEndpointSelection.Choose(new[] { a, b }, "", Guid.Empty));
        });
        check("v0.5 ambiguous and mismatched physical endpoints rejected", () =>
        {
            var id = Guid.NewGuid(); var a = new AudioEndpointInfo("a", "Wireless Controller", id, 4, 48000, "");
            var b = a with { Id = "b" }; Reject(() => AudioEndpointSelection.Choose(new[] { a, b }, "", id));
            Reject(() => AudioEndpointSelection.Choose(new[] { a }, "a", Guid.NewGuid()));
            Reject(() => AudioEndpointSelection.Choose(new[] { a }, "missing", id));
        });
        check("v0.5 explicit controller-only selection without container metadata", () =>
        {
            var a = new AudioEndpointInfo("a", "Wireless Controller", Guid.Empty, 4, 48000, "");
            Assert(AudioEndpointSelection.Choose(new[] { a }, "a", Guid.Empty) == a);
            Reject(() => AudioEndpointSelection.Choose(new[] { a with { Name = "Speakers" } }, "a", Guid.Empty));
        });
        check("v0.5 unknown numeric type may use an explicit kerb material", () =>
        {
            var w = Frame(0, 0).Wheels[0]; Assert((w with { SurfaceType = null, TerrainName = "  CURB_A" }).IsKerb);
            Assert(!(w with { SurfaceType = null, TerrainName = "ROAD" }).IsKerb);
        });
        check("v0.5 tagged flat kerb produces PCM without suspension motion", () =>
        {
            var e = new RoadSignalEngine(); TelemetryFrame f = Frame(0, 0);
            for (int n = 0; n < 10; n++) { f = Wheel(Frame(n, n * .01), 0, w => w with { SurfaceType = 5 }); e.Update(f, new()); }
            var p = new Profile { SpatialSeparation = 100 }; var c = VoiceCoilCommand.Create(e, default, f, .09, p, 1, 1);
            Assert(c.Left.Kerb > .1 && c.Right.Kerb == 0 && Energy(Render(c, now: .09), 2) > 1);
        });
        check("v0.5 kerb off and road off remain independently controllable", () =>
        {
            var e = new RoadSignalEngine(); e.Wheels[0].Texture = .5; e.Wheels[0].Kerb = .7; var f = Frame(0, 0);
            var p = new Profile { Road = new(false, 100), Kerb = new(true, 75), SpatialSeparation = 100 };
            var c = VoiceCoilCommand.Create(e, default, f, 0, p, 1, 1); Assert(c.Left.Road == 0 && c.Left.Kerb > 0);
            c = VoiceCoilCommand.Create(e, default, f, 0, p with { Kerb = new(false, 75) }, 1, 1); Assert(c.Left.Kerb == 0);
        });
        check("v0.5 audio deadline bounded by watchdog and source age", () =>
        {
            var f = Frame(0, 5) with { SourceAgeSec = .24 }; var c = VoiceCoilCommand.Create(new(), default, f, 5, new(), 1, 1);
            Assert(Math.Abs(c.ValidUntil - 5.01) < 1e-9);
            Assert(VoiceCoilCommand.Create(new(), default, f with { SourceAgeSec = 0 }, 5, new(), 1, 1).ValidUntil == 5.075);
        });
        check("v0.5 repeated one-side unlabelled vibrations infer kerb-like cue", () => Assert(Inference() > .05));
        check("v0.5 symmetric vibration does not infer a one-side kerb", () => Assert(Inference(both: true) == 0));
        check("v0.5 isolated impact does not infer continuous kerb", () => Assert(Inference(single: true) == 0));
        check("v0.5 dynamic fallback can be fully disabled", () => Assert(Inference(enabled: false) == 0));
        check("v0.5 known grass is not relabelled by dynamic fallback", () => Assert(Inference(surface: 3) == 0));
        check("v0.5 stationary and airborne kerb is immediately gated", () =>
        {
            var e = new RoadSignalEngine(); var f = Wheel(Frame(0, 0), 0, w => w with { SurfaceType = 5 }); e.Update(f, new());
            f = f with { Sequence = 1, SourceTimeSec = .01, CapturedAtSec = .01 }; e.Update(f, new()); Assert(e.Wheels[0].Kerb > 0);
            f = f with { Sequence = 2, SourceTimeSec = .02, CapturedAtSec = .02, SpeedMps = 0 }; e.Update(f, new()); Assert(e.Wheels[0].Combined == 0);
            f = Wheel(f with { Sequence = 3, SourceTimeSec = .03, SpeedMps = 20 }, 0, w => w with { TireLoadN = 0 });
            e.Update(f, new()); Assert(e.Wheels[0].Combined == 0);
        });
    }
}
