# 子系统：Godot 客户端

> 本页只写类型、语义与契约；总体流程见 [architecture](../architecture.md)，决策理由见 [Note](../../.agents/notes/implemented/feature/2026-10-31-godot-minimal-loop.md)，任务口径与验收见 [T-6](../milestones/M0.2/T-6-Godot最小闭环.md)。命名空间 `Mahjong.Client`，视图在 `Mahjong.Client.View`、输入在 `Mahjong.Client.Input`；依赖 `Mahjong.Core` 与 `Mahjong.Session`。

## 1. 启动与场景

- 主场景 `Startup.tscn`，根 `Startup`（`Node`，`Startup.cs`）：打印 `M0.1 START`；`--smoke` 加载内容与 6 个查表、打印 `CONTENT OK <hex>`/`TABLES OK ...`、30 帧后退出；普通启动 `Callable.From(OpenLocalGame).CallDeferred()` 切到 `res://LocalGame.tscn`。
- `LocalGame.tscn` 根 `Control` 挂 `LocalGame.cs`；`Table` 是 `Table.tscn` 实例；`ResultOverlay`（`Control`，`visible=false`）子节点 `Title`/`Body`（`Label`）与 `ContinueButton`/`RestartButton`/`BackButton`（`Button`）。
- `Table.tscn` 脚本 `View/Table.cs`，可复用、不引用 Session 程序集；固定节点路径（可加纯装饰子节点，不改名/类型/层级）：

```text
Table (Control)
├─ Info (Label)
├─ Players (Control)
│  ├─ Player0..3 (Control)
│  │  ├─ Name (Label)  Score (Label)  Melds (HBoxContainer)
│  │  ├─ BankerMark (Control) → Text (Label, "B")
│  │  └─ Discards (Control) → List (Label)
├─ Hand (HBoxContainer)
└─ Actions (HBoxContainer) → Discard/Pong/Kong/Win/Pass (Button)
```

- 相对座位方位：`Player0` 自己（下）、`Player1` 下家（右）、`Player2` 对家（上）、`Player3` 上家（左）；1280×720 固定布局。
- 占位牌面：手牌 `Panel` + `Label`，鬼牌容器红色 2px 边框、选中黄色 2px 边框；默认字体、ASCII 文案，中文文案与 CJK 字体归 M4。
- 所有交互控件 `mouse_filter=Ignore`，点击统一走 `ClickRouter`。

## 2. 会话生命周期与 tick

- `LocalGame._Ready`：`ContentPaths.Resolve()` → `ContentCatalog.Load` → `TableCatalog.Load(Path.Combine(dir, "tables"))` → `LocalSessionFactory.CreateSinglePlayer(content, tables, seed, tickRate: 10)` → `session.Start()`；加载失败 `GD.PrintErr("local game content load failed: ...")` 并 `GetTree().Quit(1)`。
- seed：`BaseSeed = 20261031`，每次新会话 `seed = BaseSeed + restartCount`（从 0 起）。
- tick：`_Process(delta)` 按 10Hz 累加，每满 0.1s 调 `session.Step(session.Tick + 1)`；单帧最多 5 步、超出丢弃累计时间；每步后 `DrainEvents(0)` 排空事件（`ConsumeEvents`）。
- 每帧顺序：清理已不在手牌的选中（`PruneSelection`）→ 投影 → `ClickRouter.BeginFrame` → `Table.Render` → 登记结算层按钮 → 排空指针 → 推进 tick。
- 视图每批从 `session.State` 重建；事件只更新 `StatusLine`、结算层与 KONG 复核用的最近弃牌目标，不重放局面。
- 暂停：`RoundEnded`（本批无 `GameEnded`）暂停 tick 并显示局结算；`CONTINUE` 隐藏、恢复，tick 从 `session.Tick + 1` 继续。
- 结束：`GameEnded` 显示整局结算，`CONTINUE` 隐藏，`RESTART`/`BACK` 可见；`_gameOver` 后不 `Step`。
- 重开：`restartCount++`、新建会话并 `Start()`、清空选中、隐藏结算层、重置累加器；不复用旧会话。
- 返回：`BACK` 调 `GetTree().Quit()`。
- 单线程：所有 `GameSession` 调用在 `_Ready`/`_Process`/`_UnhandledInput` 内；不加锁、不用 async/Task。

## 3. 视图投影

`View/TableViewState.cs`（命名空间 `Mahjong.Client.View`）：

```csharp
// 签名摘要（省略实现体与 using）。
public readonly record struct TileView(ushort Id, string Label, bool IsJoker);

public sealed record PlayerView(
    byte Seat, byte RelativeSeat, string Name, short Score, bool IsBanker, bool IsCurrent,
    int HandCount, IReadOnlyList<TileView> Melds, IReadOnlyList<TileView> Discards);

public readonly record struct HandTileView(ushort Id, string Label, bool IsJoker, int HandSlot);

public sealed record TableViewState(
    byte RoundNo, byte ViewerSeat, ushort WallCount,
    IReadOnlyList<PlayerView> Players,          // 固定 4 项，下标=相对座位 0=自己
    IReadOnlyList<HandTileView> Hand,           // 展示序
    ushort? SelectedTileId,
    IReadOnlyList<CommandVerb> EnabledActions,  // 固定顺序子集
    string StatusLine);
```

- 投影在 `internal static TableViewProjection`（`View/TableViewProjection.cs`）：`Project(session, content, viewerSeat, selectedTileId, statusLine, enabledActions)` 一次读 `State` 生成四家/手牌/副露/弃牌快照；按钮启用由 `LocalGame` 计算后传入，投影不读 `ActiveRequests`、不缓存、不重放。
- 相对座位 `(seat - viewerSeat + 4) % 4`；`Players` 下标即相对座位；`Name` 为 `YOU/NEXT/ACROSS/PREV`。M0.2 `ViewerSeat = 0`，仍按公式计算。
- 自己手牌：`HandOrdering.SortedForDisplay(hand.Items, content.Joker)` 决定展示序；`HandSlot = Hand.IndexOf(tile)` 回填真实槽位。
- 他人手牌只给 `HandCount`；`PlayerView`/`TableViewState` 不携带他人牌 id 或牌种。
- `Melds` 按 `Meld` 顺序，每项 Label 为 `PONG/KONG/CONCEALED_KONG/ADDED_KONG` + 每张牌标签；`Discards` 按弃牌区顺序。
- 牌面标签：万 `1M..9M`、筒 `1P..9P`、条 `1S..9S`、风 `WE/WS/WW/WN`、箭 `DR/DG/DW`；未知牌 id 为 `??`。
- `EnabledActions`：`LocalGame.BuildEnabledActions` 取本座位开放请求的 `Allowed` 位集，经 `TableViewProjection.MapAllowedActions` 按 Discard/Pong/Kong/Win/Pass 固定顺序输出；响应窗口（`RequestKind.Pong`）的 Kong 位再用 Core `CardAndSkillVerifier` 对最近一次 `Discarded` 目标牌复核，不通过则移除；无请求为空列表。
- `Table.Render(state, router, onTile, onAction)`：内容变化时刷新 Info/四家/手牌与按钮状态，每次重登记手牌 `Panel` 与未禁用按钮。
- `Info.Text` 两行：首行 `Round {n}/8  Wall {n}  You {score}`，第二行 `StatusLine`；当前行动者 `Name` 前缀 `"> "`，其他人为反引号加两个空格。
- 按钮 `Disabled` 与 `EnabledActions` 一致，Discard 额外要求已选中手牌；`CONTINUE` 仅局结算可见。
- 状态行（ASCII）：`DEALING`、`ROUND n START`、`YOUR TURN`/`{NAME} TURN`、`{NAME} DISCARDED {tile}`、`{NAME} {meld}`、`YOU DREW`/`{NAME} DREW`、`YOUR TURN - SELECT A TILE`、`TILE SELECTED - CLICK AGAIN OR DISCARD`、`WIN AVAILABLE - WIN OR PASS`、`RESPONSE WINDOW - PONG/KONG/PASS`、`RESPONSE WINDOW - PASS`、`ROUND {刚结束局号} RESULT - CONTINUE OR RESTART`、`GAME OVER - RESTART OR BACK`。
- 交互控件 `MouseFilter = Ignore`；不连接 `Button.Pressed`、不用 `_GuiInput`；点击统一走 `ClickRouter`。

## 4. 输入

`Input/`（命名空间 `Mahjong.Client.Input`）：

```csharp
// 签名摘要（省略实现体与 using）；Position 为 Vector2。
public enum PointerPhase : byte { Down = 0, Move = 1, Up = 2 }
public readonly record struct PointerEvent(PointerPhase Phase, int Index, Vector2 Position);

public interface IPointerSource
{
    void Push(Godot.InputEvent inputEvent);
    bool TryRead(out PointerEvent pointerEvent);
}

public sealed class MousePointerSource : IPointerSource   // InputEventMouseButton/Motion；Index=按钮号
public sealed class TouchPointerSource : IPointerSource   // InputEventScreenTouch/Drag；Index=手指号

public sealed class ClickRouter
{
    public void Register(Control target, Action onClick);
    public void BeginFrame();
    public void Feed(PointerEvent pointerEvent);
}

public sealed class InputRouter
{
    public InputRouter(GameSession session, byte humanSeat);
    public bool SubmitVerb(CommandVerb verb, int handSlot);
    public bool SubmitDiscard(int handSlot);
}
```

- 唯一 `GameSession.Submit` 调用点是 `InputRouter.SubmitVerb`；其余 `src/Mahjong.Client/**` 不出现 `.Submit(`。
- 命令构造：`Seq` 单调（从 1）、`ReceivedTick = session.Tick`、`Seat = humanSeat`、`RequestId` = `ActiveRequests` 中本座位首个；Discard → `Slot = handSlot`、`CardId = Hand[handSlot].Id`，其余动词 `Slot = 0`/`CardId = 0`。
- `SubmitVerb`：无开放请求 / 动词不在 `Allowed` / 槽位越界 → false 不 Submit；接受 → true；被拒 → `GD.PrintErr($"command rejected: {reason}")`、保持局面、不重试。
- 手牌点击：仅当 `Discard` 可用；未选中→选中；同牌二次点击→`SubmitDiscard(HandSlot)`；另一张→改选。
- `DISCARD` 按钮用选中牌的真实槽位；`PONG/KONG/WIN/PASS` 调 `SubmitVerb(verb, 0)`，可用性只看 `EnabledActions`。
- 鼠标与触摸都喂入；`MousePointerSource` 丢弃 `Device == -1` 的触摸仿真鼠标事件，避免与触摸源重复触发；键盘不接入。
- 点击路由：`_UnhandledInput` 把事件推给两条源，每帧 `BeginFrame` → `Render` 重登记 → `Feed` 排空；Down 记起点，Up 命中同一控件才触发一次；结算层按钮在 `Table.Render` 之后登记，命中优先。

## 5. 结算层

- 局结算：`Title = "ROUND {刚结束局号} RESULT"`（`RoundStarted` 记录；`State.RoundNo` 是下一局，只用于 Info 首行）；`Body` 为 `DRAW`/`WINNER {name}`、`ACTION {ActionType}`、四家 `{NAME} {delta}  TOTAL {total}`；`CONTINUE`/`RESTART`/`BACK` 可见。
- 整局结算：`Title = "GAME OVER"`；`Body` 为四家 `{NAME} {score}` 与 `1st..4th {NAME}` 名次行；`CONTINUE` 隐藏，`RESTART`/`BACK` 可见。
- 结算层按钮同样经 `ClickRouter` 登记，不用原生 `Pressed`。

## 6. 接缝与口径

- **Session**：`LocalGame` 持有 `GameSession` 并消费事件流；命令只经 `InputRouter` 提交；不直接改 `State`。
- **AOT**：无反射/动态类型生成/动态 `Enum.Parse` 依赖；场景切换用 `GetTree().ChangeSceneToFile`。
- **状态**：无 `static` 可变状态与静态缓存，每个 `LocalGame` 实例独占自己的 session/router/view；不读环境变量决定玩法（`MAHJONG_CONTENT_DIR` 归 `ContentPaths`）；产品代码不写盘；M0.2 单机同进程，网络/回放/观战/主菜单不在本页。
