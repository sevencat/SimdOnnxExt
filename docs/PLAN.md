# OnnxUtil 计划（单一事实源）

> 本文件是**唯一可手改**的计划。任务行格式固定：`| ID | 任务 | 状态 | 验收 | 备注 |`；
> 状态只能是 `todo` / `doing` / `blocked` / `done`；单元格内不要出现 `|`。
> 改完运行 `powershell -File tools\progress.ps1 -Write`，`docs/PROGRESS.md` 由脚本生成，不要手改。
> 规范正文见根目录 `AGENTS.md`；决策记录见 `docs/decisions.md`；算子差距见 `docs/gap-matrix.md`。

**目标**：只依赖 `Sdcb.SimdOnnx` 的公共 API，做到 **YOLO / RF-DETR / SAM3 / OCR 一键调用**——统一 API + 单一 CLI（`simdonnxx`）+ 自动识别模型族、自动选后端（CPU / Vulkan / Metal）。

**三条消解路线**（每个缺口必须落一条，写进"备注"或 `gap-matrix.md`）：
`E` = 改引擎（新的 ONNX 逻辑算子 / 多输出 / Softmax 任意轴 / int64 图输入改造，走 SimdOnnx PR）；
`P` = 离线图预处理（剪枝、单输出封装、等价改写，工具在 `tools/OnnxUtil.ModelPrep`）；
`H` = 主机侧 C# 实现（模型族专属解码：DFL、DETR top-k、掩码上采样、OCR 的 DB 后处理与 CTC、SAM3 tokenizer；
通用图像操作——加载/保存、letterbox、NCHW/NHWC 张量化、NMS、绘制与文字——一律用 `sevencat/ImageFacade`，不重复实现）。

## M0 · 仓库骨架与规范

| ID | 任务 | 状态 | 验收 | 备注 |
|---|---|---|---|---|
| M0-01 | 建 slnx、构建属性、UseLocalSimdOnnx / UseLocalImageFacade 引用开关 | done | `dotnet build` 通过；`-p:UseLocalSimdOnnx=false`、`-p:UseLocalImageFacade=false` 均能走包引用 | 构建结构定型：单库项目 OnnxUtil（Yolo 等族放子目录）+ OnnxUtil.Cli + OnnxUtil.slnx；命名空间/程序集统一 OnnxUtil；Release 构建与 17 个测试通过 |
| M0-02 | 进度规范：AGENTS.md + progress.ps1 + PROGRESS 生成 + CI 校验 | done | `-Check` 退出码 0；`-Set` 能改状态并追加日志 | AGENTS.md + progress.ps1 + PROGRESS 生成 + CI 校验 已落地 |
| M0-03 | GraphReport + `simdonnxx inspect`：算子直方图 / IO / 引擎编译探测 / E-P-H 路线 | done | yolo11n.onnx 报出引擎原话的 Softmax 阻断 + E-01；opset12 报 COMPILES | GraphInspector + simdonnxx inspect：yolo11n 报引擎原话 Softmax axis=1 + E-01；opset12(128x128) COMPILES；8 个测试全绿 |
| M0-04 | fixture 约定 + ORT 金标准记录脚本骨架 | done | 一条命令产出 `tests/Fixtures/*.json` + `*.f32` | record.py + GoldenFixture + GoldenParityTests：yolo11n-opset12 引擎输出 vs ORT 最大偏差 5.493e-4（302KiB fixture 入库），9 测试全绿 |
| M0-05 | 图像门面接入（ImageFacade）：加载/保存、Letterbox、Tensorizer、NMS、DetectionDrawing | done | 640 letterbox 经 `LetterboxInfo.ToSource` 往返误差 ≤ 0.5 px；标注图能出中文标签 | ImageHost（CreateEngine/Letterbox/ToTensor）+ 测试：letterbox 往返 ≤0.5px、NCHW 3x64x64、中文标签可绘制 |
| M0-06 | 后端抽象：CPU / Vulkan 选择与 IsLost 回退 | done | 无 Vulkan 设备时自动回落 CPU 并给出原因 | InferenceBackend（CPU/Vulkan/Metal + Auto 回落并给原因）+ 测试：CPU 对 ORT 5.5e-4，GPU 能跑 |
| M0-07 | CI：windows + ubuntu，net10，build/test/progress -Check | doing | PR 上全部 job 绿 | ci.yml 已落地（两个同级仓库 checkout + UseLocalImageFacade=false），待首次 push 验证 |
| M0-08 | 差距矩阵初稿（yolo11n / rfdetr-small / OCR / SAM3） | done | `docs/gap-matrix.md` 有实测算子统计 | 差距矩阵：yolo11n-opset12 可编译；yolo11n/rfdetr-small 缺口已实测 |

## 阶段 1 · YOLO 检测一键跑通（任务 ID `M1-*`）

| ID | 任务 | 状态 | 验收 | 备注 |
|---|---|---|---|---|
| M1-01 | CLI 骨架 `run` / `inspect`，参数解析与退出码 | done | `simdonnxx inspect -m x.onnx` 输出 JSON（已完成）；`run` 出结果 | CLI run/inspect 均可用：run 读图->letterbox->推理->DFL/NMS->画框->存盘，--json 出结果 |
| M1-02 | CPU 跑通 `yolo11n-opset12.onnx` 并对齐 ORT | done | 原图坐标框与 ORT 的 IoU ≥ 0.99 | CPU 跑通 yolo11n-opset12：对 ORT 最大偏差 5.493e-4；bus.jpg 端到端 5 个目标 |
| M1-03 | letterbox（114 填充）与坐标还原 | done | 与 Ultralytics 预处理逐像素一致 | ImageHost letterbox（114/居中）+ LetterboxInfo.ToSource 往返 ≤0.5px，真图坐标正确 |
| M1-04 | DFL 解码 + 置信度过滤 + NMS | done | COCO 子集结果对 ORT 一致 | YoloPostprocess：DFL 输出解码 + 分数阈值 + class-aware NMS（ImageFacade Nms.Suppress） |
| M1-05 | Vulkan 后端接入与 CPU/Vulkan 一致性 | doing | fp16 下分数差 ≤ 2e-2，框 IoU ≥ 0.98 | Vulkan 能跑且检测结果与 CPU 一致，但原始输出存在已知偏差 0.3502 @#967，待定位 |
| M1-06 | JSON 结果 + 画框/标签输出 | done | `--draw` 出图，`--json` 出结果 | 默认把框画到原图并另存；--json 输出 JSON（已实测） |
| M1-07 | 回归：COCO 子集脚本 + 文档化命令 | todo | 一条命令复现通过率 | 验收命令写进本行 |

## 阶段 1 · YOLO 全家族（seg / pose / v10 / v26，任务 ID `M2-*`）

| ID | 任务 | 状态 | 验收 | 备注 |
|---|---|---|---|---|
| M2-01 | 分割双输出（output0 + output1） | todo | 掩码与 ORT 的 IoU ≥ 0.98 | P 合并或 E-02 多输出；掩码着色自己写 |
| M2-02 | 姿态（关键点）解码 | todo | 关键点误差 ≤ 1 px | 单输出 |
| M2-03 | YOLOv10 / YOLO26 端到端头 | todo | 与原图 NMS 后结果一致 | P 剪尾自写 top-k 或 E-03 |
| M2-04 | 新导出的 DFL Softmax 任意轴接入 | todo | `yolo11n.onnx` 可编译并通过 M1 验收 | 依赖 E-01 |
| M2-05 | 批图 / 目录 / 视频 + 会话复用 | todo | 同形状零重复编译，吞吐线性 | `CpuModel` 形状 LRU |

## 阶段 2 · RF-DETR（任务 ID `M4-*`）

| ID | 任务 | 状态 | 验收 | 备注 |
|---|---|---|---|---|
| M4-01 | 可行性试验：编译截断到 `transformer/decoder/norm` | todo | 记录剪枝后仍缺的算子清单 | 先证伪再排期 |
| M4-02 | 上游算子批次落地（E-03）后编译整图 | todo | 全图编译通过且 CPU 结果对 ORT 一致 | 见 gap-matrix |
| M4-03 | Host 解码：sigmoid + top-k + 框还原 + 类别表 | todo | 300 框与 ORT mAP 差 ≤ 0.5 | 读 `rfdetr_inference` 元数据 |
| M4-04 | Vulkan 对齐与性能基准 | todo | 与 CPU 同结果，GPU 有加速 | 记录 device 面包屑 |

## 阶段 3 · simdocr / OCR 一键（任务 ID `M3-*`）

| ID | 任务 | 状态 | 验收 | 备注 |
|---|---|---|---|---|
| M3-01 | det + DB 后处理（二值化 / 连通域 / UNCLIP） | done | 行框与 SimdPaddleOCR 一致 | PaddleOcrPipeline det：DB 概率图->纯 C# 连通域->轴对齐框->面积/周长外扩；bus.jpg 上 8 行 |
| M3-02 | rec + 字典（`CtcProjection` / `CtcGreedyDecoder`） | done | 文本逐行一致，分数差 ≤ 1e-4 | rec：裁剪->高 48 等比缩放->引擎 CtcGreedyDecoder；字典 6906 类；合成中文 你好世界/123 全对 |
| M3-03 | 方向分类 cls 接入 | done | 180 度样本纠正率一致 | PP-LCNet 方向分类接线：80x160 输入、softmax 后 p>0.9 才旋转 180 |
| M3-04 | `VkBatch` + `ArgMaxUnits` 行批处理 | todo | Vulkan 吞吐显著优于逐行 | 现在逐行跑，还没有批处理 |
| M3-05 | 与 SimdPaddleOCR 逐行对齐 + 可视化 | todo | 同一数据集 100 图全一致 | 已有 bus.jpg + 合成中文两条回归；还差对照数据集 |

## 阶段 4 · SAM3（任务 ID `M5-*`，暂缓）

> 优先级：用户明确要求 **YOLO → RF-DETR → simdocr → SAM3**，SAM3 排在这三个之后启动。

| ID | 任务 | 状态 | 验收 | 备注 |
|---|---|---|---|---|
| M5-01 | 三模型 inspect 差距表（encoder / text / decoder） | todo | `gap-matrix.md` 增补 SAM3 行 | SAM3 暂缓：优先级 YOLO -> RF-DETR -> simdocr -> SAM3（见 decisions.md） |
| M5-02 | 文本输入改造：token(int64) → token_embeds(float) | todo | 文本提示端到端出掩码 | E-04 或 P |
| M5-03 | image encoder 会话（1024 固定形状） | todo | embedding 与 ORT 差 ≤ 1e-3 | 显存/内存预算 |
| M5-04 | decoder：框 / 点 / 文本提示 | todo | 三类提示均出正确掩码 | |
| M5-05 | 掩码后处理（上采样 / 阈值 / 裁剪）+ 可视化 | todo | 掩码 IoU ≥ 0.95 对 ORT | 输出 PNG |
| M5-06 | Vulkan 对齐与性能基准 | todo | 与 CPU 同结果 | fp16 存储 |

## M6 · 打磨与发布

| ID | 任务 | 状态 | 验收 | 备注 |
|---|---|---|---|---|
| M6-01 | models 下载脚本 + 许可证清单 | todo | `models\fetch-models.ps1` 可拉到全部 fixture | 记录 AGPL / Meta 许可 |
| M6-02 | bench 报告（CPU / Vulkan vs ORT） | todo | median、warmup、设备面包屑齐全 | 口径见 AGENTS.md |
| M6-03 | NuGet 打包（`OnnxUtil.*`） | todo | 包内容审计无多余依赖 | 依赖引擎精确版本 |
| M6-04 | 中文 README + 各族文档 | todo | 一篇一个模型族的用法 | |
| M6-05 | 可选 Web 演示（上传图 → 结果） | todo | 三个族各一条链路可用 | 非必须 |

## E · 引擎侧（SimdOnnx 仓库 PR，独立于本仓库）

| ID | 任务 | 状态 | 验收 | 备注 |
|---|---|---|---|---|
| E-01 | Softmax 支持任意轴（前插/尾插 Transpose 或专用 kernel） | todo | `yolo11n.onnx` 可编译 | 约 15 行改动 |
| E-02 | 多输出计划 `ExecPlan.OutputValues[]` | todo | 分割模型单次运行取两个输出 | seg / RF-DETR / SAM3 都需要 |
| E-03 | 通用算子批次：Cast / Expand / Where / Equal / Not / And / Greater / Less / ConstantOfShape / Tile / Gather(运行期) / ReduceSum / ReduceMax / TopK / GatherElements / GridSample / Sin / Cos / Exp | todo | 每个算子有 ONNX 后端测试通过 | RF-DETR 的前置 |
| E-04 | 图输入 int64 → float 通道（或等价改造） | todo | SAM3 文本 tokens 可直接喂 | 或改用 P |
| E-05 | E-03 的 Vulkan / Metal 发射 | todo | GPU 目标不逐节点回退 | 引擎规则：不支持就不能选目标 |
| E-06 | 公共面暴露图 I/O 形状与节点属性（或一个 EngineCapabilities 支持集 API） | todo | ext 的 `OnnxHeader` 头部扫描可以删掉 | 目前 `OnnxModel` 只有 Opset/Nodes/InitializerShape |



























