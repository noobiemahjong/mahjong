# Agent Note: architecture.md 预算上调到 10240B

Status: implemented — `docs/architecture.md` 的 budget 由 8192B 上调到 10240B，同批改 `docs/AGENTS.md` 预算行、`doc-budgets.json` 与 `doc-budgets-baseline.txt`。

## Problem

`docs/architecture.md` 在 M0.3 收口前实测 8187B（8192B 的 99.9%，余 5B）。T-5 §4 要求本页写当前实现的公共揭示区 `Revealed`、会话侧 `dealerStreak` 与状态哈希布局 3，三段各是一句事实加符号，合计约 260B。下沉无落点：`docs/subsystems/flow.md` 已承载揭示区段位、`scoring.md` 已承载计分位细节，本页只放定位与循环，删任一句都会让实现地图缺一格；精简也无冗余句可压，69 行里全是组成、循环与 seam 事实。

## Decision

1. `docs/architecture.md` 的 `limitBytes` 由 8192 改为 10240，与子系统页同档；`docs/AGENTS.md` 的该行由「8 KB」改为「10 KB」。
2. 同批改 `tools/doc-gate/doc-budgets.json` 与 `doc-budgets-baseline.txt`，按 G8 在本 Note 记录理由。
3. 同一批的两页不提额：`docs/subsystems/flow.md` 与 `docs/subsystems/scoring.md` 先按「下沉 → 精简」处理（合并掩码与位分布、压缩金额上界、位值表语义列收敛到「位分布」行），实测 10121B / 10133B，仍在 10240B 内。
4. 新增的 `docs/cookbook/rules-variants.md` 登记 10240B（与 `docs/cookbook/aot-export.md` 同档），两篇 Note 登记 8192B。

## Alternatives considered

- **维持 8192B、三段只写链接**：读本页的人要跳两次才知道揭示区与会话连庄计数属哪一层，实现地图失去「一眼看全」的作用。
- **把 Revealed 与 dealerStreak 全部下沉到 subsystems 页**：flow.md 已有揭示区段位，但「局面里有哪些区」「哪一层维护连庄计数」属实现地图；下沉后本页答不出这两个问题。
- **提到 9216B 或 12288B**：9216B 只够本轮，再写一条事实又要提额；12288B 高于本轮实测 8448B 一档多。10240B 是最近一档，余量约 1.7KB。
- **拆出 `docs/architecture-state.md` 子页**：实现地图被拆成两页，「组成 + 循环 + seam」的单一入口消失，且与 subsystems 的边界更模糊。
- **把 cookbook 新篇压到 8192B**：步骤与输出已含 8 段命令，压到 8192B 只能删输出样例，「每步含命令与输出」的判据会被削弱。

## Consequences

- G7 以 json 为准、G8 以 baseline 为准：两处 10240 与 `docs/AGENTS.md` 表行 10 KB 同批一致，门禁维持 0 errors。
- 提额后 `docs/architecture.md` 实测 8448B，余 1792B；后续再加实现事实先按「下沉 → 精简」处理，接近上限再按本 Note 的机制单列新 Note 提额。
- 预算变更属门禁规则变更，评审按 G8 对照 json、baseline、`docs/AGENTS.md` 与本 Note 四处一致。
