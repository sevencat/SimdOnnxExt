# Copilot / agent 指令

本仓库的进度与计划规范见根目录 **AGENTS.md**，唯一计划来源是 `docs/PLAN.md`。

任何会改变任务状态的改动（开始、完成、受阻、方向变更）都必须：

1. 用 `powershell -File tools\progress.ps1 -Set <ID> <状态> -Note "说明"` 更新 `docs/PLAN.md`；
2. 收尾时 `-Write` 重新生成 `docs/PROGRESS.md`，并保证 `-Check` 退出码为 0。

禁止手改 `docs/PROGRESS.md` 与 `docs/progress/*.md` 的历史行。