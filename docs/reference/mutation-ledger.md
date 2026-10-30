# 变异台账

本文件是变异台账的唯一登记处：常规任务只登记待跑条目，不执行；整批执行由用户安排时间统一跑，结果回填本表。纪律见根 [AGENTS.md](../../AGENTS.md)，流程见 [development.md](../development.md)。

## 条目字段

| 字段 | 写法 |
|---|---|
| 编号 | `ML-<n>`，全仓连续、只增不减；不复用、不重排。 |
| 目标 | 被变异的文件与符号：`相对路径` + 类型/方法名，不写行号。 |
| 变异 | 注入的语义变异，写清改前 / 改后；组合变异逐处写明并说明组合理由。 |
| 期望判死闸 | 变异后应先变红的用例或检查点；无对应闸即测试缺口，登记时写明。 |
| 登记任务 | 产生该条目的任务文档路径。 |
| 结果 | `未跑` / `KILLED` / `SURVIVED` / `BUILD_FAIL`；回填时附判死用例与证据落点（证据在 gitignore 的 `.reviews/`、`build/`）。 |

## 台账

| 编号 | 目标 | 变异 | 期望判死闸 | 登记任务 | 结果 |
|---|---|---|---|---|---|
| ML-001 | `src/Mahjong.Core/Flow/ResponseWindow.cs`（`Arbitrate` / `Close` 比较链） | 仲裁优先级反转 + arrivalSeq 比较反转（两处同时注入；单点各已 KILLED，组合验证两道守卫不互相掩护） | 仲裁族用例（`Arbitration_*`）先红 | [M0.2 T-2](../milestones/M0.2/T-2-技能与响应.md) | 未跑 |
| ML-002 | `ScoreManager.ApplySelfDrawn`（每份） | 有鬼/无鬼倍数反转：`(hasJoker ? 2 : 4)` → `(hasJoker ? 4 : 2)` | `ApplySelfDrawn_NoJoker_PaysFourEach`、`..._WithJoker_PaysTwoEach`（12/-4、6/-2） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-003 | `ScoreManager.ApplySelfDrawn`（NoJoker 标记） | 标记取反：`if (!hasJoker)` → `if (hasJoker)` | `ApplySelfDrawn_NoJoker_*`、`..._WithJoker_*`（ActionType 精确相等） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-004 | `ScoreManager.ApplyKong`（三种杠金额） | 三支同改：明杠 3→6、暗杠 6/2→3/1、补杠 3/1→6/2 | `ApplyKong_Exposed_*`、`..._Concealed_*`、`..._Supplement_*`（字面量精确） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-005 | `ScoreManager.RollbackRound` | 删除 `Array.Clear(_roundDelta)`（暂存保留） | `RollbackRound_ClearsStaging*`、`DrawRound_RollbackThenRelease_*` | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-006 | `RoundController.CompleteWin`（庄家轮换） | 非庄赢不换庄：`_bankerSeat = winnerSeat` → 仅庄赢时赋值 | `Win_NonBanker_BecomesNextBanker` | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-007 | `RoundController.CompleteWin`（轮换目标） | 非庄赢换下家：`_bankerSeat = winnerSeat` → `(roundBanker + 1) % 4` | `Win_WinnerNotAdjacentToBanker_BecomesBankerAndDealsNextRound`（赢家 2 → 下局庄 2） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-008 | `RoundController.CompleteDraw`（流局轮换） | 流局换庄：`_bankerSeat` 加 1 | `EmptyCommands_PlayDeterministic*`（BankerSeat=0）、`DrawRound_AfterScoringRound_*` | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-009 | `RoundController.Rank`（同分比较） | 同分倒序：`left.CompareTo(right)` → `right.CompareTo(left)` | `Rank_OrdersByScoreDescending*`（[0,2,3,1]） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-010 | `RoundController.CompleteWin`（七对优先） | 两口径同真时按普通自摸：`sevenPairs = IsSevenPairs(...)` → `IsSevenPairs(...) && !IsWin(...)` | `Win_SevenPairsPriority_*`（24/-8、调用序） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-011 | `RoundController.RunRound`（起庄） | `% 4` → `% 3` | `FirstRunRound_DeterminesBanker*`（6→2）、`DifferentSeeds_*`（42→1、20261029→3） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-012 | `RoundController.CompleteWin`（取样时机） | delta/ActionType 取样后移到 Release 之后 | `RoundResult_SnapshotsScoresBeforeRelease*`、`RunRound_OpeningState_*` | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-013 | `RoundController.RunRound`（命令座位） | 删除 `command.Seat != seat` 抛 `ArgumentException` | `SeatMismatch_*`（精确异常 + 独立开局快照/StateHash） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-014 | `RoundController.FindDefaultDiscardSlot` | 取最后一个非鬼牌槽位而非第一个（保留跳过鬼牌） | `EmptyCommands_DefaultDiscardTakesFirstNonJokerHandSlot`（seed16 首弃 36、seed42 首弃 75） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-015 | `RoundController.IsJokerActive` | 恒真（删花色 1–5 校验），`default` 鬼牌按 Id 0 命中一万 | `Win_WithDefaultJoker_DoesNotTreatTileTypeZeroAsJoker`（seed27 含一万 → 12/-4 与 NoJoker） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-016 | `RoundController.RunRound`（流局返回点） | 先 `CurrentSeat = nextSeat` 再判墙空（流局也交接座位） | `EmptyCommands_PlayDeterministicDiscardsUntilWallExhaustionDraw`（`CurrentSeat`=3、仅庄 `IsBanker`） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-017 | `RoundController.RunRound`（每局发牌庄家） | 第 2 局起仍用首局庄家发牌 | `Win_WinnerNotAdjacentToBanker_BecomesBankerAndDealsNextRound`（第 2 局 `CurrentSeat`=2、仅座 2 `IsBanker`、庄 14 他 13） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-018 | `RoundController.RunRound`（墙空判据） | 墙空检查移到出牌前 | `EmptyCommands_PlayDeterministic*`（84 弃牌、Wall 0、136 守恒） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-019 | `ScoreManager.Apply`（未定义位） | 删除位掩码校验 | `Apply_UndefinedFlagBits_*`（128/256/32768） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-020 | `ScoreManager.Release`（清零暂存） | 只并入总分、不归零暂存 | `Release_IsIdempotent`（第二次不得再加 9）、`Release_MergesStagingIntoTotalAndClears` | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-021 | `ScoreManager.RoundDelta`（只读视图） | 返回内部数组（可写）而非 `ReadOnlyCollection` | `RoundDelta_IsLengthFour...` | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-022 | `RoundController.CompleteDraw`（镜像写回） | 删除 `WriteBackScores(state)` | `DrawRound_AfterScoringRound_*`（Score 12/-4） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-023 | `RoundController.RunRound`（局号/结束） | 删 `_roundNo++` 且 `IsGameOver` 改 `>=`（两处组合） | `EightRounds_WriteMirroredScores*`、`NinthRound_Throws*` | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-024 | `ScoreManager.ApplySelfDrawn`（七对+杠） | 删「七对 && 本局已发生杠位」检查 | `ApplySelfDrawn_SevenPairsAfterKong_*` | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-025 | `ScoreManager.ApplyKong`（无目标座位） | 删 `RequireNoTargetSeat` | `ApplyKong_NoTargetKongWithSourceSeat_*` | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-026 | `RoundTestSupport.StubWinChecker` / `CompleteWin`（判定器入参） | 传给 `IsSevenPairs`/`IsWin` 的 hand/melds/joker 换成 `default` 或他人手牌（stub 不校验入参） | `Win_WithJokerInHand_ScoresJokerRate`（joker=31/hand=14/melds=0/首牌 id）、`Win_SevenPairsPriority_*`（joker=0/hand=14） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
| ML-027 | `RoundController.CompleteWin`（仅七对胡） | 七对也要求常规胡成立：`sevenPairs = IsSevenPairs(...)` → `&& IsWin(...)` | `Win_OnlySevenPairs_IsAcceptedWithoutRegularWin`（24/-8；有鬼 12/-4） | [M0.2 T-3](../milestones/M0.2/T-3-计分与轮次.md) | 未跑 |
