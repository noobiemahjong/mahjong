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
| ML-028 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`Decide` 响应分支） | 优先级反转：先判 `Pong` 再判 `Kong` | `Response_KongAndPongBothLegal_ReturnsKong`、`AvailableSkillsOrder_DoesNotChangeDecision`（两用例都断言 Kong） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-029 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`Decide` 自己回合技能分支） | 优先级反转：先判 `SelfDrawn` 再判 `SelfDrawnWith7Pairs` | `SelfTurn_SevenPairsWinsOverStandardWin`（断言不调 IsWin）、`SelfTurn_StandardWinningHand_ReturnsSelfDrawnSkill`（断言调用序） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-030 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`HasSkill` 门） | 删除 `AvailableSkills` 门：不判列表，直接按 verifier 结果返回技能 | `SelfTurn_WinningHandButSkillNotListed_Discards`、`SelfTurn_SevenPairsHandWithOnlySelfDrawnListed_Discards`、`Response_EmptyAvailableSkills_ReturnsPass` | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-031 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`DecideDiscard`） | 删除 `LastDrawn` 优先：直接从槽位 0 起找 | `SelfTurn_LastDrawnLegal_IsPreferredOverEarlierHandSlot`（期望手牌第 7 张） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-032 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`DecideDiscard` 槽位循环） | 槽位顺序反转：从 `hand.Count - 1` 递减到 0 | `SelfTurn_LastDrawnNotInHand_FallsBackToFirstHandSlot`（期望 `T(0,0)`）、`SelfTurn_FirstHandSlotIsJoker_FallsBackToNextLegalSlot`（期望 `T(0,0)`） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-033 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（技能与出牌返回前） | 返回未经 verifier 的候选：跳过 `Verify` 直接返回 | `SelfTurn_LastDrawnIsJoker_FallsBackToFirstHandSlot`、`Response_TargetCardIsJoker_ReturnsPass`、`Response_WallEmpty_RejectsKongAndReturnsPong` | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-034 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`DecideDiscard` 无合法牌） | 无合法出牌返回 `default` 而非抛 `InvalidOperationException` | `SelfTurn_NoLegalDiscard_ThrowsAndKeepsState`（精确异常 + 快照/哈希不变） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-035 | `src/Mahjong.Core/Bot/BotContext.cs`（构造函数） | 删除 `ReferenceEquals(state.Players[self.Seat], self)` 同引用校验 | `Constructor_LookalikePlayerWithSameSeat_Throws`（精确异常与 `ParamName`） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-036 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`Decide`） | 决策内改状态：返回前执行 `self.Score++`（`StateHash` 含 score） | `Decide_DoesNotChangeState_OnEveryDecisionPath`（快照/哈希）、`Decide_ConservesAll136Tiles` | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-037 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（自己回合自摸分支） | 自摸绕过 verifier：`HasSkill(SelfDrawn)` 为真即返回 | `SelfTurn_SevenPairsHandWithOnlySelfDrawnListed_Discards`（期望出牌）、`SelfTurn_StandardWinningHand_ReturnsSelfDrawnSkill`（调用序断言） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-038 | `src/Mahjong.Core/Bot/BotContext.cs`（`IsDefinedSkill` / 构造校验） | 未定义技能校验恒开：`IsDefinedSkill` 恒 `true` | `Constructor_UndefinedSkill_Throws`（0/5/255 三组 Theory，精确 `ArgumentException` + `ParamName` + 消息含编号） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-039 | `src/Mahjong.Core/Bot/BotContext.cs`（构造函数重复检测循环） | 删除重复技能检测（重复技能被接受） | `Constructor_DuplicateSkill_Throws`（相邻 + 非相邻两段） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-040 | `src/Mahjong.Core/Bot/BotContext.cs`（`Hand` 属性） | 实时视图改复制快照：`Self.Hand.Items` → `Self.Hand.Items.ToArray()` | `Hand_IsLiveReadOnlyViewOfSelfHand`、`Hand_IsNotSorted_NorCopied`（均 `Assert.Same`） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-041 | `src/Mahjong.Core/Bot/BotContext.cs`（`AvailableSkills` 赋值） | 只读快照改保存调用方列表引用：`Array.AsReadOnly(availableSkills.ToArray())` → `availableSkills` | `AvailableSkills_IsSnapshot_InputListMutationDoesNotReachContext`（追加后仍只有 Pong） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-042 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（构造 + 自己回合自摸分支） | 保存并使用注入 wins：`_wins = wins`，自摸分支用 `_wins.IsWin(...) && !_wins.IsSevenPairs(...)` 直接判胡、不经 verifier | `Constructor_WinsDependency_IsOnlyNullChecked`（`ThrowingWinChecker` 被调用即抛 `InvalidOperationException`） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-043 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`Decide` 自己回合分支） | 加自己回合杠分支（违反「自己回合不做暗杠/补杠」）：SelfDrawn 分支后插 `if (HasSkill(Kong) && _verifier.Verify(state, self, Kong, null, null)) return new BotDecision(Kong, null);` | `SelfTurn_ConcealedKongIsLegalButPolicyStillDiscards`（自己回合暗杠经 verifier 合法，策略仍出牌） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-044 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（技能返回点） | 技能动作同时携带 Card：`new BotDecision(SkillId.Kong, null)` → `new BotDecision(SkillId.Kong, targetCard)`（其余技能返回点同理） | `Response_KongAndPongBothLegal_ReturnsKong`、`DecidedSkills_AreVerifiedAndListed`、`SelfTurn_StandardWinningHand_ReturnsSelfDrawnSkill`（均 `Assert.Null(decision.Card)`） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-045 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`Decide` 入口） | 消费随机：决策前 `_ = context.Random.NextUInt();` | `Decide_DoesNotConsumeRandom`（`CountingRandomSource.Calls == 0`） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-046 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`DecideDiscard` 与响应分支的 `_verifier.Verify`） | 手写规则替代 verifier：出牌=非鬼（硬编码 FixedType 31）+在手中+折算 14+轮到自己；碰=目标非鬼+同牌种 ≥2；杠=墙非空+同牌种 ≥3 | `Response_Type31TargetPong_IsIllegalOnlyForJoker31Verifier`、`Response_Type0TargetPong_IsIllegalOnlyForJokerType0Verifier`（同一目标牌随 verifier 鬼牌牌种改变合法性） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-047 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`Decide` 末分支） | 非自己回合且无 TargetCard 返回 pass 而非抛：`throw new InvalidOperationException(...)` → `return default;` | `NotSelfTurn_WithoutTargetCard_ThrowsAndKeepsState`（精确异常 + 局面三口径不变）、`Pass_IsReturnedOnlyInResponseWindow` | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-048 | `src/Mahjong.Core/Bot/BotContext.cs`（构造函数） | 增加 TargetCard/LastDrawn 物理 id 校验（不在手牌/弃牌区即抛 `ArgumentException`），违反「构造不校验物理 id」 | `Constructor_DoesNotValidateTargetCardOrLastDrawn`（原样保存 + 不在手/不在弃牌区仍可构造） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-049 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`Decide` 分支顺序） | 自己回合与响应窗口顺序反转：先判 `TargetCard != null` 再判自己回合 | `SelfTurn_WithTargetCard_StillTakesSelfTurnBranch`（期望 `SelfDrawn`） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
| ML-050 | `src/Mahjong.Core/Bot/SimpleBotPolicy.cs`（`DecideDiscard` 鬼牌判断/verifier 调用） | 仅出牌分支改手写规则：硬编码 Type 31 为鬼并跳过、不走 verifier；响应分支仍委托 verifier | `SelfTurn_LastDrawnType31_IsDiscardedOnlyWhenNotJoker`（无鬼 verifier 出 T31；joker=31 verifier 回退槽位 0） | [M0.2 T-4](../milestones/M0.2/T-4-Bot.md) | 未跑 |
