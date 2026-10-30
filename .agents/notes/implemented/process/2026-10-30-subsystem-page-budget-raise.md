# Agent Note: 子系统页预算上调到 10240B

Status: implemented — `docs/subsystems/core.md` 与 `docs/subsystems/flow.md` 的 budget 从 8192B 上调到 10240B，同批改 doc-budgets 与 baseline。

## Problem

两页在 M0.2 T-2 前已占 7840B / 7481B（8192B 的 96% / 91%）。T-2 往 Core 加了一整族新契约：`SkillId`/`SkillContext`/`Skill`/四个技能实现/鬼牌注入规则与 `CardAndSkillVerifier` 的检查顺序归 `Rules`；`ResponseWindow`/`ResponseResult`/`TryPass`/仲裁键/Close 结算归 `Flow`。这些是「类型/语义/契约」，当前事实的规范家是子系统页（里程碑规格只保留任务级裁定与链接）；压缩到 8192 内要删的是检查顺序、异常类型与张数口径这类会被测试和审核逐条对照的事实，不是可省的过渡句。下沉也无落点：`architecture.md` 只放定位与 seam，`TABLES.md` 只管查表字节格式，milestone 规格不承载当前事实。`docs/AGENTS.md` 的超预算处理顺序是「下沉迁移 → 精简浓缩 → 提额」，本例在第二步没有不损事实的空间。

## Decision

`docs/subsystems/core.md` 与 `docs/subsystems/flow.md` 的 `limitBytes` 由 8192 改为 10240，与单篇 task/实施规格同级；同批改 `tools/doc-gate/doc-budgets.json` 与 `doc-budgets-baseline.txt`，并按 G8 在本 Note 记录理由。同批新增的两篇 Note（本 Note 与 [技能与响应窗口](../architecture/2026-10-30-skill-and-response-window.md)）一并登记为 8192B。

## Alternatives considered

- **维持 8192，删细节**：只能删掉 Z 顺序、异常类型、张数 13/14 这类可核对的契约；审核要拿文档对代码，删了就得去代码里找，子系统页失去存在意义。
- **拆出第三页（如 `subsystems/rules-skills.md`）**：能绕开既有 limitBytes，但技能/校验器本来就属 `Mahjong.Core.Rules` 这一子系统，拆页会让「Core 的类型契约在哪」变成两处，违反「一个事实只有一个家」。
- **把查表整节下沉到 TABLES.md**：能把 core.md 压回预算内，但 Class 契约（`TableCatalog` 的加载/查询/异常）会被拆到数据格式页，读者要在两页之间跳；TABLES.md 也只剩 8KB 余量。
- **只给 flow.md 提额、core.md 继续硬压**：同样是「提额」，还得多删事实，不如两页一致。
- **提额到 12288**：给未来的余量更大，但当前两页实测 10203B / 9723B，10240 已够且少改一档。

## Consequences

- doc-gate G8 以 baseline 为准：两页 10240 与两篇新 Note 8192 同批写入 json 与 baseline，门禁维持 0 errors。
- 两页仍有约 37B / 517B 余量（core.md 已接近上限）；后续再加子系统契约时先按「下沉 → 精简」处理，仍不够就按本 Note 的先例单列新页而不是继续提额。
- 预算变更属门禁规则变更，评审时按 G8 逐项对照 json/baseline/Note 三处一致。
