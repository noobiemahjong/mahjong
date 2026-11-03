# tools/AGENTS.md（工具子树规则）

适用 `tools/**`。全仓常驻规矩见根 [AGENTS.md](../AGENTS.md)。

## 工具清单

| 工具 | 用途 |
|---|---|
| `doc-gate` | 文档门禁：链接/BOM/LF/表结构/预算/Note 路径/编号 |
| `git-hooks` | `commit-msg` 钩子：Conventional Commit 标题与正文长度 |
| `table-inspect` | 解码 `content/tables/*.bytes` 为 JSONL（只读） |
| `aot-smoke` | 客户端 AOT 导出 + 独立 exe 冒烟 |
| `team-taskbook` | 团队任务书模板（Lead 工作标准、批次契约、单元任务书）；批次型任务用 |
| `team-taskbook/check-review-sessions.ps1` | 审核会话超期判定（读 DSH 会话投影缓存）：步数 / 平均上下文 / 是否超期（>80 步或 avg >200K）；`-List N` 列最近会话、`-Json` 机读；ASCII 输出 |

## 规则

- **脚本纯 ASCII**（注释/输出）；处理中文文件时显式 UTF-8，不用默认编码往返。
- **只读优先**：查表/回放检查工具默认不写仓库；需要写时输出到临时目录，并说明。
- **禁止手改生成物**：生成器产物只由工具重跑产生；工具与产物同批提交。
- **门禁确定性**：同一输入同一退出码；规则变更必须同步 [docs/AGENTS.md](../docs/AGENTS.md) 的门禁表。
- **工具自带模板预算**：超过 `docs/AGENTS.md` 预算表「模板 4KB」时按提额流程在 `doc-budgets.json` 登记，并在 Note 写理由（例：`tools/team-taskbook/templates/lead-brief.md` 22528）。
- **不碰外部资产**：旧 Unity 项目、`E:/learn` 下的参考仓只读；修改需明确授权。
- **MCP 写操作**（场景/资产/导入）必须属于已确认任务；工具不得隐式触发。
- 工具目录：`tools/doc-gate/`、`tools/git-hooks/`、`tools/aot-smoke/`、`tools/build-android/`、`tools/benchmarks/`、`tools/team-taskbook/`；每个工具有 `README.md` 与可复制命令。
- 工具自身的行为变化（参数、输出格式、退出码）按非平凡改动处理：写 Agent Note 并更新使用文档。
