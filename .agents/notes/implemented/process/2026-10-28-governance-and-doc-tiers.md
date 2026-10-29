# Agent Note: 文档治理采用 DSH 三块分工，裁剪重型治理方案

Status: implemented

## Problem

vibe coding 项目的维护性取决于「下一个 agent 能否不猜」。既有的一套重型治理（49 篇 ADR、19 条纪律、全量门禁、census）已验证可行，但体量对麻将项目过重；DeepSeek Harness 的公开文档体系给出了更轻的三块分工：根 AGENTS 管常驻规矩、docs 管当前事实、notes 管决策因果。

同时必须避免两件事：把治理做成文档 KPI；把实现绑死在旧 Unity 版本上。

## Decision

采用「三块 + 分层」：

1. **根 AGENTS.md**：只放跨目录常驻规矩，每条 1–3 行 + 链接；子目录各自 `AGENTS.md`，根不复述。
2. **docs/**：写当前事实与规格。tier 固定为 architecture（当前地图）/ subsystems（实现后按子系统建页）/ cookbook（操作步骤）/ postmortem（事故）/ DESIGN（目标设计基线）/ TABLES+reference（数据与证据）/ milestones（计划结果）/ legacy（旧项目历史资料，仅素材）。
3. **.agents/notes/**：只写决策的 why、放弃的方案、后果；路径编码 lifecycle/class，禁止总索引。
4. **不采用**：旧 Unity 行为等价/差异登记、代码出处/逆向地址锚点、Java 基线/submodule、49 篇 ADR 规模、GitHub Issue 镜像、red-test/变异台账、doc-slim 全量普查。
5. **验证分层**：L1 构建+相关单测；L2 doc-gate+全量单测；L3 AOT 冒烟+金标回放。门禁规范先立，`tools/doc-gate` 在 M0.1 交付，未实现前不声称已跑。
6. **开发思路**：内核与引擎分离；先 spike/量测再承诺；设计先行；小步可验收；任务单元化；做完停下汇报；不 push。

## Alternatives considered

- **完整复制重型治理**：49 篇 ADR、19 条纪律、全量门禁；治理成本会压过开发。团队 >3 人且里程碑并行 >4 时再评估。
- **不做治理，只写 DESIGN**：决策失忆、重复踩坑、协议契约漂移；不重开。
- **把所有规则塞进根 AGENTS.md**：单文件膨胀、子树无法按需加载；不重开，按子树拆分。
- **继续用 ADR + 总索引**：与 notes 的路径编码/无索引冲突，且索引在多 agent 并行时冲突；改为 Note 后不再维护索引。
- **与旧 Unity 版做行为等价与差异登记**：旧版不合理处多，会把实现绑死；仅复用旧规则细节时查旧代码。

## Consequences

- M0.1 必须交付 `tools/doc-gate`、commit-msg hook、CI（构建 + 单测 + 文档门禁，无 paths 过滤）。
- 文档预算按类别设上限；超了先下沉、再精简、最后提额。
- 治理成本若持续压过开发，按本 Note 的「不采用」清单继续收窄，而不是加规则。
