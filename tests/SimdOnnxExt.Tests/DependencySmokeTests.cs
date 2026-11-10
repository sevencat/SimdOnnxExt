using ImageFacade;
using ImageFacade.Pipeline;
using ImageFacade.SdcbSimdCodecs;
using Sdcb.SimdOnnx.Engine;

namespace Sdcb.SimdOnnxExt.Tests;

/// <summary>
/// M0-01 的依赖冒烟：证明两条引用通道（引擎 + ImageFacade）在编译期与运行期都可用。
/// 不做模型推理，只碰公共类型与最小调用路径，避免把 M0 的骨架验证拖到 M1。
/// </summary>
public class DependencySmokeTests
{
    [Fact]
    public void Engine_PublicSurface_IsReachable()
    {
        CompileOptions defaults = CompileOptions.Default;
        Assert.Equal(PlanTarget.Cpu, defaults.Target);
        Assert.False(defaults.Gpu);
        Assert.NotNull(typeof(BackendKind));   // 本仓库自己的公共类型也参与编译
    }

    [Fact]
    public void ImageFacade_PipelineTypes_AreReachable()
    {
        var options = new LetterboxOptions { Width = 640, Height = 640 };
        Assert.Equal(640, options.Width);
        Assert.Equal((byte)114, Letterbox.DefaultPadColor.R);   // YOLO 的 letterbox 灰

        // 两个 10x10 方框横向重叠 5：交 50 / 并 150
        float iou = Nms.IoU(new RectF(0, 0, 10, 10), new RectF(5, 0, 10, 10));
        Assert.Equal(1f / 3f, iou, 3);

        Assert.NotNull(typeof(Detection));
        Assert.NotNull(typeof(DetectionDrawing));
        Assert.NotNull(typeof(Tensorizer));
    }

    [Fact]
    public void ImageFacade_SoftwareBackend_LetterboxTensorizes()
    {
        using var engine = new ImageEngine(new SoftwareBackend());
        using FacadeImage image = engine.Create(320, 200, new Rgba32(10, 20, 30));

        float[] tensor = image.ToTensor(new TensorOptions { Width = 64, Height = 64 }, out LetterboxInfo info);

        Assert.Equal(3 * 64 * 64, tensor.Length);
        Assert.Equal(64, info.ModelSize.Width);
        Assert.Equal(40, info.ScaledSize.Height);        // 320x200 -> 0.2x -> 64x40
        Assert.Equal(12, info.PadTop);                  // (64-40)/2
        Assert.Equal(0, info.PadLeft);
        Assert.True(info.Scale > 0f);

        // 反算：模型坐标 (32, 32) → 原图坐标（letterbox 居中）
        PointF source = info.ToSource(32f, 32f);
        Assert.Equal(160f, source.X, 2);
        Assert.Equal(100f, source.Y, 2);
    }
}
