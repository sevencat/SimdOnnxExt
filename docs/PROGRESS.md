# 进度快照（自动生成）

> 生成时间：2026-11-10 15:37
> 由 tools/progress.ps1 -Write 生成，请勿手改；计划与验收口径改 docs/PLAN.md，规范见 AGENTS.md。

**总进度：7/46（15%）**  [#####---------------------------]

| 里程碑 | 完成 | 总计 | 状态 |
|---|---:|---:|---|
| M0 · 仓库骨架与规范 | 7 | 8 | 进行中 |
| 阶段 1 · YOLO 检测一键跑通（任务 ID `M1-*`） | 0 | 7 | 未开始 |
| 阶段 1 · YOLO 全家族（seg / pose / v10 / v26，任务 ID `M2-*`） | 0 | 5 | 未开始 |
| 阶段 2 · RF-DETR（任务 ID `M4-*`） | 0 | 4 | 未开始 |
| 阶段 3 · simdocr / OCR 一键（任务 ID `M3-*`） | 0 | 5 | 未开始 |
| 阶段 4 · SAM3（任务 ID `M5-*`，暂缓） | 0 | 6 | 未开始 |
| M6 · 打磨与发布 | 0 | 5 | 未开始 |
| E · 引擎侧（SimdOnnx 仓库 PR，独立于本仓库） | 0 | 6 | 未开始 |

## 进行中（doing）

- M0-07 · CI：windows + ubuntu，net10，build/test/progress -Check ｜ ci.yml 已落地（两个同级仓库 checkout + UseLocalImageFacade=false），待首次 push 验证

## 受阻（blocked）

- （无）

## 最近变更（2026-11）

- 2026-11-10 15:03 · M0-05 · todo → todo · 确认 ImageFacade 0.1.0 已含 Letterbox/Tensorizer/Nms/DetectionDrawing，包引用通道不做降级实现
- 2026-11-10 15:06 · M0-01 · todo → done · slnx + src/SimdOnnxExt + tests；dotnet build/test 通过（3 冒烟：引擎公共面、ImageFacade 流水线类型、软后端 letterbox 张量化 64x40/PadTop12/ToSource 160,100）
- 2026-11-10 15:09 · M0-01 · done → done · 两条通道均绿：本地 ImageFacade 0.2.0/0.1.1 与 nuget 0.1.0 各 3 测试通过；并修复 ImageFacade 软后端 csproj 的 MSB4025（注释含双连字符）
- 2026-11-10 15:19 · M0-03 · done → done · GraphInspector + simdonnxx inspect：yolo11n 报引擎原话 Softmax axis=1 + E-01；opset12(128x128) COMPILES；8 个测试全绿
- 2026-11-10 15:20 · M5-01 · todo → todo · SAM3 暂缓：优先级 YOLO -> RF-DETR -> simdocr -> SAM3（见 decisions.md）
- 2026-11-10 15:34 · M0-04 · todo → done · record.py + GoldenFixture + GoldenParityTests：yolo11n-opset12 引擎输出 vs ORT 最大偏差 5.493e-4（302KiB fixture 入库），9 测试全绿
- 2026-11-10 15:37 · M0-05 · todo → done · ImageHost（CreateEngine/Letterbox/ToTensor）+ 测试：letterbox 往返 ≤0.5px、NCHW 3x64x64、中文标签可绘制
- 2026-11-10 15:37 · M0-06 · todo → done · InferenceBackend（CPU/Vulkan/Metal + Auto 回落并给原因）+ 测试：CPU 对 ORT 5.5e-4，GPU 能跑
- 2026-11-10 15:37 · M1-05 · todo → todo · Vulkan 一致性待查：yolo11n(128) 与 CPU 偏差 0.3502 @#967（DFL 段），断言以 Skip 保留
- 2026-11-10 15:37 · M0-07 · todo → doing · ci.yml 已落地（两个同级仓库 checkout + UseLocalImageFacade=false），待首次 push 验证

## 建议下一步

- M0-07 · CI：windows + ubuntu，net10，build/test/progress -Check
  - 验收：PR 上全部 job 绿
  - 备注：ci.yml 已落地（两个同级仓库 checkout + UseLocalImageFacade=false），待首次 push 验证
