// Adapted from ACC-DualSense-Feedback (MIT); see THIRD_PARTY_NOTICES.md.
namespace LMUDualSenseHaptics.Hardware;

public readonly record struct TriggerEffect(
    byte Mode,
    byte P0 = 0, byte P1 = 0, byte P2 = 0, byte P3 = 0, byte P4 = 0,
    byte P5 = 0, byte P6 = 0, byte P7 = 0, byte P8 = 0, byte P9 = 0)
{
    public static TriggerEffect Off => new(0x05);

    public static TriggerEffect Rigid(float normalizedForce)
        => normalizedForce <= 0.002f
            ? Off
            : new TriggerEffect(0x01, 0, ToByte(normalizedForce));

    public static TriggerEffect Vibration(byte position, byte amplitude, byte frequency)
    {
        position = (byte)Math.Clamp((int)position, 0, 9);
        amplitude = (byte)Math.Clamp((int)amplitude, 0, 8);
        if (amplitude == 0 || frequency == 0)
            return Off;

        ushort active = 0;
        uint packed = 0;
        byte encodedAmplitude = (byte)(amplitude - 1);
        for (int index = position; index < 10; index++)
        {
            active |= (ushort)(1 << index);
            packed |= (uint)encodedAmplitude << (index * 3);
        }

        return new TriggerEffect(
            0x26,
            (byte)active,
            (byte)(active >> 8),
            (byte)packed,
            (byte)(packed >> 8),
            (byte)(packed >> 16),
            (byte)(packed >> 24),
            0,
            0,
            frequency);
    }

    public static TriggerEffect FeedbackZones(
        byte z0, byte z1, byte z2, byte z3, byte z4,
        byte z5, byte z6, byte z7, byte z8, byte z9)
    {
        Span<byte> strengths = stackalloc byte[10] { z0, z1, z2, z3, z4, z5, z6, z7, z8, z9 };
        return FeedbackZones(strengths);
    }

    public static TriggerEffect FeedbackZones(ReadOnlySpan<byte> strengths)
    {
        if (strengths.Length < 10)
            throw new ArgumentException("Ten trigger zones are required.", nameof(strengths));

        ushort active = 0;
        uint packed = 0;
        for (int index = 0; index < 10; index++)
        {
            byte strength = (byte)Math.Clamp((int)strengths[index], 0, 8);
            if (strength == 0)
                continue;

            active |= (ushort)(1 << index);
            packed |= (uint)(strength - 1) << (index * 3);
        }

        return new TriggerEffect(
            0x21,
            (byte)active,
            (byte)(active >> 8),
            (byte)packed,
            (byte)(packed >> 8),
            (byte)(packed >> 16),
            (byte)(packed >> 24));
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < 11)
            throw new ArgumentException("Trigger destination must contain 11 bytes.", nameof(destination));

        destination[0] = Mode;
        destination[1] = P0;
        destination[2] = P1;
        destination[3] = P2;
        destination[4] = P3;
        destination[5] = P4;
        destination[6] = P5;
        destination[7] = P6;
        destination[8] = P7;
        destination[9] = P8;
        destination[10] = P9;
    }

    private static byte ToByte(float value)
        => (byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f), 0, 255);
}
