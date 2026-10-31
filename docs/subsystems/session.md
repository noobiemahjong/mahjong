# 子系统：会话与事件流

> 本页只写类型、语义与契约；总体流程见 [architecture](../architecture.md)，决策理由见 [Note](../../.agents/notes/implemented/feature/2026-10-31-local-game-session.md)，任务口径与验收见 [T-5](../milestones/M0.2/T-5-本地会话.md)。命名空间 `Mahjong.Session`，依赖 `Mahjong.Core`，不引用 Godot/网络/时钟。

## 1. 类型

- `SessionEventKind`：`byte` 枚举，12 种事件；数值是事件流协议常量，payload 字节布局的权威表在 [T-5 §1.1](../milestones/M0.2/T-5-本地会话.md)，本页不复制。
- `SessionEvent`：`readonly record struct`，字段 `Seq`（从 1 单调）、`Tick`（产出逻辑 tick）、`Kind`、`Payload`（`ReadOnlyMemory<byte>`，小端紧排无对齐的只读快照）。
- `SessionEventPayloadReader`：payload 的唯一公开读取入口，12 个 `ReadXxx(SessionEvent)` 返回对应 payload record struct；种类不匹配抛 `ArgumentException`，截断/计数矛盾/非法 UTF-8/名次越界抛 `InvalidDataException`，返回数组是副本。
- 13 个 payload record struct：12 种事件各一个，加 `RequestAnswer`（`RequestClosedPayload.Answer` 的类型，仅 `Answered` 时非 null）。
- `SessionConfig`：`HumanSeat`（0–3 或 null）、`BotCount`、`TickRate`、`TurnTimeoutTicks`、`ResponseTimeoutTicks`；只承载值，合法性在 `GameSession` 构造统一校验。
- `SubmitResult`：`Accepted`、`Reason`（人类可读短句、非协议）、`RequestId`（接受时为被应答请求 id，拒绝为 0）。
- `GameSession`：`sealed partial class`，是权威会话，也是 `IRequestRegistry` 与 `ISessionRequestSink` 的实现者；公开面是 `State`、`Tick`、`IsStarted`、`IsGameOver`、`ActiveRequests`、`Start`、`Step(long)`、`Submit(GameCommand)`、`DrainEvents(byte)`。
- `LocalSessionFactory`：`static` 组装根，`Create`/`CreateSinglePlayer`/`CreateFourBots` 返回未启动的 `GameSession`。
- 命令、请求与响应类型复用 `Mahjong.Core.Flow`（`GameCommand`、`CommandVerb`、`AllowedVerbs`、`RequestKind`、`PendingRequest`、`RequestOutcome`、`RequestOutcomeKind`），Session 不重复定义。

## 2. 生命周期

- 构造：校验配置与依赖，装配局面、计分账本、校验器、Bot 策略、事件缓冲、请求登记表与调度器；不启动、不产事件、不推进 tick。依赖任一 null 抛 `ArgumentNullException`；配置非法抛 `ArgumentException`/`ArgumentOutOfRangeException`（`ParamName` 为 `config`）。
- 配置合法组合只有 `(null, 4)` 与 `(0–3, 3)`；`TickRate`/`TurnTimeoutTicks`/`ResponseTimeoutTicks` 均至少 1。
- `Start()`：只允许一次，二次调用抛 `InvalidOperationException`；立即按座位 0–3 产出 `SeatStateChanged`（真人 Online，其余 Bot），创建单根动作协程交给调度器，不推进流程。
- `Step(tick)`：未启动抛 `InvalidOperationException`；`tick` 不大于当前 `Tick` 抛 `ArgumentOutOfRangeException`；`GameOver` 后只推进逻辑时钟（不改局面、不产事件、不处理请求）。
- `Submit(command)`：只记录应答，不推进局面、不产事件、不推进 tick；任何非法输入/状态返回 `false` + 非空 `Reason`，不抛异常。
- `DrainEvents(viewerSeat)`：破坏性单消费者读取，同一事件不重复返回；合法 viewer 为 0–3 或 255，其他值抛 `ArgumentOutOfRangeException`（参数校验先于状态检查）；未启动或已排空返回空列表。
- 单线程：Step/Submit/DrainEvents 不加锁、不跨线程；实例之间零共享可变状态，无静态可变字段。

## 3. 事件流

- `SessionEventBuffer`（internal）在 `Emit` 时分配 `Seq` 并保留 `Tick`，在 `Drain` 时按 viewer 编码 payload 并清空缓冲；因此可见性过滤发生在 drain 侧，事件产生侧不保存 per-viewer 游标。
- 可见性只作用于 `Deal`/`HandSync`/`Drew`：viewer 等于该事件座位或 255 时给出真实牌 id，其他座位只得到 `count`（Deal/HandSync）或 `tileId = -1`（Drew）；其余事件全量可见，暗杠不隐藏。
- `Discarded.Slot` 是**弃牌区槽位**（出牌追加完成后的下标），不是手牌槽位；`TileId` 是物理牌 id。
- `HandSync` 的枚举与读写保留，M0.2 流程不产出该事件。
- 事件顺序：同一 tick 内 `IRequestRegistry.BeginTick` 结算产出的 `RequestClosed` 先于动作协程恢复后产出的 `Discarded`/`Meld`/`Drew`；`RoundEnded` 在 `Release` 与镜像写回之后，最后 `RoundEnded` 之后同 tick 产出 `GameEnded`。
- `RoundStarted.wallCount` 是发牌后牌墙张数；`Drew.wallCount` 是摸牌后张数；`TurnStarted` 只在该座位从墙头摸牌开始新回合时产出。

## 4. 请求

- 单座请求用 `SessionRequestRegistry.OpenSingle` 打开：`RequestKind.Win`（摸牌后可自摸，allowed = Win|Pass）或 `RequestKind.Discard`（出牌；自己回合且杠合法时 allowed = Discard|Kong）；请求 id 由登记表从 1 单调分配，关闭时按 id 升序结算。
- 弃牌响应窗口用 `OpenGroup` 打开：`RequestKind.Pong`、allowed 恒为 Pong|Kong|Pass，候选按座位升序、按候选顺序占用连续请求 id；候选为空返回 null 且不分配 id。仲裁复用 Core 的 `ResponseWindow`：杠 > 碰 → arrivalTick 升序 → 座位升序。
- `SessionRequestRegistry`（internal，实现 `IRequestRegistry`）持有请求与结果；单座结算结果 `TakeOutcome` 与接口 `TryConsume` 共享一次性消费状态（`TryConsume` 失败返回 false）；`TakeOutcome` 未结算/重复消费抛 `InvalidOperationException`；响应窗口结果用 `TakeGroupResult` 取快照（窗口关闭后可重复取同一快照）。
- `BeginTick` 固定顺序：先按请求 id 升序结算单座请求（已应答 → Answered，到 deadline → TimedOut），再关闭「全体应答或到 deadline」的响应窗口并按候选顺序逐座上报 `RequestClosed`；每个请求只产出一个结果。
- 单座请求 `RequestOutcome.Payload` 是 boxed `GameCommand` 或 null；`Submit` 成功只标记应答，实际结算发生在下一次 `BeginTick`。
- `ActiveRequests` 只含未结算请求、按 requestId 升序；无请求时返回空只读列表。

## 5. 流程

- 单根动作协程 `RunGameLoop` 是 8 局线性状态机，接受 `ActionScheduler` 的 `WaitUntil` 等待；流程不用子动作树、不用 abort。
- 每局：第 1 局用 `rng.NextUInt() % 4` 抽庄并消费一次随机源，之后由轮换规则决定庄家；`Deal` 后 `Wall` 84、四家 13、`CurrentSeat` = 庄家，产 `RoundStarted` 与 4 条 `Deal`，庄家摸第 14 张（`Wall` 83）并产 `TurnStarted`/`Drew`。
- 自己回合：可自摸/七对时先开 Win 请求，应答 Win 即结算本局；否则开 Discard 请求，合法杠位扩展 allowed；应答 Discard 或超时自动出牌后产 `Discarded`，应答 Kong 则 Commit 杠、产 `Meld`、记杠分、补牌并产 `Drew`、继续同一回合。
- 弃牌响应：只处理碰/明杠；有候选就开窗等待，胜者 Commit 后产 `Meld`；碰后强制出牌（allowed = Discard、不产 `TurnStarted`），明杠记杠分并补牌。
- 无人响应：下家摸牌开始新回合；摸牌前牌墙为空即流局，不产下家的 `TurnStarted`。
- 结算：自摸先取样 delta 与 actionType，`Release` 后写回 `PlayerState.Score` 镜像，再用公开 `RoundResult` 构造器产 `RoundEnded`；流局 `RollbackRound` + `Release`（净 0）、产 `RoundEnded(-1, 全 0, None)`。赢家坐庄、流局庄家不变。
- 8 局后产 `GameEnded`（总分、`RoundRanking.Rank` 名次、空 replayId），`IsGameOver` 置真；之后 Step 只更新 tick。
- 回合之间无 Ready 门：`RoundEnded` 后同一 tick 立即开下一局；整场结束即流程终止。
- 超时托管：Discard/Win 请求到 deadline 未应答时，Discard 自动打「手牌槽位升序第一张通过校验器的牌」，Win 按 Pass；响应窗口到 deadline 时未应答者 TimedOut，已提交者按仲裁键结算。
- Bot：非真人座位在请求打开后 `max(1, ceil(TickRate × 300 / 1000))` 个 tick 提交（10Hz = 3 tick），走与人类相同的 `Submit` 路径，决策被拒视为 bug 抛 `InvalidOperationException`；多个到期请求按请求 id 升序处理。

## 6. 接缝

- **Core.Flow**：`ActionScheduler` 每 tick 先调 `IRequestRegistry.BeginTick` 再推进动作树；`GameSession` 把 `BeginTick`/`TryConsume` 转发给 `SessionRequestRegistry`，把请求开闭事件写入 `SessionEventBuffer`。
- **Core.Rules**：`RoundResult` 构造器公开，供会话构造结算 payload；`RoundRanking.Rank` 是终局名次唯一实现；`ScoreManager.ApplySevenPairsSelfDrawn` 只结算七对、不检查其他家的杠位，`ApplySelfDrawn` 的 `kongUpFlower` 由会话按「赢家本回合最近一次摸牌是否杠后补牌」传入。
- **组装根**：`LocalSessionFactory` 负责内容目录解析后的 `ContentCatalog`、`TableCatalog` → `WinChecker`、`Pcg32` 装配；`GameSession` 构造只接收 `ContentCatalog`、`IWinChecker`、`IRandomSource`，不持有 `TableCatalog`、不读文件系统。
- **宿主**：宿主按固定 tick 调 `Step` 并消费 `DrainEvents`；M0.2 没有网络、回放录制与重连，`HandSync` 不产出。
- 会话内部的 `SessionEventBuffer`、`SessionRequestRegistry`、`ISessionRequestSink` 是程序集 internal，测试通过 `InternalsVisibleTo` 直接使用。
