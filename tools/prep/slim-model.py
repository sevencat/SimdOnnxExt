"""P 路线：用 onnxslim 折叠静态形状子图，让模型落进引擎的算子集。

RF-DETR（rfdetr-small.onnx）实测：1537 → 821 节点，不支持算子 19 类 → 12 类，
再算上引擎已有的 Cast/Gather/Expand/Tile/Where/Sin/Cos/Exp 之后只剩 5 类 10 个节点：
ReduceMax(1) TopK(1) GatherElements(2) GridSample(3) ReduceSum(3)。

数值等价（onnxruntime，同一随机输入）：
    dets   max|diff| = 1.9e-05
    labels max|diff| = 4.0e-05

用法：
    & F:\\code\\TinyLabel\\train\\py314\\python\\python.exe tools\\prep\\slim-model.py `
        in.onnx out/rfdetr-slim.onnx

只做一次，产物入库/缓存即可；测试与 CLI 用 slim 后的模型。
"""

import os
import sys

import onnxslim


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    src, dst = sys.argv[1], sys.argv[2]
    if not os.path.isfile(src):
        print(f"找不到输入模型：{src}")
        return 1
    os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
    onnxslim.slim(src, dst)
    print(f"{src} -> {dst}  ({os.path.getsize(dst) / 1e6:.1f} MB)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
