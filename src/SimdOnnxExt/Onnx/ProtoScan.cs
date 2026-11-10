namespace Sdcb.SimdOnnxExt.Onnx;

/// <summary>
/// 最小 protobuf 读取器：只解析 ONNX 头部字段，数据块（raw_data / float_data）一律按长度跳过。
/// 字段号取自 ONNX proto 定义（与 <c>Sdcb.SimdOnnx</c> 的解析器一致）：
/// Model 1=ir_version 2=producer_name 7=graph 8=opset_import 14=metadata_props；
/// Graph 1=node 5=initializer 11=input 12=output 13=value_info；
/// Node 1=input 2=output 3=name 4=op_type 5=attribute 7=domain；
/// Attribute 1=name 20=type 2=f 3=i 4=s 5=t 7=floats 8=ints 9=strings；
/// Tensor 1=dims 2=data_type 8=name；ValueInfo 1=name 2=type；
/// Type 1=tensor_type；TensorType 1=elem_type 2=shape；Shape 1=dim；Dimension 1=dim_value 2=dim_param。
/// </summary>
internal ref struct ProtoScan
{
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;
    private readonly int _end;

    public ProtoScan(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
        _end = data.Length;
    }

    private ProtoScan(ReadOnlySpan<byte> data, int start, int length)
    {
        _data = data;
        _pos = start;
        _end = start + length;
    }

    public readonly int Remaining => _end - _pos;

    public bool TryReadTag(out int field, out int wire)
    {
        if (_pos >= _end)
        {
            field = 0;
            wire = 0;
            return false;
        }
        ulong tag = ReadVarint();
        field = checked((int)(tag >> 3));
        wire = (int)(tag & 7);
        return field != 0;
    }

    public ulong ReadVarint()
    {
        ulong value = 0;
        int shift = 0;
        while (true)
        {
            if (_pos >= _end) throw new InvalidDataException("ONNX 数据在 varint 中途截断。");
            byte b = _data[_pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
            if (shift >= 64) throw new InvalidDataException("ONNX varint 超过 64 位。");
        }
    }

    public uint ReadFixed32()
    {
        Ensure(4);
        uint v = (uint)(_data[_pos] | (_data[_pos + 1] << 8) | (_data[_pos + 2] << 16) | (_data[_pos + 3] << 24));
        _pos += 4;
        return v;
    }

    public ulong ReadFixed64()
    {
        ulong lo = ReadFixed32();
        ulong hi = ReadFixed32();
        return lo | (hi << 32);
    }

    public ReadOnlySpan<byte> ReadLengthDelimited()
    {
        int length = checked((int)ReadVarint());
        Ensure(length);
        ReadOnlySpan<byte> slice = _data.Slice(_pos, length);
        _pos += length;
        return slice;
    }

    public string ReadString() => System.Text.Encoding.UTF8.GetString(ReadLengthDelimited());

    public ProtoScan EnterMessage()
    {
        int length = checked((int)ReadVarint());
        Ensure(length);
        ProtoScan nested = new(_data, _pos, length);
        _pos += length;
        return nested;
    }

    public void Skip(int wire)
    {
        switch (wire)
        {
            case 0: ReadVarint(); break;
            case 1: Ensure(8); _pos += 8; break;
            case 2:
                int length = checked((int)ReadVarint());
                Ensure(length);
                _pos += length;
                break;
            case 5: Ensure(4); _pos += 4; break;
            default: throw new InvalidDataException($"ONNX proto 不支持 wire type {wire}。");
        }
    }

    private readonly void Ensure(int count)
    {
        if (count < 0 || count > Remaining) throw new InvalidDataException("ONNX 数据越界。");
    }
}
