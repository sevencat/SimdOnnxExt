using System.Text.Json;
using System.Text.Json.Serialization;

namespace OnnxUtil.Tests;

/// <summary>
/// 一份 ORT 金标准（<c>tools/fixtures/record.py</c> 录制）。测试只读产物，不依赖 onnxruntime。
/// </summary>
internal sealed class GoldenFixture
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private GoldenFixture(MetaRoot meta, string directory)
    {
        Id = meta.Id;
        ModelPath = meta.Model;
        ModelSha256 = meta.ModelSha256;
        RecordedAt = meta.RecordedAt;
        Directory = directory;
        Input = meta.Input;
        Outputs = meta.Outputs;
    }

    public string Id { get; }
    /// <summary>录制时用的模型绝对路径（只作来源记录；<see cref="ResolveModel"/> 会优先用仓库 models/）。</summary>
    public string ModelPath { get; }
    public string ModelSha256 { get; }
    public string RecordedAt { get; }
    public string Directory { get; }
    public MetaTensor Input { get; }
    public MetaTensor[] Outputs { get; }

    /// <summary>仓库根（含 <c>OnnxUtil.slnx</c>）。</summary>
    public static string? RepositoryRoot
    {
        get
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OnnxUtil.slnx"))) dir = dir.Parent;
            return dir?.FullName;
        }
    }

    /// <summary>找 <c>tests/Fixtures/&lt;relative&gt;</c>；不存在返回 null（测试用 Skip）。</summary>
    public static string? FindDirectory(string relative)
    {
        if (RepositoryRoot is not { } root) return null;
        string dir = Path.Combine(root, "tests", "Fixtures", relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(Path.Combine(dir, "meta.json")) ? dir : null;
    }

    public static GoldenFixture Load(string directory)
    {
        MetaRoot meta = JsonSerializer.Deserialize<MetaRoot>(File.ReadAllText(Path.Combine(directory, "meta.json")), Options)
            ?? throw new InvalidDataException($"{directory}: meta.json 解析失败");
        return new GoldenFixture(meta, directory);
    }

    /// <summary>优先用仓库 <c>models/</c> 下的同名文件，否则回退录制时的路径；都没有返回 null。</summary>
    public string? ResolveModel(string relativeInModels)
    {
        if (RepositoryRoot is { } root)
        {
            string local = Path.Combine(root, "models", relativeInModels.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(local)) return local;
        }
        return File.Exists(ModelPath) ? ModelPath : null;
    }

    public float[] ReadInput() => ReadFloats(Input.File, (int)ElementCount(Input.Shape));

    public float[] ReadOutput(int index) => ReadFloats(Outputs[index].File, (int)ElementCount(Outputs[index].Shape));

    public static long ElementCount(int[] shape)
    {
        long n = 1;
        foreach (int d in shape) n *= d;
        return n;
    }

    private float[] ReadFloats(string file, int count)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(Directory, file));
        if (bytes.Length != (long)count * sizeof(float))
            throw new InvalidDataException($"{file}: {bytes.Length} 字节，期望 {count * sizeof(float)}");
        float[] values = new float[count];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    internal sealed record MetaTensor(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("shape")] int[] Shape,
        [property: JsonPropertyName("file")] string File);

    internal sealed record MetaRoot(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("model_sha256")] string ModelSha256,
        [property: JsonPropertyName("input")] MetaTensor Input,
        [property: JsonPropertyName("outputs")] MetaTensor[] Outputs,
        [property: JsonPropertyName("recorded_at")] string RecordedAt);
}
