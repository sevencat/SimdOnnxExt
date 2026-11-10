# AGENTS.md — 进度与计划自更新规范

> 本文件是本仓库的**协作协议**。人或 AI 代理在动手前先读一遍；提交前对照第 4、6 节。
> 计划只有一份、进度只有一份、历史只追加——三者都由 `tools/progress.ps1` 串起来。

## 0. 一句话

- **计划**：`docs/PLAN.md`，唯一可手改的计划（任务表格）。
- **进度**：`docs/PROGRESS.md`，**只**由 `tools/progress.ps1 -Write` 生成，禁止手改。
- **历史**：`docs/progress/YYYY-MM.md`，只追加，由 `-Set` 自动写；历史行不许改。

任何状态变化都必须经过脚本，这样 `git log`、`PLAN.md`、`PROGRESS.md` 三者永远一致。

## 1. 文件职责

| 文件 | 谁写 | 可手改 | 作用 |
|---|---|---|---|
| `docs/PLAN.md` | 人 / agent | ✅ 唯一可改 | 里程碑 + 任务表：`ID / 任务 / 状态 / 验收 / 备注` |
| `docs/PROGRESS.md` | `progress.ps1 -Write` | ❌ 生成物 | 总进度、各里程碑计数、doing / blocked、最近变更、下一步 |
| `docs/progress/YYYY-MM.md` | `progress.ps1 -Set` | ❌ 只追加 | 状态变更日志（谁、何时、从什么到什么、为什么） |
| `docs/decisions.md` | 人 / agent | ✅ | ADR：方向性决定、被推翻的决定、复审条件 |
| `docs/gap-matrix.md` | 人 / agent | ✅ | 模型 × 算子 × 路线（E/P/H）的实测证据 |
| `docs/model-setup.md` | 人 / agent | ✅ | fixture 从哪来、许可证、金标准怎么生成 |
| `AGENTS.md` | 人 | ✅ | 本规范；改规范要写 ADR |

## 2. 任务与状态

**ID 规则**：`M<里程碑序号>-<两位序号>`（如 `M1-03`）；引擎侧任务用 `E-<两位序号>`（如 `E-01`）。
ID 一旦分配就**不复用**，删除任务时保留行并置 `done`/`blocked` 并写原因。

**状态机**：`todo` → `doing` → `done`，中途可 `blocked`（必须写原因），解除后回 `doing`。
状态只有这四个值，脚本会校验，写错会直接失败。

**任务行格式**（严格，脚本靠它解析）：

```
| M1-03 | letterbox（114 填充）与坐标还原 | todo | 与 Ultralytics 预处理逐像素一致 | 支持非方形输入 |
```

四条硬规则：ID 在第一列；状态在第三列且是四个值之一；**单元格里不能出现 `|`**；"验收"必须是一条**可被机器或人复现**的判据（命令或阈值）。

**新增任务时**：在对应里程碑表格末尾加行；如果它来自某个算子/图结构缺口，备注里写清是哪条路线（`E` / `P` / `H`），并在 `docs/gap-matrix.md` 留证据。

## 3. 命令

```powershell
# 改状态（同时改 PLAN、追加日志、重新生成 PROGRESS）
powershell -File tools\progress.ps1 -Set M1-03 doing
powershell -File tools\progress.ps1 -Set M1-03 done -Note "letterbox 用 ImageFacade Letterbox.Apply + ToSource 反算"

# 只重生成快照（改完 PLAN 的文案/验收时）
powershell -File tools\progress.ps1 -Write

# 校验：PROGRESS 是否与 PLAN 一致（CI 也跑它，不一致退出码 1）
powershell -File tools\progress.ps1 -Check
```

退出码：`0` = 一致 / 成功；`1` = 过期；抛错 = 计划格式非法（状态拼错、ID 找不到）。

## 4. 必须更新进度的时刻

| 时刻 | 动作 |
|---|---|
| 领取任务、开始动手 | `-Set <ID> doing` |
| 任务达成验收 | `-Set <ID> done -Note "<证据：命令/数值/文件>"` |
| 被外部依赖卡住 | `-Set <ID> blocked -Note "<卡在哪，谁解锁>"` |
| 一次会话/一次提交结束 | `-Write`（或至少保证 `-Check` 为 0） |
| 改验收口径、拆分子任务 | 改 `PLAN.md` 后 `-Write` |
| 方向性改变（换路线、砍功能、换后端策略） | 改 `PLAN.md` + 在 `docs/decisions.md` 写一条 ADR |

## 5. 提交规范

- 提交信息：`<ID>: <一句话结果>`，例如 `M1-03: letterbox 预处理与坐标还原`。
- 一个任务尽量一个提交；跨任务的重构单独提交，信息里列出涉及的 ID。
- 不提交：`models/` 下的权重、`*.onnx`、`bin/`、`obj/`、`tools/nupkgs/`（`.gitignore` 已挡）。
- 提交前必须：`-Check` 为 0（CI 也会拦）。

## 6. Definition of Done

一个任务算完成，必须同时满足：

1. 验收列里写的命令/阈值可复现，并且**实测过**（`-Note` 里贴关键数值）。
2. 有对应自动化覆盖：金标准对齐、CPU↔Vulkan 一致性、或 `inspect` 报表测试（三者取适用的）。
3. 新加的公开 API 有 XML 注释；面向用户的行为写进 `README.md` 或 `docs/`。
4. 失败路径有明确异常信息（不要吞异常、不要静默回退）。
5. 性能改动给出 CPU 与 GPU 两边的数字（没有 GPU 就标 `CPU only` 并说明原因）。
6. `powershell -File tools\progress.ps1 -Check` 通过。

## 7. 度量口径（正确性与性能）

**正确性**（数值容差写进测试，不要靠肉眼）：

| 对比 | fp32（CPU） | fp16 存储（Vulkan） |
|---|---|---|
| 原始输出逐元素 | ≤ 1e-3（YOLO11n 实测 5.493e-4，Conv 累加顺序 + DFL softmax） | ≤ 2e-2 |
| 检测框 IoU | ≥ 0.99 | ≥ 0.98 |
| 掩码 IoU | ≥ 0.98 | ≥ 0.95 |
| OCR 文本 | 逐行一致 | 逐行一致 |

**性能**：同机、空闲、高性能电源；每个 case 至少 3 轮交替 A/B（比值在 3% 内加到 6 轮），取中位数，丢弃 1 次预热；报告里写清 CPU/GPU、线程数、fp16/fp32、输入尺寸、设备面包屑（`VkBackend.Device` 或 `GCDRNET_VK_VERBOSE=1`）、内存峰值。对照基准：同一张图、同一份 f32 输入，用本机 `onnxruntime` 记录一次。

## 8. 方向变更必须留 ADR

`docs/decisions.md` 条目格式（与 `sdcb/SimdOnnx` 的 `docs/decisions.md` 一致）：

```
## 2026-11-10 — 标题

背景 / 决定 / 后果 / 复审条件（什么情况下重新评估）
```

被推翻的旧决定**不要删**，在条目里写 `Superseded by ...` 或 `Reversed`。

## 9. 红线

1. 不手改 `docs/PROGRESS.md`；不改历史日志行。
2. 不放宽验收列让任务"变绿"；做不到就 `blocked` 并写原因。
3. 不把没实测的数字写进 `-Note` 或验收。
4. 不在 `PLAN.md` 的单元格里用 `|`；不要在表格之间插其他表格打乱解析。
5. 不引入原生依赖：引擎是纯托管；图像侧只用 `Sevencat.ImageFacade` + 软后端 `ImageFacade.SdcbSimdCodecs`（SkiaSharp / ImageSharp / OpenCvSharp 后端存在，但**不默认使用**）。这是本仓库的卖点。

## 10. 环境与编码

- SDK：`global.json` 固定 `10.0.101`（`rollForward: latestFeature`）。
- 引擎：默认 `ProjectReference` 到同级仓库 `..\SimdOnnx\src\Sdcb.SimdOnnx\Sdcb.SimdOnnx.csproj`；
  无源码环境用 `-p:UseLocalSimdOnnx=false`（走 `Directory.Packages.props` 的精确版本）。
- 图像处理：默认 `ProjectReference` 同级 `..\ImageFacade\src`（核心 0.2.0 + 软后端 0.1.1）；
  没有同级仓库时用 `-p:UseLocalImageFacade=false` 走 nuget `Sevencat.ImageFacade` 0.1.0
  （已验证包含 `Letterbox` / `Tensorizer` / `Nms` / `DetectionDrawing`）。两条通道都有冒烟测试兜底。
  图片格式包（`Sdcb.SimdCodecs.Jpeg/Png/Webp/Bmp`）由 CLI 显式引用并注册，软后端本身只带 Abstractions。
- 文本一律 **UTF-8**（无 BOM）；**例外**：`*.ps1` 必须带 BOM，否则 Windows PowerShell 5.1 会按 ANSI 解码，中文变乱码并写进生成物。`.editorconfig` 已声明。
- 本机只有 PowerShell 5.1：脚本内容要 5.1/7 双兼容（不用 `??`、不用三元表达式）。
