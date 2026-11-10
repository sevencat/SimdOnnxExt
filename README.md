# SimdOnnxExt

用纯托管 C# 推理引擎 [Sdcb.SimdOnnx](https://github.com/sdcb/SimdOnnx) 的**公共 API**，把
**YOLO / RF-DETR / SAM3 / OCR（PP-OCRv6）** 包成"一键调用"：统一的对象 API + 一个 CLI，
CPU（SIMD）与 Vulkan / Metal 同一套代码，不引入 ONNX Runtime、OpenCV 或任何原生二进制。

> 当前处于**骨架阶段**：计划与实时进度见 [`docs/PROGRESS.md`](docs/PROGRESS.md)，
> 协作与自更新规范见 [`AGENTS.md`](AGENTS.md)。

## 目标形态

```powershell
# 一条命令：自动识别模型族、自动选后端、出 JSON + 标注图
simdonnxx run -m models\yolo\yolo11n-opset12.onnx -i bus.jpg -o out\ --draw

# OCR：det + cls + rec + 字典
simdonnxx ocr -i page.png --models models\ocr\ --dict ppocr_keys_v5.txt --draw

# SAM3：文本 / 框 / 点提示
simdonnxx sam -i bus.jpg --models models\sam3\ --text person

# 新模型进不来时：先体检，再预处理
simdonnxx inspect -m x.onnx     # 算子直方图 + 引擎兼容性 + 建议路线
simdonnxx prep    -m x.onnx     # 剪枝 / 单输出封装 / 等价改写
```

库侧同一个入口：

```csharp
using Sdcb.SimdOnnxExt;

using var zoo = SimdOnnxZoo.Open(new ZooOptions { Backend = BackendKind.Auto });
using IVisionModel model = zoo.Load(@"models\yolo\yolo11n-opset12.onnx");
VisionResult result = model.Run(pixels, width, height, stride, PixelFormat.Bgr24);
```

## 为什么需要这一层

引擎只负责"把 ONNX 图编译成 SIMD/GPU 执行计划"，而**每个模型族的差异都在图外**：
DFL 解码与 NMS、DETR 的 top-k、SAM 的提示编码与掩码上采样、OCR 的 DB 后处理与 CTC 字典。
图像侧（加载/保存、letterbox、NCHW/NHWC 张量化、NMS、标注绘制、中文标签）交给
[sevencat/ImageFacade](https://github.com/sevencat/ImageFacade) 的门面 API，本仓库不重复实现；
引擎侧的缺口（新算子、多输出、Softmax 任意轴）按 `E` 路线回到 SimdOnnx。

三种消解路线（详见 [`docs/gap-matrix.md`](docs/gap-matrix.md)）：`E` 改引擎、`P` 离线图预处理、`H` 主机侧 C#。

依赖的角色分工：

| 组件 | 提供什么 | 我们不做什么 |
|---|---|---|
| `Sdcb.SimdOnnx` | ONNX 解析、图编译、SIMD/GPU kernel、arena、CTC 解码 | 不写算子、不碰设备层 |
| `Sevencat.ImageFacade` | 图片编解码门面、Resize 六模式、Letterbox、张量化、NMS、绘制与文字（内置中文字体） | 不自己拼像素、不自己写字体 |
| 本仓库 | 模型族适配（DFL/top-k/掩码/DB+CTC）、后端选择、结果类型、CLI | 不重复上面两层 |

## 目录

| 路径 | 内容 |
|---|---|
| `src/` | 核心库 + 每族适配器 + CLI（M0 起逐步落地） |
| `tools/` | `progress.ps1`（进度自更新）、`SimdOnnxExt.ModelPrep`（M2 起）、fixture 记录脚本 |
| `tests/` | 金标准对齐、CPU↔Vulkan 一致、CLI e2e |
| `bench/` | CPU / Vulkan 与 onnxruntime 的 A/B |
| `docs/` | 计划、进度、差距矩阵、决策记录 |
| `models/` | fixture 清单（权重不入库） |

## 构建

引擎与图像门面都在积极开发中，默认**都走同级目录源码**；装包环境可以把图像侧切到 nuget：

```
F:\code\
  SimdOnnx\        # github.com/sdcb/SimdOnnx        （引擎，未上 nuget.org，必须同级或私有源）
  ImageFacade\     # github.com/sevencat/ImageFacade （本地 0.2.0；也可切 nuget 0.1.0）
  SimdOnnxExt\     # 本仓库
```

```powershell
dotnet --version                 # 需要 10.0.101（global.json 已固定）
dotnet build                     # 默认：引擎 + ImageFacade 都走同级源码
dotnet test                      # 依赖冒烟 + 后续金标准测试
dotnet build -p:UseLocalImageFacade=false   # ImageFacade 改走 nuget 0.1.0（无需同级仓库）
dotnet build -p:UseLocalSimdOnnx=false      # 引擎改走包引用（需 nuget.config 配私有源）
```

## 文档

- [协作规范（进度/计划自更新）](AGENTS.md) — 先读这个
- [计划（单一事实源）](docs/PLAN.md) · [进度快照](docs/PROGRESS.md)
- [算子差距矩阵](docs/gap-matrix.md) · [决策记录](docs/decisions.md) · [模型与金标准](docs/model-setup.md)

## 许可证

本仓库代码为 MIT（见 `LICENSE`）。**模型权重不随仓库分发**，各自许可见 `models/README.md`：
YOLO 权重为 AGPL-3.0（或商业授权），PP-OCRv6 为 Apache-2.0，SAM3 为 Meta SAM 系列许可（使用前自行确认）。
