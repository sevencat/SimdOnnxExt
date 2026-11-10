"""把 FunASR Paraformer 的 int8 ONNX 反量化成 fp32 权重包（纯托管 C# 端读取）。

为什么这样做：sherpa 的 Paraformer 图里带 `Loop/NonZero/GatherND/Sequence`
（CIF 预测器 + CTC top-k），静态执行计划跑不了；但**架构本身**只用
MatMul / Conv / Softmax / ReLU / LayerNorm(拆成 ReduceMean/Sqrt/Pow)。

本脚本只做一件事：把 222 个 `MatMulInteger` 的量化权重还原成 fp32，
连同其余 fp32 initializer 一起写成紧凑的权重包 + manifest，
C# 侧自己实现 Paraformer 前向（fbank/LFR → encoder → CIF → decoder → argmax）。

量化模式（本模型实测）：
    DynamicQuantizeLinear(A) -> A_q(uint8), a_scale, a_zp
    MatMulInteger(A_q, W_q, a_zp, w_zp) -> out
    W_f32 = (W_q.astype(float32) - w_zp) * w_scale      # 每输出通道一个 scale/zp

用法：
    & F:\\code\\TinyLabel\\train\\py314\\python\\python.exe tools\\prep\\export-paraformer-f32.py `
        F:\\ai\\sherpa\\model.int8.onnx out\\paraformer-f32
产物：
    out\\paraformer-f32.bin     # little-endian float32，按 manifest 偏移拼接
    out\\paraformer-f32.json    # {"tensors": [{name, shape, offset, count}], "source": ...}
"""

import json
import os
import sys

import numpy as np
import onnx
from onnx import numpy_helper


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    src, prefix = sys.argv[1], sys.argv[2]
    if not os.path.isfile(src):
        print(f"找不到模型：{src}")
        return 1

    model = onnx.load(src, load_external_data=True)
    graph = model.graph
    initializers = {i.name: i for i in graph.initializer}

    tensors: dict[str, np.ndarray] = {}

    # 1) 反量化 MatMulInteger 的权重
    dequantized = 0
    for node in graph.node:
        if node.op_type != "MatMulInteger":
            continue
        if len(node.input) < 4:
            continue
        weight_name = node.input[1]
        if not weight_name.endswith("_quantized"):
            raise SystemExit(f"意外的量化权重命名：{weight_name}")
        base = weight_name[: -len("_quantized")]
        scale_name, zp_name = base + "_scale", base + "_zero_point"
        if weight_name not in initializers or scale_name not in initializers or zp_name not in initializers:
            raise SystemExit(f"量化权重 {weight_name} 缺少 scale/zero_point")
        w = numpy_helper.to_array(initializers[weight_name]).astype(np.float32)
        scale = numpy_helper.to_array(initializers[scale_name]).astype(np.float32)
        zero = numpy_helper.to_array(initializers[zp_name]).astype(np.float32)
        tensors[node.name or base] = (w - zero.reshape(1, -1)) * scale.reshape(1, -1)
        dequantized += 1

    # 2) 其余 float 权重原样带出
    kept = 0
    for name, init in initializers.items():
        if init.data_type != onnx.TensorProto.FLOAT or name in tensors:
            continue
        if name.endswith("_scale") or name.endswith("_zero_point"):
            continue
        tensors[name] = numpy_helper.to_array(init).astype(np.float32)
        kept += 1

    os.makedirs(os.path.dirname(os.path.abspath(prefix)) or ".", exist_ok=True)
    manifest = {"source": os.path.abspath(src), "tensors": []}
    offset = 0
    with open(prefix + ".bin", "wb") as blob:
        for name, array in tensors.items():
            array = np.ascontiguousarray(array, dtype="<f4")
            manifest["tensors"].append(
                {"name": name, "shape": list(array.shape), "offset": offset, "count": int(array.size)}
            )
            blob.write(array.tobytes())
            offset += array.nbytes

    with open(prefix + ".json", "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")

    total_mb = offset / 1e6
    print(f"dequantized {dequantized} matmul weights, kept {kept} fp32 tensors")
    print(f"wrote {prefix}.bin ({total_mb:.1f} MB) + {prefix}.json ({len(manifest['tensors'])} tensors)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
