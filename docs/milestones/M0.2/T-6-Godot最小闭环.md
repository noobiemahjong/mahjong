# T-6 Godot 最小闭环（实施规格）

状态：见任务表
依赖：T-5
对应 master：[M0.2](../M0.2-单机可玩闭环.md)
验收：手工清单全过；能打 8 局、结算、重开。
实现：`src/Mahjong.Client/`；类型契约见 [client.md](../../subsystems/client.md)，决策理由见 [Note](../../../.agents/notes/implemented/feature/2026-10-31-godot-minimal-loop.md)。

## 1. 场景

主场景 `Startup.tscn`（根 `Startup`，`Node`，`Startup.cs`）：普通启动打印 `M0.1 START` 后 `CallDeferred` 切到 `res://LocalGame.tscn`；带 `--smoke` 时加载内容与查表、打印 `CONTENT OK <hex>`/`TABLES OK ...`、30 帧后退出。

```text
LocalGame (Control, script LocalGame.cs)
├─ Table (instance of Table.tscn)
└─ ResultOverlay (Control, visible=false)
   ├─ Title (Label)  Body (Label)
   └─ ContinueButton / RestartButton / BackButton (Button, mouse_filter=Ignore)
```

`Table.tscn` 是复用子场景（M1 回放也用），脚本 `View/Table.cs`，不引用 Session 程序集：

```text
Table (Control)
├─ Info (Label)
├─ Players (Control)
│  ├─ Player0..3 (Control)
│  │  ├─ Name (Label)  Score (Label)  Melds (HBoxContainer)
│  │  ├─ BankerMark (Control) → Text (Label, "B")
│  │  └─ Discards (Control) → List (Label)
├─ Hand (HBoxContainer)
└─ Actions (HBoxContainer) → Discard/Pong/Kong/Win/Pass (Button, 初始 disabled)
```

- 节点路径（相对场景根）：`LocalGame/Table`、`LocalGame/ResultOverlay/{Title,Body,ContinueButton,RestartButton,BackButton}`；`Table/Info`、`Table/Players/Player0..3/{Name,Score,Melds,BankerMark,Discards}`、`Table/Hand`、`Table/Actions/{Discard,Pong,Kong,Win,Pass}`。
- 玩家区按相对座位固定方位：Player0 自己（下）、Player1 下家（右）、Player2 对家（上）、Player3 上家（左）；1280×720 固定布局，交互控件 `mouse_filter=Ignore`，点击统一走 `ClickRouter`。
- 占位牌面：手牌 `Panel` + `Label`；鬼牌容器红色 2px 边框、选中黄色 2px 边框；不引入字体/美术资源，文案全 ASCII。

## 2. 视图、输入与控制器

文件：`View/TableViewState.cs`（四个投影类型）、`View/TableViewProjection.cs`（`Project`/`MapAllowedActions` 与标签/相对座位映射）、`View/Table.cs`（渲染与交互登记）、`Input/`（指针源、点击路由、命令路由）、`LocalGame.cs`（宿主）。

- 投影：`TableViewProjection.Project(session, content, viewerSeat, selectedTileId, statusLine, enabledActions)` 每帧从 `session.State` 重建四家/手牌/副露/弃牌，按钮启用由 `LocalGame` 计算后传入，不缓存局面、不从事件重放。
- 相对座位 `(seat - viewerSeat + 4) % 4`，`Players` 下标即相对座位；`Name` 为 `YOU/NEXT/ACROSS/PREV`；他人手牌只给 `HandCount`，视图类型不携带他人牌 id 或牌种。
- 自己手牌：`HandOrdering.SortedForDisplay(hand.Items, content.Joker)` 决定展示序，`HandTileView.HandSlot = Hand.IndexOf(tile)` 回填真实槽位。
- `EnabledActions`：`LocalGame.BuildEnabledActions` 取本座位开放请求的 `Allowed` 位集，经 `TableViewProjection.MapAllowedActions` 按 Discard/Pong/Kong/Win/Pass 固定顺序输出；响应窗口（`RequestKind.Pong`）的 Kong 位再用 Core `CardAndSkillVerifier` 对最近一次 `Discarded` 目标牌复核，不通过则移除；无请求为空列表。
- 牌面标签：万/筒/条 `1M..9M`/`1P..9P`/`1S..9S`、风 `WE/WS/WW/WN`、箭 `DR/DG/DW`；副露文本为 `PONG/KONG/CONCEALED_KONG/ADDED_KONG` + 每张牌标签；未知牌 id 为 `??`。
- 渲染：`Table.Render(state, router, onTile, onAction)` 内容变化时刷新 Info/四家/手牌与按钮状态，每次重登记手牌 `Panel` 与未禁用按钮；`Info.Text` 首行 `Round {n}/8  Wall {n}  You {score}`、第二行 `StatusLine`；当前行动者 `Name` 前缀 `"> "`，其他人为反引号加两个空格。
- 输入：`LocalGame._UnhandledInput` 把引擎事件推给 `MousePointerSource` 与 `TouchPointerSource`；`_Process` 每帧 `ClickRouter.BeginFrame` → `Table.Render` 重登记 → 排空指针事件 `Feed`；鼠标源丢弃触摸仿真鼠标事件（`Device == -1`），避免触摸时两条源重复触发。
- 命令：唯一 `GameSession.Submit` 调用点是 `InputRouter.SubmitVerb`（`Seq` 单调、`ReceivedTick = session.Tick`、`RequestId` 取本座位首个开放请求）；Discard 用真实手牌槽位与物理牌 id，其余动词 `Slot = 0`/`CardId = 0`；被拒打印原因、不重试。
- tick：`LocalGame._Process` 10Hz 累加器，每满 0.1s 调 `Step(session.Tick + 1)`；单帧最多 5 步，超出丢弃累计时间；每步后 `ConsumeEvents` 排空事件并更新状态行或结算层，并记录最近 `Discarded` 牌 id 供 KONG 复核。
- 状态行（ASCII）：`DEALING`、`ROUND n START`、`YOUR TURN`/`{NAME} TURN`、`{NAME} DISCARDED {tile}`、`{NAME} {meld}`、`YOU DREW`/`{NAME} DREW`、`YOUR TURN - SELECT A TILE`、`TILE SELECTED - CLICK AGAIN OR DISCARD`、`WIN AVAILABLE - WIN OR PASS`、`RESPONSE WINDOW - PONG/KONG/PASS`、`RESPONSE WINDOW - PASS`、`ROUND {刚结束局号} RESULT - CONTINUE OR RESTART`、`GAME OVER - RESTART OR BACK`。

### 2.1 生命周期与结算

- `_Ready`：`ContentPaths.Resolve()` → `ContentCatalog.Load` → `TableCatalog.Load` → `LocalSessionFactory.CreateSinglePlayer(content, tables, seed, tickRate: 10)` → `session.Start()`；加载失败 `GD.PrintErr` 原因并 `GetTree().Quit(1)`。
- seed：`BaseSeed = 20261031`，每次新会话 `seed = BaseSeed + restartCount`（从 0 起）。
- 局结算（`RoundEnded` 且本批无 `GameEnded`）：暂停 tick；`Title = "ROUND {刚结束局号} RESULT"`（`LocalGame` 在 `RoundStarted` 记录当前局号；`State.RoundNo` 已推进到下一局，只用于 Info 首行）；`Body` 为 `DRAW`/`WINNER {name}`、`ACTION {ActionType}`、四家 `{NAME} {delta}  TOTAL {total}`；`CONTINUE`/`RESTART`/`BACK` 可见。
- 整局结算（`GameEnded`）：`Title = "GAME OVER"`；`Body` 为四家总分与 `1st..4th {NAME}` 名次行；`CONTINUE` 隐藏，`RESTART`/`BACK` 可见；之后不 `Step`。
- 重开：`restartCount++`、新建会话并 `Start()`、清空选中、隐藏结算层、重置累加器；不复用旧会话。
- 返回：`BACK` 调 `GetTree().Quit()`（M4 前无主菜单）。

## 3. 手工验收清单

1. 启动 → 本地对局 → 发牌（四家各 13，庄家首回合摸后 14）→ 轮流出牌。
2. 看到：自己手牌、四家弃牌/副露、剩余牌数、分数、当前轮到谁、庄家标记。
3. 操作：出牌/碰/杠/胡/过；非法操作不可点；碰后强制出牌。
4. 一局结算；8 局总分/名次；返回/重开。
5. 连续两局无崩溃、无卡死、无控制台红字。

## 4. 测试

- 无自动化 UI 测试；客户端由手工清单 + 截图/日志与独立审核验收，证据在 `.reviews/m02t6/evidence/`（gitignored）。
- 逻辑层由 T-1、T-2、T-3、T-4、T-5 用例覆盖；本任务不新增 xUnit 用例。

## 实施记录

日期：2026-10-31

实际改动：新增 `LocalGame.cs`、`LocalGame.tscn`、`Table.tscn`、`View/TableViewState.cs`、`View/TableViewProjection.cs`、`View/Table.cs`、`Input/{IPointerSource,MousePointerSource,TouchPointerSource,ClickRouter,InputRouter}.cs`；`Startup.cs` 普通启动分支追加 `CallDeferred` 切场景与 `OpenLocalGame`，`--smoke` 行为不变；`LocalGame` 创建 Core `CardAndSkillVerifier`（`WinChecker` + 鬼牌）用于收窄响应窗口 KONG 位。

验证（真跑）：

| 命令 | 结果 |
|---|---|
| `dotnet build Mahjong.sln -warnaserror` | exit 0，0 警告 0 错误 |
| `dotnet test Mahjong.sln` | exit 0，991 通过 / 0 失败 |
| `pwsh -File tools/doc-gate/doc-gate.ps1 -Fail` | exit 0、0 errors |

口径与后续：`Info.Text` 首行为冻结格式、第二行显示 `StatusLine`；`BACK` 直接退出进程、主菜单归 M4；AOT 导出与性能基线归 T-7；结算层为覆盖在暗化牌桌上的模态层，`CONTINUE`/`RESTART`/`BACK` 与暗化手牌区域纵向有视觉重叠（按钮仍可读可点），UI 布局归 M4。

运行证据（`.reviews/m02t6/evidence/impl/`，gitignored）：单次运行完整 8 局到 `GAME OVER`（7 次流局 + 第 4 局自摸，总分 `YOU +6 / NEXT -2 / ACROSS -2 / PREV -2`，名次 `1st YOU / 2nd NEXT / 3rd ACROSS / 4th PREV`）；截图 `01-deal`、`02-midgame`、`03-response`、`03b-pongonly`、第 1–7 局结算截图、`05-game-over`、`06-restart`、`07-pass-before`（1280×720）；真实鼠标 360 次点击与 1 次 `InputEventScreenTouch` 触摸都驱动选中/出牌，PONG/明杠/自摸/PASS 均在运行中触发；`RESTART` 后新会话 Round 1（庄家 ACROSS→PREV，seed 变化），`BACK` 后 `play_state=stopped`；game 日志 0 error，editor 日志 0 error（118 条 warn 为 MCP 驱动脚本的 GDScript UNREACHABLE_CODE）。

敌对审核：review-code PASS（P0=0/P1=0）、review-accept PASS（P0=0/P1=0）、review-docs PASS（P0=0/P1=0）；环形交叉核查三向完成（review-code↔review-docs、review-docs↔review-accept、review-accept↔review-code 均 PASS；BACK 证据 F1 已补证关闭、F2–F5 已修正；残余 P2 证据时点/措辞已记录）。
