using Sdcb.SimdOnnxExt.Onnx;

namespace Sdcb.SimdOnnxExt.Tests;

/// <summary>
/// M0-03：<see cref="GraphInspector"/> 在真实模型上的结论。
/// fixture 优先取仓库 <c>models/</c>，没有就退回本机导出路径；两边都没有则跳过（CI 不依赖大文件）。
/// </summary>
public class GraphInspectorTests
{
    private static string? Resolve(string relative, string fallback)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SimdOnnxExt.slnx"))) dir = dir.Parent;
        if (dir is not null)
        {
            string local = Path.Combine(dir.FullName, relative);
            if (File.Exists(local)) return local;
        }
        return File.Exists(fallback) ? fallback : null;
    }

    private static string? Yolo(string file) => Resolve(Path.Combine("models", "yolo", file), Path.Combine(@"F:\code\SimdYolo\models\onnx", file));

    private static string? RfDetr(string file) => Resolve(Path.Combine("models", "rfdetr", file), Path.Combine(@"F:\code\TinyLabel\train\testrftedr\runs", file));

    [SkippableFact]
    public void Yolo11n_OpSet12_CompilesOnTheEngine()
    {
        string? path = Yolo("yolo11n-opset12.onnx");
        Skip.If(path is null, "找不到 yolo11n-opset12.onnx");

        GraphReport report = GraphInspector.Inspect(path!);

        Assert.True(report.Compiles, "opset12 导出应当能被引擎直接编译；blocker=" + report.Blocker);
        // 引擎在 Load 时折叠 Constant / Conv+BN，节点数小于文件里的 454 是正常的
        Assert.True(report.NodeCount > 300, $"节点数 {report.NodeCount}");
        // 注意：SimdYolo 的 *-opset12.onnx 是 128x128 小尺寸导出（336 anchors），不是 640
        Assert.Equal("[1,3,128,128]", report.Inputs[0].ShapeText);
        Assert.Single(report.Outputs);
        Assert.Contains(report.Ops, o => o.Op == "Conv" && o.Supported);
        Assert.True(report.IsRunnable, "只剩主机侧解码，不该有 E/P 级缺口");
    }

    [SkippableFact]
    public void Yolo11n_NewExport_BlockerIsTheDflSoftmaxAxis()
    {
        string? path = Yolo("yolo11n.onnx");
        Skip.If(path is null, "找不到 yolo11n.onnx");

        GraphReport report = GraphInspector.Inspect(path!);

        Assert.False(report.Compiles);
        Assert.NotNull(report.Blocker);
        Assert.Contains("Softmax", report.Blocker!, StringComparison.Ordinal);
        Assert.Contains("last", report.Blocker!, StringComparison.Ordinal);
        Assert.Contains(report.Issues, i => i.Route == FixRoute.Engine && i.PlanTask == "E-01" && i.Op == "Softmax");
    }

    [SkippableFact]
    public void Yolo11nSeg_ReportsMultipleOutputs()
    {
        string? path = Yolo("yolo11n-seg.onnx");
        Skip.If(path is null, "找不到 yolo11n-seg.onnx");

        GraphReport report = GraphInspector.Inspect(path!);

        Assert.Equal(2, report.Outputs.Length);
        Assert.Contains(report.Issues, i => i.PlanTask == "E-02" && i.Op == "Outputs");
        Assert.Contains(report.Outputs, o => o.Name == "output1" && o.ShapeText == "[1,32,160,160]");
    }

    [SkippableFact]
    public void RfDetr_HeaderShowsTheKnownEngineGaps()
    {
        string? path = RfDetr("rfdetr-small.onnx");
        Skip.If(path is null, "找不到 rfdetr-small.onnx");

        // 不探测：这个模型 1500+ 节点、上百 MB 权重，M0-03 只验证头部与静态判断
        GraphReport report = GraphInspector.Inspect(path!, probe: false);

        Assert.Equal(17, report.Opset);
        Assert.Equal("[1,3,512,512]", report.Inputs[0].ShapeText);
        Assert.Equal(2, report.Outputs.Length);
        Assert.Contains(report.Issues, i => i.PlanTask == "E-02");
        Assert.Contains(report.Issues, i => i.Op == "TopK");
        Assert.Contains(report.Issues, i => i.Op == "GridSample");
        Assert.True(report.InitializerCount > 300, $"权重数 {report.InitializerCount}");
        Assert.False(report.IsRunnable);
    }

    [SkippableFact]
    public void Header_KeepsSymbolicDimsForDynamicExports()
    {
        string? path = Yolo("yolo11n-dynamic.onnx");
        Skip.If(path is null, "找不到 yolo11n-dynamic.onnx");

        OnnxHeader header = OnnxHeader.Load(path!);

        Assert.Single(header.Inputs);
        Assert.True(header.Inputs[0].HasDynamicDims);
        Assert.Contains("batch", header.Inputs[0].ShapeText);       // 符号维保留原名，不吞成 ?
        Assert.NotEmpty(header.Nodes);
    }
}
