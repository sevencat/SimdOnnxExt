# 进度快照（自动生成）

> 生成时间：2026-11-10 15:46
> 由 tools/progress.ps1 -Write 生成，请勿手改；计划与验收口径改 docs/PLAN.md，规范见 AGENTS.md。

**总进度：12/46（26%）**  [########------------------------]

| 里程碑 | 完成 | 总计 | 状态 |
|---|---:|---:|---|
| M0 · 仓库骨架与规范 | 7 | 8 | 进行中 |
| 阶段 1 · YOLO 检测一键跑通（任务 ID `M1-*`） | 5 | 7 | 进行中 |
| 阶段 1 · YOLO 全家族（seg / pose / v10 / v26，任务 ID `M2-*`） | 0 | 5 | 未开始 |
| 阶段 2 · RF-DETR（任务 ID `M4-*`） | 0 | 4 | 未开始 |
| 阶段 3 · simdocr / OCR 一键（任务 ID `M3-*`） | 0 | 5 | 未开始 |
| 阶段 4 · SAM3（任务 ID `M5-*`，暂缓） | 0 | 6 | 未开始 |
| M6 · 打磨与发布 | 0 | 5 | 未开始 |
| E · 引擎侧（SimdOnnx 仓库 PR，独立于本仓库） | 0 | 6 | 未开始 |

## 进行中（doing）

- M0-07 · CI：windows + ubuntu，net10，build/test/progress -Check ｜ ci.yml 已落地（两个同级仓库 checkout + UseLocalImageFacade=false），待首次 push 验证
- M1-05 · Vulkan 后端接入与 CPU/Vulkan 一致性 ｜ Vulkan 能跑且检测结果与 CPU 一致，但原始输出存在已知偏差 0.3502 @#967，待定位

## 受阻（blocked）

- （无）

## 最近变更（2026-11）

- 2026-11-10 15:37 · M0-05 · todo → done · ImageHost（CreateEngine/Letterbox/ToTensor）+ 测试：letterbox 往返 ≤0.5px、NCHW 3x64x64、中文标签可绘制
- 2026-11-10 15:37 · M0-06 · todo → done · InferenceBackend（CPU/Vulkan/Metal + Auto 回落并给原因）+ 测试：CPU 对 ORT 5.5e-4，GPU 能跑
- 2026-11-10 15:37 · M1-05 · todo → todo · Vulkan 一致性待查：yolo11n(128) 与 CPU 偏差 0.3502 @#967（DFL 段），断言以 Skip 保留
- 2026-11-10 15:37 · M0-07 · todo → doing · ci.yml 已落地（两个同级仓库 checkout + UseLocalImageFacade=false），待首次 push 验证
- 2026-11-10 15:46 · M1-01 · todo → done · CLI run/inspect 均可用：run 读图->letterbox->推理->DFL/NMS->画框->存盘，--json 出结果
- 2026-11-10 15:46 · M1-02 · todo → done · CPU 跑通 yolo11n-opset12：对 ORT 最大偏差 5.493e-4；bus.jpg 端到端 5 个目标
- 2026-11-10 15:46 · M1-03 · todo → done · ImageHost letterbox（114/居中）+ LetterboxInfo.ToSource 往返 ≤0.5px，真图坐标正确
- 2026-11-10 15:46 · M1-04 · todo → done · YoloPostprocess：DFL 输出解码 + 分数阈值 + class-aware NMS（ImageFacade Nms.Suppress）
- 2026-11-10 15:46 · M1-06 · todo → done · 默认把框画到原图并另存；--json 输出 JSON（已实测）
- 2026-11-10 15:46 · M1-05 · todo → doing · Vulkan 能跑且检测结果与 CPU 一致，但原始输出存在已知偏差 0.3502 @#967，待定位

## 建议下一步

- M0-07 · CI：windows + ubuntu，net10，build/test/progress -Check
  - 验收：PR 上全部 job 绿
  - 备注：ci.yml 已落地（两个同级仓库 checkout + UseLocalImageFacade=false），待首次 push 验证
