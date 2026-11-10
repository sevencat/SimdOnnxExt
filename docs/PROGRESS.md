# 进度快照（自动生成）

> 生成时间：2026-11-10 16:55
> 由 tools/progress.ps1 -Write 生成，请勿手改；计划与验收口径改 docs/PLAN.md，规范见 AGENTS.md。

**总进度：17/55（31%）**  [##########----------------------]

| 里程碑 | 完成 | 总计 | 状态 |
|---|---:|---:|---|
| M0 · 仓库骨架与规范 | 7 | 8 | 进行中 |
| 阶段 1 · YOLO 检测一键跑通（任务 ID `M1-*`） | 5 | 7 | 进行中 |
| 阶段 1 · YOLO 全家族（seg / pose / v10 / v26，任务 ID `M2-*`） | 0 | 5 | 未开始 |
| 阶段 2 · RF-DETR（任务 ID `M4-*`） | 1 | 4 | 进行中 |
| 阶段 3 · simdocr / OCR 一键（任务 ID `M3-*`） | 3 | 5 | 进行中 |
| 阶段 4 · SAM3（任务 ID `M5-*`，暂缓） | 0 | 6 | 未开始 |
| 阶段 5 · ~~语音 ASR（sherpa-onnx，可选）~~ — **已移除** | 0 | 3 | 进行中，有阻塞 |
| 阶段 6 · 纯托管 ASR（Paraformer on SimdOnnx，任务 ID `P-*`） | 1 | 6 | 进行中 |
| M6 · 打磨与发布 | 0 | 5 | 未开始 |
| E · 引擎侧（SimdOnnx 仓库 PR，独立于本仓库） | 0 | 6 | 未开始 |

## 进行中（doing）

- M0-07 · CI：windows + ubuntu，net10，build/test/progress -Check ｜ ci.yml 已落地（两个同级仓库 checkout + UseLocalImageFacade=false），待首次 push 验证
- M1-05 · Vulkan 后端接入与 CPU/Vulkan 一致性 ｜ Vulkan 能跑且检测结果与 CPU 一致，但原始输出存在已知偏差 0.3502 @#967，待定位

## 受阻（blocked）

- A-01 · sherpa-onnx 接线：WavReader + SherpaAsr + CLI `asr` ｜ 原因：已按需求移除：sherpa 自带 Microsoft ONNX Runtime；包/开关/SherpaAsr/CLI asr 全删，WavReader 保留
- A-02 · 用真实模型跑通识别 ｜ 原因：曾跑通（你好这是语音识别测试今天天气不错，5.75s/80.5ms），但依赖 sherpa+ORT，已删除
- A-03 · VAD 分段 + 长音频批量识别 ｜ 原因：VAD 待纯托管路线再评估（Silero VAD fp32 可用 SimdOnnx 跑）

## 最近变更（2026-11）

- 2026-11-10 16:09 · M3-03 · done → done · PP-LCNet 方向分类接线：80x160 输入、softmax 后 p>0.9 才旋转 180
- 2026-11-10 16:09 · M3-01 · done → done · PaddleOcrPipeline det：DB 概率图->纯 C# 连通域->轴对齐框->面积/周长外扩；bus.jpg 上 8 行
- 2026-11-10 16:16 · M3-05 · todo → todo · 可视化改进：标签字号/线宽按图幅缩放（最短边 4.5%）+ --font/--stroke/--max-side；大图小字用 --det-limit 1600 修正车牌浙A·3M15X
- 2026-11-10 16:19 · M4-01 · done → done · P 路线验证：onnxslim 1537->821 节点、19->12 类、dets/labels 与原始 1.9e-5/4.0e-5 等价；引擎首块绊脚石为 patch-embed Cast，脚本 tools/prep/slim-model.py
- 2026-11-10 16:20 · M4-02 · todo → todo · slim 模型的引擎首个失败点是 bool 张量（/transformer/Cast_2_output_0 raw dtype 9），E-03 需先补 bool/int-as-float 表达再补 11 个算子
- 2026-11-10 16:29 · A-01 · done → done · sherpa-onnx 接线完成（可选开关 -p:UseSherpaOnnx=true）：WavReader(纯托管)+SherpaAsr(5 种模型)+CLI asr；带开关构建 0 警告、原生绑定可加载
- 2026-11-10 16:40 · A-02 · done → done · 真实模型跑通：F:\ai\sherpa（Paraformer int8 + tokens.txt）+ SAPI Huihui 合成语音，识别出 你好这是语音识别测试今天天气不错，5.75s 音频 80.5ms
- 2026-11-10 16:55 · A-01 · blocked → blocked · 已按需求移除：sherpa 自带 Microsoft ONNX Runtime；包/开关/SherpaAsr/CLI asr 全删，WavReader 保留
- 2026-11-10 16:55 · A-02 · blocked → blocked · 曾跑通（你好这是语音识别测试今天天气不错，5.75s/80.5ms），但依赖 sherpa+ORT，已删除
- 2026-11-10 16:55 · A-03 · blocked → blocked · VAD 待纯托管路线再评估（Silero VAD fp32 可用 SimdOnnx 跑）

## 建议下一步

- M0-07 · CI：windows + ubuntu，net10，build/test/progress -Check
  - 验收：PR 上全部 job 绿
  - 备注：ci.yml 已落地（两个同级仓库 checkout + UseLocalImageFacade=false），待首次 push 验证
