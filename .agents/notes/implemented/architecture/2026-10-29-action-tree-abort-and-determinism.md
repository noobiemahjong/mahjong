# Agent Note: 动作树的 abort/Complete 语义与调度确定性

Status: implemented — abort/Complete 是返回值而非异常，整棵树的清理、枚举器 Dispose 与执行顺序都由调度器单点决定。

## Problem

动作树要表达"当前动作作废祖先或兄弟"和"以结果结束整局"，两者都会让一批动作不再执行。若用异常做控制流，`finally` 与半改状态交错、日志缺段、回放难对齐；若由每个动作自己清理子树，父子两侧容易各清一半，出现"已完成但未 Dispose"或"重复 Dispose"的节点。同一父节点下有多个子动作时，执行顺序还必须与机器、插入时机无关，否则状态哈希与回放会分叉。

## Decision

1. `ActionStatus` 七态由动作协程 `yield return` 声明；调度器在 `Step` 内单点解释，动作协程不直接改父子结构。
2. `Complete(result)` 写 `ActionScheduler.Result`、把整棵树（含根与当前动作）标记 `Completed`、清空所有子列表、对每个尚未 Dispose 的枚举器 Dispose 一次、置 `IsRunning=false`，本次 `Step` 结束。
3. `AbortParents(depth)` 由近及远处理至多 `depth` 个祖先：逐个标记完成、清理其子树（跳过当前动作分支）、从各自父节点移除；处理完把当前动作挂到最后一个被中断祖先的父节点（经 `AddChild` 重新分配创建序），没有则成为新根且创建序归 0。
4. `AbortSiblings` 清理当前动作父节点下除当前之外的全部子节点，当前动作与父节点保留。
5. 被清理子树里的每个节点都标记 `Completed` 并 Dispose 枚举器各一次；`GameAction` 用内部标记实现"恰好一次"，不依赖枚举器自身 Dispose 的幂等性。
6. abort/Complete 清空当前动作的阻塞状态（wakeTick、谓词、请求 id）；已完成动作不再 `MoveNext`，`current` 只沿父链上收。
7. 子动作选择跳过 `Completed`，按 `Priority` 降序、同优先级 `CreationOrder` 升序；`CreationOrder` 由父节点 `AddChild` 单调分配，改挂后拿新序，后挂的子树不会打乱已有顺序。
8. 根动作协程自然结束（`MoveNext` 返回 false 且无父）时 `IsRunning=false`、`current=null`；`Result` 只在 `Complete` 写入。

实现见 [ActionScheduler](../../../../src/Mahjong.Core/Flow/ActionScheduler.cs) 与 [GameAction](../../../../src/Mahjong.Core/Flow/GameAction.cs)。

## Alternatives considered

- **异常终止（旧引擎的 `GameOverException` 做法）**：调用点少，但流程中断变成栈展开，`finally` 执行顺序不可控，被 `catch` 后状态半改，回放无法重演。
- **每个动作自带清理回调**：责任分散，父子两侧都可能清理同一子树，调度器仍要判重，复杂度只增不减。
- **abort 时保留旧 `CreationOrder`**：重挂后可能与新父节点已有子动作的序冲突，选择顺序退化成"看挂载时机"，确定性丢失。
- **按子列表顺序执行**：省一次比较，但重挂与插挂会让顺序隐式变化，测试与回放难以复现。
- **abort 后只移除、不标记 `Completed`**：被移除节点不可达，逻辑上也安全，但"已完成 ⇔ 不会再执行"的不变式出现反例，排障时无法用单一字段判断节点生死。

## Consequences

- 动作协程可以在 `try/finally` 里做清理：abort 与 Complete 都会让受影响枚举器的 `finally` 执行一次，测试用自定义计数枚举器验证 Dispose 次数。
- 中断顺序固定为"由近及远"；`depth` 大于祖先层数时取到根为止，等价于中断整条祖先链。
- 重挂把创建序让给新父节点，因此 abort 之后同一父节点下的执行顺序由挂载时刻决定；这是确定性的来源，也是排障要看 `CreationOrder` 而不是 `Id` 的原因。
- 树的生命周期只有两个出口：`Complete` 给结果，根自然结束不给结果；两者都置 `IsRunning=false`，之后的 `Step` 只结算请求注册表。
- 相关：[调度器 seam](2026-10-29-scheduler-seams-and-tick-boundary.md) 说明命令队列与请求注册表为何不进动作树；[状态哈希](2026-10-29-state-hash-byte-layout.md) 说明动作树不参与哈希。
