# Agent Note: 迁入团队任务书与 Lead 工作标准

Status: implemented — 模板与接线已落地，依赖全部指向本仓

## Problem

批次型任务（多单元、多产物、需独立审核与预算预报）缺少成文的 Lead 工作标准与派单模板；`docs/development.md` 只有单元口径（≤80 步 / ≤150K、≤8 单元、任务板、一任务一 commit），没有规划期契约、单元任务书、交接单与敌对审核的可复制载体。没有标准时，多会话任务容易漏审、虚报结论强度、超范围改动与重复劳动。

## Decision

迁入 Ursa 仓 `tools/team-taskbook/` 的三份模板并适配本仓：`lead-brief.md`（Lead 工作标准）、`batch-contract.md`（规划单元产出的批次契约）、`cell-brief.md`（单元任务书 + 交接单四段），加 `README.md`（用法与依赖）。工作区 `.agent-work/` 加入 `.gitignore`；模板登记进 `doc-budgets.json` 与 baseline（`docs/AGENTS.md` 预算表「模板 4KB」同样适用于工具自带模板，因 Lead 标准要单文件自包含，按提额流程登记 lead-brief 22528、cell-brief 10240、batch-contract 8192、README 4096；该口径写进 `tools/AGENTS.md`）；`doc-gate` 扫描排除加入 `.agent-work`，批次工作区不再产生 G7 噪音；`tools/AGENTS.md`、`docs/development.md`、根 `AGENTS.md` 各加一行指针。

模板按上游 44221177（2026-11-02）通用化版本同步：不预设「写文档」，交付形态、必填项、登记与验收按文档 / 代码 / 数据·配置映射（见 `lead-brief.md` §0）。模板为派单自包含：重复出现的单元/批次数字以 `docs/development.md` §团队与委派为准，仓库规则细节只在原处维护。依赖替换（旧名仅作追溯，本仓不引用）：ADR-048 → `docs/development.md` §团队与委派（团队口径只保留一份，不复制）；`tools/doc-slim/run-doc-gates.ps1` → `tools/doc-gate/doc-gate.ps1`；`docs/red-test/` → `docs/reference/mutation-ledger.md`；`Ursa.slnx` → `Mahjong.sln`；提交三段式 → 本仓「一任务一 commit」；本仓代码引用只写符号锚点，不写行号。

## Alternatives considered

- 原样照搬 Ursa 版：依赖路径（ADR-048、doc-slim、red-test、Ursa.slnx）在本仓不存在，照搬即失效。
- 只迁 lead-brief：没有契约与单元任务书，规划与派单不可执行。
- 并入 `docs/development.md`：该文档 8KB 预算且属操作层，装不下单份约 20KB、合计约 38KB 的模板，会破坏 tier 与预算。
- 放进 `docs/reference/`：reference 只放技术证据与外部资料勘察，任务书属于执行工具，放这里混淆 tier。
- 做成 DSH skill：skill 依赖会话侧注册，不可随仓库移植；仓库内文档可被任何会话按路径读取。
- 不引入：维持现状，但多会话批次的漏审与虚报风险仍在。

## Consequences

- 现有文档：`tools/team-taskbook/README.md`、`templates/` 三份模板、本 Note；`doc-budgets.json` 与 baseline 已登记。
- 指向本目录的落点：`tools/AGENTS.md` 工具清单、`docs/development.md` §团队与委派、根 `AGENTS.md` 仓库图。
- 现有落点：Lead 标准与三份模板、README 口径一致；改一处同批改其余。
- 验证：`pwsh -File tools/doc-gate/doc-gate.ps1 -Fail` 退出 0；Note 受 G10/G12 约束。
