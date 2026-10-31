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
- 占位牌面：手牌 `Panel` + `Label`；鬼牌容器红色 2px 边框、选中黄色 2px 边框；不新增字体/美术资源，文案中文，字体用 `SystemFont`/`Theme` 运行时回退（§2.1）。

## 2. 视图、输入与控制器

文件：`View/TableViewState.cs`（四个投影类型）、`View/TableViewProjection.cs`（`Project`/`MapAllowedActions` 与中文标签/相对座位映射）、`View/Table.cs`（渲染与交互登记）、`Input/`（指针源、点击路由、命令路由）、`LocalGame.cs`（宿主、事件消费、结算层与字体回退）。

- 投影：`TableViewProjection.Project(session, content, viewerSeat, selectedTileId, statusLine, enabledActions)` 每帧从 `session.State` 重建四家/手牌/副露/弃牌，按钮启用由 `LocalGame` 计算后传入，不缓存局面、不从事件重放。
- 相对座位 `(seat - viewerSeat + 4) % 4`，`Players` 下标即相对座位；`Name` 为 你/下家/对家/上家；他人手牌只给 `HandCount`，视图类型不携带他人牌 id 或牌种。
- 自己手牌：`HandOrdering.SortedForDisplay(hand.Items, content.Joker)` 决定展示序，`HandTileView.HandSlot = Hand.IndexOf(tile)` 回填真实槽位。
- `EnabledActions`：`LocalGame.BuildEnabledActions` 取本座位开放请求的 `Allowed` 位集，经 `TableViewProjection.MapAllowedActions` 按 Discard/Pong/Kong/Win/Pass 固定顺序输出；响应窗口（`RequestKind.Pong`）的 Kong 位再用 Core `CardAndSkillVerifier` 对最近一次 `Discarded` 目标牌复核，不通过则移除；无请求为空列表。
- 牌面标签：万/筒/条 `1万..9万`/`1筒..9筒`/`1条..9条`、风 `东风/南风/西风/北风`、箭 `红中/发财/白板`；副露文本为 `碰/明杠/暗杠/补杠` + 每张牌标签；未知牌 id 为 `？？`。
- 渲染：`Table.Render(state, router, onTile, onAction)` 内容变化时刷新 Info/四家/手牌与按钮状态，每次重登记手牌 `Panel` 与未禁用按钮；`Info.Text` 首行 `第 {n}/8 局  余牌 {w}  你 {score}`、第二行 `StatusLine`；当前行动者 `Name` 前缀 `"> "`，其他人为两个空格；庄家标记文本 `庄`。
- 输入：`LocalGame._UnhandledInput` 把引擎事件推给 `MousePointerSource` 与 `TouchPointerSource`；`_Process` 每帧 `ClickRouter.BeginFrame` → `Table.Render` 重登记 → 排空指针事件 `Feed`；鼠标源丢弃触摸仿真鼠标事件（`Device == -1`），避免触摸时两条源重复触发。
- 命令：唯一 `GameSession.Submit` 调用点是 `InputRouter.SubmitVerb`（`Seq` 单调、`ReceivedTick = session.Tick`、`RequestId` 取本座位首个开放请求）；Discard 用真实手牌槽位与物理牌 id，其余动词 `Slot = 0`/`CardId = 0`；被拒打印原因、不重试。
- tick：`LocalGame._Process` 10Hz 累加器，每满 0.1s 调 `Step(session.Tick + 1)`；单帧最多 5 步，超出丢弃累计时间；每步后 `ConsumeEvents` 排空事件并更新状态行或结算层，并记录最近 `Discarded` 牌 id 供 KONG 复核。
- 状态行（中文）：`发牌中`、`第 n 局开始`、`轮到你`/`{NAME}的回合`、`{NAME} 打出 {tile}`、`{NAME} {碰/明杠/暗杠/补杠}`、`你摸牌`/`{NAME} 摸牌`、`轮到你 - 请选择一张牌`、`已选牌 - 再点一次或按出牌`、`赢牌可用 - 胡或过`、`响应窗口 - 碰/杠/过`、`响应窗口 - 过`、`第 {刚结束局号} 局结束 - 继续或重开`、`牌局结束 - 重开或返回`。

### 2.1 生命周期与结算

- `_Ready`：首行 `ApplyUiFont()`；`ContentPaths.Resolve()` → `ContentCatalog.Load` → `TableCatalog.Load` → `LocalSessionFactory.CreateSinglePlayer(content, tables, seed, tickRate: 10)` → `session.Start()`；加载失败 `GD.PrintErr` 原因并 `GetTree().Quit(1)`。
- 字体：`_Ready` 首行 `ApplyUiFont()` 建 `SystemFont`（FontNames 微软雅黑 UI/微软雅黑/黑体/Noto Sans CJK SC，`AllowSystemFallback = true`），设 `Theme.DefaultFont`/`DefaultFontSize = 16` 级联全部控件与运行时牌面 `Label`；不新增 `.ttf`/`.tres` 字体资源，AOT 无反射，随包终版字体归 M4。
- seed：`BaseSeed = 20261031`，每次新会话 `seed = BaseSeed + restartCount`（从 0 起）。
- 局结算（`RoundEnded` 且本批无 `GameEnded`）：暂停 tick；`Title = "第 {刚结束局号} 局结束"`（`LocalGame` 在 `RoundStarted` 记录当前局号；`State.RoundNo` 已推进到下一局，只用于 Info 首行）；`Body` 为 `流局`/`赢家 {name}`、`动作 {动作名}`、四家 `{NAME} {delta}  总分 {total}`；动作名按 `ScoreActionType` 位集映射为 自摸/七对/暗杠/明杠/补杠/杠上开花/无鬼，空为 `无`；`继续`/`重开`/`返回` 可见。
- 整局结算（`GameEnded`）：`Title = "牌局结束"`；`Body` 为四家总分与 `第1名 {NAME}` 到 `第4名 {NAME}` 的名次行；`继续` 隐藏，`重开`/`返回` 可见；之后不 `Step`。
- 重开：`restartCount++`、新建会话并 `Start()`、清空选中、隐藏结算层、重置累加器；不复用旧会话。
- 返回：`返回` 调 `GetTree().Quit()`（M4 前无主菜单）。

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

口径与后续：`Info` 首行格式、第二行 `StatusLine`；`返回` 直接退出进程、主菜单归 M4；AOT 与性能基线归 T-7；结算层为暗化牌桌上的模态层，按钮与暗化手牌区域纵向重叠（仍可读可点），UI 布局归 M4。

运行证据（ASCII 版，已被下方中文修订取代）：`.reviews/m02t6/evidence/impl/`（8 局到 GAME OVER、截图与日志，gitignored）。

敌对审核：review-code PASS（P0=0/P1=0）、review-accept PASS（P0=0/P1=0）、review-docs PASS（P0=0/P1=0）；环形交叉核查三向完成（review-code↔review-docs、review-docs↔review-accept、review-accept↔review-code 均 PASS；BACK 证据 F1 已补证关闭、F2–F5 已修正；残余 P2 证据时点/措辞已记录）。

### 修订 2026-10-31（D-T6-9 中文 UI + 字体回退）

改动：UI 文案中文化（相对名/牌面/副露/状态行/Info/按钮/结算层/庄家），代码标识与日志保留英文；注释沿用既有中文；`LocalGame._Ready` 首行 `ApplyUiFont()` 用 `SystemFont` + `Theme`（不新增字体资源）；D-T6-7 作废。

验证：`dotnet build Mahjong.sln -warnaserror` 0 警告 0 错误；`dotnet test Mahjong.sln` 991 通过 / 0 失败；`doc-gate -Fail` exit 0、0 errors。

运行证据：`.reviews/m02t6/evidence/impl-zh/`（27 文件含 15 截图；8 局到「牌局结束」，你 +6/下家 -2/对家 -2/上家 -2，第1名 你；重开余牌 82、庄家换人；返回退出；鼠标 360 次 + 触摸 1 次；game/editor error 0；字体 before/after 与 has_char 见 `font-fallback.md`）。偏差：默认字体依赖系统回退（`has_char('你')=false`）故显式 `SystemFont`；随包字体归 M4。
