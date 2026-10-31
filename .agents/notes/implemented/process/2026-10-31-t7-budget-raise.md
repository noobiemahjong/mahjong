# Agent Note: T-7 收口的三处预算提额

Status: implemented — aot-export 8192→10240、M0.2 master 12288→16384、变异台账 34816→40960，同批改 json/baseline 与预算表。

## Problem

T-7 收口要把三块内容挂进既有归宿，三篇都顶到上限：

- [cookbook/aot-export](../../../../docs/cookbook/aot-export.md) 占 8180B/8192B（只差 12B）。T-7 把 `tools/aot-smoke` 从「导出 + 3 次 `--smoke`」扩成 smoke/autoplay/GUI 三段断言与计时，手动步骤、MARKER 名、阈值（3000/5000/3000ms）与失败处置必须与脚本一一对应；这些是「怎么做」，不能只留脚本链接。
- [M0.2 master](../../../../docs/milestones/M0.2-单机可玩闭环.md) 占 12188B/12288B。收口要在同一篇追加 T-7 结果行（数字、真跑命令、审核结论）与 §7 完成定义的逐条证据指向；精简既有 T-1b…T-6 结果行等于删各任务的真跑数字与证据措辞。
- [变异台账](../../../../docs/reference/mutation-ledger.md) 占 33556B/34816B。T-7 要新增 autoplay 五项校验、事件排空、`maxTicks`、退出码、`LOCAL READY` 时机、脚本阈值删除、p99 分位算错、内存守卫反转等条目，字段齐全（目标/变异/判死闸/登记任务/结果），台账纪律禁删禁合并。

## Decision

按 [D-T7-10](../../../../docs/milestones/M0.2/T-7-收口.md) 的「下沉 → 精简 → 提额」顺序，在无法下沉、精简即删事实的前提下提额三处：`docs/cookbook/aot-export.md` 8192→10240、`docs/milestones/M0.2-单机可玩闭环.md` 12288→16384、`docs/reference/mutation-ledger.md` 34816→40960。同批改 `tools/doc-gate/doc-budgets.json`、`doc-budgets-baseline.txt` 与本 Note，并把 [docs/AGENTS.md](../../../../docs/AGENTS.md) 预算表的 milestone 12 KB→16 KB、reference 34 KB→40 KB（等长替换，该文件字节数不变）。同批登记新页 `docs/subsystems/rules.md`（10240）、`docs/cookbook/play.md`（8192）与两篇 T-7 Note（8192）。

提额前已做的精简：aot-export 的手写计时 PowerShell（约 1.5KB）在重写为「按脚本与 §1 一键跑」的短引用，手动步骤只保留与脚本对应的导出/复制/断言三段；master 与台账未删任何既有事实或条目。

## Alternatives considered

- **把 aot-export 死压回 8192**：只能删 MARKER 名、阈值与失败处置，或把 autoplay/GUI 段留给脚本源码看；cookbook 失去「可复制操作手册」的职能。
- **把 autoplay/GUI 段下沉到 `tools/aot-smoke/README.md`**：该 README 是纯 ASCII 的 4KB 级工具说明，装不下中文步骤与失败处置；且同一操作说明会分裂成两处。
- **精简 master 既有结果行换空间**：删的是各任务真跑数字与证据措辞，先例（[T-6 台账提额](../process/2026-10-31-mutation-ledger-budget-raise-t6.md)）已记录「精简既有条目」被判违规；拆出独立「M0.2 结果」页则破坏「收口时在同一篇补结果」的规则。
- **台账条目压成短句或合并同类**：违反只增不减与字段齐全，后续用户批次无法按条执行。
- **台账只提到 36KB**：新增 ≥8 条按每条约 400B 估算会顶到 36864B，下一批再加条目又要提额；一次到 40KB 留出余量。
- **把 aot-export 的 autoplay 段并入 `cookbook/play.md`**：play.md 写「怎么玩」，导出断言属导出流程，混写会让 play.md 顶到自己的上限并形成第二份导出说明。

## Consequences

- G8 以 baseline 为准：三处提额在同批 json 与 baseline 同值，doc-gate 维持 0 errors；新登记的三处（rules.md、play.md、两篇 Note）同批落两处登记，不留「未登记路径」警告。
- `docs/AGENTS.md` 预算表与 json 上限保持一致；该表其余类别不动。
- 三篇新上限各留约 1.9KB（aot-export）、5.7KB（台账）、1.8KB（master）余量：后续再加台账条目、导出步骤或结果数字先用余量，写不下时仍按「下沉 → 精简」处理，不默认提额。
- 提额只改单篇上限，不改任何事实、命令、链接与数字。
