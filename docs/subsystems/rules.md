# 子系统：胡牌判定

> 本页写胡牌判定的类型、输入口径与查表遍历；技能与校验器见 [core.md](core.md) §5，弃牌响应窗口见 [flow.md](flow.md) §8，计分与轮次见 [scoring.md](scoring.md)，决策理由见 [Note 查表遍历](../../.agents/notes/implemented/architecture/2026-10-30-win-checker-table-traversal.md)，任务口径与金标见 [T-1](../milestones/M0.2/T-1-胡牌与七对.md)。命名空间 `Mahjong.Core.Rules`。

## 1. 类型与输入

- `IWinChecker`：`IsWin(hand, melds, joker)`、`GetWinningTypes(hand, melds, joker)`、`IsSevenPairs(hand, joker)`；`WinChecker` 是唯一实现，构造只接收已加载的 `TableCatalog`，判定期只读该快照。
- `hand` 是 `IReadOnlyList<Tile>`、`melds` 是 `IReadOnlyList<Meld>`、`joker` 是当前局鬼牌牌种；`hand`/`melds` 为 null 抛 `ArgumentNullException`，其余非法输入返回 false 或空列表，不抛异常。
- 折算张数 = 手牌张数 + 3 × 副露组数；`IsWin` 要求恰好 14，`GetWinningTypes` 要求恰好 13，七对要求手牌恰好 14 张且不参与副露折算。
- 鬼牌只按 `TileType.Id` 识别：`joker.Suit` 在 1–5 且 `Id` 小于 34 才算鬼牌生效，无鬼牌时调用方传 `default(TileType)`，其 `Suit` 不在 1–5，牌种 id 0 的实体牌不会被当成鬼牌。
- 鬼牌规则来自 `content/rules.json` 的 `joker`，加载后是 `ContentCatalog.Joker`（见 [core.md](core.md) §1）；判定器不读文件、不读时钟、不消费随机。
- 入参非法：牌种 id 不小于 34、同牌种实体牌超过 4 张（鬼牌自身也按 4 张上限）返回 false/空列表；副露组内牌种只在 `GetWinningTypes` 统计 4 张上限时解析，`IsWin` 只看副露组数。
- 无静态可变状态、无跨调用缓存；`rankCounts`/`keys` 等中间数组用 `stackalloc` 落在栈上，同输入重复调用结果一致。

## 2. 常规胡：查表遍历

- 统计：按牌种 id 0–33 计数为 `rankCounts`，鬼牌只累加 `jokerCount`、不进入计数；34 个 id 依次是万/筒/条 1–9、风 1–4、箭 1–3。
- 花色分组：万/筒/条共用 `dictionary_normal`（9 个 rank），风用 `dictionary_wind`（4），箭用 `dictionary_dragon`（3）；组内无实体牌时不查表，等价于该花色贡献 0 组面子且不含将。
- rank key：`TableKey.Encode(rankCounts)` 按 rank 顺序做十进制拼接（`cur = cur * 10 + count`），前导 0 省略；例如 `[0,0,1,2,1,0,2,2,0]` 编码为 `1210220`。
- 行语义：`WinRow(NeedJoker, HasPair, HasWin, WinningRanks)`；`HasWin` 行描述已完成的分解，未成胡行描述听牌分解，14 张成胡判定只用 `HasWin` 行。
- 回溯：`TryMatch` 对每个有牌的花色取 key 候选行，再跨花色回溯选行，累加 `NeedJoker` 与 `HasPair`；某花色有牌但查表无候选行即整手不胡。
- 终态：剩余鬼牌补面子（每 3 张一组）且已含恰好 1 将，或剩余鬼牌补将（2 张）且尚未含将；两者都不满足返回 false。
- 非法输入与不胡都返回 false，不区分原因；判定过程不分配跨调用状态，候选行只读。

## 3. 七对

- 手牌恰好 14 张；剔除鬼牌后按牌种计奇偶，奇数张数的牌种数不超过鬼牌数即成七对（每张鬼牌补一个单张成对）。
- 4 张同牌种按两对计；副露不参与七对判定，判定只看手牌 14 张，不解析副露里的牌。
- 七对与常规胡互斥：`SelfDrawnSkill` 要求 `IsWin` 为真且 `IsSevenPairs` 为假，`SelfDrawnWith7PairsSkill` 只认 `IsSevenPairs`；七对优先的判定顺序在流程层，见 [scoring.md](scoring.md) §3。

## 4. 胡牌张

- `GetWinningTypes` 是胡牌张查询：折算 13 张时遍历牌种 id 0–33，逐个试加一张后调 `TryMatch`，命中即收入结果。
- 试加鬼牌牌种时只把 `jokerCount + 1` 交给 `TryMatch`；试加普通牌种时临时 `rankCounts[typeId]++`（回溯前 `--`）。
- 实体牌上限 4 张：手牌该牌种张数加副露内该牌种张数已达 4 时跳过；结果按牌种 id 升序、不重复，每次返回新建列表。
- 张数不合 13 或输入非法返回共享只读空集合；合法张数但无命中返回空列表，空结果不参与调用方语义区分。
- M0.2 的会话与 `RoundController` 只用 `IsWin`/`IsSevenPairs` 做结算，不调用 `GetWinningTypes`；客户端投影不提供听牌展示。
- 没有独立听牌 API：内容表 `WinRow.WinningRanks` 承载未成胡分解的补张信息，判定器不使用该字段。

## 5. 与 CardAndSkillVerifier 的接缝

- `CardAndSkillVerifier(IWinChecker wins, TileType joker)` 用判定器构造 `SelfDrawnSkill` 与 `SelfDrawnWith7PairsSkill`，并把鬼牌透传给四个技能；技能与校验器的分派细节见 [core.md](core.md) §5。
- 校验顺序固定：玩家同引用 → 出牌分支 → 未定义技能 → 目标牌解析 → 折算张数 → `Skill.Validate`；自摸类分支的 `Validate` 才调用判定器，因此非胡动作不触发查表。
- 自摸张数口径由校验器先挡：`Pong` 13、`Kong` 13 或 14、自摸与七对 14；判定器自身的 14 张检查是第二道闸，两处都通过才提交。
- 组装根 `LocalSessionFactory` 用 `TableCatalog` 建 `WinChecker`，`GameSession` 只接收 `IWinChecker`；客户端 `LocalGame` 用同一 `TableCatalog` 另建 `WinChecker` 交给 `CardAndSkillVerifier`，供响应窗口杠位等二次校验；判定器无共享可变状态，多处复用互不影响。
- 判定的结算后果（自摸/七对番数、杠上开花）不归本页，见 [scoring.md](scoring.md) §3 与 §2。

## 6. 抢杠候选判定

- 抢杠候选的判定输入是「该座位手牌 + 第 4 张」：13 张手牌加上补杠要加进已有碰的那张牌，按 14 张手牌送入判定器，副露沿用该座位的 `Melds`。
- 常规胡按 `IWinChecker.IsWin(手牌 + 第 4 张, 副露, 鬼牌)` 判定，七对按 `IWinChecker.IsSevenPairs(手牌 + 第 4 张, 鬼牌)` 判定；两条判定共用这 14 张输入，第 4 张按已入手的第 14 张计、不单独计张。
- 判定只读：候选阶段不移动牌、不改手牌与副露，第 4 张的物理 id 与所属碰都不变；把第 4 张移入赢家手牌属结算，见 [scoring.md](scoring.md) §2 与 [T-3 §2](../milestones/M0.3/T-3-抢杠与承包.md#2-抢杠结算)。
- 抢杠胡不算自摸，候选判定不经 `SkillId.SelfDrawn`/`SkillId.SelfDrawnWith7Pairs`，赢家笔不置 `SelfDrawn`/`NoJoker`；位语义见 [scoring.md](scoring.md) §2。
- 候选来源只限 `MeldKind.KongSupplement`，第 4 张取补杠方案实际会加进碰的那张牌；候选顺序（座位升序）、窗口与超时归会话与请求层，见 [T-3 §1](../milestones/M0.3/T-3-抢杠与承包.md#1-抢杠窗口)。
