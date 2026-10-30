# Architecture（当前实现地图）

> 读代码前先读本页：组成、tick 循环、seam、扩展点。类型与契约见 [subsystems/core.md](subsystems/core.md)、[subsystems/flow.md](subsystems/flow.md) 与 [subsystems/scoring.md](subsystems/scoring.md)；为什么这么定见 [.agents/notes/](../.agents/notes/README.md)；目标设计见 [DESIGN.md](DESIGN.md)。

## 组成

| 工程 | 内容 | 依赖 |
|---|---|---|
| `Mahjong.Core` | `Rules`（牌/区域/局面/随机/技能与校验器/计分与轮次）、`Flow`（动作树/调度/命令/请求/响应窗口/哈希）、`Content`（内容加载/查表）、`CoreAssembly` 身份锚点 | 无工程与包引用；`IsAotCompatible`，不引用 Godot/网络/时钟 |
| `Mahjong.Session` | 只有 `Mahjong.Session.csproj`，不含类型 | Core |
| `Mahjong.Client` | Godot 4.7.2 宿主；场景根节点 `Startup`（`Node`）：普通启动打印 `M0.1 START`；`--smoke` 加载内容与查表、打印 `CONTENT OK <hash>` 与 `TABLES OK ...`，30 帧后退出 | Core、Session；`PublishAot`，root 为 Client/Session/Core/GodotSharp |
| `Mahjong.Server` | 控制台入口 `Program.Main`：打印 `M0.1 SERVER OK` 后返回 0 | Core、Session |
| `Mahjong.Tests` | xUnit：`Architecture`、`Content`、`Flow`、`Rules` 四组用例 | Core、Session；不引用 Godot 与 Client |

依赖方向是 Client → Session → Core、Server → Session → Core，Core 不反向依赖宿主。TFM、Nullable、警告即错误在 `Directory.Build.props`，SDK 版本由 `global.json` 锁定。

## 核心循环

一个 tick 是一次 `ActionScheduler.Step(tick)`：先调 `IRequestRegistry.BeginTick(tick)` 结算超时/取消，再把动作树推进到阻塞或整棵树结束。

动作树节点是 `GameAction`，持一个产出 `ActionState` 的协程；每步优先下探未完成子动作（优先级降序、同优先级创建序升序），当前动作未阻塞才 `MoveNext`。

命令不进入调度器：调用方在 `Step` 前用 `CommandQueue.DrainForTick(tick)` 取走 `ReceivedTick <= tick` 的快照并按序应用，调度器不认识 `GameCommand`。

局面由 `GameState` 承载：`Deal` 洗牌发牌，`DrawFromWall`/`DrawFromTail` 摸牌，`Discard` 出牌，`DeclareMeld` 声明副露；`StateHash.Compute(state)` 把模型字段编码成 SHA-256 小写 hex。

事件侧只有 `GameEventArgs`（`GameState` + 可选 `Payload`）与抽象基类 `Trigger`，没有触发器注册表或事件派发器。

弃牌响应在调度器之外：`ResponseWindow` 向最多 3 个候选座位开窗、收应答并按固定仲裁键产出 `RequestOutcome`，调度器的 `WaitRequest` 只等调用方把结算写进 `IRequestRegistry`。

轮次驱动 `RoundController` 在没有会话时跑完 8 局：`Deal` 后庄家摸第 14 张，按 `RoundCommand` 脚本出牌或自摸，结算走 `ScoreManager` 的暂存→`Release` 两段式（流局回滚净 0），每局把权威总分写回 `PlayerState.Score` 镜像并轮换庄家；`RoundResult` 是 T-5 结算事件的 payload 来源。

启动路径不驱动 tick：`Mahjong.Server` 的 `Main` 打印后返回；客户端 `Startup` 是场景根节点：`--smoke` 下加载内容与查表并打印标记，计数 30 帧后退出。`Mahjong.Session` 不含装配代码，调度器、命令队列与内容加载在 Core 里可直接构造。

## Seam

- **SessionStream**：`Mahjong.Session` 不含类型，事件流、帧编码与可见性过滤没有对应代码；事件相关类型只有 `Flow/GameEventArgs.cs` 与 `Flow/Trigger.cs`。
- **Command**：上行命令模型在 `Flow/Commands.cs`（`CommandVerb`、`AllowedVerbs`、`GameCommand`、`CommandQueue`）；队列只缓冲与排序，动作合法性由 `Rules/CardAndSkillVerifier` 单点提供，座位复核与请求应答的流化仍不在 Core。
- **ResponseWindow**：`Flow/ResponseWindow.cs` 管弃牌响应的开窗、`TrySubmit`/`TryPass`、`Close` 与仲裁，产出 `ResponseResult` 与逐座 `RequestOutcome`；它不实现 `IRequestRegistry`，注册表挂接、超时驱入与座位过滤由 T-5 会话适配。
- **Flow/Action**：`Flow/` 下的状态类型、`GameAction`、`ActionScheduler`、`IRequestRegistry`、`RequestGroup` 组成纯 C# 编排层，不引用引擎类型，不读墙钟。
- **Content**：`Content/ContentCatalog` 校验 4 个 JSON 与 manifest 哈希，`Content/Tables/TableCatalog` 读 6 个 `.bytes`，`ContentPaths` 解析内容根；内容目录由调用方显式传入。
- **协议/回放**：格式契约在 [specs/protocol.md](specs/protocol.md)、[specs/replay-format.md](specs/replay-format.md) 与 [specs/discovery.md](specs/discovery.md)；Core 里的协议常量是 `CommandVerb`、`AllowedVerbs`、`RequestKind`、`RequestOutcomeKind` 与各枚举的数值。

## 扩展点

- **内容**：改 `content/` 下文件必须同步 `manifest.json` 与 `contentHash`，加载器按固定清单逐项校验；格式见 [TABLES.md](TABLES.md)。
- **查表**：`TableCatalog.Load(tablesDirectory)` 一次读入 6 个文件，查询按 `(SuitType, key)` 走字典；新增表要改固定清单与加载代码。
- **流程**：新流程写成一个 `IEnumerator<ActionState>` 协程，用 `ActionScheduler.CreateAction` 建节点、`GameAction.AddChild` 挂子树。
- **技能**：新技能 = 新 `SkillId` 数值 + `Skill` 子类（`Id`/`Validate`/`Commit`）+ 校验器的张数与分派分支；`SkillId` 是协议常量，加技能等于改协议。
- **触发器**：继承 `Trigger` 实现 `Run(GameEventArgs)` 与 `Priority`；派发由外部代码做，调度器不查触发器。
- **宿主**：`Mahjong.Server/Program.cs` 是控制台入口；`Mahjong.Client/Startup.cs` 是 Godot 场景根节点，AOT root 在 `Mahjong.Client.csproj`。

## 子系统页

- [core.md](subsystems/core.md)：内容加载、查表、牌与区域模型、随机与展示排序、技能与校验器。
- [flow.md](subsystems/flow.md)：Action 树、调度器、命令与请求、响应窗口、状态哈希。
- [scoring.md](subsystems/scoring.md)：计分账本与番种/杠位、轮次驱动、单局结果与排名。

确定性、并发、AOT 与序列化口径见 [src/AGENTS.md](../src/AGENTS.md)；文档 tier 与门禁见 [docs/AGENTS.md](AGENTS.md)。
