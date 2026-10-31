# 子系统：Bot 策略

> 本页只写类型、语义与契约；流程见 [architecture](../architecture.md)，决策理由见 [Note](../../.agents/notes/implemented/feature/2026-10-31-simple-bot-policy.md)，任务口径与验收见 [T-4](../milestones/M0.2/T-4-Bot.md)。命名空间 `Mahjong.Core.Bot`，不引用 Godot/网络/时钟。

## 1. 类型

- `BotDecision(SkillId? Skill, Tile? Card)` 是 `readonly record struct`，只承载选择、不校验合法性。`Skill != null` 是技能动作，`Card` 为 null，目标弃牌由 `BotContext.TargetCard` 提供；`Skill == null && Card != null` 是出牌；两者皆 null 是「过」，只在弃牌响应窗口合法。策略不返回 `Skill` 与 `Card` 同时非 null 的组合。
- `BotContext` 是一次决策的只读输入：权威 `GameState`、`PlayerState Self`、`AvailableSkills`、`IRandomSource Random`、可选 `TargetCard`/`LastDrawn`。`Hand` 是 `Self.Hand.Items` 的实时只读视图（不复制、不排序）；`AvailableSkills` 是构造时复制的只读快照；`TargetCard`/`LastDrawn` 原样保存，构造不校验物理 id 或是否在区域。实例创建后不可变。
- `IBotPolicy` 只有 `BotDecision Decide(BotContext context)`：实现不得读时钟、sleep、做 IO、持有跨调用可变状态，也不得改局面。
- `SimpleBotPolicy` 是 M0.2 的「合法即可」实现，构造接收 `IWinChecker wins` 与 `ICardAndSkillVerifier verifier`，只保存 verifier。

## 2. 决策顺序

| 语境 | 顺序 | 返回 |
|---|---|---|
| 自己回合（`State.CurrentSeat == Self.Seat`） | 七对自摸 → 普通自摸 → 出牌 | `SelfDrawnWith7Pairs` / `SelfDrawn` / 出牌 |
| 弃牌响应窗口（非自己回合且 `TargetCard` 非 null） | 杠 → 碰 → 过 | `Kong` / `Pong` / `default` |
| 其它（非自己回合且无 `TargetCard`） | — | `InvalidOperationException` |

- 出牌分支：`LastDrawn` 非 null 且 verifier 通过则出它；否则按手牌槽位 0..n-1 升序取第一张 verifier 通过的牌；全部不合法抛 `InvalidOperationException`。
- 技能动作必须 ∈ `AvailableSkills` 且先经 verifier；出牌不受 `AvailableSkills` 约束，但也先经 verifier。列表顺序无语义，策略按成员判定。
- 每个非「过」返回值都先经 `ICardAndSkillVerifier` 一次，策略不按手牌自建第二套合法性判断。
- 自己回合只做「胡 → 出牌」，不做暗杠/补杠；响应窗口只处理碰/杠。自己回合即使 `TargetCard` 非 null 也走自己回合分支。
- 技能列表为空时自己回合只出牌、响应窗口只过。

## 3. 构造与异常边界

| 场景 | 异常 | 校验点 |
|---|---|---|
| `BotContext` 的 `state`/`self`/`availableSkills`/`random` 为 null | `ArgumentNullException` | 构造参数 |
| `self` 不是 `state.Players[self.Seat]` 的同一引用 | `ArgumentException` | 构造 |
| 技能列表含未定义 `SkillId`（如 0/5/255）或重复项 | `ArgumentException` | 构造 |
| `SimpleBotPolicy` 的 `wins`/`verifier` 为 null | `ArgumentNullException` | 构造 |
| `Decide(null)` | `ArgumentNullException` | 决策入口 |
| 非自己回合且无 `TargetCard` | `InvalidOperationException` | 决策入口 |
| 自己回合没有任何合法出牌 | `InvalidOperationException` | 出牌分支 |

- 构造与决策失败都不改局面；异常只用于违反不变式，不用异常做正常流程。
- `BotDecision` 的三种形态由调用方按语境消费：自己回合不应收到「过」，响应窗口不应收到出牌。

## 4. 确定性与 Random

- 策略不消费 `BotContext.Random`：M0.2 的候选优先级全是固定序（七对 > 普通自摸、`Kong > Pong`、`LastDrawn` > 槽位升序），没有同优先级候选需要 tie-break。`Random` 留在上下文供 M4 策略扩展，不为「用掉」而引入随机。
- 同一 context 两次 `Decide` 必得同一 decision；无静态可变状态、无跨调用缓存、不读墙钟、不 sleep、不做 IO。
- 对局确定性来自固定优先级与 `Pcg32` 洗牌，不由策略抽签；同 seed 的单局终局哈希与弃牌序列一致由测试本地驱动证明。

## 5. wins 口径

- `SimpleBotPolicy` 构造接收的 `IWinChecker wins` 只做 null 校验、不保存：策略不知道该局鬼牌牌种，无法正确调用胡牌判定；保存一个不能正确使用的依赖只会误导维护者。
- 动作合法性唯一权威是 `ICardAndSkillVerifier`，它内部已注入 `IWinChecker` 与鬼牌牌种；策略的胡牌/鬼牌判断全部经它完成。
- 该口径与 [T-4](../milestones/M0.2/T-4-Bot.md) §1、[Note](../../.agents/notes/implemented/feature/2026-10-31-simple-bot-policy.md) 一致。

## 6. 与 T-5 的接缝

- T-5 会话从请求构造 `BotContext`：`self` 取 `state.Players[seat]` 同一引用，`AvailableSkills` 填本回合请求的技能集，`TargetCard`/`LastDrawn` 按语境填，`Random` 用会话随机源。
- 固定 300ms 请求延迟、请求/超时/托管、4 Bot 8 局驱动与座位过滤归 T-5；策略只做单次同步决策，不等待、不驱动循环。
- `BotDecision` 转成命令或应答由会话做；T-4 不接入生产，Session/Client/Server 不引用 `Mahjong.Core.Bot`。
- M4 强策略实现 `IBotPolicy` 即可替换并消费 `BotContext.Random`；`SimpleBotPolicy` 保留为合法基线。
