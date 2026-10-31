# Agent Note: 变异台账预算上调到 16384B

Status: implemented — `docs/reference/mutation-ledger.md` 的 budget 从 8192B 上调到 16384B，同批改 doc-budgets 与 baseline 并登记 T-4 新增文档。

## Problem

变异台账是变异条目的唯一登记处，只增不减：常规任务登记待跑条目，用户批次执行后回填结果与证据。M0.2 T-3 收尾时台账 8092B，距 8192B 预算只剩 100B；T-4 登记 ML-028–ML-050 共 23 条 bot 策略变异（每条含目标/变异/期望判死闸/登记任务/结果，约 300–400B）后实测 16160B，8KB 档放不下。按 [docs/AGENTS.md](../../../../docs/AGENTS.md) 的超预算顺序先试前两步：下沉只能把台账拆成按任务或按子系统的多个文件，而「唯一登记处」正是台账存在的理由，编号连续性与「不复用、不重排」也要跨文件维护；精简只能删字段、期望判死闸或链接，而登记行与判据是只增不减的必填内容。两步都会破坏台账本身，因此走到第三步提额。

## Decision

`docs/reference/mutation-ledger.md` 的 `limitBytes` 由 8192 改为 16384，按 reference 标准档（16KB）；同批改 `docs/AGENTS.md` 预算表 reference 行 `8 KB` → `16 KB`、`tools/doc-gate/doc-budgets.json` 与 `doc-budgets-baseline.txt` 两处 16384。同批新增的 [bot.md](../../../../docs/subsystems/bot.md)（10240）与本篇、[简单 Bot 策略](../feature/2026-10-31-simple-bot-policy.md)（8192）一并写入 json 与 baseline。

## Alternatives considered

- **只提到 12288（实测 16160B 仍不够）**：12KB 档仍放不下 23 条条目；放弃。
- **按任务/子系统拆多个台账文件**：能绕开单文件 limitBytes，但「唯一登记处」被拆散，编号连续性、结果回填与「只增不减」都要跨文件维护；放弃，除非台账再次逼近上限且出现新的登记处决策。
- **提到 32768**：T-4 后实测 16160B，16384 已够且贴近 reference 标准档；32768 只留过大余量，无必要；放弃。
- **只提台账单篇预算、不改 docs/AGENTS.md 预算表**：注册表与标准表不一致，下个任务按旧档判断会重复提额；放弃。
- **压缩表格写法（合并字段、缩写）**：字段是必填且要能直接照做，压缩后不可读；放弃。

## Consequences

- doc-gate G8 以 baseline 为准：`docs/AGENTS.md`、json、baseline 三处同批一致，门禁维持 0 errors。
- 台账实测 16160B，余量 224B；后续任务继续按「下沉 → 精简 → 提额」处理，再逼近时先评估按任务拆分的代价。
- 预算变更属门禁规则变更，评审按 G8 逐项对照三处一致。
