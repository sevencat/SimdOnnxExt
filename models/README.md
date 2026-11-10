# models — 测试与演示用的模型文件

本目录**不入库权重**（见 `.gitignore`）：只保留 `manifest.json`、下载脚本和本说明。
`manifest.json` 是唯一清单：每个 fixture 记录 `family` / `task` / 落盘相对路径 / 来源 / 用途 / 许可证。

## 约定

- 落盘路径：`models/<family>/<id>.onnx`（大文件、`.onnx.data` 一起忽略）。
- 每个 fixture 都必须在 `manifest.json` 里说明"为什么需要它"（覆盖哪个验收）。
- 拿不到官方权重时，用本机已有的导出（`source` 写绝对路径），并在 `model-setup.md` 记录它是怎么导出的。
- 记录 `sha256`（首次落盘后补），CI 或本地回归可校验文件没换。

## 本机现成来源

| 来源 | 内容 |
|---|---|
| `F:\code\SimdYolo\models\onnx\*` | YOLOv8/v9/v10/v11/v26 的 det / seg / pose ONNX（含 `*-opset12.onnx`） |
| `F:\code\TinyLabel\train\testrftedr\runs\*` | RF-DETR small 的 `rfdetr-small.onnx` / `rfdetr-small-coco.onnx` |
| SimdPaddleOCR 的模型包 | PP-OCRv6 det / rec / cls + 字典（M3 用） |
| `wkentaro/sam3-onnx-models-v0.6.0`（HF） | SAM3 image encoder / language encoder / decoder（M5 用） |

## 许可证（必须如实记录）

- **YOLO 权重**：Ultralytics 许可（AGPL-3.0 或商业授权）。本仓库只做推理适配，不重新分发权重。
- **RF-DETR**：RoBoflow 发布，代码 Apache-2.0；权重许可以其发布页为准。
- **PP-OCRv6**：PaddlePaddle 许可（Apache-2.0）。
- **SAM3**：Meta SAM 系列许可，可能限研究/非商业用途——**用前自行确认**。

新增 fixture 时同步更新本表；拿不准就写 `license: UNKNOWN — 使用前确认`，不要留空。
