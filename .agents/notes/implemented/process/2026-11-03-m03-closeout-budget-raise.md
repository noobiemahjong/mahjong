# Agent Note: M0.3 收口页与变异台账预算上调

Status: implemented — T-3/T-4/T-5 三篇实施规格与 `docs/reference/mutation-ledger.md` 的 budget 上调，同批改 `docs/AGENTS.md` 预算行、`doc-budgets.json` 与 `doc-budgets-baseline.txt`。

## Problem

M0.3 收口轮把三篇实施规格的「实施记录」段由占位替换为实际改动、真跑验证表、L3 实测与偏差承接后，三页实测 14331B / 14310B / 16347B，原登记各 10240B；台账按 CONTRACT §4 续号 ML-126…ML-159 共 34 行后实测 50032B，原登记 45056B。

下沉在这四页无落点：三篇实施记录是三份判据的收口面（T-3 §5 手算表、T-4 §5 受影响测试、T-5 §2/§3 金标与性能），下沉到 reference 会让规格与证据分家；台账条目是已登记判据，删字即削弱判死闸，拆档会让「全仓连续编号」跨文件。

精简已做完：三篇验证表压成「命令/退出码/计数/日志」四列，L3 数字只留 T-5 §3 唯一家，payload 逐字节表与 hash 三算细节均不复制；台账沿用既有条目形态。

## Decision

1. `docs/milestones/M0.3/T-3-抢杠与承包.md` 与 `T-4-中马.md` 的 `limitBytes` 10240 → 14336，回到 `docs/AGENTS.md` 的 task 页 14 KB 档。
2. `docs/milestones/M0.3/T-5-回归与收口.md` 10240 → 16384：该页承载金标表、性能表、D-39/D-41 覆盖判据、A-1…B-5 核对表与台账指针，是唯一超 14 KB 的一页，`docs/AGENTS.md` 表行标注「（T-5 16 KB）」。
3. `docs/reference/mutation-ledger.md` 45056 → 65536：34 条新登记后实测 50032B，余量留给后续批次续号；`docs/AGENTS.md` 的 reference 档由 44 KB 改为 64 KB。
4. 同批改 `tools/doc-gate/doc-budgets.json` 与 `doc-budgets-baseline.txt`，按 G8 在本 Note 记录理由。

## Alternatives considered

- **把 T-5 压回 14 KB**：需删 §5 核对表的一列证据或 §3 的 201 场依据，两者都是 D-39/D-41 的口径证据，删了就回到「判据不可满足却写成通过」。
- **把实施记录下沉到 `.agent-work/` 交接单**：交接单是 gitignored 的过程产物，规格页会失去可提交的收口证据。
- **台账拆成归档表 + 现表**：编号跨文件后「ML-001…ML-159 连续」要两处拼接校验，且新增条目仍会继续撞顶；台账是唯一登记处，拆档与「只增不减」冲突。
- **精简台账旧条目说明**：旧条目是已登记判据，删字即削弱判死闸；125 条里省出的空间只够一两批。

## Consequences

- 四处一致：`docs/AGENTS.md` 表行、`doc-budgets.json`、`doc-budgets-baseline.txt` 与本 Note；doc-gate 维持 0 errors。
- 提额后余量：T-3 5B、T-4 26B、T-5 37B、台账 15504B；三篇接近上限，后续再加判据先按下沉/精简处理；台账按每批 34 条约 8KB 计约够一批半，再撞顶时按本 Note 机制单列新 Note。
- 预算变更属门禁规则变更，评审按 G8 对照四处一致。
