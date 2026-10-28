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

public interface IRequestRegistry
{
    bool TryConsume(long requestId, out RequestOutcome outcome);
}
public readonly record struct RequestOutcome(long RequestId, bool TimedOut, object? Payload);

public sealed class ActionScheduler
{
    public ActionScheduler(IRequestRegistry requests, int maxStepsPerTick = 10_000);
    public bool IsRunning { get; }
    public object? Result { get; }
    public void Start(GameAction root);
    public void Step(long tick);
}
````

`GameEventArgs` 本阶段放 `GameState State` 与 `object? Payload`；M0.2 再扩展。

### 1.1 动作/命令/请求类型（必须定义）

```csharp
public sealed class GameAction
{
    public long Id { get; }
    public string Name { get; }
    public int Priority { get; }
    public GameAction? Parent { get; }
    public IEnumerator<ActionState> Routine { get; }
    public bool Completed { get; internal set; }
    public void AddChild(GameAction child);
    public bool Interruptible { get; }
}

public enum CommandVerb : byte { Discard = 1, Pong = 2, Kong = 3, Win = 4, Pass = 5 }
public readonly record struct GameCommand(long Seq, long ReceivedTick, byte Seat, CommandVerb Verb, int Slot, ushort CardId, long RequestId);
public sealed class CommandQueue
{
    public void Enqueue(GameCommand command);
    public IReadOnlyList<GameCommand> DrainForTick(long tick);
}

public enum RequestKind : byte { Discard, Pong, Kong, Win, Pass }
public readonly record struct PendingRequest(long RequestId, byte Seat, RequestKind Kind, ushort AllowedVerbs, long DeadlineTick);
public sealed class RequestGroup
{
    public IReadOnlyList<PendingRequest> Requests { get; }
    public long RequestGroupId { get; }
}
```

> 根动作由 Flow 构造，子动作由 `AddChild` 挂载。

## 2. Step 语义（必须按此实现）

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
   - `AbortParents(depth)`：向上 abort 至多 depth 个 ancestor（不含 current），current 继续。
   - `AbortSiblings`：清空 parent 的待执行子队列，current 继续。
   - `Complete(result)`：`Result=result`；abort 整棵树，标记完成，结束。
7. 每 tick 步数超过 `maxStepsPerTick` → 抛 `InvalidOperationException`（防止死循环，属 bug）。

约束：

- 子动作优先级降序、同级按创建序号升序，保证确定性。
- 所有 abort 必须清理该动作的子队列；已完成动作不得再执行。
- 引擎不 sleep、不读墙钟、不分配每 tick 的大对象；谓词/请求结果不缓存跨 tick 的旧值。
- `Start` 时若已有运行中动作 → 抛 `InvalidOperationException`。

## 3. 状态哈希

`Mahjong.Core.Flow.StateHash.Compute(GameState)` → SHA-256 小写 hex。规范字节序：

1. 版本号 1；
2. 牌墙/牌尾按槽位顺序的 tile id；
3. 每个玩家：座位、手牌 id、副露（kind/opened/target/ids）、弃牌 id、分数、是否庄家；
4. 当前座位、局数。

只哈希模型字段；不哈希时间、Session、日志。两次同输入哈希必须一致。

## 4. 无静态状态

测试用反射扫描 `Mahjong.Core` 与 `Mahjong.Session`：允许 `const` 与 `static readonly` 的不可变值/委托（如 `ActionState.Continue`），禁止 static 非 readonly、static 可变集合/缓存；需要缓存时放实例字段或注入。
- 本任务依赖 T-4 的 `GameState`，因此 master 的并行建议改为 T-5 在 T-4 之后。

## 5. 测试与微基准

- Action 状态：Continue/WaitTicks/WaitUntil/WaitRequest/AbortParents/AbortSiblings/Complete 各一例。
- 顺序：同 tick 多子动作按优先级；同优先级按创建序。
- 中断：abort 后子动作不再执行；Complete 后结果正确且整棵树停止。
- 确定性：同一脚本跑两遍状态哈希一致；两个调度器交错 Step 不串状态。
- 微基准：一个 1,000,000 步的简单循环，记录 ns/step（Stopwatch，3 次取中位数）。

## 实施记录

开工后在此追加：日期、实际改动、验证命令与结果、偏差/后续。
