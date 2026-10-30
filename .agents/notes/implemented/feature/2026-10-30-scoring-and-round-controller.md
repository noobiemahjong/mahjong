# Agent Note: 计分账本与轮次驱动

Status: implemented — 分数走两层账本与暂存/发布/回滚三个操作、自摸与杠由命名入口收口组合校验、8 局驱动只服务无会话的测试与模拟。

## Problem

M0.2 要结算自摸（含七对、杠上开花）与三种杠，并在 8 局后按总分排名，同时满足三个约束：牌桌随时要能读到分数，但一局可能连续发生多次计分（杠在前、自摸在后），而流局必须整局撤回；七对与杠上开花互斥、七对要求 0 副露、杠上开花要求有杠，这些组合规则需要有明确落点；T-3 阶段没有会话，要独立验证 8 局循环、庄家轮换与排名，测试若各自复刻发牌与摸打循环就会与规则分叉。若把单局分数直接记进总分，流局只能事后冲销，`RoundResult` 的局内净变化也要另算；若把组合校验塞进单笔录账入口，纯金额调整（`None`）就没有干净的表达方式。T-5 才定义事件与真实回合流程，T-3 必须留下可核对的接缝而不是先造一套事件。

## Decision

1. `ScoreManager` 分两层：本局暂存 `RoundDelta` 与累计 `TotalScores`，加本局番种聚合 `RoundActionType`；`Release` 把暂存并入总分，`RollbackRound` 撤回暂存。流局口径是先 `RollbackRound` 再 `Release`，净 0。
2. 单笔 `Apply` 只记账、只校验座位与位集合法性；自摸与杠走命名入口 `ApplySelfDrawn`/`ApplyKong`，由它们做组合校验并把单次动作一次性写进暂存。`RoundActionType` 是「本局已发生杠位」的唯一来源，既供组合校验，也供 `RoundResult.ActionType` 取样。
3. `RoundController` 承担 8 局驱动：第 1 局庄家在首次 `RunRound` 开始时消耗一次 RNG（构造不消耗，保证同 seed 从首局可复现）；每局 `Deal` 后庄家摸第 14 张；按 `RoundCommand` 脚本逐条推进，命令耗尽用「第一个非鬼牌槽位」确定性补全；墙空流局；赢家非庄换庄。该驱动只服务无会话的测试与模拟。
4. 每局结束（含流局）把 `Scores.TotalScores` 逐座写进 `PlayerState.Score`：总分权威只在 `Scores`，`Score` 是镜像，保证 `StateHash` 反映权威总分。
5. `RoundResult` 是 T-5 `RoundEnded`/`GameEnded` 的 payload 来源；T-3 不定义事件类型与编码。杠分由 T-5 在杠 `Commit` 后调 `Scores.ApplyKong`，自摸时按局内杠位传 `kongUpFlower`；驱动本身不产生杠，恒传 `false`。
6. 分数用 `short`：单次自摸单座最多 +24、单次杠最多 +6，8 局累计远小于 ±32767，不做溢出保护；上界依据写在类型 XML doc。

## Alternatives considered

- **直接写总分、流局事后冲销**：省掉暂存层，但流局要造相反数记录，`RoundResult.ScoreDelta` 还得单独累计，局中「本局净变化」没有语义落点。放弃。
- **把组合校验放进单笔 `Apply`**：调用方每次都得自己拼番种位并知道先后约束，纯金额调整（`None`）也会被误判；拆出 `ApplySelfDrawn`/`ApplyKong` 后校验与一次写暂存都在入口内完成。放弃。
- **`RoundController` 只做纯结算（不包 `Deal` 与摸打）**：每个测试要自己复制发牌、庄家摸牌、下家摸牌与流局判定，容易与规则层分叉；驱动把确定性循环收在一处。放弃。
- **让 `RoundController` 直接产出事件**：事件名、payload、座位过滤与编码都归 T-5，T-3 造事件会重复定义协议。放弃，只留 `RoundResult`。
- **用 `int` 记分数**：避免溢出讨论，但会扩大回放/协议与 `StateHash` 的字段面，而 `short` 上界足够。放弃。
- **流局用一次零和 `Apply` 代替 `RollbackRound`**：账本里会多出无意义的零和记录，违反「暂存只记本局真实动作」。放弃。

## Consequences

- T-5 必须实现两处挂接：杠 `Commit` 后调 `Scores.ApplyKong`，自摸时把局内杠位转成 `kongUpFlower`；缺任一处则杠分与杠上开花永远不生效。
- `PlayerState.Score` 是镜像：读总分以 `Scores.TotalScores` 为准，写回只发生在每局结束，用于 `StateHash` 与展示。
- `RunRound` 的默认出牌不是玩家策略，T-5 的真实流程用 Action 树、响应窗口与请求/超时，不复用它当生产驱动。
- `RoundResult` 的两个分数序列是构造时快照，`Release` 清账本不会污染历史结果；M0.3 加点炮/承包/连庄奖励时新增计分入口与位，两段式账本不变。
- 计分口径是 M0.2 子集（无连庄奖励/承包/跟庄/中马），扩展项在 [M0.3](../../../../docs/milestones/M0.3-规则完整化.md)。
