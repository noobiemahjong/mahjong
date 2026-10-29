# T-5 tick 引擎（实施规格）

状态：见任务表
依赖：T-1、T-4（`GameState`/模型）
对应 master：[M0.1](../M0.1-工程底座与内核骨架.md)
验收：Action 状态语义、调度顺序、请求/中断、状态哈希、无静态状态、微基准全部通过。

## 1. 命名空间与 API

`Mahjong.Core.Flow`，不引用 Godot/网络/真实时钟。

````csharp
public enum ActionStatus : byte
{
    Continue, WaitTicks, WaitUntil, WaitRequest,
    AbortParents, AbortSiblings, Complete,
}

public readonly struct ActionState
{
    public ActionStatus Status { get; }
    public int Ticks { get; }
    public Func<bool>? Predicate { get; }
    public long RequestId { get; }
    public int Depth { get; }
    public object? Result { get; }

    public static ActionState Continue { get; }
    public static ActionState WaitTicks(int ticks);
    public static ActionState WaitUntil(Func<bool> predicate);
    public static ActionState WaitRequest(long requestId);
    public static ActionState AbortParents(int depth = 1);
    public static ActionState AbortSiblings { get; }
    public static ActionState Complete(object? result = null);
}

public abstract class Trigger
{
    public virtual int Priority => 10;
    public abstract IEnumerator<ActionState> Run(GameEventArgs args);
}

public enum RequestOutcomeKind : byte { Answered = 0, TimedOut = 1, Cancelled = 2 }

public interface IRequestRegistry
{
    void BeginTick(long tick);                              // 推进 deadline，把超时/取消写成结果
    bool TryConsume(long requestId, out RequestOutcome outcome);
}
public readonly record struct RequestOutcome(long RequestId, RequestOutcomeKind Kind, object? Payload);

public sealed class ActionScheduler
{
    public ActionScheduler(IRequestRegistry requests, int maxStepsPerTick = 10_000);
    public bool IsRunning { get; }
    public object? Result { get; }
    public long Tick { get; }
    public GameAction CreateAction(string name, IEnumerator<ActionState> routine,
        int priority = 10, bool interruptible = true);   // 分配 Id 与同父创建序
    public void Start(GameAction root);
    public void Step(long tick);
}
````

`GameEventArgs` 本阶段放 `GameState State` 与 `object? Payload`；M0.2 再扩展。

### 1.1 动作/命令/请求类型（必须定义）

```csharp
public sealed class GameAction   // 只能由 ActionScheduler.CreateAction 创建
{
    public long Id { get; }
    public long CreationOrder { get; }      // 同父下的创建序，由 AddChild 分配
    public string Name { get; }
    public int Priority { get; }
    public GameAction? Parent { get; }
    public IEnumerator<ActionState> Routine { get; }
    public bool Completed { get; internal set; }
    public bool Interruptible { get; }
    public void AddChild(GameAction child);  // child.Parent = this；创建序递增
}

public enum CommandVerb : byte { Discard = 1, Pong = 2, Kong = 3, Win = 4, Pass = 5 }
[Flags] public enum AllowedVerbs : ushort
{
    None = 0, Discard = 1, Pong = 2, Kong = 4, Win = 8, Pass = 16,
}
public readonly record struct GameCommand(long Seq, long ReceivedTick, byte Seat, CommandVerb Verb, int Slot, ushort CardId, long RequestId);
public sealed class CommandQueue  // 由 Session 在 tick 边界排空并应用；调度器不感知命令
{
    public void Enqueue(GameCommand command);
    public IReadOnlyList<GameCommand> DrainForTick(long tick);  // ReceivedTick <= tick，按 (ReceivedTick, Seq) 升序
}

public enum RequestKind : byte { Discard, Pong, Kong, Win, Pass }
public readonly record struct PendingRequest(long RequestId, byte Seat, RequestKind Kind, AllowedVerbs Allowed, long DeadlineTick);
public sealed class RequestGroup
{
    public IReadOnlyList<PendingRequest> Requests { get; }
    public long RequestGroupId { get; }
}
```

> 根动作由 Flow 用 `CreateAction` 创建、`AddChild` 挂载子树；`Id`/创建序由调度器实例分配，禁止静态计数器。
> `AllowedVerbs` 定义在 `Mahjong.Core.Flow`（命令与请求共用），M0.2 的 Session 直接引用，不重复定义。
> `RequestGroup` 只是请求快照；开窗、分配 requestId、产出 outcome 由 Session（M0.2 T-2/T-5）负责，调度器只消费 `TryConsume`。
> `Trigger` 本阶段只是类型占位：M0.1 不实现触发器派发，M0.2 的回合树直接构造动作。

## 2. Step 语义（必须按此实现）

`Step(tick)` 固定顺序：① `requests.BeginTick(tick)` 结算超时/取消；② 推进 Action 树（下面的循环）；③ 返回。命令由 Session 在调用 `Step` 前排空并应用，调度器不感知命令。

单 tick 内循环，直到阻塞/完成：

1. 无 current → 结束。
2. current 已完成且有 parent → current = parent，继续。
2.1 已完成动作不得再执行；abort 时必须清空其子队列与阻塞状态。
3. current 有子动作 → 取优先级最高（同优先级按创建序号）→ current = 子，继续。
4. 若 current 处于阻塞：
   - `WaitTicks`：`tick < wakeTick` 则返回；否则解除，继续。
   - `WaitUntil`：谓词 false 则返回；true 解除，继续。
   - `WaitRequest`：`TryConsume` false 则返回；true 解除，继续。
5. `MoveNext()` 返回 false → 标记完成，继续（回到 2）。
6. 处理 `ActionState`：
   - `Continue`：继续循环。
   - `WaitTicks(n)`：`wakeTick = tick + n`，返回。
   - `WaitUntil(p)`：存谓词，返回。
   - `WaitRequest(id)`：存 id，返回。
   - `AbortParents(depth)`：从 current 向上取至多 depth 个祖先，逐个「标记 Completed + 清空待执行子队列 + Dispose 枚举器」；current 改挂到最后一个被 abort 祖先的父节点（无则成为根），然后 current 继续。
   - `AbortSiblings`：只清空 current 的所有兄弟（Completed、清空子队列、Dispose 枚举器），current 与 parent 保留，current 继续。
   - `Complete(result)`：`Result=result`；整棵树标记完成（Completed、清空子队列、Dispose 枚举器），结束本次 Step 循环。
7. 每 tick 步数超过 `maxStepsPerTick` → 抛 `InvalidOperationException`（防止死循环，属 bug）。

约束：

- 子动作优先级降序、同级按创建序号升序，保证确定性。
- 所有 abort 必须清理该动作的子队列；已完成动作不得再执行。
- abort/Complete 必须对受影响动作的 `IEnumerator` 调用 `Dispose()`（`finally` 必须执行），并清空阻塞状态（wakeTick/谓词/requestId）。
- 父被 abort 后，其子动作不再指向它（改为挂到被 abort 祖先的父节点），保证 `current = parent` 不会落回已完成动作。
- 引擎不 sleep、不读墙钟、不分配每 tick 的大对象；谓词/请求结果不缓存跨 tick 的旧值。
- `Start` 时若已有运行中动作 → 抛 `InvalidOperationException`。

## 3. 状态哈希

`Mahjong.Core.Flow.StateHash.Compute(GameState)` → SHA-256 小写 hex。字节布局（整型小端，长度前缀 `u16`）：

````
u8  version = 1
u16 wallCount + wallCount × u16 tileId        // 按槽位顺序
u16 tailCount + tailCount × u16 tileId
u8  playerCount (=4)
per seat:
  u8  seat
  u8  isBanker (0/1)
  i32 score
  u16 handCount + handCount × u16 tileId
  u8  meldCount
  per meld: u8 kind, u8 opened(0/1), u8 targetSeat(255=无), u8 tileCount, tileCount × u16 tileId
  u16 discardCount + discardCount × u16 tileId
u8  currentSeat
u16 roundNo
````

只哈希模型字段；不哈希时间、Session、日志、contentHash、rulesVersion。同一实现同输入必须同哈希；跨实现对齐以本布局为准。

## 4. 无静态状态

测试用反射扫描 `Mahjong.Core` 与 `Mahjong.Session`：允许 `const`、`static readonly` 的不可变值/委托（如 `ActionState.Continue`）与只读查找表；禁止 static 非 readonly、static 可变集合/缓存；需要缓存时放实例字段或注入。
- `Id`/创建序/requestId 一律由调度器或注册表实例分配，禁止静态计数器。

## 5. 测试与微基准

- Action 状态：Continue/WaitTicks/WaitUntil/WaitRequest/AbortParents/AbortSiblings/Complete 各一例。
- 顺序：同 tick 多子动作按优先级；同优先级按创建序。
- 中断：abort 后子动作不再执行；Complete 后结果正确且整棵树停止。
- 确定性：同一脚本跑两遍状态哈希一致；两个调度器交错 Step 不串状态。
- 微基准：`maxStepsPerTick` 调到足够大，构造只有 `Continue` 的简单动作树，跑 1,000,000 次 `MoveNext`，记录 ns/step（Stopwatch，3 次取中位数）。载体是 `Mahjong.Tests` 里 `[Trait("Category","Benchmark")]` 的用例，命令 `dotnet test --filter Category=Benchmark -l "console;verbosity=detailed"`，结果写进 M0.1 结果。

## 实施记录

开工后在此追加：日期、实际改动、验证命令与结果、偏差/后续。
