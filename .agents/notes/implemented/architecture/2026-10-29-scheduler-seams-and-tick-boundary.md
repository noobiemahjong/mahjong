# Agent Note: 调度器的 seam 与 tick 事务边界

Status: implemented — 调度器只推进动作树；命令队列、请求注册表、Id 分配与 tick 边界都在它的外部或注入侧。

## Problem

一个 tick 内"外部命令不得中途插入"和"动作流程要能等超时/应答"同时成立，调度器就必须和命令、请求、时间三者划清边界：命令如果自己排空并应用，Flow 会绑上模型校验与事件流；请求如果自己造 id、造结果，超时策略就散落在动作里；Id 如果用静态计数，两个房间会串号。每个 tick 还需要一个可回滚的观察点：动作树在本 tick 内推进到阻塞或结束为止，对外只有"tick 前/后"两个状态。

## Decision

1. `ActionScheduler(IRequestRegistry requests, int maxStepsPerTick = 10_000)`：请求注册表构造注入；调度器每 tick 首步调 `BeginTick` 结算超时/取消，`WaitRequest` 解除时调 `TryConsume`，结果只用一次、不跨 tick 缓存。
2. 命令队列 [CommandQueue](../../../../src/Mahjong.Core/Flow/Commands.cs) 不属于调度器：会话在调用 `Step` 前排空 `DrainForTick(tick)` 并按序应用，调度器完全不感知 `GameCommand`；排序键 `(ReceivedTick, Seq, 入队序)` 保留同一 tick 同一 `Seq` 命令的入队序，让"谁先到"成为确定性事实，而不是排序实现（不稳定排序）的副产物。
3. `Id` 由调度器实例字段从 1 递增；`CreationOrder` 由父节点 `AddChild` 分配；没有静态计数器，两个调度器交错 `Step` 互不影响。
4. `Step(tick)` 先写 `Tick`、无条件调 `BeginTick`，再按固定顺序推进；未 `Start` 或树已结束时只结算注册表并返回。
5. `maxStepsPerTick` 只计 `MoveNext` 次数：第 N 次允许，第 N+1 次发生前抛 `InvalidOperationException`；计数每 tick 复位，升降与阻塞检查不计入。
6. 阻塞与解除：`WaitTicks` 用 `wakeTick = tick + n`（`tick >= wakeTick` 解除，`n = 0` 表示本次先返回）；`WaitUntil` 每次检查重新求值谓词；`WaitRequest` 消费一次结果；解除时清空该动作的等待并在同一 tick 继续推进。阻塞状态按动作持有（存 `GameAction` 私有字段），因此同一时刻可以有多层动作各自等待，只有当前动作的等待参与解除判断：父动作等待期间其已挂载的子动作照常执行，子动作的等待也不会覆盖父的 wakeTick、谓词或请求 id。
7. `CreateAction` 分配 `Id`，`Start` 一次性接管一棵树；`IsRunning` 只表示"是否还有树在推进"，与 `Result` 是否为空无关。

实现见 [ActionScheduler](../../../../src/Mahjong.Core/Flow/ActionScheduler.cs) 与 [Requests](../../../../src/Mahjong.Core/Flow/Requests.cs)。

## Alternatives considered

- **调度器直接持有命令队列并应用命令**：少一次注入，但 Flow 会依赖命令校验与模型变更，违反"命令在 tick 边界由会话应用"的分层，也让 Flow 无法脱离会话单测。
- **让动作 await 任务或真实时间**：可读性好，但引入时钟与线程，tick 语义与确定性丢失，客户端和服务端还要对时。
- **静态 `nextId` 或全局请求表**：实现省事，但跨房间串号、并发不安全，静态状态守卫也会直接判违规。
- **`maxStepsPerTick` 计循环轮次而不是 `MoveNext`**：空转的升降循环（只上收/下探）会先触发上限，异常指向错误方向；契约要求护栏只盯"动作真正执行"的次数。
- **`BeginTick` 只在有运行中动作时调用**：省一次调用，但 tick 推进本身要结算注册表中的超时，与有没有动作树无关。
- **调度器持有一组全局阻塞字段**：少一层访问，但等待旗标会被下探继承：父动作刚挂上待执行子动作后进入等待，子动作会被父的等待拦下，父若等 `child.Completed` 就是死锁；两层同时等待时后进入者还会覆盖前者的 wakeTick、谓词与请求 id。按动作持有把等待变成节点自身状态，与规格 §2 步骤 3（先下探子动作）和步骤 4（再判当前动作阻塞）的顺序一致。
- **把 `Result` 放到会话层**：动作树结束时就要知道结果，放本地让"树结束即有结果"可断言，也避免调度器反向依赖会话。

## Consequences

- 动作协程只表达流程，不碰命令、请求 id 与 tick；会话负责开窗、分配 id、产出结算结果。
- 同一 tick 内动作树推进到阻塞/结束为止；会话在该边界前后分别排空命令与生成事件批次，tick 是唯一的事务边界。
- 父动作等待期间子动作照常推进：动作协程可以在挂子后 `yield return WaitUntil(() => child.Completed)` 这类写法里等待子流程结束，不需要在父子之间传阻塞状态或轮询。
- `WaitRequest` 不缓存结果：结果只被消费一次，重复 `TryConsume` 返回 false，动作不会读到过期应答。
- 两个调度器可以在同一线程交错 `Step`，各自 `Tick`、`Result`、Id 序列独立；这也是多房间隔离的测试口径。
- 相关：[abort/Complete 语义](2026-10-29-action-tree-abort-and-determinism.md) 说明动作树内部的清理与顺序；[状态哈希](2026-10-29-state-hash-byte-layout.md) 说明哈希在事务边界之外计算。
