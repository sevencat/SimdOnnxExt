# 模型与金标准（fixture）准备

本文件说明：**测试/回归用的模型从哪来、放哪、怎么和 onnxruntime 对齐、许可证怎么记**。
清单的唯一来源是 `models/manifest.json`。

## 1. 落盘约定

```
models/
  manifest.json          # 清单（入库）
  README.md              # 说明与许可证（入库）
  fetch-models.ps1       # 下载/复制脚本（M6-01）
  yolo/*.onnx            # 不入库
  rfdetr/*.onnx          # 不入库
  ocr/*                  # 不入库
  sam3/*                 # 不入库
```

`.gitignore` 只放行 `models/README.md`、`models/manifest.json`、`models/fetch-models.ps1`。

## 2. 本机现成来源（立即可用，复制即可）

```powershell
$dst = 'F:\code\SimdOnnxExt\models'
New-Item -ItemType Directory -Force "$dst\yolo", "$dst\rfdetr" | Out-Null

# YOLO：M1 基线用 opset12 导出（DFL Softmax 在最后一维）
Copy-Item 'F:\code\SimdYolo\models\onnx\yolo11n-opset12.onnx', 'F:\code\SimdYolo\models\onnx\yolov8n-opset12.onnx' "$dst\yolo\"
# YOLO：新导出 / seg / pose / 端到端头（M2 验收用）
Copy-Item 'F:\code\SimdYolo\models\onnx\yolo11n.onnx', 'F:\code\SimdYolo\models\onnx\yolo11n-seg.onnx', 'F:\code\SimdYolo\models\onnx\yolo11n-pose.onnx', 'F:\code\SimdYolo\models\onnx\yolov10n.onnx', 'F:\code\SimdYolo\models\onnx\yolo26n.onnx' "$dst\yolo\"

# RF-DETR small / small-coco（M4 验收用）
Copy-Item 'F:\code\TinyLabel\train\testrftedr\runs\rfdetr-small.onnx', 'F:\code\TinyLabel\train\testrftedr\runs\rfdetr-small-coco.onnx' "$dst\rfdetr\"
```

OCR（M3）：用 `Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny`（模型是程序集嵌入资源，不落盘）；
需要裸 ONNX 时从 PaddleOCR 官方模型走 `paddle2onnx` 导出，并在 `manifest.json` 注明导出命令。

SAM3（M5）：`wkentaro/sam3-onnx-models-v0.6.0`，三个文件（image encoder / language encoder / decoder）。

## 3. 金标准（对齐 onnxruntime）

原则：**测试不依赖 ORT**。用本机 python 一次性记录，产物入库，C# 测试只读产物。

```powershell
$py = 'F:\code\TinyLabel\train\py314\python\python.exe'   # onnx 1.23.2 + onnxruntime 1.31.1

# 录制：tests/Fixtures/<family>/<id>/{input.f32, out.<i>.<name>.f32, meta.json}
& $py tools\fixtures\record.py `
    --model F:\code\SimdYolo\models\onnx\yolo11n-opset12.onnx `
    --out tests\Fixtures\yolo\yolo11n-opset12 `
    --id yolo11n-opset12

# 校验：C# 侧读产物、跑引擎、逐元素比对（已入库的 9 个测试之一）
dotnet test tests\SimdOnnxExt.Tests -c Release --filter FullyQualifiedName~GoldenParity
```

录制规范（`record.py` 已实现）：

- 输入是**确定性**的：`v[i] = ((i * 0.6180339887498949) mod 1) * 2 − 1`（与 numpy 随机数实现无关，换版本也得到同一份数据）。
- `meta.json` 记录：模型绝对路径 + sha256、输入名/形状/配方、每个输出的名字/文件/形状、ORT 与 numpy 版本、录制时间。
- C# 侧先校验模型 sha256（防止"换了模型却说精度对齐"），再比对输出；容差见 `AGENTS.md` 第 7 节。
- 图像类 fixture（letterbox 之后）在 M1-03 起加：直接存 letterbox 结果的 f32，不存原图，避免解码差异混进来。

## 4. 文件校验

首次落盘后把 sha256 写回 `models/manifest.json`：

```powershell
Get-FileHash -Algorithm SHA256 F:\code\SimdOnnxExt\models\yolo\yolo11n-opset12.onnx | Format-List
```

回归时比对清单里的 sha256，避免"换了模型却说精度对齐"。

## 5. 许可证

见 `models/README.md` 的表；`manifest.json` 每条都有 `license` 字段，允许写
`UNKNOWN — 使用前确认`，但不允许留空。YOLO 权重是 AGPL-3.0（或商业授权），SAM3 为 Meta SAM 系列许可，
**本仓库不重新分发任何权重**。
