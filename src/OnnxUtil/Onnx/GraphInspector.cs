using Sdcb.SimdOnnx.Engine;
using Sdcb.SimdOnnx.Onnx;

namespace OnnxUtil.Onnx;

/// <summary>缺口的消解路线（见 docs/gap-matrix.md）。</summary>
public enum FixRoute
{
    /// <summary>不是缺口。</summary>
    None = 0,
    /// <summary>改引擎：新的 ONNX 逻辑算子 / 多输出 / Softmax 任意轴 / int64 图输入。</summary>
    Engine = 1,
    /// <summary>离线图预处理：剪枝、单输出封装、等价改写（tools/OnnxUtil.ModelPrep）。</summary>
    Preprocess = 2,
    /// <summary>主机侧 C#：模型族专属解码（DFL / top-k / 掩码 / OCR 后处理）。</summary>
    Host = 3,
}

/// <summary>
/// 一条兼容性结论：哪个节点、为什么、走哪条路、对应计划里的哪个任务。
/// <paramref name="Predicted"/> 为 true 表示"按属性推断"（引擎没确认过），编译成功后这类结论会被丢弃。
/// </summary>
public sealed record GraphIssue(string Node, string Op, string Reason, FixRoute Route, string? PlanTask, bool Predicted = false);

/// <summary>算子使用统计；<see cref="Supported"/> 来自引擎 <c>GraphCompiler</c> 的支持集。</summary>
public sealed record OpUse(string Op, int Count, bool Supported);

/// <summary>
/// <c>inspect</c> 的结果。<see cref="Compiles"/> 是**引擎的真实判定**（编译探测），其余字段只是解释与预测。
/// </summary>
public sealed record GraphReport(
    string Source,
    int Opset,
    string Producer,
    int NodeCount,
    int InitializerCount,
    OnnxTensorSpec[] Inputs,
    OnnxTensorSpec[] Outputs,
    OpUse[] Ops,
    GraphIssue[] Issues,
    int[]? ProbeShape,
    bool? Compiles,
    string? Blocker)
{
    /// <summary>没有"必须改引擎或预处理"才能跑的缺口（<see cref="FixRoute.Host"/> 只是还没写解码）。</summary>
    public bool IsRunnable => Compiles ?? Issues.All(i => i.Route is FixRoute.None or FixRoute.Host);
}

/// <summary>
/// 引擎优先的体检：算子直方图取自 <see cref="OnnxModel.Nodes"/>，能否运行由
/// <see cref="CompiledModel.Compile(OnnxModel, ReadOnlySpan{int})"/> 探测（失败信息点名节点），
/// <see cref="OnnxHeader"/> 只补引擎公共面没有的事实（图 I/O 形状、节点属性）。
/// </summary>
public static class GraphInspector
{
    // 引擎 GraphCompiler.EmitNode 的 float 分支
    private static readonly HashSet<string> FloatOps = new(StringComparer.Ordinal)
    {
        "Add", "Sub", "Mul", "Div", "Conv", "ConvTranspose", "MaxPool", "AveragePool", "GlobalAveragePool",
        "BatchNormalization", "Relu", "Sigmoid", "Tanh", "LeakyRelu", "HardSigmoid", "Clip", "Erf", "Sqrt",
        "Pow", "MatMul", "Gemm", "Softmax", "LayerNormalization", "ReduceMean", "Resize", "Transpose",
        "Reshape", "Squeeze", "Unsqueeze", "Concat", "Split", "Slice", "Pad", "DepthToSpace", "Identity",
    };

    // 只在"全部输入都是编译期整数张量"时才可用（GraphCompiler.IntOps + Shape/Size/Constant）
    private static readonly HashSet<string> IntOps = new(StringComparer.Ordinal)
    {
        "Shape", "Size", "Constant", "Mod", "Cast", "Gather", "Floor", "Ceil", "Neg", "Abs", "Min", "Max",
        "Equal", "Less", "Where", "ConstantOfShape", "Expand", "Tile",
    };

    // 典型"图内后处理/动态构造"算子：要么进引擎（E-03），要么剪掉自己写（P）
    private static readonly HashSet<string> TailOps = new(StringComparer.Ordinal)
    {
        "TopK", "GatherElements", "NonMaxSuppression", "GridSample", "Sin", "Cos", "Exp", "Not", "And",
        "Greater", "ReduceMax", "ReduceSum", "Flatten", "ScatterND", "ArgMax",
    };

    /// <param name="path">模型文件。</param>
    /// <param name="probeShape">探测形状；null 时取第一个图输入的静态维。</param>
    /// <param name="probe">是否真的让引擎编译一次（默认 true：这是唯一可靠的"能不能跑"）。</param>
    public static GraphReport Inspect(string path, int[]? probeShape = null, bool probe = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        OnnxHeader header = OnnxHeader.Load(path);

        // 引擎自己的解析器：只有探测时才付权重解码的代价
        OnnxModel? model = probe ? OnnxModel.Load(path) : null;
        int[]? shape = probeShape ?? StaticInputShape(header);
        bool? compiles = null;
        string? blocker = null;
        if (model is not null && shape is not null)
        {
            try
            {
                CompiledModel plan = CompiledModel.Compile(model, shape);
                compiles = plan.NodeCount >= 0;
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or ArgumentException or InvalidOperationException)
            {
                compiles = false;
                blocker = ex.GetType().Name + ": " + FirstLine(ex.Message);
            }
        }

        IEnumerable<string> opTypes = model is not null ? model.Nodes.Select(n => n.OpType) : header.Nodes.Select(n => n.OpType);
        List<OpUse> ops = [.. opTypes
            .GroupBy(op => op, StringComparer.Ordinal)
            .Select(g => new OpUse(g.Key, g.Count(), FloatOps.Contains(g.Key) || IntOps.Contains(g.Key)))
            .OrderByDescending(o => o.Count)
            .ThenBy(o => o.Op, StringComparer.Ordinal)];

        List<GraphIssue> issues = [];
        if (header.Outputs.Count != 1)
            issues.Add(new GraphIssue("(graph)", "Outputs", $"图有 {header.Outputs.Count} 个输出（引擎只支持单输出）", FixRoute.Engine, "E-02"));
        foreach (OnnxTensorSpec input in header.Inputs)
        {
            if (!input.IsFloat)
                issues.Add(new GraphIssue(input.Name, "Input", $"图输入 elem_type={input.ElementType}（引擎只接受 float32）", FixRoute.Engine, "E-04"));
            if (input.HasDynamicDims)
                issues.Add(new GraphIssue(input.Name, "Input", $"动态维度 {input.ShapeText}：探测/编译需要显式形状", FixRoute.None, null));
        }
        foreach (OnnxNodeSpec node in header.Nodes) CheckNode(node, issues);
        if (compiles == true) issues.RemoveAll(i => i.Predicted);   // 引擎已经确认能编译：属性推断作废

        return new GraphReport(path, header.Opset, header.Producer, model?.Nodes.Count ?? header.Nodes.Count, header.Initializers.Count,
            [.. header.Inputs], [.. header.Outputs], [.. ops], [.. issues], shape, compiles, blocker);
    }

    /// <summary>第一个图输入全是静态维时用它当探测形状。</summary>
    private static int[]? StaticInputShape(OnnxHeader header)
        => header.Inputs.Count > 0 && header.Inputs[0].Dims.Length > 0 && !header.Inputs[0].HasDynamicDims
            ? (int[])header.Inputs[0].Dims.Clone()
            : null;

    private static string FirstLine(string text)
    {
        int i = text.IndexOfAny(['\r', '\n']);
        return i < 0 ? text : text[..i];
    }

    private static void CheckNode(OnnxNodeSpec node, List<GraphIssue> issues)
    {
        string op = node.OpType;
        if (TailOps.Contains(op))
        {
            issues.Add(new GraphIssue(node.Name, op, "图内动态/后处理算子：引擎没有对应实现", FixRoute.Engine, "E-03"));
            return;
        }
        if (!FloatOps.Contains(op) && !IntOps.Contains(op))
        {
            issues.Add(new GraphIssue(node.Name, op, "不在引擎支持集内", FixRoute.Engine, "E-03"));
            return;
        }

        switch (op)
        {
            case "Softmax":
            {
                long axis = node.Attr("axis")?.IntOr(-1) ?? -1;
                if (axis != -1)
                    issues.Add(new GraphIssue(node.Name, op, $"axis={axis}：引擎只支持最后一维", FixRoute.Engine, "E-01", Predicted: true));
                break;
            }
            case "ConvTranspose":
            {
                long group = node.Attr("group")?.IntOr(1) ?? 1;
                long[] pads = node.Attr("pads")?.IntsOrEmpty ?? [];
                long[] strides = node.Attr("strides")?.IntsOrEmpty ?? [];
                long[] kernel = node.Attr("kernel_shape")?.IntsOrEmpty ?? [];
                bool strideOk = strides.Length == 0 || kernel.Length == 0 || strides.SequenceEqual(kernel);
                if (group != 1 || pads.Any(p => p != 0) || !strideOk)
                    issues.Add(new GraphIssue(node.Name, op,
                        $"group={group} pads=[{string.Join(",", pads)}] strides=[{string.Join(",", strides)}]：引擎只支持 group=1、stride==kernel、无 pad",
                        FixRoute.Engine, "E-03", Predicted: true));
                break;
            }
            case "Resize":
            {
                string mode = node.Attr("mode")?.String ?? "nearest";
                string ctm = node.Attr("coordinate_transformation_mode")?.String ?? "half_pixel";
                string nearestMode = node.Attr("nearest_mode")?.String ?? "round_prefer_floor";
                long antialias = node.Attr("antialias")?.IntOr(0) ?? 0;
                bool nearestOk = mode == "nearest" && ctm == "asymmetric" && nearestMode == "floor";
                bool linearOk = mode == "linear" && (ctm == "half_pixel" || ctm == "pytorch_half_pixel");
                if (antialias != 0 || (!nearestOk && !linearOk))
                    issues.Add(new GraphIssue(node.Name, op,
                        $"mode={mode} ctm={ctm} nearest_mode={nearestMode} antialias={antialias}", FixRoute.Engine, "E-03", Predicted: true));
                break;
            }
            case "Gemm":
            {
                float alpha = node.Attr("alpha")?.FloatOr(1f) ?? 1f;
                if (alpha != 1f)
                    issues.Add(new GraphIssue(node.Name, op, $"alpha={alpha}：引擎只支持 1", FixRoute.Engine, "E-03", Predicted: true));
                break;
            }
            case "LayerNormalization":
            case "ReduceMean":
            {
                long axis = node.Attr("axis")?.IntOr(-1) ?? -1;
                long[] axes = node.Attr("axes")?.IntsOrEmpty ?? [];
                bool lastOnly = axis == -1 && axes.All(a => a == -1);
                if (!lastOnly)
                    issues.Add(new GraphIssue(node.Name, op, $"axis={axis} axes=[{string.Join(",", axes)}]：引擎只支持最后一维", FixRoute.Engine, "E-03", Predicted: true));
                break;
            }
        }
    }
}
