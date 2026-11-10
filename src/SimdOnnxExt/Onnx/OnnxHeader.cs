namespace Sdcb.SimdOnnxExt.Onnx;

/// <summary>ONNX 里的一个张量声明（图输入 / 输出 / 权重）。符号维记 −1，原文留在 <see cref="Symbols"/>。</summary>
public sealed record OnnxTensorSpec(string Name, int ElementType, int[] Dims, string?[] Symbols)
{
    /// <summary>ONNX elem_type 1（float32）——引擎的图输入目前只接受这个。</summary>
    public bool IsFloat => ElementType == 1;

    /// <summary>含符号维（维度未知）。</summary>
    public bool HasDynamicDims => Dims.Any(d => d <= 0);

    /// <summary>形如 <c>[1,3,640,640]</c>，符号维显示为原名。</summary>
    public string ShapeText => "[" + string.Join(",", Dims.Select((d, i) => d > 0 ? d.ToString() : Symbols[i] ?? "?")) + "]";

    public override string ToString() => Name + ShapeText;
}

/// <summary>一个节点属性。只保留报告需要的几种类型（标量 / 列表 / 字符串）。</summary>
public readonly record struct OnnxAttribute(string Name, int Type, long Int, float Float, string? String, long[] Ints, float[] Floats)
{
    public long IntOr(long fallback) => Type == 2 ? Int : fallback;

    public float FloatOr(float fallback) => Type == 1 ? Float : fallback;

    public long[] IntsOrEmpty => Ints ?? [];

    /// <summary>属性是否真的出现过（用于区分"默认值"与"显式写了默认值"）。</summary>
    public bool IsPresent => Type != 0;
}

/// <summary>ONNX 里的一个节点：算子、名字、输入输出名、属性。</summary>
public sealed record OnnxNodeSpec(string OpType, string Name, string Domain, string[] Inputs, string[] Outputs,
    IReadOnlyDictionary<string, OnnxAttribute> Attributes)
{
    public OnnxAttribute? Attr(string name) => Attributes.TryGetValue(name, out OnnxAttribute a) ? a : null;
}

/// <summary>
/// 模型头部信息：opset、图输入/输出、算子与属性、权重形状。不解码任何权重数据，
/// 因此 <c>inspect</c> 跑大模型也很快（权重块按长度跳过）。
/// </summary>
public sealed class OnnxHeader
{
    private OnnxHeader(long irVersion, int opset, string producer, Dictionary<string, string> metadata,
        List<OnnxTensorSpec> inputs, List<OnnxTensorSpec> outputs, List<OnnxTensorSpec> initializers, List<OnnxNodeSpec> nodes)
    {
        IrVersion = irVersion;
        Opset = opset;
        Producer = producer;
        Metadata = metadata;
        Inputs = inputs;
        Outputs = outputs;
        Initializers = initializers;
        Nodes = nodes;
    }

    public long IrVersion { get; }
    public int Opset { get; }
    public string Producer { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public IReadOnlyList<OnnxTensorSpec> Inputs { get; }
    public IReadOnlyList<OnnxTensorSpec> Outputs { get; }
    public IReadOnlyList<OnnxTensorSpec> Initializers { get; }
    public IReadOnlyList<OnnxNodeSpec> Nodes { get; }

    public static OnnxHeader Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Parse(File.ReadAllBytes(path));
    }

    public static OnnxHeader Parse(ReadOnlySpan<byte> data)
    {
        long irVersion = 0;
        int opset = 0;
        string producer = "";
        Dictionary<string, string> metadata = new(StringComparer.Ordinal);
        List<OnnxTensorSpec> inputs = [], outputs = [], initializers = [];
        List<OnnxNodeSpec> nodes = [];
        bool sawGraph = false;

        ProtoScan model = new(data);
        while (model.TryReadTag(out int field, out int wire))
        {
            switch (field)
            {
                case 1 when wire == 0:
                    irVersion = checked((long)model.ReadVarint());
                    break;
                case 2 when wire == 2:
                    producer = model.ReadString();
                    break;
                case 8 when wire == 2:
                {
                    ProtoScan opsetProto = model.EnterMessage();
                    string domain = "";
                    long version = 0;
                    while (opsetProto.TryReadTag(out int f, out int w))
                    {
                        if (f == 1 && w == 2) domain = opsetProto.ReadString();
                        else if (f == 2 && w == 0) version = checked((long)opsetProto.ReadVarint());
                        else opsetProto.Skip(w);
                    }
                    // 默认域（空 或 ai.onnx）才是引擎读的那个
                    if (domain.Length == 0 || domain == "ai.onnx") opset = (int)Math.Min(version, int.MaxValue);
                    break;
                }
                case 14 when wire == 2:
                {
                    ProtoScan entry = model.EnterMessage();
                    string key = "", value = "";
                    while (entry.TryReadTag(out int f, out int w))
                    {
                        if (f == 1 && w == 2) key = entry.ReadString();
                        else if (f == 2 && w == 2) value = entry.ReadString();
                        else entry.Skip(w);
                    }
                    if (key.Length != 0) metadata[key] = value;
                    break;
                }
                case 7 when wire == 2:
                {
                    sawGraph = true;
                    ProtoScan graph = model.EnterMessage();
                    while (graph.TryReadTag(out int f, out int w))
                    {
                        switch (f)
                        {
                            case 1 when w == 2: nodes.Add(ParseNode(graph.EnterMessage())); break;
                            case 5 when w == 2: initializers.Add(ParseTensor(graph.EnterMessage())); break;
                            case 11 when w == 2: inputs.Add(ParseValueInfo(graph.EnterMessage())); break;
                            case 12 when w == 2: outputs.Add(ParseValueInfo(graph.EnterMessage())); break;
                            default: graph.Skip(w); break;
                        }
                    }
                    break;
                }
                default:
                    model.Skip(wire);
                    break;
            }
        }

        if (!sawGraph) throw new InvalidDataException("ONNX 里没有 graph。");
        return new OnnxHeader(irVersion, opset, producer, metadata, inputs, outputs, initializers, nodes);
    }

    private static OnnxNodeSpec ParseNode(ProtoScan node)
    {
        string opType = "", name = "", domain = "";
        List<string> inputs = [], outputs = [];
        Dictionary<string, OnnxAttribute> attributes = new(StringComparer.Ordinal);
        while (node.TryReadTag(out int field, out int wire))
        {
            switch (field)
            {
                case 1 when wire == 2: inputs.Add(node.ReadString()); break;
                case 2 when wire == 2: outputs.Add(node.ReadString()); break;
                case 3 when wire == 2: name = node.ReadString(); break;
                case 4 when wire == 2: opType = node.ReadString(); break;
                case 7 when wire == 2: domain = node.ReadString(); break;
                case 5 when wire == 2:
                {
                    OnnxAttribute attribute = ParseAttribute(node.EnterMessage());
                    if (attribute.Name.Length != 0) attributes[attribute.Name] = attribute;
                    break;
                }
                default: node.Skip(wire); break;
            }
        }
        if (opType.Length == 0) throw new InvalidDataException("ONNX 节点没有 op_type。");
        return new OnnxNodeSpec(opType, name, domain, [.. inputs], [.. outputs], attributes);
    }

    private static OnnxAttribute ParseAttribute(ProtoScan attribute)
    {
        string name = "";
        int type = 0;
        long intValue = 0;
        float floatValue = 0;
        string? stringValue = null;
        long[] ints = [];
        float[] floats = [];
        while (attribute.TryReadTag(out int field, out int wire))
        {
            switch (field)
            {
                case 1 when wire == 2: name = attribute.ReadString(); break;
                case 20 when wire == 0: type = checked((int)attribute.ReadVarint()); break;
                case 2 when wire == 5: floatValue = BitConverter.Int32BitsToSingle(unchecked((int)attribute.ReadFixed32())); break;
                case 3 when wire == 0: intValue = unchecked((long)attribute.ReadVarint()); break;
                case 4 when wire == 2: stringValue = attribute.ReadString(); break;
                case 7 when wire == 2: floats = ReadPackedFloats(attribute.ReadLengthDelimited()); break;
                case 7 when wire == 5: floats = [.. floats, BitConverter.Int32BitsToSingle(unchecked((int)attribute.ReadFixed32()))]; break;
                case 8 when wire == 2: ints = ReadPackedInt64(attribute.ReadLengthDelimited()); break;
                case 8 when wire == 0: ints = [.. ints, unchecked((long)attribute.ReadVarint())]; break;
                default: attribute.Skip(wire); break;
            }
        }
        if (type == 0)
        {
            // 非标准导出可能省掉 type：按出现过的字段推断，够报告用
            type = stringValue is not null ? 3 : ints.Length > 0 ? 7 : floats.Length > 0 ? 6 : intValue != 0 ? 2 : floatValue != 0 ? 1 : 0;
        }
        return new OnnxAttribute(name, type, intValue, floatValue, stringValue, ints, floats);
    }

    private static OnnxTensorSpec ParseTensor(ProtoScan tensor)
    {
        string name = "";
        int dataType = 0;
        int[] dims = [];
        while (tensor.TryReadTag(out int field, out int wire))
        {
            switch (field)
            {
                case 1 when wire == 2: dims = [.. ReadPackedInt64(tensor.ReadLengthDelimited()).Select(d => checked((int)d))]; break;
                case 1 when wire == 0: dims = [.. dims, checked((int)tensor.ReadVarint())]; break;
                case 2 when wire == 0: dataType = checked((int)tensor.ReadVarint()); break;
                case 8 when wire == 2: name = tensor.ReadString(); break;
                default: tensor.Skip(wire); break;
            }
        }
        return new OnnxTensorSpec(name, dataType, dims, new string?[dims.Length]);
    }

    private static OnnxTensorSpec ParseValueInfo(ProtoScan valueInfo)
    {
        string name = "";
        int elementType = 0;
        int[] dims = [];
        string?[] symbols = [];
        while (valueInfo.TryReadTag(out int field, out int wire))
        {
            switch (field)
            {
                case 1 when wire == 2:
                    name = valueInfo.ReadString();
                    break;
                case 2 when wire == 2:
                {
                    ProtoScan type = valueInfo.EnterMessage();
                    while (type.TryReadTag(out int tf, out int tw))
                    {
                        if (tf == 1 && tw == 2)
                        {
                            ProtoScan tensorType = type.EnterMessage();
                            while (tensorType.TryReadTag(out int f, out int w))
                            {
                                if (f == 1 && w == 0) elementType = checked((int)tensorType.ReadVarint());
                                else if (f == 2 && w == 2) ParseShape(tensorType.EnterMessage(), out dims, out symbols);
                                else tensorType.Skip(w);
                            }
                        }
                        else type.Skip(tw);
                    }
                    break;
                }
                default:
                    valueInfo.Skip(wire);
                    break;
            }
        }
        return new OnnxTensorSpec(name, elementType, dims, symbols);
    }

    private static void ParseShape(ProtoScan shape, out int[] dims, out string?[] symbols)
    {
        List<int> values = [];
        List<string?> names = [];
        while (shape.TryReadTag(out int field, out int wire))
        {
            if (field != 1 || wire != 2)
            {
                shape.Skip(wire);
                continue;
            }
            ProtoScan dimension = shape.EnterMessage();
            long value = 0;
            string? symbol = null;
            while (dimension.TryReadTag(out int f, out int w))
            {
                if (f == 1 && w == 0) value = checked((long)dimension.ReadVarint());
                else if (f == 2 && w == 2) symbol = dimension.ReadString();
                else dimension.Skip(w);
            }
            values.Add(value > 0 && value <= int.MaxValue ? (int)value : -1);
            names.Add(symbol);
        }
        dims = [.. values];
        symbols = [.. names];
    }

    private static long[] ReadPackedInt64(ReadOnlySpan<byte> packed)
    {
        ProtoScan scan = new(packed);
        List<long> values = [];
        while (scan.Remaining > 0) values.Add(unchecked((long)scan.ReadVarint()));
        return [.. values];
    }

    private static float[] ReadPackedFloats(ReadOnlySpan<byte> packed)
    {
        if ((packed.Length & 3) != 0) throw new InvalidDataException("ONNX floats 字段长度不是 4 的倍数。");
        float[] values = new float[packed.Length / 4];
        for (int i = 0; i < values.Length; i++)
        {
            uint bits = (uint)(packed[i * 4] | (packed[i * 4 + 1] << 8) | (packed[i * 4 + 2] << 16) | (packed[i * 4 + 3] << 24));
            values[i] = BitConverter.Int32BitsToSingle(unchecked((int)bits));
        }
        return values;
    }
}
