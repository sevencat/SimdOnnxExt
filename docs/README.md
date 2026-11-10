# 文档导航

| 文件 | 作用 |
|---|---|
| [../README.md](../README.md) | 项目是什么、怎么用、当前状态 |
| [../AGENTS.md](../AGENTS.md) | **协作协议**：进度/计划如何自动更新（先读这个） |
| [PLAN.md](PLAN.md) | 唯一可手改的计划：里程碑 + 任务表 |
| [PROGRESS.md](PROGRESS.md) | 进度快照（脚本生成，禁止手改） |
| [gap-matrix.md](gap-matrix.md) | 模型 × 算子 × 路线（E/P/H）的实测证据 |
| [model-setup.md](model-setup.md) | fixture 来源、许可证、ORT 金标准怎么生成 |
| [decisions.md](decisions.md) | ADR：方向性决定与复审条件 |
| [progress/](progress/) | 月度变更日志（append-only） |

## 阅读顺序

1. **想改代码**：`AGENTS.md` → `PLAN.md` 找到自己的任务 ID → 动手 → `-Set` 更新状态。
2. **想知道做到哪了**：`PROGRESS.md`（自动生成，含"建议下一步"）。
3. **想知道某个模型为什么跑不起来**：`gap-matrix.md`（每个缺口都标了 E / P / H 和负责的任务）。
4. **想换路线**：先读 `decisions.md`，改完计划后追加一条 ADR。
