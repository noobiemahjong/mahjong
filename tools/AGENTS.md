# tools/AGENTS.md（工具子树规则）

适用 `tools/**`。全仓常驻规矩见根 [AGENTS.md](../AGENTS.md)。

## 工具清单（随实现补充）

| 工具 | 用途 |
|---|---|
| `doc-gate` | 文档门禁：链接/BOM/LF/表结构/预算/Note 路径/编号 |
| `table-inspect` | 解码 `content/tables/*.bytes` 为 JSONL（只读） |
| `aot-smoke` | 客户端 AOT 导出 + 独立 exe 冒烟 |

## 规则

- **脚本纯 ASCII**（注释/输出）；处理中文文件时显式 UTF-8，不用默认编码往返。
- **只读优先**：查表/回放检查工具默认不写仓库；需要写时输出到临时目录，并说明。
- **禁止手改生成物**：生成器产物只由工具重跑产生；工具与产物同批提交。
- **门禁确定性**：同一输入同一退出码；规则变更必须同步 [docs/AGENTS.md](../docs/AGENTS.md) 的门禁表。
- **不碰外部资产**：旧 Unity 项目、Ursa 两仓、SGS 只读；修改需明确授权。
- **MCP 写操作**（场景/资产/导入）必须属于已确认任务；工具不得隐式触发。
- 工具目录：`tools/doc-gate/`、`tools/aot-smoke/`、`tools/build-android/`、`tools/benchmarks/`；每个工具有 `README.md` 与可复制命令。
- 工具自身的行为变化（参数、输出格式、退出码）按非平凡改动处理：写 Agent Note 并更新使用文档。
