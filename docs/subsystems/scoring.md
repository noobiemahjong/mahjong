# 子系统：计分与轮次

> 本页只写类型、语义与契约；流程见 [architecture](../architecture.md)，决策理由见 [Note](../../.agents/notes/implemented/feature/2026-10-30-scoring-and-round-controller.md)，任务口径与验收见 [T-3](../milestones/M0.2/T-3-计分与轮次.md)。命名空间 `Mahjong.Core.Rules`，不引用 Godot/网络/时钟。

## 1. 计分账本

- `ScoreManager` 把分数分成两层：本局暂存 `RoundDelta` 与累计总分 `TotalScores`，各是 4 个 `short`（下标座位 0–3）；`RoundActionType` 聚合本局已发生的番种与杠位。
- 状态转移只有三种：计分动作写暂存；`Release()` 把暂存逐座并入总分后清零；`RollbackRound()` 撤回暂存、总分不变。流局口径是先 `RollbackRound` 再 `Release`，净变化为 0。
- 单次计分动作先完成全部校验、再一次性写暂存，失败不改变任何状态；`Release` 与 `RollbackRound` 幂等。
- `RoundDelta`/`TotalScores` 是长度恒为 4 的 `ReadOnlyCollection<short>` 实时视图：后续计分动作可见，但通过视图写入抛 `NotSupportedException`，不能改内部状态。
- `RoundActionType` 只读，`Release`/`RollbackRound` 后回到 `ScoreActionType.None`。
- 金额上界：单次自摸单座最多 +24（七对与杠上开花互斥，各自只能再乘一次 ×2），单次杠单座最多 +6；每局单座净额是这些动作的累加，8 局累计远小于 `short` 的 ±32767，用 `short` 记账且不做溢出保护。
- 实例非线程安全；座位固定 4 个。

## 2. 番种与杠位位集

`ScoreActionType : ushort` 是 `[Flags]` 位集，数值参与回放与协议编码，已发布后不得改动。

| 成员 | 值 | 语义 |
|---|---|---|
| `None` | 0 | 无番种：纯金额调整，不计入任何番种或杠位 |
| `SelfDrawn` | 1 | 自摸；七对自摸与杠上开花自摸同样置位 |
| `SevenPairs` | 2 | 七对 |
| `ConcealedKong` | 4 | 暗杠 |
| `ExposedKong` | 8 | 明杠 |
| `KongSupplement` | 16 | 补杠 |
| `KongUpFlower` | 32 | 杠上开花 |
| `NoJoker` | 64 | 无鬼牌自摸 |

- 未定义位（不在以上并集）是非法输入，`Apply` 用 `(value | defined) == defined` 判定。
- 三种杠位的并集用于「七对须 0 副露」与「杠上开花须有杠」的判定。

## 3. 自摸与杠

`ApplySelfDrawn(winnerSeat, hasJoker, sevenPairs, kongUpFlower)` 每份 `per = (hasJoker ? 2 : 4) * (sevenPairs ? 2 : 1) * (kongUpFlower ? 2 : 1)`，赢家 `+3*per`、其余三家各 `-per`。

| 条件 | per | 赢家 | 其余每家 |
|---|---|---|---|
| 无鬼 | 4 | +12 | -4 |
| 有鬼 | 2 | +6 | -2 |
| 七对、无鬼 | 8 | +24 | -8 |
| 七对、有鬼 | 4 | +12 | -4 |
| 杠上开花、无鬼 | 8 | +24 | -8 |
| 杠上开花、有鬼 | 4 | +12 | -4 |

- 标记：`SelfDrawn` 恒置；`hasJoker == false` 置 `NoJoker`；`sevenPairs` 置 `SevenPairs`；`kongUpFlower` 置 `KongUpFlower`。
- 不可组合校验（全部校验先于写暂存，违反抛 `InvalidOperationException` 且状态不变）：七对与杠上开花同局；七对而本局已有任一杠位；杠上开花而本局尚无杠位。
- `winnerSeat >= 4` 抛 `ArgumentOutOfRangeException`，状态不变。

`ApplyKong(seat, kind, sourceSeat = Meld.NoTargetSeat)` 三种杠：

| kind | 杠家 | 其余 | 标记 | sourceSeat |
|---|---|---|---|---|
| `MeldKind.KongExposed` | +3 | 放杠者 -3 | `ExposedKong` | 0–3 且 ≠ `seat` |
| `MeldKind.KongConcealed` | +6 | 其余各 -2 | `ConcealedKong` | 必须 `Meld.NoTargetSeat`（255） |
| `MeldKind.KongSupplement` | +3 | 其余各 -1 | `KongSupplement` | 必须 `Meld.NoTargetSeat`（255） |

- 三类杠单次四座 delta 之和为 0；`seat >= 4`、`kind` 不是三种杠（含 `Pong` 与未定义值）抛 `ArgumentOutOfRangeException`；`sourceSeat` 不符抛 `ArgumentException`；失败状态不变。

## 4. 轮次驱动

- `RoundController(IWinChecker wins, IRandomSource rng, TileType joker = default)`：`wins`/`rng` 为 null 抛 `ArgumentNullException`；内部用同一对 `wins`+`joker` 构造 `CardAndSkillVerifier`；`joker` 整场不变，`default(TileType)` 表示无鬼。
- 公开面：`TotalRounds = 8`、`RoundNo`（下一次 `RunRound` 的局号 1–8；9 表示 8 局已打完）、`BankerSeat`、`IsGameOver`（`RoundNo > 8`）、`Scores`（控制器独占的账本）、`RunRound(GameState, IReadOnlyList<RoundCommand>)`、`Rank()`。
- 起庄：第 1 局在 `RunRound` 开始时用 `(byte)(rng.NextUInt() % 4)` 抽取，构造不消耗 RNG；之后由轮换规则决定。`RunRound` 之前的 `BankerSeat` 是占位 0。
- 每局开工：`state.RoundNo = RoundNo` → `state.Deal(rng, BankerSeat)`（`Wall` 84、四家 13、`CurrentSeat = 庄家`、仅庄家 `IsBanker`）→ 庄家 `DrawFromWall`（手牌 14、`Wall` 83）。
- 回合循环：当前座位 = `state.CurrentSeat`；按序消费 `commands`。`Seat` 不等于当前座位抛 `ArgumentException`，`Kind` 未定义抛 `ArgumentOutOfRangeException`，都不应用该命令；本局结束（自摸或流局）后，其后剩余命令不再消费。
- 出牌命令：槽位越界或 `CardAndSkillVerifier.Verify(state, player, skill: null, card, targetCard: null)` 返回 false 抛 `InvalidOperationException`（不应用）；成功后走 `state.Discard(seat, slot)`。
- 自摸命令：先 `IsSevenPairs`，再 `IsWin` 且仅在非七对时算常规胡；两者皆假抛 `InvalidOperationException`（不应用）。结算取 `hasJoker = joker 花色在 1–5 且手牌中存在同 Type.Id`（`default(TileType)` 视为无鬼），调 `ApplySelfDrawn(seat, hasJoker, sevenPairs, kongUpFlower: false)`，再 `Release`、写回镜像、庄家置赢家、局号加一。
- 命令耗尽：当前手牌中第一个非鬼牌槽位确定性出牌；手牌全是鬼牌抛 `InvalidOperationException`。该默认策略只服务无会话的测试与模拟，不是 T-5 的真实玩家策略。
- 每次出牌后下家从墙头摸牌；摸牌前 `Wall.Count == 0` 即流局：`RollbackRound` + `Release`（净 0）、写回镜像、庄家不变、局号加一，返回 `WinnerSeat = 255`、`ActionType = None`、`ScoreDelta` 全 0。流局在下家摸牌与座位交接之前返回：`CurrentSeat` 仍是最后出牌者，`PlayerState.IsBanker` 仍是本局 `Deal` 写入的庄家。
- 写回：每局正常结束（含流局）把 `Scores.TotalScores` 逐座写进 `state.Players[i].Score`；唯一权威是 `Scores`，`PlayerState.Score` 只是镜像，保证 `StateHash` 反映总分。
- 异常边界：`ArgumentException` 表示调用方脚本非法，`InvalidOperationException` 表示当前局面下动作非法；单条命令/单步原子，失败步不应用，`RunRound` 不保证整局事务性。8 局已打完再调用抛 `InvalidOperationException` 且不改局面。
- 确定性：同 seed + 同脚本两次 8 局，最终 `StateHash` 与 `TotalScores` 一致。

## 5. 命令与单局结果

- `RoundCommandKind : byte` 取 `Discard = 1`、`Win = 2`，数值是脚本与回放编码的一部分。
- `RoundCommand(byte Seat, RoundCommandKind Kind, int Slot = 0)` 是 `readonly record struct`，只承载数据、不做合法性校验；`RoundCommand.Discard(seat, slot)` 与 `RoundCommand.Win(seat)` 是工厂。
- `RoundResult` 是单局结果：`NoWinnerSeat = 255`、`RoundNo`、`BankerSeat`（本局坐庄、轮换前）、`WinnerSeat`、`IsDraw`、`ActionType`、`ScoreDelta`（`Release` 之前取样）、`TotalScores`（`Release` 之后）。两个分数序列在构造时复制为只读快照，不随后续对局变化；构造器是 internal。

## 6. 庄家轮换与排名

- 赢家非庄 → 下一局 `BankerSeat = 赢家`；赢家为庄 → 不变；流局 → 不变。
- `Rank()` 返回长度 4 的座位列表：`TotalScores` 降序、同分座位号升序；纯函数，随时可调，不要求 8 局已打完。

## 7. 与 T-5 的接缝

- `RoundResult` 是 T-5 `RoundEnded`（`winnerSeat`/`scoreDelta`/`actionType`）与 `GameEnded`（`totalScores`/排名）的 payload 来源；T-3 不定义事件类型、不做事件编码。
- 杠分：T-5 在杠 `Commit` 之后调用 `Scores.ApplyKong(seat, kind, sourceSeat)`；自摸时按本局是否已有杠位传 `kongUpFlower`。T-3 驱动不产生杠，自摸恒传 `kongUpFlower: false`。
- 真实会话的回合推进用 Action 树、响应窗口与请求/超时；`RunRound` 的脚本命令与默认出牌不复用于 T-5。
