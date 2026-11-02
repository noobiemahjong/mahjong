# 团队任务书（team-taskbook）

本目录是**批次型任务的 Lead 工作标准与派单模板**：把一个大任务拆成会话单元（规划 / 采集 / 产出 / 集成 / 审核），逐单元派单、独立审核、收口汇报。依赖已全部换成本仓等价物，迁移取舍见 [迁移 Note](../../.agents/notes/implemented/process/2026-10-31-team-taskbook-migration.md)。

## 什么时候用

- 任务能拆成多个可独立验收的单元（≥3 个交付块：文档章节块 / 代码模块 / 数据面 等），或需要独立对抗审核；
- 需要预算预报与检查点（用户要看到「范围 + 单元 + 预算」再开工）；
- 常规单文件小改动**不用**，按 `docs/development.md` 的普通任务流程走。
- 标准不预设任务类型：文档、代码、缺陷修复、重构、数据、配置都适用；产物形态由目标文档规定。

## 怎么用

1. 用户填 [templates/lead-brief.md](templates/lead-brief.md) §1 任务卡（目标文档 / 本次范围标识 / 上一交付物），再把它交给 Lead 会话（`@` 引用或直接给路径）。
2. Lead 先规划：用 [batch-contract.md](templates/batch-contract.md) 产 `.agent-work/<本批标识>/CONTRACT.md`，用 [cell-brief.md](templates/cell-brief.md) 产每单元任务书（`.agent-work/<本批标识>/units/U-<n>-<角色>.md`）。
3. 派单、审核、返工、收口都按 lead-brief 的纪律；工作区 `.agent-work/` 已被 `.gitignore` 覆盖，不污工作树，批次结束按需清理（不入库）。
4. 收口跑 L1/L2/L3（见 `docs/development.md`），按「一任务一 commit」提交，不 push。

## 文件

| 文件 | 用途 |
|---|---|
| [templates/lead-brief.md](templates/lead-brief.md) | Lead 工作标准：任务卡、读序、质量底线、纪律、预算、检查点、完成判据、提交粒度 |
| [templates/batch-contract.md](templates/batch-contract.md) | 批次契约：范围清单、角色与文件所有权、编号、验收 |
| [templates/cell-brief.md](templates/cell-brief.md) | 单元任务书 + 交接单四段（结论 / 已排除 / 待办 / 风险） |

## 依赖的本仓口径

- 根 [AGENTS.md](../../AGENTS.md)：常驻规矩与红线（一任务一 commit、不 push、不手改查表、变异只登记）。
- `docs/AGENTS.md`：tier / 预算 / 门禁；`docs/development.md`：L1/L2/L3、团队与委派、MCP、提交。
- [doc-gate](../doc-gate/README.md)：`pwsh -File tools/doc-gate/doc-gate.ps1 -Fail`（扫描已排除 `.agent-work/`）。
- [变异台账](../../docs/reference/mutation-ledger.md)：常规任务只登记「未跑」，用户统一批次执行。
- DSH 的 subagent / teammate / 共享任务板：承载选择与状态跟踪见 lead-brief §3.2-2。

## 维护

维护约定见 `tools/AGENTS.md`「工具自身的行为变化」条；改模板时同批改 README 与迁移 Note。
