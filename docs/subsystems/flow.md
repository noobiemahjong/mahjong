# 子系统：Action 树、调度、命令与状态哈希

> 本页只写类型、语义与契约；流程见 [architecture](../architecture.md)，决策理由见 [.agents/notes](../../.agents/notes/README.md)。命名空间 `Mahjong.Core.Flow`，不引用 Godot/网络/时钟。

## 1. 动作状态

| 状态 | 语义 |
|---|---|
| `Continue` | 本 tick 继续执行当前动作 |
| `WaitTicks` | 等待若干逻辑 tick，`wakeTick = tick + n` |
| `WaitUntil` | 每 tick 求值谓词一次，为真后继续 |
| `WaitRequest` | 等待请求结算（应答/超时/取消） |
| `AbortParents` | 向上中断至多 `Depth` 层祖先，然后继续当前动作 |
| `AbortSiblings` | 中断当前动作的全部兄弟，然后继续当前动作 |
| `Complete` | 以 `Result` 终止整棵动作树 |

- `ActionStatus : byte` 取值为 0–6，顺序与上表一致。
- `ActionState` 只表达一个状态，未用字段取默认值（0/null）；谓词每次被调度器访问时重新求值、不缓存；不提供值相等性。
- 工厂契约：`WaitTicks(ticks)` 的 `ticks` 为负抛 `ArgumentOutOfRangeException`；`WaitUntil(predicate)` 的谓词为 null 抛 `ArgumentNullException`；`AbortParents(depth)` 的 `depth` 小于 1 抛 `ArgumentOutOfRangeException`；`Complete(result)` 把结果写入 `ActionScheduler.Result`。

## 2. 动作树

- `GameAction` 只能由 `ActionScheduler.CreateAction` 创建，外部无法构造，避免绕过 `Id` 分配。
- `Id` 在调度器实例内从 1 递增；`CreationOrder` 是同父下的创建序，由 `AddChild` 从 0 分配，改挂后重新分配，成为根时为 0。
- `Name` 只用于排障；`Priority` 参与同父子动作排序；`Interruptible` 只是标记，调度器不据此改变推进逻辑。
- 阻塞状态按动作节点持有（`WaitTicks` 的 `wakeTick`、`WaitUntil` 的谓词、`WaitRequest` 的请求 id），同一时刻多层动作可以各自等待，只有当前动作的等待参与解除判断。
- `AddChild(child)`：child 为 null 抛 `ArgumentNullException`；是自身、已有父动作或已完成抛 `InvalidOperationException`，失败时父子两侧都不变。
- `Completed` 由调度器维护；`Routine` 在被中断或以 `Complete` 收尾时由调度器 Dispose 至多一次；协程自然结束不 Dispose。

## 3. 调度器

- `ActionScheduler(IRequestRegistry requests, int maxStepsPerTick = 10_000)`：`requests` 为 null 抛 `ArgumentNullException`，`maxStepsPerTick` 小于 1 抛 `ArgumentOutOfRangeException`。
- `Start(root)`：已有动作树在运行时或 `root.Completed` 为 true 抛 `InvalidOperationException`，`root.Parent` 非空抛 `ArgumentException`；本方法只接管一棵树，不推进。
- `Step(tick)` 先写 `Tick`、无条件调 `requests.BeginTick(tick)` 结算超时/取消，再推进动作树；未 `Start` 或树已结束时只结算注册表就返回；`Tick` 只来自参数，调度器不读时钟。
- 单 tick 循环顺序：当前动作已完成则上收到父动作（根已完成即整棵树结束）；有未完成子动作则进入优先级最高者（同优先级取 `CreationOrder` 最小），本动作本 tick 不执行；当前动作阻塞且未解除则返回；否则 `MoveNext` 并按状态处理。
- 解除规则只针对当前动作自己的等待：`WaitTicks` 要求 `tick >= wakeTick`，`WaitUntil` 要求谓词为真，`WaitRequest` 要求 `TryConsume` 成功；解除后清空等待并在同一 tick 继续推进。
- `AbortParents(depth)`：由近及远中断至多 `depth` 个祖先（标记完成、清空子队列、Dispose 子树但跳过当前分支），把当前动作挂到最后一个被中断祖先的父节点，没有则成为新的根，然后继续。
- `AbortSiblings`：中断并移出当前动作的全部兄弟（含子树），当前动作与父动作保留，然后继续。
- `Complete(result)`：写 `Result`、把整棵树标记完成并 Dispose 各枚举器、`IsRunning` 置 false；根协程自然结束不写 `Result`。
- `maxStepsPerTick` 只计 `MoveNext` 次数，每 tick 复位；超过上限抛 `InvalidOperationException`（死循环属 bug）。`IsRunning` 只表示还有树在推进，与 `Result` 是否为空无关。

## 4. 命令

- `CommandVerb : byte` 取 1–5（Discard/Pong/Kong/Win/Pass）；`AllowedVerbs : ushort` 是位集（None 0、Discard 1、Pong 2、Kong 4、Win 8、Pass 16），命令与请求共用，数值是协议常量。
- `GameCommand(long Seq, long ReceivedTick, byte Seat, CommandVerb Verb, int Slot, ushort CardId, long RequestId)`：`Seq` 是同一连接内的命令序号，`RequestId` 只对应答命令非 0，`CardId` 无牌时为 0，`Slot` 语义由动词决定。
- `CommandQueue.Enqueue` 只追加、保留入队序；`DrainForTick(tick)` 移除并返回 `ReceivedTick <= tick` 的命令快照，按 `(ReceivedTick, Seq, 入队序)` 升序，无命令返回空列表；同一命令只返回一次，后续入队不影响已返回快照。
- 队列只做缓冲与排序，不做合法性校验；实例非线程安全。调度器不感知命令，排空与应用的时机由调用方在 `Step` 前决定。

## 5. 请求

- `IRequestRegistry` 两个方法：`BeginTick(tick)` 把到期未应答的请求写成超时、把被取消的请求写成取消；`TryConsume(requestId, out outcome)` 同一结算只成功一次。
- `RequestOutcomeKind : byte` 取 Answered 0、TimedOut 1、Cancelled 2；`RequestOutcome(long RequestId, RequestOutcomeKind Kind, object? Payload)` 是一次结算。
- `RequestKind : byte` 取 Discard 0、Pong 1、Kong 2、Win 3、Pass 4；`PendingRequest(long RequestId, byte Seat, RequestKind Kind, AllowedVerbs Allowed, long DeadlineTick)` 是待结算请求。
- `RequestGroup(long requestGroupId, IEnumerable<PendingRequest> requests)` 是同一时刻向多个座位征询的只读快照，构造时复制传入集合、顺序保持；仲裁与产出结算由会话负责。

## 6. 触发器与事件参数

- `Trigger` 是抽象基类：`Priority` 默认 10（数值大的先），`Run(GameEventArgs)` 返回产出 `ActionState` 的协程；调度器不派发触发器，也不持有触发器注册表。
- `GameEventArgs(GameState state, object? payload = null)`：`State` 是触发时的权威局面（只读不改），`Payload` 语义由事件种类决定；`state` 为 null 抛 `ArgumentNullException`。

## 7. 状态哈希

`StateHash.Compute(GameState)` 把模型字段按固定布局编码后取 SHA-256，返回 64 字符小写 hex；整型小端、长度前缀 `u16`、布局版本写在首字节。

> **已知偏差**：`tailCount` 段属于待修正的 `WallTail` 模型；单牌墙由 [M0.2 T-1b](../../docs/milestones/M0.2/T-1b-单牌墙与发牌修正.md) 落地，背景见 [proposed Note](../../.agents/notes/proposed/architecture/2026-10-30-single-wall-no-reserved-tail.md)。

```
u8  version = 1
u16 wallCount + wallCount × u16 tileId        // 槽位顺序，不排序
u16 tailCount + tailCount × u16 tileId
u8  playerCount (= 4)
per seat:
  u8  seat
  u8  isBanker (0/1)
  i32 score                                   // short 提升为 int
  u16 handCount + handCount × u16 tileId
  u8  meldCount
  per meld: u8 kind, u8 opened(0/1), u8 targetSeat(255=无), u8 tileCount, tileCount × u16 tileId
  u16 discardCount + discardCount × u16 tileId
u8  currentSeat
u16 roundNo
```

- 只哈希模型字段：不含时间、会话、日志、内容清单与规则版本；动作树与请求状态不参与。
- 先按布局量出字节数再一次性分配写入；`state` 为 null 抛 `ArgumentNullException`；同一实现同输入必得同一 hex，跨实现对齐以本布局为准。
