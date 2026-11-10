using System.Buffers.Binary;

namespace OnnxUtil.Audio;

/// <summary>一段单声道音频（float32，值域 −1..1）与它的采样率。</summary>
public sealed record WavAudio(int SampleRate, float[] Samples)
{
    public double DurationSeconds => SampleRate > 0 ? (double)Samples.Length / SampleRate : 0;
}

/// <summary>
/// 极简 WAV 读取器（纯托管，无依赖）：RIFF/WAVE，PCM 8/16/24/32 位与 IEEE float32，
/// 任意声道数（按平均下混成单声道）。保持原始采样率——重采样交给使用方（ASR 前端需要 16 kHz）。
/// </summary>
public static class WavReader
{
    public static WavAudio Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Read(File.ReadAllBytes(path));
    }

    public static WavAudio Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 44) throw new InvalidDataException("不是有效的 WAV：数据太短。");
        if (!data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("不是有效的 WAV：缺少 RIFF/WAVE 头。");

        int format = 0, channels = 0, sampleRate = 0, bits = 0, blockAlign = 0;
        ReadOnlySpan<byte> samples = default;

        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(data.Slice(pos, 4));
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 4, 4));
            int body = pos + 8;
            if (size < 0 || body + size > data.Length) break;

            switch (id)
            {
                case "fmt ":
                    format = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body, 2));
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body + 2, 2));
                    sampleRate = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(body + 4, 4));
                    blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body + 12, 2));
                    bits = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body + 14, 2));
                    // WAVE_FORMAT_EXTENSIBLE：真实格式在 SubFormat GUID 的前两字节
                    if (format == 0xFFFE && size >= 26)
                        format = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body + 24, 2));
                    break;
                case "data":
                    samples = data.Slice(body, size);
                    break;
            }
            pos = body + size + (size & 1);   // 块按 2 字节对齐
        }

        if (channels <= 0 || sampleRate <= 0) throw new InvalidDataException("WAV 缺少 fmt 块。");
        if (samples.IsEmpty) throw new InvalidDataException("WAV 缺少 data 块。");
        if (blockAlign <= 0) blockAlign = channels * Math.Max(1, bits / 8);

        int frames = samples.Length / blockAlign;
        float[] mono = new float[frames];
        int bytesPerSample = Math.Max(1, bits / 8);
        double scale = format switch
        {
            1 => bits switch { 8 => 128.0, 16 => 32768.0, 24 => 8388608.0, 32 => 2147483648.0, _ => 0 },
            3 => 1.0,
            _ => 0,
        };
        if (scale == 0) throw new NotSupportedException($"不支持的 WAV 编码：format={format} bits={bits}");

        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            int offset = f * blockAlign;
            for (int c = 0; c < channels; c++)
            {
                ReadOnlySpan<byte> p = samples.Slice(offset + c * bytesPerSample, bytesPerSample);
                sum += format == 3
                    ? BinaryPrimitives.ReadSingleLittleEndian(p)
                    : bits switch
                    {
                        8 => (p[0] - 128) / scale,
                        16 => BinaryPrimitives.ReadInt16LittleEndian(p) / scale,
                        24 => ((p[2] << 16) | (p[1] << 8) | p[0]) is var v && (v & 0x800000) != 0 ? (v - 0x1000000) / scale : v / scale,
                        32 => BinaryPrimitives.ReadInt32LittleEndian(p) / scale,
                        _ => 0,
                    };
            }
            mono[f] = (float)(sum / channels);
        }
        return new WavAudio(sampleRate, mono);
    }
}
