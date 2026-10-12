using System.Buffers.Binary;
namespace LMUDualSenseHaptics.Hardware;

// Pure, testable WAVEFORMATEX/EXTENSIBLE parsing and PCM interleaving.
public sealed record AudioPcmFormat(int SampleRate, int Channels, int Bits, int ValidBits,
    int BlockAlign, bool Float, uint ChannelMask)
{
    public static AudioPcmFormat Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 18) throw new InvalidDataException("WAVEFORMATEX 太短");
        ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]);
        int rate = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        int align = BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]);
        int valid = bits; uint mask = 0;
        if (tag == 0xFFFE)
        {
            if (bytes.Length < 40 || BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..]) < 22)
                throw new InvalidDataException("WAVEFORMATEXTENSIBLE 太短");
            valid = BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]);
            mask = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]);
            var subtype = new Guid(bytes.Slice(24, 16));
            tag = subtype == new Guid("00000003-0000-0010-8000-00aa00389b71") ? (ushort)3 :
                  subtype == new Guid("00000001-0000-0010-8000-00aa00389b71") ? (ushort)1 : (ushort)0;
        }
        if (channels != 4 || mask is not (0 or 0x33))
            throw new InvalidDataException($"音圈需要四声道 Quadraphonic；当前 {channels} 声道，mask=0x{mask:X}。不会转成立体声或输出到默认扬声器。");
        if (rate is < 8000 or > 192000 || bits is not (16 or 24 or 32) || align != channels * bits / 8 ||
            valid <= 0 || valid > bits || tag is not (1 or 3) || tag == 3 && (bits != 32 || valid != 32))
            throw new InvalidDataException("不支持此 PCM 格式；请选择四声道 48000 Hz");
        return new(rate, channels, bits, valid, align, tag == 3, mask);
    }

    public void Encode(ReadOnlySpan<float> samples, Span<byte> bytes)
    {
        int width = Bits / 8;
        if (samples.Length % 4 != 0 || bytes.Length != samples.Length * width)
            throw new ArgumentException("PCM 缓冲区必须包含完整四声道帧");
        for (int i = 0; i < samples.Length; i++)
        {
            // Defense in depth: even a faulty upstream mixer cannot feed speakers.
            double value = i % 4 < 2 || !float.IsFinite(samples[i]) ? 0 : Math.Clamp(samples[i], -1, 1);
            var target = bytes.Slice(i * width, width);
            if (Float) { BinaryPrimitives.WriteInt32LittleEndian(target, BitConverter.SingleToInt32Bits((float)value)); continue; }
            long positive = (1L << (ValidBits - 1)) - 1;
            long signed = (long)Math.Round(value * positive) << (Bits - ValidBits);
            if (Bits == 16) BinaryPrimitives.WriteInt16LittleEndian(target, (short)signed);
            else if (Bits == 24)
            { target[0] = (byte)signed; target[1] = (byte)(signed >> 8); target[2] = (byte)(signed >> 16); }
            else BinaryPrimitives.WriteInt32LittleEndian(target, (int)signed);
        }
    }
}

public sealed record AudioEndpointInfo(string Id, string Name, Guid ContainerId, int Channels, int SampleRate, string Problem)
{
    public string DisplayName => $"{Name} · {Channels} ch / {SampleRate} Hz" + (Problem.Length > 0 ? " · " + Problem : "");
    public bool HasControllerName => Name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("无线控制器", StringComparison.OrdinalIgnoreCase) || Name.Contains("無線控制器", StringComparison.OrdinalIgnoreCase);
}

public static class AudioEndpointSelection
{
    public static AudioEndpointInfo Choose(IReadOnlyList<AudioEndpointInfo> endpoints, string selectedId, Guid hidContainer)
    {
        if (selectedId.Length > 0)
        {
            var selected = endpoints.FirstOrDefault(e => e.Id == selectedId)
                ?? throw new InvalidDataException("所选音频端点已离线；请刷新音频设备");
            if (hidContainer != Guid.Empty && selected.ContainerId != Guid.Empty && hidContainer != selected.ContainerId)
                throw new InvalidDataException("所选音频端点与当前 HID 手柄不是同一个设备");
            if (selected.ContainerId != hidContainer || hidContainer == Guid.Empty)
                if (!selected.HasControllerName) throw new InvalidDataException("无法确认所选设备是 DualSense；拒绝向普通声卡输出");
            return selected;
        }
        // No implicit default-device fallback, and no arbitrary first controller.
        var matched = endpoints.Where(e => hidContainer != Guid.Empty && e.ContainerId == hidContainer).ToArray();
        if (matched.Length == 1) return matched[0];
        throw new InvalidDataException(matched.Length > 1 ? "同一手柄存在多个音频端点，请手动选择" :
            "未匹配到当前 USB 手柄的音频端点。请检查 USB 数据线、启用手柄播放设备并刷新；无法取得设备身份时须手动选择。");
    }
}
