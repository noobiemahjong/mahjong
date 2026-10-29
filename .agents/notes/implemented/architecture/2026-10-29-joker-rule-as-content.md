# Agent Note: 鬼牌规则数据化（content/rules.json）

Status: implemented — 鬼牌模式进入内容清单，代码不再硬编码红中。

## Problem

鬼牌（红中）此前只以散文形式写在 T-3/T-4 里，代码与内容清单都没有承载它：content schema 只有 `{id,suit,rank}`，contentHash 覆盖不到「谁是鬼牌」。同时 `dictionary_dragon` 的 64 个 key 里有 48 个含「中」的非零计数，与「鬼牌不进 rank 计数」的硬编码口径正面冲突；将来若要做「无鬼牌」或「翻牌决定鬼牌」的变体，把红中写死会直接堵死。

## Decision

1. 新增 `content/rules.json`：`{ "version": 1, "joker": { "mode": "FixedType", "typeId": 31 } }`。
2. `joker.mode` ∈ `None` / `FixedType` / `FlipIndicator`；v1 实现前两种，`FlipIndicator` 加载时抛可读 `NotSupportedException`，等轮次级 SyncValue 能力就绪再接。
3. 规则语义写成「**当前这一局的鬼牌不进 rank 计数**」，只作万能牌参与胡牌判定；不可打出、不可作碰杠材料、不可当普通箭牌。
4. `ContentCatalog` 暴露 `JokerRule`；`IWinChecker` 的 joker 参数从它取，实现不得硬编码 31。
5. `rules.json` 进入 manifest，contentHash 覆盖它：改鬼牌规则必然换哈希。

## Alternatives considered

- **继续硬编码红中**：实现最简单，但「无鬼牌/翻牌鬼牌」无法配置，且 contentHash 覆盖不到规则变化。
- **把 joker 塞进 `suits.json`**：文件数不变、DESIGN 清单不动，但花色规则与鬼牌规则语义混杂，后续加别的规则没有落点。
- **把鬼牌移出 34 种牌**（当作第 35 种标记牌）：表 key 空间、136 张物理 id 模型、碰杠校验都要改，代价远大于收益。
- **v1 就实现 FlipIndicator**：需要「翻出第 N 张 → 写进流 → 各端一致」的同步能力，属 M2 的流工作，M0.1 没有承载。

## Consequences

- `suits.json` 维持花色语义，`rules.json` 承载规则开关；两者都进 manifest。
- dragon 表含「中」≠0 的 48 个 key 在 v1 默认配置下不可达，这是配置决定而非数据错误；TABLES 明确写出。
- M0.2 胡牌判定通过 `IWinChecker(joker)` 注入鬼牌，换模式不改算法。
- `FlipIndicator` 只留类型位与文档；实现它时需在流里加轮次级 SyncValue，并按基线变更流程改 DESIGN。
