# Architecture（当前实现地图）

> 组成、tick 循环、seam、扩展点。类型与契约见 [subsystems/](subsystems/) 各页；为什么这么定见 [.agents/notes/](../.agents/notes/README.md)；目标设计见 [DESIGN.md](DESIGN.md)。

## 组成

| 工程 | 内容 | 依赖 |
|---|---|---|
| `Mahjong.Core` | `Rules`（牌/区域/局面/随机/技能与校验器/计分与轮次）、`Flow`（动作树/调度/命令/请求/响应窗口/哈希）、`Content`（内容加载/查表）、`Bot`、`CoreAssembly` 身份锚点 | 无工程与包引用；`IsAotCompatible`，不引用 Godot/网络/时钟 |
| `Mahjong.Session` | `GameSession`（8 局流程/事件流/请求登记/Bot 驱动）、`LocalSessionFactory`、事件 payload 与读取器 | Core |
| `Mahjong.Client` | Godot 4.7.2 宿主：`LocalGame`（控制器：内容加载/10Hz tick/事件消费/投影/结算层/重开）、`Table`（复用视图，`Render` 只接收 `TableViewState`）、`Input`（鼠标与触摸指针源、`ClickRouter`、唯一命令出口 `InputRouter`）、`View`（投影类型与投影）；`Startup` 承载 `--smoke` 与 `--autoplay`（无 UI 四 Bot 8 局） | Core、Session；`PublishAot`，root 为 Client/Session/Core/GodotSharp |
| `Mahjong.Server` | 控制台入口 `Program.Main`：打印 `M0.1 SERVER OK` 后返回 0 | Core、Session |
| `Mahjong.Tests` | xUnit 六组：`Architecture`、`Content`、`Flow`、`Rules`、`Bot`、`Session` | Core、Session；不引用 Godot 与 Client |

依赖方向 Client → Session → Core、Server → Session → Core，Core 不反向依赖宿主；TFM、Nullable、警告即错误在 `Directory.Build.props`，SDK 由 `global.json` 锁定。

## 核心循环

一个 tick 是一次 `ActionScheduler.Step(tick)`：先 `IRequestRegistry.BeginTick(tick)` 结算超时/取消，再把动作树推进到阻塞或整棵树结束。

`GameAction` 节点持产出 `ActionState` 的协程；每步优先下探未完成子动作（优先级降序、同优先级创建序升序），当前动作未阻塞才 `MoveNext`。

命令不进调度器：调用方在 `Step` 前用 `CommandQueue.DrainForTick(tick)` 取 `ReceivedTick <= tick` 快照并应用，调度器不认识 `GameCommand`。

局面由 `GameState` 承载：`Deal`/`DrawFromWall`/`DrawFromTail`/`Discard`/`DeclareMeld` 维护牌与区域，`Revealed` 是公共揭示区（翻马追加、`Deal` 清空）；`StateHash.Compute` 按布局 3 编码 SHA-256 小写 hex。

事件侧只有 `GameEventArgs`（`GameState` + 可选 `Payload`）与 `Trigger` 基类，没有触发器注册表或派发器。

弃牌响应在调度器之外：`ResponseWindow` 向最多 3 个候选座位开窗、收应答并按固定仲裁键产出 `RequestOutcome`；`WaitRequest` 只等调用方把结算写进 `IRequestRegistry`。

本地会话把各层装成 tick 机器：`GameSession.Step` 先处理到期 Bot 应答，再调 `ActionScheduler.Step`（内部先 `BeginTick` 结算请求），最后记录 tick；单根协程经 `WaitUntil` 等请求结算，事件写进 `SessionEventBuffer`、由 `DrainEvents` 破坏性取出。

轮次驱动 `RoundController` 在没有会话时跑完 8 局：`Deal` 后庄家摸第 14 张，按 `RoundCommand` 脚本出牌或自摸，结算走 `ScoreManager` 暂存→`Release`（流局回滚净 0），每局写回 `PlayerState.Score` 镜像并轮换庄家；`RoundResult` 供会话构造 `RoundEnded` payload。会话侧的 8 局循环另维护 `dealerStreak`，每局随 `RoundStarted` 下发，胡牌按胡牌金额→跟庄→连庄→中马→收尾五步结算，马牌落在 `Revealed`。

Bot 决策也在调度器之外：`SimpleBotPolicy.Decide` 只读 `BotContext`（局面、自身、可用技能、目标弃牌与刚摸的牌）返回 `BotDecision`；自己回合按 七对自摸→普通自摸→出牌，响应窗口按 杠→碰→过，非「过」选择先经 `CardAndSkillVerifier`；策略不消费随机、不改局面，`GameSession` 按 tick 延迟驱动。

客户端：`LocalSessionFactory` 是装配根（内容与查表 → `WinChecker`/`Pcg32`）；`LocalGame` 每帧投影 `TableViewState` 并 `Table.Render`，按 10Hz 累加器调 `GameSession.Step`、每步后 `DrainEvents(0)`；`RoundEnded` 暂停 tick，`GameEnded` 停步，`RESTART` 新建会话、`BACK` 退出。`Startup` 的 `--smoke` 加载内容与查表、30 帧后退出；`--autoplay` 不切 UI，装配四 Bot 会话跑满 8 局后打印 `GAME OK rounds=8` 退出。`LocalGame` 首帧完整渲染后打印一次 `LOCAL READY`，作为 AOT 启动口径终点。

## Seam

- **SessionStream**：`SessionEventBuffer`（internal）按产出顺序缓存事件，`Drain` 时按 viewer 编码可见性并清空；`SessionEvent`/`SessionEventKind`/`SessionEventPayloadReader` 是公开事件面，payload 布局见 [T-5 §1.1](milestones/M0.2/T-5-本地会话.md)。
- **Command**：上行命令模型在 `Flow/Commands.cs`；`GameSession.Submit` 复核座位、开放请求、动词位与动作合法性（`Rules/CardAndSkillVerifier`），只记应答，结算在下一 tick 的 `BeginTick`。
- **ResponseWindow**：`ResponseWindow` 管弃牌响应的开窗、`TrySubmit`/`TryPass`、`Close` 与仲裁，产出 `ResponseResult` 与逐座 `RequestOutcome`；挂接、超时与座位过滤由 `GameSession`/`SessionRequestRegistry` 适配。
- **Flow/Action**：`Flow/` 下的状态类型、`GameAction`、`ActionScheduler`、`IRequestRegistry`、`RequestGroup` 组成纯 C# 编排层，不引用引擎类型，不读墙钟。
- **Content**：`ContentCatalog` 校验 4 个 JSON 与 manifest 哈希，`TableCatalog` 读 6 个 `.bytes`，`ContentPaths` 解析内容根；内容目录由调用方显式传入。
- **Client 投影/输入**：`TableViewState` 由 `TableViewProjection` 只读 `GameSession.State` 生成，按钮启用由 `LocalGame` 按 `ActiveRequests` 计算后传入；`InputRouter.SubmitVerb` 是唯一 `Submit` 调用点，`Table.Render` 不引用 Session 类型。
- **协议/回放**：格式契约在 [specs/protocol.md](specs/protocol.md)、[specs/replay-format.md](specs/replay-format.md) 与 [specs/discovery.md](specs/discovery.md)；Core 里的协议常量是 `CommandVerb`、`AllowedVerbs`、`RequestKind`、`RequestOutcomeKind`。

## 扩展点

- **内容**：改 `content/` 文件必须同步 `manifest.json` 与 `contentHash`，加载器按固定清单校验；格式见 [TABLES.md](TABLES.md)。
- **查表**：`TableCatalog.Load` 一次读入 6 个文件，查询按 `(SuitType, key)` 走字典；新增表要改固定清单与加载代码。
- **流程**：新流程写成 `IEnumerator<ActionState>` 协程，用 `ActionScheduler.CreateAction` 建节点、`GameAction.AddChild` 挂子树。
- **事件/请求**：新事件 = `SessionEventKind` 数值 + `SessionEventBuffer` 的 Emit/Encode + `SessionEventPayloadReader` 读取方法（数值是协议常量，改协议走 [specs](specs/protocol.md)）；新请求复用 `Core.Flow` 的 `RequestKind`/`PendingRequest`，不在 Session 重复定义。
- **技能**：新技能 = 新 `SkillId` 数值 + `Skill` 子类（`Id`/`Validate`/`Commit`）+ 校验器的张数与分派分支；`SkillId` 是协议常量，加技能等于改协议。
- **触发器**：继承 `Trigger` 实现 `Run` 与 `Priority`；派发由外部代码做，调度器不查触发器。
- **宿主**：`Server/Program.cs` 是控制台入口；`Client/Startup.cs` 是场景根节点，AOT root 见 `Mahjong.Client.csproj`。

## 子系统页

- [core.md](subsystems/core.md)：内容加载、查表、牌与区域模型、随机与展示排序、技能与校验器。
- [flow.md](subsystems/flow.md)：Action 树、调度器、命令与请求、响应窗口、状态哈希。
- [scoring.md](subsystems/scoring.md)：计分账本与番种/杠位、轮次驱动、单局结果与排名。
- [bot.md](subsystems/bot.md)：Bot 决策类型、决策顺序、异常边界、确定性与 T-5 接缝。
- [session.md](subsystems/session.md)：会话类型、生命周期、事件流、请求登记、流程与接缝。
- [rules.md](subsystems/rules.md)：胡牌判定（查表遍历、七对、鬼牌补齐、胡牌张）。
- [client.md](subsystems/client.md)：启动与场景、10Hz tick 与暂停、视图投影、输入与命令映射、结算层。

确定性、并发、AOT 与序列化口径见 [src/AGENTS.md](../src/AGENTS.md)；文档 tier 与门禁见 [docs/AGENTS.md](AGENTS.md)。
