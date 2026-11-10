using Sdcb.SimdOnnx.Onnx;
using Sdcb.SimdPaddleOCR.ModelProvider;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny;
using Sdcb.SimdPaddleOCR.Models.TextLineOrientation;

namespace OnnxUtil.Ocr;

/// <summary>
/// 一套 PaddleOCR 模型（det / rec / 可选 cls + 字典）。模型来自
/// <c>Sdcb.SimdPaddleOCR.Models.*</c>：那些包只提供 ONNX 流与字典，不含推理代码，
/// 所以这里直接交给本地引擎 <see cref="OnnxModel"/>，不引入 Sdcb.SimdPaddleOCR 库。
/// </summary>
public sealed class PaddleOcrModel
{
    private PaddleOcrModel(string name, string language, OnnxModel detection, OnnxModel recognition,
        OnnxModel? classification, string[] classes, int detLimitSide)
    {
        Name = name;
        Language = language;
        Detection = detection;
        Recognition = recognition;
        Classification = classification;
        Classes = classes;
        DetLimitSide = detLimitSide;
    }

    public string Name { get; }
    public string Language { get; }
    public OnnxModel Detection { get; }
    public OnnxModel Recognition { get; }
    /// <summary>文本行方向分类（PP-LCNet），可为 null。</summary>
    public OnnxModel? Classification { get; }
    /// <summary>CTC 类别表：索引 0 = blank，末位 = 空格（PaddleOCR 约定）。</summary>
    public string[] Classes { get; }
    /// <summary>检测输入的最长边上限（PaddleOCR 的 limit_side_len）。</summary>
    public int DetLimitSide { get; }

    /// <summary>PP-OCRv6 tiny（含文本行方向分类）。</summary>
    public static PaddleOcrModel ChineseV6Tiny(bool withClassification = true)
        => FromProviders("PP-OCRv6_tiny", "zh",
            ChineseV6TinyModel.Detection,
            ChineseV6TinyModel.Recognition,
            withClassification ? TextLineOrientationModel.Provider : null,
            ChineseV6TinyModel.Dictionary);

    public static PaddleOcrModel FromBundle(PaddleOcrModelBundle bundle, bool useClassification = true)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return FromProviders(bundle.Name, bundle.LanguageCode ?? string.Empty,
            bundle.Detection, bundle.Recognition,
            useClassification ? bundle.Classification : null,
            bundle.Dictionary);
    }

    private static PaddleOcrModel FromProviders(string name, string language,
        IPaddleOcrModelProvider detection, IPaddleOcrModelProvider recognition,
        IPaddleOcrModelProvider? classification, IPaddleOcrModelProvider dictionary)
    {
        ArgumentNullException.ThrowIfNull(detection);
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(dictionary);

        OnnxModel det = LoadOnnx(detection);
        OnnxModel rec = LoadOnnx(recognition);
        OnnxModel? cls = classification is null ? null : LoadOnnx(classification);
        return new PaddleOcrModel(name, language, det, rec, cls, LoadClasses(dictionary), 960);
    }

    private static OnnxModel LoadOnnx(IPaddleOcrModelProvider provider)
    {
        using Stream stream = provider.OpenRead();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return OnnxModel.Load(buffer.ToArray());
    }

    private static string[] LoadClasses(IPaddleOcrModelProvider dictionary)
    {
        using Stream stream = dictionary.OpenRead();
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        List<string> raw = [];
        while (reader.ReadLine() is { } line)
        {
            // Windows 换行符带出的 \r 去掉；空行保留（PaddleOCR 字典里确有空白字符）
            raw.Add(line.TrimEnd('\r'));
        }
        // PaddleOCR: 索引 0 是 blank，末尾再补一个空格（use_space_char）
        string[] classes = new string[raw.Count + 2];
        classes[0] = string.Empty;
        for (int i = 0; i < raw.Count; i++) classes[i + 1] = raw[i];
        classes[^1] = " ";
        return classes;
    }

    public override string ToString() => $"{Name} ({Language}) classes={Classes.Length} cls={(Classification is null ? "off" : "on")}";
}
