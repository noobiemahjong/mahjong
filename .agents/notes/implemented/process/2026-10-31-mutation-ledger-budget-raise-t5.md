# Agent Note: 变异台账预算上调到 32768B

Status: implemented — `docs/reference/mutation-ledger.md` 的 limitBytes 由 16384 上调到 32768，同批改基线、预算表并登记 T-5 新增文档。

## Problem

变异台账是变异条目的唯一登记处，只增不减：常规任务登记待跑条目，用户批次执行后回填结果与证据，编号全仓连续、不复用不重排。T-4 收尾时台账 16160B、预算 16384B，只剩 224B；T-5 登记事件编码/可见性/seq、请求 id/超时/仲裁、Bot 延迟、自动出牌、杠、结算、轮换、排名、镜像与隔离共 40 条变异，每条含目标/变异/期望判死闸/登记任务/结果，实测台账到 28384B，16KB 档放不下。按 [docs/AGENTS.md](../../../../docs/AGENTS.md) 的超预算顺序先试前两步：下沉只能把台账拆成多个文件，而「唯一登记处」正是台账存在的理由，编号连续性与结果回填要跨文件维护；精简只能删字段、判死闸或链接，而登记行与判据是只增不减的必填内容。两步都会破坏台账本身，因此走到第三步提额。

## Decision

`docs/reference/mutation-ledger.md` 的 `limitBytes` 由 16384 改为 32768；同批改 `docs/AGENTS.md` 预算表 reference 行 `16 KB` → `32 KB`、`tools/doc-gate/doc-budgets.json` 与 `doc-budgets-baseline.txt` 两处 32768。同批新增的 [session.md](../../../../docs/subsystems/session.md)（10240）与本篇、[本地会话 Note](../feature/2026-10-31-local-game-session.md)（8192）一并写入 json 与 baseline。

## Alternatives considered

- **只提到 24576**：T-5 后台账 28384B，24576 档余量不足 2KB，下一次登记就可能再次提额；32768 是 reference 下沉前的标准档位上界，留出整批回填余量。放弃 24576。
- **按任务/子系统拆多个台账文件**：能绕开单文件 limitBytes，但「唯一登记处」被拆散，编号连续性、结果回填与「只增不减」都要跨文件维护；放弃。
- **压缩表格写法（合并字段、缩写）**：字段是必填且要能直接照做，压缩后不可读；放弃。
- **只提台账单篇预算、不改 docs/AGENTS.md 预算表**：注册表与标准表不一致，下个任务按旧档判断会重复提额；放弃。
- **分批登记、暂不追加 T-5 条目**：条目会因未跑而缺失，违反「条目不因未跑而省略」；放弃。

## Consequences

- doc-gate G8 以 baseline 为准：`docs/AGENTS.md`、json、baseline 三处同批一致，门禁维持 0 errors。
- 台账追加 T-5 条目后仍有余量；后续任务继续按「下沉 → 精简 → 提额」处理，再逼近时先评估按任务拆分的代价。
- 预算变更属门禁规则变更，评审按 G8 逐项对照三处一致。
