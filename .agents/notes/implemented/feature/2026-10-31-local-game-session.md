# Agent Note: M0.2 本地会话与事件流

Status: implemented — 单根协程驱动 8 局、会话独占局面与计分、tick 边界结算请求、事件破坏性单消费者读取。

## Problem

M0.2 要求 1 真人 + 3 Bot 走完 8 局、事件流可被客户端消费、两个会话互不串状态、无网络代码；此前 Core 只有可复用的动作树、请求/响应窗口与计分账本，没有会话层。会话要把「谁在什么时候等什么请求、超时怎么托管、杠在什么地方可达、结算 payload 从哪来」固化成唯一流程，同时不把查表、内容目录、随机源以外的宿主概念带进程序集。规格留了若干开放点：会话是否持有 `TableCatalog`、是否新增 `RequestKind`、回合之间要不要 Ready 门、`DrainEvents` 是否按 viewer 保留游标、超时后能否自走完、杠上开花用什么口径、七对是否受其他家杠影响。任一处选错都会形成双权威或让流程停在等待态。

## Decision

1. 复用 `Mahjong.Core.Flow` 的 `GameCommand`/`CommandVerb`/`AllowedVerbs`/`RequestKind`/`PendingRequest`/`RequestOutcome`/`RequestOutcomeKind`，Session 不重复定义请求/命令类型（D1）。
2. `GameSession` 构造为 `(SessionConfig, ContentCatalog, IWinChecker, IRandomSource)`，不含 `TableCatalog`；查表加载与 `WinChecker` 构造留在组装根 `LocalSessionFactory`（D2）。`SessionConfig` 不含内容目录，会话不读文件系统（D3）。
3. 自己回合的 `Discard` 请求允许位在杠合法时扩展为 `Discard | Kong`，不新增 `RequestKind`，使暗杠/补杠可达（D4）。
4. 回合之间不设 Ready 门：`RoundEnded` 后同一 tick 立即开下一局，`GameEnded` 后流程结束（D5）。
5. `HandSync` 枚举与编解码保留但不产出：M0.2 无重连（D6）。
6. 动作树是单根动作协程 + `WaitUntil`，不用子动作与 abort（D7）。
7. `DrainEvents(viewerSeat)` 是破坏性单消费者读取：viewer 只影响本次 payload 编码，不保存 per-viewer 游标（D8）。
8. 超时托管：出牌请求超时自动打手牌槽位升序第一张合法牌；Win 请求超时按 Pass；响应窗口到 deadline 即关闭，未应答者 TimedOut（D9）。
9. Core 允许三处改动：`RoundResult` 构造器 internal→public；新增 `RoundRanking.Rank` 且 `RoundController.Rank` 委托同一实现；新增 `ScoreManager.ApplySevenPairsSelfDrawn(byte, bool)`，既有方法不修改（D10）。
10. additive API：`IsStarted`/`IsGameOver`/`ActiveRequests`、`SessionEventPayloadReader` 与 payload record structs（D11）。
11. Bot 请求延迟 `max(1, ceil(TickRate × 300 / 1000))` tick，10Hz 为 3 tick（D12）。
12. `Submit` 的 `arrivalSeq` 取调用时的 `GameSession.Tick`，跨 tick 先到先得（D13）。
13. 结算口径：七对调 `ApplySevenPairsSelfDrawn`（不检查其他家杠位）；普通自摸的 `kongUpFlower` 只看赢家本回合最近一次摸牌是否为杠后补牌；顺序固定为取样 delta/actionType → `Release` → 写回 `PlayerState.Score` 镜像 → 构造 `RoundResult` → 产 `RoundEnded`。

## Alternatives considered

- **子动作树 + abort**：用子动作回传结果、abort 取消等待，表达力更强；但 8 局流程是线性状态机，子动作结果回传与 abort 时序都要额外测试，M0.1 已覆盖这些语义，本任务不需要。放弃，单根协程 + `WaitUntil`。
- **回合间 Ready 门**：让结算展示与下一局解耦；M0.2 命令集没有 Ready，加入门控会让无真人会话无法自走，也把 T-6 的显示节奏带进会话。放弃，T-6 用暂停 Step 显示结算。
- **per-viewer 事件游标**：按 viewer 保存已读位置，支持多消费者；单机单消费者下只会引入重复事件与内存增长，M2 重连再设计。放弃，破坏性单次 Drain。
- **`GameSession` 持 `TableCatalog` 并自行装配 `WinChecker`**：会话直接可用，但把查表加载与内容目录带进程序集，破坏「会话只接收判定器与随机源」的边界，也难在测试里替换判定器。放弃，组装根 `LocalSessionFactory` 负责。
- **新建 Session 自己的 `RequestKind`/`GameCommand` 枚举**：看似隔离，实际与 Core.Flow 同名同值，形成两份协议常量。放弃，复用（D1）。
- **用局内全局杠位聚合计算杠上开花**：实现简单，但会把其他家本局的杠误算成赢家的杠上开花；七对也会被其他家的杠误伤。放弃，会话跟踪「赢家本回合最近一次摸牌是否杠后补牌」的布尔。
- **七对复用 `ApplySelfDrawn`**：既有方法检查「本局已有任一杠位」，而七对只要求赢家 0 副露，其他家杠不应阻断七对。放弃，新增 `ApplySevenPairsSelfDrawn`（additive）。
- **Bot 用真实 300ms 时钟**：违反不读墙钟；改为 tick 延迟并放在 `Step` 开头处理，保证确定性。
- **超时不做托管、把等待留给宿主**：会话更薄，但无人值守的 4 Bot 局会停在等待态，流程无法自走完；超时口径已写进契约 §6.6，宿主只驱动 tick。放弃。
- **`DrainEvents` 直接返回内部缓冲引用**：零拷贝，但调用方写坏或跨 drain 持有会污染 seq；改为每次编码成独立 `SessionEvent` 快照。
- **`Submit` 立即推进局面**：少一次 tick 等待，但同 tick 多条命令的到达顺序与请求结算顺序耦合，无法保证先到先得。放弃，只记录应答，结算固定在 `BeginTick`。

## Consequences

- 会话是权威局面的唯一持有者：`GameSession.State` 同一实例贯穿整场，计分账本随会话生命周期；终局后 `Step` 只推进逻辑时钟。
- 事件顺序可复现：同 seed + 同命令序列两遍得到相同 `StateHash` 与逐字节事件流；Bot 决策不消费随机，随机只来自起庄与发牌。
- 单线程、无锁、无静态可变状态；两个会话可交错 `Step` 且事件 seq 各自从 1 开始。
- 查表/内容目录只存在于组装根与测试支撑；Session 程序集不引用网络/Godot/宿主。
- M0.2 不产 `HandSync`、无点炮/抢杠/回放；重连、观战与回放录制留给 M2/M1。
- 实现与测试落点见 [T-5 规格](../../../../docs/milestones/M0.2/T-5-本地会话.md)，类型契约见 [session.md](../../../../docs/subsystems/session.md)。
