using ImageFacade;
using ImageFacade.Pipeline;
using Sdcb.SimdOnnxExt.Imaging;

namespace Sdcb.SimdOnnxExt.Yolo;

/// <summary>一次检测的结果（源图坐标 + 类别/分数）。</summary>
public sealed record YoloDetection(int ClassId, string Label, float Score, RectF Box)
{
    public override string ToString() => $"{Label} {Score:F3} [{Box.X:F1},{Box.Y:F1},{Box.Width:F1},{Box.Height:F1}]";
}

/// <summary>
/// 检测流水线：图片 → letterbox/张量（ImageFacade）→ 引擎前向 → DFL 解码/NMS → 源图坐标。
/// 会话由 <see cref="InferenceBackend"/> 按形状复用，所以同一实例可以连续喂不同尺寸的图。
/// </summary>
public sealed class YoloDetector : IDisposable
{
    private readonly InferenceBackend _backend;

    private YoloDetector(YoloModel model, InferenceBackend backend, string? fallbackReason)
    {
        Model = model;
        _backend = backend;
        FallbackReason = fallbackReason;
    }

    public YoloModel Model { get; }

    public InferenceBackend Backend => _backend;

    /// <summary>Auto 回落 CPU 时的原因（否则 null）。</summary>
    public string? FallbackReason { get; }

    /// <summary>分数阈值。</summary>
    public float Confidence { get; set; } = 0.25f;

    /// <summary>NMS 的 IoU 阈值。</summary>
    public float IouThreshold { get; set; } = 0.45f;

    /// <summary>最近一次推理的 letterbox 几何（调试用）。</summary>
    public LetterboxInfo LastLetterbox { get; private set; }

    public static YoloDetector Load(string modelPath, BackendKind kind = BackendKind.Auto,
        float confidence = 0.25f, float iouThreshold = 0.45f)
    {
        YoloModel model = YoloModel.Load(modelPath);
        if (model.Task != "detect")
            throw new NotSupportedException($"当前只接了检测（task={model.Task}）；分割/姿态在 M2。");
        InferenceBackend backend = InferenceBackend.Create(kind, out string? fallbackReason);
        return new YoloDetector(model, backend, fallbackReason) { Confidence = confidence, IouThreshold = iouThreshold };
    }

    /// <summary>对一张源图跑检测（返回源图坐标）。</summary>
    public YoloDetection[] Detect(FacadeImage image)
    {
        int height = Model.InputShape[2];
        int width = Model.InputShape[3];
        float[] tensor = ImageHost.ToTensor(image, width, height, out LetterboxInfo info);
        LastLetterbox = info;
        return Detect(tensor, info);
    }

    /// <summary>对已 letterbox 的张量跑检测（测试/批处理用）。</summary>
    public YoloDetection[] Detect(ReadOnlySpan<float> tensor, in LetterboxInfo info)
    {
        ReadOnlySpan<float> output = _backend.Run(Model.Graph, Model.InputShape, tensor);
        Detection[] raw = YoloPostprocess.Detect(output, Model.Channels, Model.Anchors,
            Model.ClassNames.Length, Confidence, IouThreshold, info);

        YoloDetection[] result = new YoloDetection[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            Detection d = raw[i];
            string label = d.ClassId >= 0 && d.ClassId < Model.ClassNames.Length ? Model.ClassNames[d.ClassId] : "class_" + d.ClassId;
            result[i] = new YoloDetection(d.ClassId, label, d.Score, d.Box);
        }
        return result;
    }

    /// <summary>把结果画到图上（原地绘制，颜色按类别从调色板取，标签含中文也可）。</summary>
    public FacadeImage Draw(FacadeImage image, ReadOnlySpan<YoloDetection> detections, DetectionStyle? style = null)
    {
        Detection[] drawable = new Detection[detections.Length];
        for (int i = 0; i < detections.Length; i++)
        {
            YoloDetection d = detections[i];
            drawable[i] = new Detection(d.Box, d.Label, d.Score) { ClassId = d.ClassId };
        }
        return image.DrawDetections(drawable, style);
    }

    public void Dispose() => _backend.Dispose();
}
