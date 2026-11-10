#if SHERPA_ONNX
using SherpaOnnx;

namespace OnnxUtil.Audio;

/// <summary>离线识别模型的种类（由目录里的文件名自动判断，也可显式指定）。</summary>
public enum AsrModelKind
{
    Auto = 0,
    /// <summary>Paraformer（中文常用，单个 model.onnx）。</summary>
    Paraformer = 1,
    /// <summary>Zipformer / RNN-T（encoder + decoder + joiner）。</summary>
    Transducer = 2,
    /// <summary>Zipformer-CTC / NeMo-CTC（单个 model.onnx）。</summary>
    Ctc = 3,
    /// <summary>SenseVoice（自带标点与逆文本标准化）。</summary>
    SenseVoice = 4,
    /// <summary>Whisper（encoder + decoder）。</summary>
    Whisper = 5,
}

/// <summary>sherpa-onnx 离线识别的配置。</summary>
public sealed record AsrOptions
{
    /// <summary>模型目录（含 tokens.txt 与 *.onnx）。</summary>
    public required string ModelDirectory { get; init; }
    public AsrModelKind Kind { get; init; } = AsrModelKind.Auto;
    /// <summary>线程数（默认 2）。</summary>
    public int Threads { get; init; } = 2;
    /// <summary>tokens 文件；null = 目录下的 tokens.txt。</summary>
    public string? Tokens { get; init; }
    /// <summary>执行提供者：cpu / cuda / coreml；null = cpu。</summary>
    public string? Provider { get; init; }
    /// <summary>Whisper / SenseVoice 的语言（如 "zh"、"en"、"auto"）。</summary>
    public string? Language { get; init; }
    /// <summary>SenseVoice 的逆文本标准化（数字/标点更自然），默认开。</summary>
    public bool UseInverseTextNormalization { get; init; } = true;
}

/// <summary>识别结果（tokens / 时间戳一并带出）。</summary>
public sealed record AsrResult(string Text, string[] Tokens, float[] Timestamps, double DurationSeconds);

/// <summary>
/// sherpa-onnx 离线识别（ASR）的薄封装。**原生依赖**：包自带 onnxruntime.dll，
/// 因此整个文件只在 <c>-p:UseSherpaOnnx=true</c> 时编译。
/// </summary>
public sealed class SherpaAsr : IDisposable
{
    private readonly OfflineRecognizer _recognizer;

    private SherpaAsr(OfflineRecognizer recognizer, string kind, string modelDirectory)
    {
        _recognizer = recognizer;
        Kind = kind;
        ModelDirectory = modelDirectory;
    }

    public string Kind { get; }
    public string ModelDirectory { get; }

    public static SherpaAsr Load(AsrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string dir = Path.GetFullPath(options.ModelDirectory);
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"模型目录不存在：{dir}");

        string tokens = options.Tokens ?? Path.Combine(dir, "tokens.txt");
        if (!File.Exists(tokens)) throw new FileNotFoundException($"缺少 tokens 文件：{tokens}");

        OfflineModelConfig model = new()
        {
            Tokens = tokens,
            NumThreads = options.Threads,
            Provider = options.Provider ?? "cpu",
            Debug = 0,
        };

        AsrModelKind resolved = options.Kind == AsrModelKind.Auto ? Detect(dir) : options.Kind;
        switch (resolved)
        {
            case AsrModelKind.Paraformer:
                model.Paraformer = new OfflineParaformerModelConfig { Model = Required(dir, "paraformer*.onnx", "model*.onnx") };
                model.ModelType = "paraformer";
                break;
            case AsrModelKind.Transducer:
                model.Transducer = new OfflineTransducerModelConfig
                {
                    Encoder = Required(dir, "encoder*.onnx"),
                    Decoder = Required(dir, "decoder*.onnx"),
                    Joiner = Required(dir, "joiner*.onnx"),
                };
                model.ModelType = "transducer";
                break;
            case AsrModelKind.Ctc:
                model.ZipformerCtc = new OfflineZipformerCtcModelConfig { Model = Required(dir, "*ctc*.onnx", "model*.onnx") };
                model.ModelType = "zipformer2_ctc";
                break;
            case AsrModelKind.SenseVoice:
                model.SenseVoice = new OfflineSenseVoiceModelConfig
                {
                    Model = Required(dir, "model*.onnx"),
                    Language = options.Language ?? "auto",
                    UseInverseTextNormalization = options.UseInverseTextNormalization ? 1 : 0,
                };
                model.ModelType = "sense_voice";
                break;
            case AsrModelKind.Whisper:
                model.Whisper = new OfflineWhisperModelConfig
                {
                    Encoder = Required(dir, "*encoder*.onnx"),
                    Decoder = Required(dir, "*decoder*.onnx"),
                    Language = options.Language ?? "en",
                    Task = "transcribe",
                };
                model.ModelType = "whisper";
                break;
            default:
                throw new NotSupportedException("无法自动判断模型种类：请用 --kind 指定 paraformer/transducer/ctc/sensevoice/whisper。");
        }

        OfflineRecognizerConfig config = new()
        {
            FeatConfig = new FeatureConfig { SampleRate = 16000, FeatureDim = 80 },
            ModelConfig = model,
            DecodingMethod = "greedy_search",
            MaxActivePaths = 4,
        };
        return new SherpaAsr(new OfflineRecognizer(config), resolved.ToString(), dir);
    }

    /// <summary>识别一段音频（sherpa 内部会重采样到 16 kHz）。</summary>
    public AsrResult Transcribe(WavAudio audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        using OfflineStream stream = _recognizer.CreateStream();
        stream.AcceptWaveform(audio.SampleRate, audio.Samples);
        _recognizer.Decode(stream);
        OfflineRecognizerResult result = stream.Result;
        return new AsrResult(result.Text ?? string.Empty, result.Tokens ?? [], result.Timestamps ?? [], audio.DurationSeconds);
    }

    public AsrResult TranscribeFile(string wavPath) => Transcribe(WavReader.Read(wavPath));

    public void Dispose() => _recognizer.Dispose();

    private static AsrModelKind Detect(string dir)
    {
        string[] files = Directory.GetFiles(dir, "*.onnx");
        bool Has(string pattern) => files.Any(f => Path.GetFileName(f).Contains(pattern, StringComparison.OrdinalIgnoreCase));
        if (Has("joiner")) return AsrModelKind.Transducer;
        if (Has("sense")) return AsrModelKind.SenseVoice;
        if (Has("whisper")) return AsrModelKind.Whisper;
        if (Has("ctc")) return AsrModelKind.Ctc;
        return AsrModelKind.Paraformer;
    }

    private static string Required(string dir, string pattern, string? fallback = null)
    {
        string[] matches = Directory.GetFiles(dir, pattern);
        if (matches.Length > 0) return matches[0];
        if (fallback is not null)
        {
            matches = Directory.GetFiles(dir, fallback);
            if (matches.Length > 0) return matches[0];
        }
        throw new FileNotFoundException($"模型目录 {dir} 里找不到 {pattern}" + (fallback is null ? "。" : $" 或 {fallback}。"));
    }
}
#endif
