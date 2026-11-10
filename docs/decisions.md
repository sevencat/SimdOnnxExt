# Decisions

格式与 `sdcb/SimdOnnx` 的 `docs/decisions.md` 一致：每条写**背景 / 决定 / 后果 / 复审条件**。
被推翻的条目不要删，追加 `Superseded by …` 或 `Reversed`。

## 2026-11-10 — 只依赖引擎公共 API，源码引用优先

**背景**：`Sdcb.SimdOnnx` 0.1.0 尚未发布到 nuget.org（走 ProGet 测试源），而本仓库是第三方扩展（MIT，`sevencat/OnnxUtil`），没有 `InternalsVisibleTo`。
**决定**：全部代码只用公共面（`OnnxModel` / `CpuModel` / `VkBackend` / `ExtOp` / `GraphPass` / `ModelHeads` / `CtcProjection` …；图像侧改用 `ImageFacade`，不再直接用 `ImageTensor` 做像素搬运）。构建默认 `ProjectReference ..\SimdOnnx\src\Sdcb.SimdOnnx`（`UseLocalSimdOnnx=true`），无源码环境用 `-p:UseLocalSimdOnnx=false` 走 `Directory.Packages.props` 的精确版本。
**后果**：能第一时间吃到引擎修复；引擎内部改动不会把我们编译打断，但公共 API 变更会。
**复审条件**：引擎正式发布到 nuget.org 后，把默认切回包引用并把版本号提到 `Directory.Build.props` 一处管理。

## 2026-11-10 — "一键调用" = 统一 API + 单 CLI，按模型族分适配器

> **Superseded（同日）**：不再"每族一个项目"——族的代码合并进单项目 `OnnxUtil`，见本文件最后一条。

**背景**：目标是用同一套东西跑 YOLO / RF-DETR / SAM3 / OCR，且新模型接进来不能每次改命令行。
**决定**：核心库提供 `SimdOnnxZoo.Open(...)` + `IVisionModel`（`Run(pixels, w, h) -> VisionResult`）；每族一个项目做预处理/解码/后处理；CLI `simdonnxx` 只做参数与输出，族识别靠图签名（输入名/形状/输出数/`metadata_props`）。
**后果**：加新模型族 = 加一个项目 + 注册表一行；CLI 与库共用同一套结果类型（JSON、绘制）。
**复审条件**：出现第 5 个族且它们的预处理高度一致时，把公共预处理抽成基类或脚本化配置。

## 2026-11-10 — 缺口只走三条路：E / P / H

**背景**：实测引擎的公共扩展点只能替换**已经 lower 成 `ExecNode`** 的节点；`GraphCompiler.EmitNode` 遇到未知 ONNX 逻辑算子直接抛，`ExtOp` 补不了"新算子"。同时编译器**先 emit 全部节点、后剪枝**，所以尾部不支持的算子会挡住整个模型，`CompileOptions.OutputName` 救不了。
**决定**：每个缺口只能落一条路——`E` 改引擎（新算子 / 多输出 / Softmax 任意轴 / int64 输入改造，走 SimdOnnx PR）；`P` 离线图预处理（剪枝、单输出封装、等价改写）；`H` 主机侧 C#（letterbox、DFL/NMS、top-k、掩码、CTC、tokenizer、绘制）。归属写进 `docs/gap-matrix.md` 与 `PLAN.md` 备注。
**后果**：避免"看起来能靠扩展解决的问题"在实现到一半才发现必须改引擎；RF-DETR / SAM3 的排期明确挂在 E 段。
**复审条件**：引擎提供"未知算子回调"或自定义 domain 支持时，重新评估 P 的必要性。

## 2026-11-10 — 图像 IO 用 Sdcb.SimdCodecs，绘制自己写

> **Reversed（同日）**：图像处理统一改用 `sevencat/ImageFacade`，见本文件最后一条。
> 原文保留作为决策轨迹：当时的假设是"门面层要自己写"。

**背景**：核心库与 CLI 需要解码 JPEG/PNG/WebP 并输出标注图；引擎自身不解码图片（只吃像素内存）。
**决定**：依赖 `Sdcb.SimdCodecs`（MIT、纯托管、可解码也可编码，与引擎同一生态）；标注绘制（框、掩码着色、标签位图字体）自己在扩展库里实现，不引 SkiaSharp/OpenCvSharp。
**后果**：保持"零原生依赖、可 NativeAOT"的卖点；代价是文字渲染要自带小位图字体。
**复审条件**：需要中文标签渲染或高质量排版时，再评估可选插件包。

## 2026-11-10 — 优先级：YOLO → RF-DETR → simdocr → SAM3

**背景**：四个族的成本差很大——YOLO 只差主机侧解码加引擎的 Softmax 任意轴（E-01）；RF-DETR 差 19 类算子 + 多输出（E-03/E-02）；OCR 引擎已由 SimdPaddleOCR 跑通、只差 DB/CTC 后处理；SAM3 还要 int64 输入改造（E-04）与三模型链路。
**决定**：按用户要求先交付 **YOLO → RF-DETR → simdocr**，SAM3 排在这三者之后。`PLAN.md` 的章节按"阶段 1/2/3/4"物理重排（任务 ID 保持不变：`M1/M2`=YOLO、`M4`=RF-DETR、`M3`=OCR、`M5`=SAM3）。
**后果**：任务 ID 与阶段号不再一一对应（`M4-*` 在阶段 2、`M3-*` 在阶段 3）；这是刻意的——`AGENTS.md` 规定 ID 不复用，顺序以"阶段"为准，`PROGRESS.md` 的"建议下一步"会按章节顺序走。
**复审条件**：三者都跑通后启动 SAM3；若 SAM3 的商业需求提前，重排一次并在此追加说明。

## 2026-11-10 — 先 YOLO（opset12 导出）+ OCR，RF-DETR / SAM3 等引擎算子

**背景**：实测（`onnx 1.23.2`，本机模型）：YOLO 全系新导出的 DFL `Softmax` 是 `axis=1`（rank-4 非末维），当前引擎直接抛；YOLOv10/26 还有 `TopK/GatherElements/Tile/Expand/...`；RF-DETR 缺 19 类算子且双输出；SAM3 是 int64 tokens + 多输出。
**决定**：M1/M2 用 `*-opset12.onnx`（末维 softmax）先把"一键调用"链路（预处理/解码/NMS/JSON/Vulkan）跑通并写成基线；OCR 走引擎已有能力（`CtcProjection`/`ModelHeads`）。同时向引擎提 E-01（Softmax 任意轴）/E-02（多输出），RF-DETR 排到 E-03 算子批次之后。
**后果**：能最快拿到可演示的一键链路，且后续引擎改动由 `inspect` 报表驱动，不盲改。
**复审条件**：E-01/E-02 合入后，M2-04/M2-01 直接切到新导出与单次运行，不再需要 P 的 Transpose/合并技巧。

## 2026-11-10 — 图像处理统一走 sevencat/ImageFacade（替换"自己写 letterbox/绘制"）

**背景**：原计划只用 `Sdcb.SimdCodecs` 解码，letterbox、张量化、NMS、绘制都自己写。核对 `F:\code\ImageFacade` 后确认：这些恰好是它的门面 API，且提供纯托管后端（Apache-2.0，net8.0/net10.0）。
**决定**：
- 核心用 `Sevencat.ImageFacade`；后端用 `Sevencat.ImageFacade.SdcbSimdCodecs`（纯托管自绘 + 按需注册解码器，AOT 友好）。SkiaSharp / ImageSharp / OpenCvSharp 后端存在但**不默认使用**（原生依赖，违背本仓库卖点）。
- 直接复用它的检测流水线：`Letterbox.Apply(image, LetterboxOptions, out LetterboxInfo)`（默认 114 填充、居中、采样器可选）与 `LetterboxInfo.ToSource(RectF)` / `ToModel` 做坐标反算；`Tensorizer.ToTensor(image, TensorOptions{ NCHW/NHWC, Channels, Normalize, Mean, Std }, out info)` 直接出模型输入（内含 letterbox）；`Nms.Suppress` / `Nms.Select`（class-aware）做后处理；`Detection` + `DetectionDrawing.DrawDetections` + `DetectionStyle` 出标注图（内置文泉驿微米黑，中文标签开箱可用）。
- 编解码：软后端只带 `Sdcb.SimdCodecs.Abstractions`，格式包（`Sdcb.SimdCodecs.Jpeg/Png/Webp/Bmp` 2.0.0）由 CLI 按需引用并在启动时注册。
- 引用方式：`UseLocalImageFacade`（`true` 默认 = 同级 `..\ImageFacade\src` 源码；`false` = nuget 0.1.0）。
- **实测（M0-01，两条通道都跑过）**：本地（核心 0.2.0 / 软后端 0.1.1）与 nuget（核心与软后端 0.1.0）
  各跑 3 个冒烟测试全绿；后者同时证明 nuget 0.1.0 **确实包含**检测流水线，不需要降级实现。
- **同日修复（ImageFacade 仓库）**：`src/ImageFacade.SdcbSimdCodecs/ImageFacade.SdcbSimdCodecs.csproj`
  的打包说明注释里写了 ASCII 双连字符（`skip-duplicate` 参数），XML 注释不允许，MSBuild 报
  `MSB4025` 直接无法加载该工程；已把注释改写为"带 skip-duplicate 开关"，本地通道恢复。若上游另有改动，
  以 `git -C ..\ImageFacade diff` 复核。
**后果**：`H` 路线的工作量明显缩小——只写 DFL 解码、DETR top-k、掩码上采样、OCR 的 DB 后处理与 CTC 解码；letterbox/张量化/NMS/绘制不再自研，也不用自带位图字体。代价是多一层门面 API 依赖（0.2.0 发布前 API 可能微调）。
**复审条件**：ImageFacade 0.2.0 上 nuget 后，把包版本提到 0.2.x 并评估是否还需要 `UseLocalImageFacade` 开关；若软后端在超大掩码合成上有性能瓶颈，再考虑仅把绘制切到 SkiaSharp 后端。

## 2026-11-10 — 单项目 + 命名空间统一为 OnnxUtil（取消按族拆项目、取消 Sdcb.* 前缀）

**背景**：最初按"每族一个项目"（`Sdcb.SimdOnnxExt.Yolo` 等）搭建。落地 YOLO 后发现：族专属代码只有一个检测器 + 一个后处理，而跨族共享的（`ImageHost` / `InferenceBackend` / `GraphInspector` / `OnnxHeader`）远多于族专属部分——拆项目只增加引用与命名负担。
**决定**：
- 只保留一个库项目 `src/OnnxUtil/OnnxUtil.csproj`；各族代码用子目录（`Yolo/`，以后 `Ocr/`、`RfDetr/`、`Sam3/`），**不再新建族项目**。
- 命名空间与程序集从 `Sdcb.SimdOnnxExt*` 统一为 `OnnxUtil*`：`OnnxUtil`、`OnnxUtil.Imaging`、`OnnxUtil.Onnx`、`OnnxUtil.Yolo`、`OnnxUtil.Cli`、`OnnxUtil.Tests`；解决方案 `OnnxUtil.slnx`；CLI 可执行名保持 `simdonnxx`。
- 仓库目录名保留 `SimdOnnxExt`（远端仓库名不变；重命名目录会打断本地工作区与工具路径），**程序集/包/命名空间才是 `OnnxUtil`**。
**后果**：对外只有一个 `OnnxUtil` 包；单项目会持续变大，靠目录分层而非项目分层，`IsPackable` / 依赖只有一处。CLI 与测试的引用面收敛成一个 `ProjectReference`。
**复审条件**：若某族引入真正独占的重依赖（如 SAM3 需要 tokenizer 资源、OCR 需要大字典包），再评估拆出独立包；默认仍留在 `OnnxUtil` 内。

## 2026-11-10 — 语音走 sherpa-onnx，但**默认关闭**（保留"零原生依赖"）

**背景**：需求里要加语音识别。sherpa-onnx 是成熟的离线 ASR（paraformer/transducer/ctc/sensevoice/whisper），但 .NET 包是**原生**的：`org.k2fsa.sherpa.onnx.runtime.win-x64` 带 `onnxruntime.dll`(17 MB) + `sherpa-onnx-c-api.dll`(4.1 MB)，与"纯托管、无原生二进制、可 AOT"的核心承诺冲突。
**决定**：代码放进同一个库项目（`src/OnnxUtil/Audio/`，与"不要按族拆项目"一致），但用 `-p:UseSherpaOnnx=true` 开关控制：只有打开时才加 `PackageReference` 并定义 `SHERPA_ONNX` 宏（`SherpaAsr` 与 CLI `asr` 都在宏内）。默认构建与发布包仍是纯托管。
**后果**：默认用户拿到的是"零原生依赖"的视觉/OCR 包；需要语音的人必须显式 `-p:UseSherpaOnnx=true` 构建，并接受 ~21 MB 原生运行库。WAV 解析自己写（纯托管，支持 PCM 8/16/24/32 与 float32、任意声道下混），避免为读音频再引依赖。
**复审条件**：如果语音成为主线需求（例如要做"视频理解"流水线），再考虑把 Audio 拆成独立包 `OnnxUtil.Audio`，让核心包完全不出现原生引用。
