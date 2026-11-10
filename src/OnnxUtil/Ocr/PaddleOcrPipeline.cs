using System.Text;
using ImageFacade;
using ImageFacade.Pipeline;
using OnnxUtil.Imaging;
using Sdcb.SimdOnnx.Decoding;
using Sdcb.SimdOnnx.Onnx;

namespace OnnxUtil.Ocr;

/// <summary>一行 OCR 结果（源图坐标）。</summary>
public sealed record OcrLine(string Text, float Score, RectF Box)
{
    public override string ToString() => $"{Text}  ({Score:F3})  [{Box.X:F0},{Box.Y:F0},{Box.Width:F0}x{Box.Height:F0}]";
}

/// <summary>
/// PaddleOCR 三段式流水线：DB 检测 → （可选）文本行方向分类 → CTC 识别。
/// 图像操作交给 ImageFacade（Resize/FromPixels），推理交给 <see cref="InferenceBackend"/>，
/// CTC 解码用引擎的 <see cref="CtcGreedyDecoder"/>，所以这里没有第二套算子实现。
/// </summary>
public sealed class PaddleOcrPipeline : IDisposable
{
    private static readonly float[] DetMean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] DetStd = [0.229f, 0.224f, 0.225f];
    private static readonly float[] Half = [0.5f, 0.5f, 0.5f];

    private readonly InferenceBackend _backend;
    private readonly ImageEngine _engine;

    private PaddleOcrPipeline(PaddleOcrModel model, InferenceBackend backend, ImageEngine engine, string? fallbackReason)
    {
        Model = model;
        _backend = backend;
        _engine = engine;
        FallbackReason = fallbackReason;
        DetLimitSide = model.DetLimitSide;
    }

    public PaddleOcrModel Model { get; }
    public InferenceBackend Backend => _backend;
    /// <summary>Auto 回落 CPU 时的原因（否则 null）。</summary>
    public string? FallbackReason { get; }

    /// <summary>检测二值化阈值（PaddleOCR 默认 0.3）。</summary>
    public float DetThreshold { get; set; } = 0.3f;
    /// <summary>框平均分阈值（默认 0.6）。</summary>
    public float BoxThreshold { get; set; } = 0.6f;
    /// <summary>框外扩比例（默认 1.6）。</summary>
    public float UnclipRatio { get; set; } = 1.6f;
    /// <summary>是否用文本行方向分类（默认开；模型没有 cls 时自动跳过）。</summary>
    public bool UseClassification { get; set; } = true;

    /// <summary>
    /// 检测输入的最长边上限（默认取模型建议的 960）。相机大图里字很小的时候调大（如 1600）能明显提升小字识别，
    /// 代价是检测阶段变慢。
    /// </summary>
    public int DetLimitSide { get; set; }

    public static PaddleOcrPipeline Create(PaddleOcrModel model, BackendKind kind = BackendKind.Auto)
    {
        ArgumentNullException.ThrowIfNull(model);
        InferenceBackend backend = InferenceBackend.Create(kind, out string? fallbackReason);
        return new PaddleOcrPipeline(model, backend, ImageHost.CreateEngine(), fallbackReason);
    }

    /// <summary>整页识别：返回按阅读顺序排列的文本行。</summary>
    public IReadOnlyList<OcrLine> Run(FacadeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        (int detWidth, int detHeight) = DetSize(image.Width, image.Height);

        float[] detInput;
        // FacadeImage.Resize 是原地改并返回 this，所以先 Clone 再缩，避免动到调用方的图
        using (FacadeImage resized = image.Clone())
        {
            resized.Resize(detWidth, detHeight);
            detInput = resized.ToTensor(new TensorOptions
            {
                Width = detWidth, Height = detHeight, Channels = ChannelOrder.Rgb, Mean = DetMean, Std = DetStd,
            }, out _);
        }

        ReadOnlySpan<float> probability = _backend.Run(Model.Detection, [1, 3, detHeight, detWidth], detInput);
        List<DbPostprocess.DetBox> boxes = DbPostprocess.Detect(probability, detWidth, detHeight,
            DetThreshold, BoxThreshold, UnclipRatio);

        byte[] rgba = image.CopyPixels(PixelFormat.Rgba32);
        int stride = image.Width * 4;
        float scaleX = (float)image.Width / detWidth;
        float scaleY = (float)image.Height / detHeight;

        List<OcrLine> lines = new(boxes.Count);
        foreach (DbPostprocess.DetBox box in boxes)
        {
            RectF rect = DbPostprocess.Scale(box.Rect, scaleX, scaleY, image.Width, image.Height);
            (int x, int y, int w, int h) = ToPixels(rect);
            if (w < 4 || h < 4) continue;

            byte[] crop = Crop(rgba, stride, x, y, w, h);
            if (UseClassification && Model.Classification is { } cls && IsUpsideDown(crop, w, h, cls))
                Rotate180(crop, w, h);

            (string text, float score) = Recognize(crop, w, h);
            if (text.Length != 0) lines.Add(new OcrLine(text, score, new RectF(x, y, w, h)));
        }
        return lines;
    }

    // ---- 检测尺寸：最长边压到 limit，宽高取 32 的整数倍（PaddleOCR 规则）----
    private (int Width, int Height) DetSize(int width, int height)
    {
        int limit = DetLimitSide > 0 ? DetLimitSide : Model.DetLimitSide;
        float ratio = MathF.Min(1f, limit / (float)Math.Max(width, height));
        int w = Math.Max(32, (int)(MathF.Round(width * ratio / 32f) * 32));
        int h = Math.Max(32, (int)(MathF.Round(height * ratio / 32f) * 32));
        return (w, h);
    }

    private static (int X, int Y, int W, int H) ToPixels(RectF rect)
    {
        int x = (int)MathF.Floor(rect.X);
        int y = (int)MathF.Floor(rect.Y);
        int right = (int)MathF.Ceiling(rect.X + rect.Width);
        int bottom = (int)MathF.Ceiling(rect.Y + rect.Height);
        return (x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    // ---- 从整图 RGBA 缓冲里裁一块（stride 由原图宽决定）----
    private static byte[] Crop(byte[] rgba, int stride, int x, int y, int width, int height)
    {
        byte[] crop = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(rgba, (y + row) * stride + x * 4, crop, row * width * 4, width * 4);
        return crop;
    }

    private static void Rotate180(byte[] crop, int width, int height)
    {
        int pixels = width * height;
        for (int i = 0; i < pixels / 2; i++)
        {
            int j = pixels - 1 - i;
            for (int c = 0; c < 4; c++)
                (crop[i * 4 + c], crop[j * 4 + c]) = (crop[j * 4 + c], crop[i * 4 + c]);
        }
    }

    // ---- 文本行方向分类（PP-LCNet，输入 3x80x160，输出 2 类；1 = 180°）----
    private bool IsUpsideDown(byte[] crop, int width, int height, OnnxModel cls)
    {
        const int targetWidth = 160, targetHeight = 80;
        using FacadeImage image = _engine.FromPixels(crop, width, height, width * 4, PixelFormat.Rgba32);
        image.Resize(targetWidth, targetHeight);      // 原地缩放
        float[] tensor = image.ToTensor(new TensorOptions
        {
            Width = targetWidth, Height = targetHeight, Channels = ChannelOrder.Rgb, Mean = Half, Std = Half,
        }, out _);

        ReadOnlySpan<float> logits = _backend.Run(cls, [1, 3, targetHeight, targetWidth], tensor);
        if (logits.Length < 2) return false;
        float max = MathF.Max(logits[0], logits[1]);
        float p0 = MathF.Exp(logits[0] - max), p1 = MathF.Exp(logits[1] - max);
        float total = p0 + p1;
        return p1 > p0 && p1 / total > 0.9f;
    }

    // ---- CTC 识别：等比缩到高 48（宽度夹在 16..640），取 argmax + 折叠 + 查字典 ----
    private (string Text, float Score) Recognize(byte[] crop, int width, int height)
    {
        const int targetHeight = 48;
        int targetWidth = Math.Clamp((int)MathF.Round(targetHeight * (float)width / height), 16, 640);

        using FacadeImage image = _engine.FromPixels(crop, width, height, width * 4, PixelFormat.Rgba32);
        image.Resize(targetWidth, targetHeight);      // 原地缩放
        float[] tensor = image.ToTensor(new TensorOptions
        {
            Width = targetWidth, Height = targetHeight, Channels = ChannelOrder.Rgb, Mean = Half, Std = Half,
        }, out _);

        ReadOnlySpan<float> logits = _backend.Run(Model.Recognition, [1, 3, targetHeight, targetWidth], tensor);
        int classes = Model.Classes.Length;
        int steps = classes > 0 ? logits.Length / classes : 0;
        if (steps <= 0) return (string.Empty, 0f);

        int[] indices = new int[steps];
        float[] maxes = new float[steps];
        for (int t = 0; t < steps; t++)
            indices[t] = CtcGreedyDecoder.ArgMax(logits.Slice(t * classes, classes), out maxes[t]);

        CtcToken[] tokens = new CtcToken[steps];
        int count = CtcGreedyDecoder.Collapse(indices, maxes, tokens, blank: 0);

        // PP-OCRv6 的 rec 图末尾带 Softmax：输出已经是概率，分数直接取该步的 argmax 值；
        // 若换成输出 logits 的模型（maxes 会 > 1），再走一次 softmax。
        bool probabilityOutput = true;
        for (int t = 0; t < steps; t++)
        {
            if (maxes[t] > 1.0001f)
            {
                probabilityOutput = false;
                break;
            }
        }

        StringBuilder text = new(count);
        double scoreSum = 0;
        for (int i = 0; i < count; i++)
        {
            CtcToken token = tokens[i];
            if ((uint)token.Class < (uint)classes) text.Append(Model.Classes[token.Class]);
            scoreSum += probabilityOutput
                ? maxes[token.Start]
                : CtcGreedyDecoder.MaxProbability(logits.Slice(token.Start * classes, classes), maxes[token.Start]);
        }
        return (text.ToString(), count == 0 ? 0f : (float)(scoreSum / count));
    }

    public void Dispose()
    {
        _backend.Dispose();
        _engine.Dispose();
    }
}
