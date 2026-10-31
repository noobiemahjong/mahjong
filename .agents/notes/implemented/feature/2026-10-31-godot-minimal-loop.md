# Agent Note: M0.2 Godot 最小闭环

Status: implemented — 客户端做 State+事件投影、单一路由输入与 10Hz tick 驱动，结算暂停由宿主停 Step，重开换新会话。

## Problem

M0.2 要让 1 真人 + 3 Bot 在 Godot 里打满 8 局：宿主按固定频率推进 `GameSession`、把权威局面与事件流投影成视图、把鼠标与触摸收束成 `GameCommand`，局/整局结束时暂停与重开；客户端入口是 `Startup` 场景与 `--smoke` 自检，没有对局界面。规格留了开放点：普通启动如何进入对局并保留 smoke 分支、视图要不要建 Mirror 或第二套 Flow、UI 能否直接调 `GameSession.Submit`、手牌展示排序放在哪一层、相对座位与手牌可见性如何表达、结算暂停用什么机制、重开如何得到不同对局、返回按钮落到哪里、占位 UI 如何避免默认字体缺 CJK 字形。任一处形成双权威或重复触发，验收就不可复现。

## Decision

1. `Startup.tscn` 仍是主场景；`Startup` 保留 `M0.1 START`，普通启动用 `CallDeferred` 切到 `res://LocalGame.tscn`，`--smoke` 分支行为不变（D1）。
2. 视图从 `GameSession.State` 投影、每批重建，按钮启用由 `LocalGame` 读 `ActiveRequests` 计算后传入；事件只驱动状态行、结算层与 KONG 复核用的最近弃牌目标，不建 Mirror、不按事件重放累积局面（D2）。
3. 输入单一路由：`IPointerSource` → `ClickRouter` → `InputRouter.SubmitVerb`；UI 控件设 `MouseFilter.Ignore`，禁原生 `Button.Pressed`/`_GuiInput`（D3）。
4. 手牌展示序复用 `Rules.HandOrdering.SortedForDisplay(tiles, content.Joker)`，`HandTileView.HandSlot` 回填 `Hand.IndexOf(tile)` 原槽位（D4）。
5. 相对座位 `(Seat - ViewerSeat + 4) % 4`，`Players` 下标即相对座位；M0.2 `ViewerSeat = 0` 仍按公式计算，不写死（D5）。
6. `RoundEnded` 且同批无 `GameEnded` 时暂停 tick 并显示局结算，`CONTINUE` 恢复；`GameEnded` 后不 `Step`，恢复从 `session.Tick + 1` 继续（D6）。
7. `RESTART` 新建 `GameSession`（`seed = BaseSeed + restartCount`），不复用旧会话；局结算与整局结算都提供 `RESTART`（D7）。
8. `BACK` 调 `GetTree().Quit()`；M4 之前没有主菜单（D8）。
9. 占位牌面用 `Panel` + `Label`，文案中文并配字体回退（默认字体缺字时用 `SystemFont`/`Theme` 运行时回退，不新增字体资源）；鬼牌在容器 `Panel` 上加红色 2px 边框并置 `IsJoker`，不只靠颜色（D9，中文口径见 [Note](2026-10-31-client-ui-chinese-text.md)）。
10. tick 用 10Hz 累加器，每满 0.1s 调一次 `Step(Tick + 1)`，单帧最多 5 步，超出的累计时间丢弃；每步后立刻排空全部事件（D10）。
11. `Table.tscn` 不引用 Session 程序集类型，`Table.Render` 只接收 `TableViewState`、`ClickRouter` 与两个回调（D11）。
12. AOT 与线程口径：不用反射/动态枚举解析，无静态可变状态，所有会话调用在 `_Ready`/`_Process`/`_UnhandledInput` 内（D12）。

## Alternatives considered

- **建 Mirror 或第二套 Flow**：客户端另存一份局面并按事件增量维护，看似与 Session 解耦；但单机同进程下权威已在 `GameSession.State`，Mirror 只增加第二权威与同步 bug。放弃，直接从 State+事件投影。
- **UI 直接调 `GameSession.Submit`**：少一层封装；但请求 id、`Allowed` 二次校验、槽位校验会分散到每个按钮，重复触发与漏校验难统一审核。放弃，唯一入口 `InputRouter.SubmitVerb`。
- **在 View 层自写手牌排序**：展示需求局部可控；但与规则层展示序分叉，鬼牌位置与稳定序各写一遍。放弃，复用 `HandOrdering`。
- **按绝对座位渲染**：布局直接对座位 0–3；但「自己在下、下家在左」需要 viewer 相关布局，M1 回放换 viewer 还要改渲染。放弃，相对座位公式 + 下标即相对座位。
- **用 `SceneTree.Paused`/`Engine.TimeScale` 暂停**：一行暂停全部节点；但会同时冻结结算层按钮与输入，且与逻辑 tick 语义混淆。放弃，宿主停 `Step`。
- **重开复用旧会话**：省一次加载；但会话的局号、累计分、请求与事件 seq 都不可回滚，无法得到新对局。放弃，新建会话并用递增 seed。
- **Back 回主菜单或占位场景**：导航更完整；M0.2 范围不含主菜单，多做一层空场景没有验收价值。放弃，直接 `Quit`，M4 导航再补。
- **牌面用中文或 CJK 字体资源**：可读性更接近成品；默认字体无 CJK 字形，新增字体资源越界。放弃，ASCII 占位 + 鬼牌边框（D-T6-9 后改为中文 + `SystemFont` 运行时回退，见 [中文 UI Note](2026-10-31-client-ui-chinese-text.md)）。
- **`Table` 直接持有 `GameSession`**：少传参数；但子场景要复用于 M1 回放，不能依赖本地会话类型。放弃，Render 只收视图数据与回调。

## Consequences

- 客户端是薄适配层：规则、流程、计分与会话仍在 Core/Session；客户端只做投影、输入映射与节奏控制。
- `Table.tscn` 可复用于 M1 回放：`Table.Render` 的入参是纯视图数据与回调，不依赖 `GameSession`。
- 无自动化 UI 测试：客户端靠手工清单、截图/日志与独立审核；逻辑边界尽量下沉到 Core 已有 API。
- 10Hz 与单帧步数上限让低帧率下不追帧；结算暂停与恢复不跳 tick。
- 同一 `restartCount` 得到同一 seed 的对局，重开对局可复现。
- Back 直接退出进程；主菜单、AOT 导出与性能基线归 T-7/M4，回放归 M1。
- 实现与验收落点见 [T-6 规格](../../../../docs/milestones/M0.2/T-6-Godot最小闭环.md)，类型契约见 [client.md](../../../../docs/subsystems/client.md)。
