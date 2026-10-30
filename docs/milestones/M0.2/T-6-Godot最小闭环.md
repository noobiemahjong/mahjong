# T-6 Godot 最小闭环（实施规格）

状态：见任务表
依赖：T-5
对应 master：[M0.2](../M0.2-单机可玩闭环.md)
验收：手工清单全过；能打 8 局、结算、重开。

## 1. 场景树

`Table.tscn` 是可复用子场景（M1 回放也用它）；`LocalGame.tscn` 实例化它并加结果层：

```
Table.tscn (Control)
├─ Info (Label)                 # 局号/剩余牌/分数
├─ Players[4] (Control)         # 相对座位；0=自己
│  ├─ Name (Label)  Score (Label)  Melds (HBoxContainer)
│  ├─ BankerMark (Control)      # 庄家标记
│  └─ Discards (Control)
├─ Hand (HBoxContainer)         # 自己手牌；点击选中/再点提交
└─ Actions (HBoxContainer)      # 出牌/碰/杠/胡/过
LocalGame.tscn (Control)
├─ Table (instance)
└─ ResultOverlay (Control)      # 局结算/整局结算；重开/返回
```

M0.2 不使用旧美术；牌用 `Panel` + `Label`（花色/点数）+ 鬼牌红框，字体用 Godot 默认字体；美术落点 M4 T-5。

## 2. 视图与 Mirror

- 单机 Client 与 `GameSession` 同进程：直接读 `GameSession.State` + `DrainEvents(viewerSeat)` 做投影，**不建第二套 Flow/Mirror**（见 Note 2026-10-28-local-view-vs-mirror）。
- `TableController`：按 10Hz 调 `session.Step`，用 State/事件刷新视图；输入 → `Command`。
- 输入抽象 `Mahjong.Client/Input/`：`IPointerSource`（Down/Move/Up）、`ClickRouter`；鼠标与触摸各一实现；UI 只能通过 `InputRouter` 发 `Command`，禁止直接改 Session。
- 手牌展示排序：普通牌按 `Type.Id`，鬼牌最后；**不改会话 zone 顺序**。
- 请求：`RequestOpened` 决定哪些按钮可点；非法操作按钮禁用。
- 结算：`RoundEnded/GameEnded` 显示分数/名次；重开 = 新建 Session。

## 3. 手工验收清单

1. 启动 → 本地对局 → 发牌（自己 13/庄 14）→ 轮流出牌。
2. 看到：自己手牌、四家弃牌/副露、剩余牌数、分数、当前轮到谁、庄家标记。
3. 操作：出牌/碰/杠/胡/过；非法操作不可点；碰后强制出牌。
4. 一局结算；8 局总分/名次；返回/重开。
5. 连续两局无崩溃、无卡死、无控制台红字。

## 4. 测试

- 无自动化 UI 测试；手工清单 + 截图/日志作为验收证据。
- 逻辑层测试由 T-1..T-5 覆盖；UI 只做薄适配。

## 实施记录

开工后在此追加：日期、实际改动、验证命令与结果、偏差/后续。
