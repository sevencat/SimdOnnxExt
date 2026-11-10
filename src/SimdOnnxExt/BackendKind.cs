namespace Sdcb.SimdOnnxExt;

/// <summary>
/// 执行后端。<see cref="Auto"/> 会尝试 Vulkan（Metal 在 macOS 上由同一条路径命中），
/// 设备不可用或已丢失时回落 CPU，并通过 <c>BackendName</c>/<c>FallbackReason</c> 说明原因。
/// </summary>
public enum BackendKind
{
    /// <summary>自动选择：能用的 GPU 后端优先，否则 CPU。</summary>
    Auto = 0,

    /// <summary>CPU（<c>CpuModel</c> / <c>CpuSession</c>，SIMD 分级在引擎内部选）。</summary>
    Cpu = 1,

    /// <summary>Vulkan（<c>VkBackend</c>；仅 net10.0）。</summary>
    Vulkan = 2,

    /// <summary>Metal（<c>MetalBackend</c>；仅 macOS / net10.0）。</summary>
    Metal = 3,
}
