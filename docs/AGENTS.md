# docs/AGENTS.md（文档标准）

适用 `docs/**`。全仓常驻规矩见根 [AGENTS.md](../AGENTS.md)；本文件管**文档放哪、怎么写、预算与门禁**。

开工读序：`README.md` → 根 [AGENTS.md](../AGENTS.md) → 本文件 → [architecture.md](architecture.md) → 相关子树 `AGENTS.md`。

## Tier（一事实一归宿）

| Tier | 只写什么 | 不写什么 |
|---|---|---|
| 根 `AGENTS.md` | 跨目录常驻规矩，每条 1–3 行 + 链接 | 故事、长流程、下位细节 |
| 子树 `AGENTS.md` | 该子树专属规矩（`src/`、`tools/`、`docs/`） | 根已有的全仓规则 |
| [architecture.md](architecture.md) | **当前实现地图**：组成、核心循环、seam、扩展点 | 类型细节（→ subsystems）、决策理由（→ notes）、实现状态标注 |
| `subsystems/` | 每个子系统一页：类型/语义/契约（有实现后建页） | 行为叙述（→ architecture）、决策理由（→ notes） |
| `cookbook/` | 带编号验证步骤的操作手册（构建、导出 AOT、看回放…） | 设计理由（→ notes） |
| `postmortem/` | 事故复盘：现象、时间线、根因、止血、防复发 | 决策、正常契约 |
| [DESIGN.md](DESIGN.md) | **目标设计基线**（未实现/实现中的规格） | 把已实现事实写成目标；实现后现状归 architecture |
| [TABLES.md](TABLES.md) / [reference/](reference/README.md) | 数据格式、外部素材证据、可复现命令 | 决策与过程纪要 |
| `milestones/` | 阶段计划/结果；**状态唯一权威在任务表** | 决策理由、进度速览 |
| [.agents/notes/](../.agents/notes/README.md) | 决策的 why、放弃的方案、必须的验证 | 操作教程、当前接口清单 |
| [legacy/](legacy/README.md) | 旧 Unity 历史资料 | 作为设计依据或验收基线 |

**放置判据**：读代码的人要的 → 注释；下一个做决定的人要的 → notes；当前怎么运转 → architecture/subsystems；怎么做 → cookbook；事故 → postmortem；数据/证据 → TABLES/reference。

## 写作规则

- **只写当前事实**：历史归 git、notes、postmortem；禁止「以前/现在/不再/曾经」。
- **一段一行**（编辑器 soft-wrap）；表格/代码块/列表保持原格式。
- **禁止状态标注**：不写「已实现/待实现/未来会」；状态由代码、目录、任务表承载。
- **禁止手抄生成物**：协议常量、表 key、API 清单由代码/生成器产出，文档只链接。
- **禁止过程叙述**：不写「我当时怎么查的」「自检轮纪要」；可复现证据进 reference。
- **禁止重复规则**：同一判据只留一处，其余链接。
- **禁止范围字面量**：不写 `001~017`；用可解析引用（`pitfall/003`、`Note 路径`）。
- **日期取真值**：git 提交日或当天；禁推断/未来日期。
- **代码块可验证**：有代码后，文档中的 C# 片段必须能编译（或明确标为伪代码）。
- **已落地 Note 禁 spec-speak**：不写「应该/计划/验收清单」，只写现状（见 notes 规则）。

## Slop 清单（评审时逐条查）

1. 重复规则：搜一句特征话，只留一处。
2. 历史跑出允许的 tier：docs 里出现变迁叙述。
3. 状态标注：「已实现/未来」这类会腐烂的句子。
4. 手抄目录：API/配置/常量清单不是生成物。
5. 过程流水账：实现步骤、测试走查、排查日志。
6. 决策理由重复：同一理由散落在多个函数/章节。
7. 段落墙：一段塞多条规则，拆开或下沉。
8. 强调通胀：满屏加粗/CAPS/「关键」，没有重点。

## 预算

| 文档 | 上限 |
|---|---|
| 根 `AGENTS.md` | 6 KB |
| 子树 `AGENTS.md` | 4 KB |
| `docs/AGENTS.md` | 6 KB |
| `docs/development.md` | 8 KB |
| `docs/architecture.md` | 8 KB |
| `docs/DESIGN.md` | 64 KB |
| `docs/TABLES.md` | 16 KB |
| `docs/legacy/ARCHITECTURE-unity-legacy.md` | 64 KB |
| 单篇 Note / milestone / postmortem / pitfall | 8 KB / 12 KB / 8 KB / 4 KB |
| 单篇 task（`M<N>/T-*`）/ reference | 8 KB / 8 KB |
| 索引行（milestones 等） | ≤300 字符 |

超预算按顺序处理：**下沉迁移 → 精简浓缩 → 提额**（提额须同提交改预算并说明理由）。

## 门禁（M0.1 交付 `tools/doc-gate`，当前为规范）

| 编号 | 规则 |
|---|---|
| G1 | 仓内相对链接可解析（仓外只报告不拦） |
| G2 | 无 UTF-8 BOM |
| G3 | 仅 LF |
| G4 | 表结构：单元格数 = 表头；无未转义双竖线 |
| G5 | 单行 ≤5000 字符 |
| G6 | 索引行 ≤300 字符 |
| G7 | 每篇 ≤ 预算 |
| G8 | 预算棘轮：上限只降不升；上调须同提交改基线并说明 |
| G9 | 脚本/钩子纯 ASCII |
| G10 | Note：文件名 `yyyy-mm-dd-slug.md`；lifecycle/class 在闭集内；`Status:` 与所在文件夹一致；禁止 `INDEX.md` |
| G11 | `engineering/pitfalls/` 编号连续，README 不维护区间 |
| G12 | 一段一行（soft-wrap；表格/代码/列表除外） |

门禁未实现前不得作为验收证据（见根 §红线）。

## 模板

- Note：[.agents/notes/000-template.md](../.agents/notes/000-template.md)
- 里程碑：[milestones/TEMPLATE.md](milestones/TEMPLATE.md)
- 事故：[postmortem/000-template.md](postmortem/000-template.md)
- 坑：[engineering/pitfalls/000-template.md](engineering/pitfalls/000-template.md)

**历史资料豁免**：`legacy/` 保留迁移原文（含旧文件行号），不受「禁行号」约束；它不是本仓代码引用，也不作为权威。
