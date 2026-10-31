# Agent Note: M0.2 简单 Bot 策略

Status: implemented — 策略只做合法动作、合法性唯一权威是校验器、无合法动作抛异常、不消费随机。

## Problem

M0.2 的验收是 4 个 Bot 自动打完 8 局、每个决策合法、确定、单次 <1ms，而 T-5 会话要等 T-4 之后才落地；策略必须独立于会话可测：给定局面、座位、可用技能与可选目标弃牌就给出一个动作，同一输入必得同一输出。M4 才做 AI 牌效表与强策略，M0.2 若引入查表打分、随机 tie-break 或出牌排序，15MB 表常驻、加载时序与调参都会挤进本任务，还会让「合法」这一唯一验收点被牌效噪声掩盖。合法性的既有权威是 `Rules/CardAndSkillVerifier`（内部已注入 `IWinChecker` 与鬼牌牌种）；策略若自建第二套判断就会与校验器分叉：会话复核拒绝的动作可能被策略当成合法，或反之。

## Decision

1. 决策只产出合法动作，顺序固定：自己回合 → 七对胡 → 普通自摸 → 出牌（`LastDrawn` 合法则出它，否则按手牌槽位升序取第一张合法牌）；弃牌响应窗口 → 杠 → 碰 → 过；其它语境抛 `InvalidOperationException`。
2. 合法性唯一权威是注入的 `ICardAndSkillVerifier`：每个非 pass 返回值先 `Verify` 一次；出牌用 `Verify(state, self, skill: null, tile, targetCard: null)`，响应技能用 `Verify(..., TargetCard)`。
3. 构造函数接收 `IWinChecker wins` 只为与既有装配签名对齐：策略不判胡、不判鬼牌，`wins` 只做 null 校验、不保存；胡牌与鬼牌判定都在 verifier 内部。
4. `AvailableSkills` 是技能动作的门：技能动作必须 ∈ `AvailableSkills` 且 verifier 通过；出牌不受该列表约束。列表顺序无语义，策略按成员判定。
5. 无合法动作（手牌全鬼、响应窗口无合法技能等）抛 `InvalidOperationException`，不返回 pass：pass 只在响应窗口「有窗口但选择放弃」时合法，用 pass 掩盖非法局面会把上层 bug 变成静默过牌。
6. 不消费 `context.Random`：M0.2 的候选优先级全是固定序（七对 > 普通胡、杠 > 碰、`LastDrawn` > 槽位升序），没有同优先级候选需要 tie-break。`Random` 留在 `BotContext` 供 M4 策略使用。
7. 自己回合只做「胡 → 出牌」，不做暗杠/补杠：本任务的响应窗口只处理弃牌响应，自己回合的杠要 Action 树与补牌流程，归 T-5。
8. 策略无状态：不缓存、不持有跨调用可变字段，同一实例可服务任意多个 `BotContext`。

## Alternatives considered

- **接入 AI 牌效表打分（M4 方向）**：需要 15MB 表常驻与排序数组，M0.2 只验合法性，引入后调参与加载问题会掩盖动作正确性。放弃，M4 重开。
- **策略自己判胡/判鬼牌**：`wins` 与鬼牌牌种都在 verifier 里，策略再判一次形成双权威，两处口径漂移时无法判断谁错。放弃，`wins` 只 null 校验。
- **无合法动作返回 pass**：实现更简单，但把「局面非法/上游漏校验」伪装成正常过牌，会话层再也发现不了。放弃，抛异常。
- **同优先级候选随机 tie-break**：M0.2 不存在同优先级候选，引入随机只会让确定性测试变复杂；`Random` 留到 M4 的强策略。放弃。
- **`AvailableSkills` 只作提示、以 verifier 为准**：会返回会话没请求的技能，越权。放弃，列表是门。
- **出牌先排序再挑**（按花色/牌效）：违反「不排序、不查表」，排序规则本身还要调参。放弃，固定槽位升序。

## Consequences

- T-5 负责构造 `BotContext`（从请求填 `AvailableSkills`/`TargetCard`/`LastDrawn`）、固定 300ms 请求延迟与 4 Bot 8 局驱动；策略本身不 sleep、不读时钟、不做 IO。
- 决策确定性：同一 context 两次 `Decide` 同结果；同 seed 对局的确定性由 `Pcg32` 洗牌与测试本地单局驱动证明，不来自策略抽签。
- `SimpleBotPolicy` 是无状态合法基线；M4 强策略实现 `IBotPolicy` 即可替换，`BotContext.Random` 是它的随机来源。
- T-4 不接入生产：Session/Client/Server 不引用 `Mahjong.Core.Bot`，接入与座位过滤归 T-5。
- 契约与任务口径见 [T-4 规格](../../../../docs/milestones/M0.2/T-4-Bot.md)，类型契约见 [bot.md](../../../../docs/subsystems/bot.md)。
