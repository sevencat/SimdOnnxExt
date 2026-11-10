using ImageFacade;
using Sdcb.SimdOnnxExt.Imaging;
using Sdcb.SimdOnnxExt.Yolo;

namespace Sdcb.SimdOnnxExt.Tests;

/// <summary>
/// M1 的端到端回归：`tests/assets/bus.jpg`（810x1080，Ultralytics 官方样例）→ letterbox → 引擎 →
/// DFL/NMS → 源图坐标。期望值是 2026-11-10 用 CPU 后端跑出来的（记在下面的数组里，容差 0.02）。
/// 只覆盖 CPU：GPU 数值一致性是 M1-05 的独立问题（见 docs/gap-matrix.md §5）。
/// </summary>
public class YoloEndToEndTests
{
    // 按分数降序；与 out\bus-cpu.jpg 上画出来的框一致
    private static readonly (string Label, float Score, float X, float Y)[] Expected =
    [
        ("person", 0.6788f, 17.8f, 399.7f),
        ("person", 0.4172f, 208.2f, 388.3f),
        ("car", 0.3826f, 21.3f, 225.2f),
        ("person", 0.3262f, 684.2f, 414.2f),
        ("bus", 0.2961f, 29.3f, 224.4f),
    ];

    [SkippableFact]
    public void DetectsTheBusPhotoLikeRecorded()
    {
        string? image = AssetPath("bus.jpg");
        Skip.If(image is null, "没有 tests/assets/bus.jpg");
        string? model = ModelPath("yolo11n-opset12.onnx");
        Skip.If(model is null, "没有 yolo11n-opset12.onnx（见 docs/model-setup.md）");

        using var engine = ImageHost.CreateEngine();
        using FacadeImage source = engine.Load(image!);
        Assert.Equal(810, source.Width);
        Assert.Equal(1080, source.Height);

        using YoloDetector detector = YoloDetector.Load(model!, BackendKind.Cpu, confidence: 0.20f, iouThreshold: 0.45f);
        Assert.Equal(80, detector.Model.ClassNames.Length);
        Assert.Equal("person", detector.Model.ClassNames[0]);

        YoloDetection[] detections = detector.Detect(source).OrderByDescending(d => d.Score).ToArray();

        Assert.Equal(Expected.Length, detections.Length);
        for (int i = 0; i < detections.Length; i++)
        {
            YoloDetection actual = detections[i];
            (string label, float score, float x, float y) = Expected[i];
            Assert.Equal(label, actual.Label);
            Assert.True(MathF.Abs(actual.Score - score) <= 0.02f, $"#{i} 分数 {actual.Score:F4} vs {score:F4}");
            Assert.True(MathF.Abs(actual.Box.X - x) <= 2f, $"#{i} x {actual.Box.X:F1} vs {x:F1}");
            Assert.True(MathF.Abs(actual.Box.Y - y) <= 2f, $"#{i} y {actual.Box.Y:F1} vs {y:F1}");
        }

        // 画一遍不该抛（含中文标签路径），并保持原图尺寸
        detector.Draw(source, detections);
        Assert.Equal(810, source.Width);
    }

    private static string? AssetPath(string name)
    {
        if (GoldenFixture.RepositoryRoot is not { } root) return null;
        string path = Path.Combine(root, "tests", "assets", name);
        return File.Exists(path) ? path : null;
    }

    private static string? ModelPath(string name)
    {
        if (GoldenFixture.RepositoryRoot is { } root)
        {
            string local = Path.Combine(root, "models", "yolo", name);
            if (File.Exists(local)) return local;
        }
        string fallback = Path.Combine(@"F:\code\SimdYolo\models\onnx", name);
        return File.Exists(fallback) ? fallback : null;
    }
}
