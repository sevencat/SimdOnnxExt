using ImageFacade;
using ImageFacade.Pipeline;
using ImageFacade.SdcbSimdCodecs;

namespace Sdcb.SimdOnnxExt.Imaging;

/// <summary>
/// 图像侧的唯一接入点：ImageFacade 的门面引擎（纯托管软后端）+ 本仓库的默认约定。
/// 编解码格式由调用方引用 <c>Sdcb.SimdCodecs.Xxx</c> 包并注册（见 models/README.md）。
/// </summary>
public static class ImageHost
{
    /// <summary>新建一个纯托管图像引擎（自绘 / Resize / 文字 / letterbox / NMS 都可用；Load/Save 需先注册格式）。</summary>
    public static ImageEngine CreateEngine() => new(new SoftwareBackend());

    /// <summary>YOLO 风格 letterbox：114 灰、居中；<paramref name="info"/> 用于把模型坐标反算回原图。</summary>
    public static FacadeImage Letterbox(FacadeImage source, int width, int height, out LetterboxInfo info,
        Rgba32? padColor = null, bool center = true)
        => source.Apply(width, height, out info, padColor ?? ImageFacade.Pipeline.Letterbox.DefaultPadColor, center);

    /// <summary>
    /// 模型输入张量（默认 NCHW / RGB / ÷255），内部先做 letterbox，
    /// 因此 <paramref name="info"/> 与张量天然一致（不需要自己再算一次缩放/填边）。
    /// </summary>
    public static float[] ToTensor(FacadeImage source, int width, int height, out LetterboxInfo info,
        bool nhwc = false, ChannelOrder channels = ChannelOrder.Rgb)
        => source.ToTensor(new TensorOptions
        {
            Width = width,
            Height = height,
            Layout = nhwc ? TensorLayout.Nhwc : TensorLayout.Nchw,
            Channels = channels,
        }, out info);
}
