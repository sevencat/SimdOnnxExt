using Sdcb.SimdOnnx.Backends.Metal;
using Sdcb.SimdOnnx.Backends.Vulkan;
using Sdcb.SimdOnnx.Engine;
using Sdcb.SimdOnnx.Onnx;

namespace Sdcb.SimdOnnxExt;

/// <summary>
/// 一个推理后端：把"模型 + 输入"跑成输出，屏蔽 CPU / Vulkan / Metal 的差异。
/// 会话按 (模型, 输入形状) 复用；<see cref="Run"/> 返回的 span 在下一次 Run 之前有效。
/// </summary>
public abstract class InferenceBackend : IDisposable
{
    /// <summary>实际使用的后端（<see cref="BackendKind.Auto"/> 解析后的结果）。</summary>
    public abstract BackendKind Kind { get; }

    /// <summary>后端/设备名（CPU 为 "cpu"，GPU 为驱动报告的名字）。</summary>
    public abstract string Name { get; }

    /// <summary>跑一次前向；<paramref name="input"/> 是模型输入形状的连续 float（逻辑 NCHW）。</summary>
    public abstract ReadOnlySpan<float> Run(OnnxModel model, int[] inputShape, ReadOnlySpan<float> input);

    public virtual void Dispose() { }

    /// <summary>
    /// 选择后端。<see cref="BackendKind.Auto"/> 依次尝试 Vulkan、Metal，都没有就回落 CPU，
    /// 并把原因写进 <paramref name="fallbackReason"/>；明确指定 GPU 但不可用时直接抛错，不静默回落。
    /// </summary>
    public static InferenceBackend Create(BackendKind kind, out string? fallbackReason)
    {
        fallbackReason = null;
        switch (kind)
        {
            case BackendKind.Vulkan:
                if (!VkBackend.TryGetDefault(out VkBackend? explicitVk)) throw new NotSupportedException("没有可用的 Vulkan 设备。");
                return new VulkanInferenceBackend(explicitVk!);
            case BackendKind.Metal:
                if (!MetalBackend.TryGetDefault(out MetalBackend? explicitMtl)) throw new NotSupportedException("没有可用的 Metal 设备（仅 macOS）。");
                return new MetalInferenceBackend(explicitMtl!);
            case BackendKind.Cpu:
                return new CpuInferenceBackend();
            case BackendKind.Auto:
                if (VkBackend.TryGetDefault(out VkBackend? vk)) return new VulkanInferenceBackend(vk!);
                if (MetalBackend.TryGetDefault(out MetalBackend? mtl)) return new MetalInferenceBackend(mtl!);
                fallbackReason = "没有可用的 Vulkan/Metal 设备：回落到 CPU";
                return new CpuInferenceBackend();
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知后端");
        }
    }

    /// <summary>自动选择后端（等价于 <c>Create(BackendKind.Auto, out _)</c>）。</summary>
    public static InferenceBackend Create() => Create(BackendKind.Auto, out _);
}

/// <summary>CPU 后端：<see cref="CpuModel"/> 按形状缓存 plan，<see cref="CpuSession"/> 复用 arena。</summary>
internal sealed class CpuInferenceBackend : InferenceBackend
{
    private readonly Dictionary<OnnxModel, (CpuModel Model, Dictionary<string, CpuSession> Sessions)> _cache = new(ReferenceEqualityComparer.Instance);

    public override BackendKind Kind => BackendKind.Cpu;

    public override string Name => "cpu";

    public override ReadOnlySpan<float> Run(OnnxModel model, int[] inputShape, ReadOnlySpan<float> input)
    {
        if (!_cache.TryGetValue(model, out var entry))
        {
            entry = (new CpuModel(model), new Dictionary<string, CpuSession>(StringComparer.Ordinal));
            _cache[model] = entry;
        }
        string key = string.Join(",", inputShape);
        if (!entry.Sessions.TryGetValue(key, out CpuSession? session))
        {
            session = new CpuSession(entry.Model);
            session.Reshape(inputShape, entry.Model);
            entry.Sessions[key] = session;
        }
        if (input.Length != session.Input.Length)
            throw new ArgumentException($"输入长度 {input.Length} 与模型输入 {session.Input.Length} 不一致", nameof(input));
        input.CopyTo(session.Input);
        return session.Run();
    }

    public override void Dispose()
    {
        foreach (var entry in _cache.Values)
            foreach (CpuSession session in entry.Sessions.Values)
                session.Dispose();
        _cache.Clear();
    }
}

/// <summary>Vulkan 后端：会话按 (模型, 形状) 复用；设备丢失时抛错，由调用方决定重建还是换 CPU。</summary>
internal sealed class VulkanInferenceBackend : InferenceBackend
{
    private readonly VkBackend _backend;
    private readonly Dictionary<(OnnxModel Model, string Shape), VkInferenceSession> _sessions = new();

    public VulkanInferenceBackend(VkBackend backend) => _backend = backend;

    public override BackendKind Kind => BackendKind.Vulkan;

    public override string Name => _backend.DeviceName;

    public override ReadOnlySpan<float> Run(OnnxModel model, int[] inputShape, ReadOnlySpan<float> input)
    {
        if (_backend.IsLost) throw new InvalidOperationException("Vulkan 设备已丢失（IsLost）：重建后端或改用 CPU。");
        (OnnxModel, string) key = (model, string.Join(",", inputShape));
        if (!_sessions.TryGetValue(key, out VkInferenceSession? session))
        {
            session = _backend.NewSession(model, inputShape, VkBackend.PlanOptions);
            _sessions[key] = session;
        }
        if (input.Length != session.Input.Length)
            throw new ArgumentException($"输入长度 {input.Length} 与模型输入 {session.Input.Length} 不一致", nameof(input));
        input.CopyTo(session.Input);
        session.Run();
        return session.Output;
    }

    public override void Dispose()
    {
        foreach (VkInferenceSession session in _sessions.Values) session.Dispose();
        _sessions.Clear();
        _backend.Dispose();
    }
}

/// <summary>Metal 后端（macOS）。</summary>
internal sealed class MetalInferenceBackend : InferenceBackend
{
    private readonly MetalBackend _backend;
    private readonly Dictionary<(OnnxModel Model, string Shape), MtlInferenceSession> _sessions = new();

    public MetalInferenceBackend(MetalBackend backend) => _backend = backend;

    public override BackendKind Kind => BackendKind.Metal;

    public override string Name => _backend.DeviceName;

    public override ReadOnlySpan<float> Run(OnnxModel model, int[] inputShape, ReadOnlySpan<float> input)
    {
        (OnnxModel, string) key = (model, string.Join(",", inputShape));
        if (!_sessions.TryGetValue(key, out MtlInferenceSession? session))
        {
            session = _backend.NewSession(model, inputShape, MetalBackend.PlanOptions);
            _sessions[key] = session;
        }
        if (input.Length != session.Input.Length)
            throw new ArgumentException($"输入长度 {input.Length} 与模型输入 {session.Input.Length} 不一致", nameof(input));
        input.CopyTo(session.Input);
        session.Run();
        return session.Output;
    }

    public override void Dispose()
    {
        foreach (MtlInferenceSession session in _sessions.Values) session.Dispose();
        _sessions.Clear();
        _backend.Dispose();
    }
}
