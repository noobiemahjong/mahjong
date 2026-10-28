# AGENTS.md（根：常驻规矩）

本文件只放**跨目录常驻规矩**：每条 1–3 行 + 链接。子目录规则见各自 `AGENTS.md`，根不复述；同一信息只写一处。

## 开工读序

1. 本文件。
2. [docs/AGENTS.md](docs/AGENTS.md)：文档标准、tier、预算、门禁。
3. [docs/architecture.md](docs/architecture.md)：当前实现地图；未实现时看 [docs/DESIGN.md](docs/DESIGN.md)（目标设计）。
4. [.agents/notes/README.md](.agents/notes/README.md)：决策记录规则。
5. 任务相关子树：[src/AGENTS.md](src/AGENTS.md)、[tools/AGENTS.md](tools/AGENTS.md)。

冲突裁决：`CLAUDE.local.md`（若存在） > 子树 `AGENTS.md` > 本文件 > `docs/` 内文档。

## 仓库图

| 路径 | 放什么 |
|---|---|
| `src/` | C# 工程：`Mahjong.Core`、`Mahjong.Session`、`Mahjong.Client`（Godot）、`Mahjong.Server`、`Mahjong.Tests`；规则见 [src/AGENTS.md](src/AGENTS.md) |
| `docs/` | 文档：tier 与写法见 [docs/AGENTS.md](docs/AGENTS.md) |
| `.agents/notes/` | 决策记录（Problem / Decision / Alternatives / Consequences）；规则见 [README](.agents/notes/README.md) |
| `content/tables/` | 6 个查表 `.bytes`，v1 唯一事实来源；见 [TABLES](docs/TABLES.md) |
| `tools/` | doc-gate、查表工具等；规则见 [tools/AGENTS.md](tools/AGENTS.md) |
| `docs/legacy/` | 旧 Unity 项目历史资料，仅素材参考 |

## 常驻规矩

1. **非平凡改动必须同批写 Agent Note**；机械/局部改动豁免。→ [notes 规则](.agents/notes/README.md)
2. **docs 写当前事实；notes 写为什么与放弃了什么；postmortem 写事故**；三者不混写。→ [docs/AGENTS.md](docs/AGENTS.md)
3. **一个事实只有一个家**；上位文档只放定位与链接，不复制下位细节。→ [docs/AGENTS.md](docs/AGENTS.md)
4. **设计基线是 docs/DESIGN.md**；改基线写新 Note，并同批更新 DESIGN 决策表。→ [notes 规则](.agents/notes/README.md)
5. **已落地 Note 跟代码事实更新**（路径/名字/默认值）；不把旧 Note 改成另一个决策；被替代时交叉链接。→ [notes 规则](.agents/notes/README.md)
6. **禁止全局 Note 索引**；状态与类别编码在路径里。→ [notes 规则](.agents/notes/README.md)
7. **禁止历史变迁词、状态标注、手抄生成物**；一段一行；Slop 清单见 [docs/AGENTS.md](docs/AGENTS.md)。
8. **content/tables 禁止手改**；变更需 Note + 金标验证。→ [TABLES](docs/TABLES.md)
9. **文档超预算**：先下沉、再精简、最后才提额。→ [docs/AGENTS.md](docs/AGENTS.md)
10. **Godot 工作先探活 Godot MCP**（硬门禁）；C# 符号 Roslyn MCP 优先（可降级）。→ [development](docs/development.md)
11. **验证匹配改动**：L1/L2/L3 按需跑，不默认全量；汇报只报真正跑过的。→ [development](docs/development.md)
12. **一任务一 commit，不 push**；做完停下汇报，等确认再下一个。→ [development](docs/development.md)
13. **旧 Unity 项目不作基线**：不追求行为等价、不登记旧版差异、不记录代码出处。→ [legacy](docs/legacy/README.md)
14. **不删必填内容换绿灯**：判据、链接、登记行、决策记录、编号只增不减。→ [docs/AGENTS.md](docs/AGENTS.md)

## 红线

- **不 push**：提交留在本地，由用户推送。
- **不手改 `content/tables/*.bytes`**：替换/重生成需 Note + 逐条对比。
- **不伪造绿灯**：门禁未实现写「待实现」；测试未跑写「未跑」。
- **不动外部仓库**：旧 Unity 项目、Ursa 两仓、`E:/learn/sgs` 只读参考。
- **不删必填内容**：判据、链接、登记行、决策记录、编号集合只增不减。
