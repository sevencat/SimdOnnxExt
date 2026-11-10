using System.Text.RegularExpressions;
using Sdcb.SimdOnnx.Onnx;
using Sdcb.SimdOnnxExt.Onnx;

namespace Sdcb.SimdOnnxExt.Yolo;

/// <summary>
/// 一个 YOLO 模型：图 + 元数据（Ultralytics 会写 <c>task</c> 与 <c>names</c>）+ 输入/输出形状。
/// 只做"模型侧"的事，图像与解码在 <see cref="YoloDetector"/> / <see cref="YoloPostprocess"/>。
/// </summary>
public sealed class YoloModel
{
    private static readonly Regex NameEntry = new(@"(\d+)\s*:\s*'([^']*)'", RegexOptions.Compiled);

    private YoloModel(OnnxModel model, OnnxHeader header, string task, string[] classNames)
    {
        Graph = model;
        Header = header;
        Task = task;
        ClassNames = classNames;
        InputShape = header.Inputs.Count > 0 ? header.Inputs[0].Dims : [];
        OutputShapes = [.. header.Outputs.Select(o => o.Dims)];
    }

    /// <summary>引擎的模型对象（推理时传给 <see cref="InferenceBackend"/>）。</summary>
    public OnnxModel Graph { get; }

    /// <summary>头部信息（opset / 图 I/O / metadata）。</summary>
    public OnnxHeader Header { get; }

    /// <summary>Ultralytics 的 <c>task</c>：detect / segment / pose / classify；缺省按 detect。</summary>
    public string Task { get; }

    /// <summary>类别名（来自 <c>names</c>）；解析不到时是 <c>class_0..class_N</c>。</summary>
    public string[] ClassNames { get; }

    /// <summary>图输入形状（探测形状）。</summary>
    public int[] InputShape { get; }

    /// <summary>每个图输出的形状。</summary>
    public int[][] OutputShapes { get; }

    /// <summary>检测头通道数 = 4 + 类别数（分割/姿态模型会更大，M2 再细分）。</summary>
    public int Channels => OutputShapes[0].Length >= 3 ? OutputShapes[0][1] : 0;

    /// <summary>anchor 数（输出最后一维）。</summary>
    public int Anchors => OutputShapes[0].Length >= 3 ? OutputShapes[0][^1] : 0;

    public static YoloModel Load(string path)
    {
        OnnxHeader header = OnnxHeader.Load(path);
        OnnxModel graph = OnnxModel.Load(path);
        string task = header.Metadata.TryGetValue("task", out string? t) && t.Length > 0 ? t : "detect";
        return new YoloModel(graph, header, task, ParseNames(header));
    }

    private static string[] ParseNames(OnnxHeader header)
    {
        if (header.Metadata.TryGetValue("names", out string? names) && names.Length > 0)
        {
            MatchCollection matches = NameEntry.Matches(names);
            if (matches.Count > 0)
            {
                string[] result = new string[matches.Count];
                foreach (Match m in matches)
                {
                    int index = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (index >= 0 && index < result.Length) result[index] = m.Groups[2].Value;
                }
                for (int i = 0; i < result.Length; i++) result[i] ??= "class_" + i;
                return result;
            }
        }

        // 没有元数据：按输出通道推类别数（4 + nc），名字用占位
        int channels = header.Outputs.Count > 0 && header.Outputs[0].Dims.Length >= 3 ? header.Outputs[0].Dims[1] : 0;
        int classes = Math.Max(0, channels - 4);
        string[] fallback = new string[classes];
        for (int i = 0; i < classes; i++) fallback[i] = "class_" + i;
        return fallback;
    }

    public override string ToString()
        => $"task={Task} classes={ClassNames.Length} in=[{string.Join(",", InputShape)}] out=[{string.Join(",", OutputShapes[0])}]";
}
