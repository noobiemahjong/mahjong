# T-1b 单牌墙与发牌修正（实施规格）

状态：见任务表
依赖：[T-1](T-1-胡牌与七对.md)
对应 master：[M0.2](../M0.2-单机可玩闭环.md)
验收：单牌墙、无 `WallTail`；庄家首回合摸第 14 张；StateHash/事件/金标与 T-1 相关测试全绿。

## 1. 目标

修正 M0.1 T-4 的 `Wall` 69 + `WallTail` 14 拆分：目标为广东麻将单牌墙，正常摸牌从墙头、杠补从墙尾、没有预留区；同时把庄家第 14 张改回“发牌 13 张 + 首回合摸 1 张”。

## 2. 规则口径

- 洗牌 136 张后全部进 `Wall`；按 `(banker, +1, +2, +3) mod 4` 各发 13 张；发牌时**不**给庄家第 14 张；发牌后 `Wall = 84`，`CurrentSeat = banker`。
- 回合循环第一个动作：庄家从 `Wall` 头摸 1 张，手牌 14，`Wall = 83`，产生 `Drew` 事件。
- 正常摸牌从 `Wall` 头；杠后补牌从 `Wall` 末尾；`IsWallEmpty` 看整条 `Wall`；牌墙空即流局。
- 不再有 `WallTail` 区、`DrawFromTail` 的独立牌区、`tailCount` 字段。
- `RoundStarted` 在发牌后发出，`wallCount = 84`；庄家首回合 `Drew` 后 `wallCount = 83`。

## 3. 代码改动

- `ZoneKind` 删除 `WallTail`，`ZoneId` 只保留 `Wall`；相关枚举数值与测试同步。
- `GameState` 删除 `WallTail`/`WallTailCapacity`；`Deal` 不再拆 14 张，也不再替庄家摸第 14 张；`DrawFromTail` 从 `Wall` 末尾取；`IsWallEmpty` 看 `Wall`。
- `StateHash` 删除牌尾段，版本升到 2；`docs/subsystems/flow.md` 与 M0.1 T-5 的布局说明同步。
- M0.2 T-5 的 `RoundStarted`/`Drew` 去掉 `tailCount`；`Dealing` 只发 13 张，庄家首回合 `Draw` 产生 `Drew`。
- 同步 `RulesTestData`、`RulesGoldenVectors`、`DealTests`、`GameStateDrawTests`、`GameStateTests`、`DeclareMeldTests`、`StateHashTests`、`StateHashOracle`、`TileZoneTests`、`RulesContentAlignmentTests`。

## 4. T-1 相关修正

T-1 的 WinChecker 单测/金标不直接依赖牌墙；要检查并修正的是共用 `FlowTestData.DealtState`、StateHash/确定性测试与任何断言“庄家 14 张/牌墙 69+14”的用例。T-1 的胡牌判定逻辑不改。

## 5. 测试与验收

- `Deal`：Wall 84、四家各 13、庄家 `CurrentSeat`、无 `WallTail`；同 seed/banker 全槽位一致。
- 首回合：庄家 `DrawFromWall` 后手牌 14、Wall 83；非庄 13。
- `DrawFromTail` 取 Wall 末尾；Wall 空后抛异常；`IsWallEmpty` 在 84→83→…→0 时正确。
- StateHash 无牌尾段，版本 2；固定 seed 的两遍哈希一致。
- 金标重录：Wall 84/首回合后 83、庄家 13/14；M0.1 T-4 旧 69/14 金标作废并记录。
- `dotnet test` 全绿；doc-gate 0 error；T-1 测试无回退。

## 实施记录

开工后在此追加：日期、实际改动、验证命令与结果、偏差/后续。
