# AGENTS.md（根：共享指令入口）

本文件是本仓的**共享指令入口**。开工先读顺序：

1. `AGENTS.md`（本文件）
2. `docs/AGENTS.md`（文档放哪、怎么写）
3. `docs/DESIGN.md`（设计基线，D1–D18 与各章节是权威）
4. `docs/adr/README.md` + `docs/porting/rejected-alternatives.md`（已做的决定、已否决的方案）
5. 与任务相关的 `docs/milestones/`、`docs/registry/`、`docs/porting/pitfalls/`、`docs/reference/`

**冲突裁决**：`CLAUDE.local.md`（若存在） > 子树 `AGENTS.md` > 本文件 > `docs/` 内文档。
**同一信息只写一处**：子树负责的规则不在根复述；`docs/` 是项目状态的唯一权威。

## 0. 治理来源（ADR-002）

本仓的文档治理**裁剪自** `E:/learn/Ursa-Server` 与 `E:/learn/Ursa-Client` 两套已跑通的体系：

- 保留：三载体分工、ADR、里程碑 master+任务、pitfalls、rejected-alternatives、registry、bugs、reference、预算与门禁分层、提交纪律、MCP 优先、收尾汇报。
- 裁剪：不照搬 49 篇 ADR 的规模；不做 GitHub Issue 镜像；不建客户端逆向目录；red-test/变异台账与 doc-slim 全量普查推迟到 M2 之后再评估；基线锚点从「Java 类.方法」换成「旧 Unity 项目 `类.方法` / 文件路径」。
- 详见 [ADR-002](docs/adr/002-文档治理裁取Ursa两仓.md)。

## 1. 三载体分工（ADR-002 继承自 Ursa ADR-024/009）

行为、理由、过程各有唯一载体，**同一信息只写一处**：

| 载体 | 只写 | 不写 |
|---|---|---|
| 代码注释 | 与旧 Unity 实现的对应关系（`类.方法`）、读代码看不出的「为什么」、有意偏离旧行为之处 | 逐句对拍、执行历史、日期、行号、完整设计论证 |
| commit body | 改了什么 / 为什么 / 验收口径（跑了哪些 L1/L2/L3） | 逐项举证明细、坑的全文、设计再复述 |
| `docs/` | 决策（ADR）、设计基线、里程碑计划/结果、登记表、可复现证据链 | 自检过程纪要、「我当时怎么查出来的」、进度速览 |

判据：**读代码的人需要这句话吗？** 不需要 ⇒ 归 commit。**下一个人要据此做决定吗？** 不需要 ⇒ 归 git log。

**锚点红线**：引用本仓代码一律写 `类型.方法` 或 `文件路径（不含行号）`；旧项目/外部仓库可写其路径与 commit；**禁止在 docs/注释/commit 里引用本仓行号**（会随提交腐化）。

## 2. 仓库导航

| 路径 | 放什么 | 规则 |
|---|---|---|
| `docs/DESIGN.md` | **设计基线**：目标/非目标、分层、tick 引擎、会话流、回放、观战、重连、部署、里程碑 | 只经 ADR 变更（ADR-001） |
| `docs/TABLES.md` | 查表二进制格式、来源、与上游差异、生成计划 | 数据唯一来源是 `content/tables/` |
| `docs/ARCHITECTURE-unity-legacy.md` | 旧 Unity 项目架构总结 | **历史参考**，不是新项目权威 |
| `docs/notes/` | 旧项目模块精读笔记 | **历史参考**，不是新项目权威 |
| `docs/adr/` | 决策：选了什么 + 为什么 + 放弃了什么 | 索引先读；编号连续 |
| `docs/milestones/` | 阶段 master + 任务文档；**状态唯一权威在任务表** | 计划先行、收口补结果 |
| `docs/porting/` | 旧→新映射、rejected-alternatives、pitfalls | 移植前先读 |
| `docs/registry/` | **有意偏离旧行为**的登记表 | 每条必须有落点与理由 |
| `docs/reference/` | 外部素材勘察、可复现证据链 | 必须给复现命令/路径 |
| `docs/bugs/` | 我们自己的缺陷与事故诊断 | 与决策、坑分开 |
| `content/tables/` | 6 个查表 `.bytes` | **禁止手改**；变更需 ADR + 金标验证 |
| `src/`、`tools/` | 代码与工具（M0 起） | 各自 `AGENTS.md` 承担细则 |

## 3. 工作流

**开工前**：

1. 读 §0 的读序；确认任务在 `docs/milestones/` 里有对应项与验收口径。
2. 查 `docs/adr/` 与 `docs/porting/rejected-alternatives.md`，避免重开已否决方案。
3. 查 `docs/registry/`：本任务是否触碰「有意偏离旧行为」的条目。
4. `git status` 必须干净；有脏树先说明再动手。

**实施中**：

- 非平凡改动必须留痕（判据见 `docs/AGENTS.md` §「非平凡改动」）。
- 不手改生成物与查表；不绕过既有决策做局部实现。
- 遇到需要停服、修改场景/资产、执行 MCP 写操作时，先征得用户同意。

**收尾**：

1. 跑 L1（构建/单测）；触及 `docs/` 跑 L2（doc-gate，M0 后）；里程碑收口跑 L3（AOT 冒烟 + 金标回放）。
2. 一个任务一个 Conventional Commit（标题 ≤50 字，body ≤10 个非空行，**不 push**）。
3. **停下来汇报**：做了什么、验收怎么验、剩余风险；等确认再做下一件。

## 4. 纪律路由表

> 每条 = 编号 + 一句话判据 + 触发条件 + 指针。执行细节在指针处，不在本表复述。

| # | 一句话判据 | 触发条件 | 指针 |
|---|---|---|---|
| 1 | 开工先读 AGENTS 链 + DESIGN + 相关 ADR/rejected | 任何任务 | 本文件 §0 |
| 2 | 非平凡改动必须留痕，且按三载体选唯一落点 | 改行为/架构/契约/工具链/测试策略/数据格式 | [docs/AGENTS.md](docs/AGENTS.md) |
| 3 | 同一信息只写一处；上位文档只放定位与链接 | 写/改文档 | [docs/AGENTS.md](docs/AGENTS.md) |
| 4 | 旧结论不改：DESIGN/ADR 一旦发布，新决策写新 ADR 并标注收窄关系 | 改设计/推新方案 | [ADR-001](docs/adr/001-设计基线与变更程序.md) |
| 5 | 有意偏离旧版行为必须登记，禁止只写在代码里 | 行为与旧版不同 | [registry](docs/registry/旧版行为偏离登记.md) |
| 6 | 踩坑写 `pitfalls/` 新编号篇，先 `Glob` 避让；只写仍有效的结论 | 踩坑后 | [porting/pitfalls](docs/porting/pitfalls/README.md) |
| 7 | 一个任务一个 Conventional Commit；不 push；收尾汇报等确认 | 提交/收尾 | 本文件 §3 |
| 8 | 日期取真值（git 提交日或当天）；禁推断/未来日期；禁历史变迁词 | 写 docs | [docs/AGENTS.md](docs/AGENTS.md) |
| 9 | 证据必须可复现：数字/枚举/路径给命令；否定结论需跨编码扫描或字节实读 | 写证据/否定结论 | [docs/reference/README.md](docs/reference/README.md) |
| 10 | 引用本仓代码用符号锚点，禁行号；外部路径可写 | 写注释/docs | 本文件 §1 |
| 11 | Godot 相关开发以 Godot MCP 探活为硬前置，不通则停下要求启动；C# 符号查询 Roslyn MCP 优先（可降级） | 任何 Godot/C# 工作 | 本文件 §5 |
| 12 | 委派任务必须写入 MCP 条款、载体、交付物与验收；一单元一产物；Lead 复核 diff 并跑门禁 | 开子任务/团队 | 本文件 §7 |
| 13 | `content/tables` 与生成物禁止手改；变更需 ADR + 金标验证 | 动数据/生成物 | [TABLES](docs/TABLES.md) |
| 14 | 改 `docs/` 后提交前跑门禁；CI 红 = P1，不带着红提交 | 改文档/提交前 | 本文件 §6 |
| 15 | 不删「必填内容」换绿灯：判据、锚点、登记行、决策记录、编号只增不减 | 精简/过门禁 | 本文件 §6 |

## 5. MCP 纪律（ADR-002 继承自 Ursa-Client ADR-012/020、Ursa-Server ADR-020）

**开工第一步 = 探活**。禁止「不探活就直接 grep」当作默认路径。

| 通道 | 范围 | 强度 | 不通时 |
|---|---|---|---|
| Godot MCP（`godot-ai`） | 场景/节点/UI/资源/运行态检查 | **硬门禁** | 停下，要求用户启动 Godot 编辑器与 MCP server，不降级 |
| Roslyn/VS MCP（`vs-mcp-ursa-client`） | C# 符号、调用链、工程结构、编译诊断 | 必须优先 | 可回退 `grep`/`read`，但汇报里逐处说明未用 MCP 的理由 |
| 仓库内数据（查表/回放/内容） | `content/tables`、`replays` | — | 用 M0 的 `tools/table-inspect` 或 python/msgpack 实读；**不靠猜** |

边界：`docs/**`、外部仓库（旧 Unity 项目、SGS、Ursa 两仓）不在 Roslyn 索引内，用文件读取/搜索才是正解，不算「回退」。
MCP 写操作（改场景、改资产、导入资源）**必须属于已确认任务**；禁止顺手改。

## 6. 验证分层与门禁

| 层 | 内容 | 何时跑 |
|---|---|---|
| L1 | `dotnet build` + `dotnet test`（Core/单测） | 每次提交前 |
| L2 | 文档门禁 doc-gate（链接/BOM/LF/表结构/预算/ADR 编号）+ 构建 + 全量单测 | 改 `docs/` 或提交前；CI 每次 push/PR |
| L3 | 客户端 AOT 冒烟（导出+独立 exe 跑一局+播回放）+ 金标对局回放 | 每个里程碑收口 |

**文档门禁规则（M0 交付 `tools/doc-gate`，当前为规范，不假装已实现）**：

| 编号 | 规则 |
|---|---|
| G1 | 仓内相对链接可解析（仓外链接只报告不拦） |
| G2 | 无 UTF-8 BOM |
| G3 | 仅 LF |
| G4 | 表结构：单元格数 = 表头；行内无未转义双竖线 |
| G5 | 单行 ≤5000 字符 |
| G6 | 索引行（ADR/里程碑 README）≤300 字符 |
| G7 | 每篇 ≤ 预算上限（见下表） |
| G8 | 预算棘轮：上限只降不升；上调须同提交改基线并说明理由 |
| G9 | 脚本/钩子纯 ASCII（M0 起） |
| G10 | ADR 编号连续，且 `docs/adr/README.md` 每篇都有行 |
| G11 | `pitfalls/` 编号连续，README 不维护编号区间 |

**文档预算（粗粒度，M0 起冻结）**：

| 类别 | 上限 |
|---|---|
| `AGENTS.md` | 12 KB |
| `docs/AGENTS.md` / `CLAUDE.md` | 6 KB / 1 KB |
| `docs/DESIGN.md`（基线） | 64 KB |
| `docs/TABLES.md` / `docs/ARCHITECTURE-unity-legacy.md` | 16 KB / 64 KB |
| 单篇 ADR / 单篇 milestone / 单篇 bug | 8 KB / 10 KB / 8 KB |
| 单篇 pitfall | 4 KB |
| 索引行 | ≤300 字符 |

## 7. 团队与委派（继承自 Ursa-Server ADR-048）

- 开工前在共享任务板建任务，写明写作用域（目录级即可），并行任务写作用域不重叠。
- 子任务书必须包含：MCP 条款、载体（改哪些目录/文件）、交付物、验收命令、停止条件。
- **一单元一产物**：默认单单元 ≤80 步 / ≤150K token；批次 ≤8 单元；同时活跃 ≤5。
- Lead 负责最终 diff 复核、跑 L1/L2、确认没有越界改动；**不 push**。
- 做完一个单元停下汇报，等确认再开下一个。

## 8. 红线全文

- **纪律 7（收尾汇报）**：做完一个任务就停下来汇报，等确认后再做下一个；不得连续开新任务。
- **不 push**：所有提交留在本地，推送由用户执行。
- **不手改查表**：`content/tables/*.bytes` 是唯一事实来源（[TABLES](docs/TABLES.md)）；任何替换/重生成需 ADR + 逐条对比。
- **不伪造绿灯**：门禁未实现就写「待实现」；测试未跑就写「未跑」；禁止声称跑过但没跑。
- **不删必填内容**：判据、锚点、登记行、决策记录、编号集合只增不减。
- **不动用户资产**：旧 Unity 项目、Ursa 两仓、`E:/learn/sgs` 只读；修改需明确授权。
