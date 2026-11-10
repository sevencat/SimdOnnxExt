"""给 SimdOnnxExt 的测试录制 onnxruntime 金标准。

约定（与 AGENTS.md 第 7 节一致：测试不依赖 ORT，金标准一次性录制、产物入库）：

    tests/Fixtures/<family>/<id>/
        input.f32            # float32 little-endian，行主序，形状见 meta.json
        out.<i>.<name>.f32   # 每个图输出一个
        meta.json            # 形状/名字/sha256/ORT 版本/输入配方

用法（本机 python 已装 onnx + onnxruntime）：

    & F:\\code\\TinyLabel\\train\\py314\\python\\python.exe tools\\fixtures\\record.py `
        --model F:\\code\\SimdYolo\\models\\onnx\\yolo11n-opset12.onnx `
        --out tests\\Fixtures\\yolo\\yolo11n-opset12 `
        --id yolo11n-opset12

输入是**确定性**的（不依赖 numpy 随机数实现）：第 i 个元素 = ((i * φ) mod 1) * 2 − 1，
φ 取 0.6180339887498949；这样换 numpy 版本也得到同一份数据。
"""

import argparse
import hashlib
import json
import os
import sys
from datetime import datetime, timezone

import numpy as np
import onnxruntime as ort

PHI = 0.6180339887498949
RECIPE = "v[i] = ((i * 0.6180339887498949) mod 1) * 2 - 1, i = flat index (NCHW)"


def make_input(shape):
    n = 1
    for d in shape:
        n *= d
    i = np.arange(n, dtype=np.float64)
    v = ((i * PHI) % 1.0) * 2.0 - 1.0
    return v.astype(np.float32).reshape(shape)


def sanitize(name):
    return "".join(c if c.isalnum() else "_" for c in name) or "out"


def static_shape(tensor):
    shape = []
    for d in tensor.shape:
        if isinstance(d, int) and d > 0:
            shape.append(d)
        else:
            return None
    return shape


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--id", default=None)
    ap.add_argument("--shape", default="", help="例如 1,3,128,128；默认取模型的静态输入形状")
    args = ap.parse_args()

    session = ort.InferenceSession(args.model, providers=["CPUExecutionProvider"])
    inp = session.get_inputs()[0]

    shape = [int(x) for x in args.shape.split(",")] if args.shape else static_shape(inp)
    if not shape or any(d <= 0 for d in shape):
        sys.exit("模型输入含动态维：请用 --shape 指定具体形状")
    if inp.type != "tensor(float)":
        sys.exit(f"只录制 float32 输入，模型输入是 {inp.type}")

    data = make_input(shape)
    outputs = session.run(None, {inp.name: data})

    os.makedirs(args.out, exist_ok=True)
    data.tofile(os.path.join(args.out, "input.f32"))

    meta = {
        "id": args.id or os.path.basename(os.path.dirname(os.path.abspath(args.model))),
        "model": os.path.abspath(args.model),
        "model_sha256": sha256(args.model),
        "input": {"name": inp.name, "shape": shape, "file": "input.f32", "recipe": RECIPE},
        "outputs": [],
        "recorded_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "runtime": {"onnxruntime": ort.__version__, "numpy": np.__version__, "provider": "CPUExecutionProvider"},
    }

    for index, (info, array) in enumerate(zip(session.get_outputs(), outputs)):
        array = np.asarray(array, dtype=np.float32)
        file = f"out.{index}.{sanitize(info.name)}.f32"
        array.tofile(os.path.join(args.out, file))
        meta["outputs"].append({"name": info.name, "file": file, "shape": list(array.shape)})

    with open(os.path.join(args.out, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, indent=2, ensure_ascii=False)
        f.write("\n")

    total = os.path.getsize(os.path.join(args.out, "input.f32")) + sum(
        os.path.getsize(os.path.join(args.out, o["file"])) for o in meta["outputs"])
    print(f"wrote {args.out}: input {shape}, {len(meta['outputs'])} outputs, {total / 1024:.1f} KiB")


if __name__ == "__main__":
    main()
