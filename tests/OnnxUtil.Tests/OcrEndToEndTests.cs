using ImageFacade;
using OnnxUtil.Imaging;
using OnnxUtil.Ocr;

namespace OnnxUtil.Tests;

/// <summary>
/// M3 的端到端回归（自包含，不依赖外部图片）：用 ImageFacade 画两行字（中文 + 拉丁/数字），
/// 再让 PP-OCRv6 tiny 把它读回来——检测、方向分类、裁剪、CTC 解码与字典全部串一遍。
/// </summary>
public class OcrEndToEndTests
{
    [Fact]
    public void ChineseText_RoundTripsThroughOcr()
    {
        using var engine = ImageHost.CreateEngine();
        using FacadeImage canvas = engine.Create(820, 240, Rgba32.White);
        canvas.DrawText("你好世界", new PointF(30, 24), new TextStyle { Size = 76, Color = Rgba32.Black });
        canvas.DrawText("OnnxUtil 123", new PointF(30, 140), new TextStyle { Size = 64, Color = Rgba32.Black });

        using PaddleOcrPipeline pipeline = PaddleOcrPipeline.Create(PaddleOcrModel.ChineseV6Tiny(), BackendKind.Cpu);
        IReadOnlyList<OcrLine> lines = pipeline.Run(canvas);
        string text = string.Concat(lines.Select(l => l.Text));

        // 实测：tiny 在合成渲染上读成 "你好世界OnnyLUtil123"（中文字形与数字全对，
        // 拉丁字母 x/y 会被 tiny 混淆），所以这里按"能力"断言而不是逐字匹配。
        Assert.True(lines.Count >= 2, $"只检测到 {lines.Count} 行：{text}");
        Assert.Contains("你好世界", text);
        Assert.Contains("123", text);
        Assert.Contains("util", text, StringComparison.OrdinalIgnoreCase);
        Assert.All(lines, l => Assert.True(l.Score > 0.5f, $"行分数过低：{l}"));
    }
}
