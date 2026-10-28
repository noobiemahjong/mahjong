# GameEventEngine 参考

来源：`E:/learn/GameEventEngine/`（.NET 7 控制台原型，ABP + UniTask）。
用途：tick 引擎（T-5）的**概念参考**；不是本仓依赖，也不照抄实现。

## 结构

- `Actions/GameAction.cs`：父子动作树节点；持有 `IEnumerator<ActionState>`、parent、子队列、完成/中断标记。
- `Actions/ActionState.cs`：状态类型 `None / WaitTask / AbortParents / AbortSiblings`；`AbortParents` 带谓词。
- `Actions/GameActionScheduler.cs`：单 current 指针 + 子队列；循环 MoveNext，遇 WaitTask 就 await，处理 abort；有 `MaximumActionLevel` 护栏。
- `Triggers/`：`Trigger.Run` 返回 `IEnumerator<ActionState>`；`GameEvent` 按名字判等；`GameEventArgs` 携带 Game/Source/Targets。
- `Games/Game.cs`：注册触发器、`Emit` 调度、示例伤害链；单例。

## 可借鉴

1. 父子 Action 树 + 子队列：适合「技能/响应」嵌套流程。
2. `AbortParents / AbortSiblings`：用中断状态替代异常回溯，比抛异常更适合 tick。
3. 动作命名与层级：排障时能打印整条链。

## 不要照搬

| 原型做法 | 新设计 |
|---|---|
| ABP + HostedService + UniTask | 纯 C# 类库；tick 驱动，无 await/真实时间 |
| `Game` 单例 + 静态上下文 | `GameState` + `ActionScheduler` 实例；每房间独立 |
| `WaitTask` 等异步任务 | `WaitTicks / WaitUntil / WaitRequest`（由 tick/请求注册表驱动） |
| 无优先级，queue 顺序即顺序 | 子动作优先级降序 + 创建序号，保证确定性 |
| 无状态哈希/无请求模型 | 每 tick 状态哈希；`PendingRequest` 显式建模 |
| 控制流靠异常 | `ActionState.Complete(result)`；异常只留给违反不变式 |

## 与 T-5 的映射

| 原型 | 新设计 |
|---|---|
| `GameAction` | `GameAction`（增加 Id/Priority） |
| `ActionState.None` | `ActionState.Continue` |
| `WaitTask` | `WaitTicks / WaitUntil / WaitRequest` |
| `AbortParents` | `AbortParents(depth)` |
| `AbortSiblings` | `AbortSiblings` |
| — | `Complete(result)`（新增） |

证据：直接读上述文件；本仓不复刻代码。T-5 的验收以 [tick 引擎规格](../milestones/M0.1/T-5-tick引擎.md) 为准。
