using ImageFacade;
using ImageFacade.Backends;
using ImageFacade.Pipeline;
using ImageFacade.SdcbSimdCodecs;
using Sdcb.SimdCodecs.Jpeg;
using Sdcb.SimdCodecs.Png;

namespace Sdcb.SimdOnnxExt.Imaging;

/// <summary>
/// 图像侧的唯一接入点：ImageFacade 的门面引擎（纯托管软后端）+ 本仓库的默认约定。
/// 编解码由 <see cref="RegisterCommonCodecs"/> 注册（核心包已带 JPEG/PNG）。
/// </summary>
public static class ImageHost
{
    private static bool s_codecs;

    /// <summary>注册 JPEG / PNG 编解码（幂等）。用 <c>ImageEngine.Load/Save</c> 之前调一次。</summary>
    public static void RegisterCommonCodecs()
    {
        if (s_codecs) return;
        ImageBackendBase.RegisterCodec(ImageFormat.Jpeg, JpegImageDecoder.Registration, JpegImageEncoder.Registration,
            request => new JpegEncodeOptions { Quality = Math.Clamp(request.Quality, 1, 100) });
        ImageBackendBase.RegisterCodec(ImageFormat.Png, PngImageDecoder.Registration, PngImageEncoder.Registration);
        s_codecs = true;
    }

    /// <summary>新建一个纯托管图像引擎（自动注册 JPEG/PNG；自绘 / Resize / 文字 / letterbox / NMS 都可用）。</summary>
    public static ImageEngine CreateEngine()
    {
        RegisterCommonCodecs();
        return new ImageEngine(new SoftwareBackend());
    }

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
