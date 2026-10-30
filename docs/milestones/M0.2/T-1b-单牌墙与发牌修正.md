# T-1b 单牌墙与发牌修正（实施规格）

状态：见任务表
依赖：[T-1](T-1-胡牌与七对.md)
对应 master：[M0.2](../M0.2-单机可玩闭环.md)
验收：单牌墙、无预留牌尾区；庄家首回合摸第 14 张；StateHash/事件/金标与 T-1 相关测试全绿。

## 1. 目标

牌墙为广东麻将单牌墙：正常摸牌从墙头、杠补从同一牌墙末尾、没有预留区；庄家第 14 张来自“发牌 13 张 + 首回合摸 1 张”。

## 2. 规则口径

- 洗牌 136 张后全部进 `Wall`；按 `(banker, +1, +2, +3) mod 4` 各发 13 张；发牌时**不**给庄家第 14 张；发牌后 `Wall = 84`，`CurrentSeat = banker`。
- 回合循环第一个动作：庄家从 `Wall` 头摸 1 张，手牌 14，`Wall = 83`，产生 `Drew` 事件。
- 正常摸牌从 `Wall` 头；杠后补牌从 `Wall` 末尾；`IsWallEmpty` 看整条 `Wall`；牌墙空即流局。
- 牌墙只有一条：没有独立牌尾区，也没有尾数字段。
- `RoundStarted` 在发牌后发出，`wallCount = 84`；庄家首回合 `Drew` 后 `wallCount = 83`。

## 3. 改动范围

- `ZoneKind` 五值：`Wall=0`、`Hand=1`、`Meld=2`、`Discard=3`、`Revealed=4`；公共区 `ZoneId` 只有 `Wall`，座位区 `Hand`/`Discard` 等仍用 `ZoneId`。
- `GameState` 只有 `Wall`；`Deal` 洗 136 张全部进 `Wall`、按座位各发 13 张、不替庄家摸第 14；`DrawFromTail` 从 `Wall` 末尾取；`IsWallEmpty` 看整条 `Wall`。
- `StateHash` 布局版本 2、无预留牌尾段；[flow](../../subsystems/flow.md) 与 M0.1 T-5 的布局说明同步。
- M0.2 T-5 的 `RoundStarted`/`Drew` 只有 `wallCount`；`Dealing` 只发 13 张，庄家首回合 `Draw` 产生 `Drew`。
- 同步 `RulesTestData`、`RulesGoldenVectors`、`DealTests`、`GameStateDrawTests`、`GameStateTests`、`DeclareMeldTests`、`StateHashTests`、`StateHashOracle`、`TileZoneTests`、`RulesContentAlignmentTests`。

## 4. T-1 相关修正

T-1 的 WinChecker 单测/金标不直接依赖牌墙；要检查并修正的是共用 `FlowTestData.DealtState`、StateHash/确定性测试与任何断言旧发牌口径（庄家发牌即 14 张、牌墙拆分两区）的用例。T-1 的胡牌判定逻辑不改。

## 5. 测试与验收

- `Deal`：Wall 84、四家各 13、庄家 `CurrentSeat`、无预留牌尾区；同 seed/banker 全槽位一致。
- 首回合：庄家 `DrawFromWall` 后手牌 14、Wall 83；非庄 13。
- `DrawFromTail` 取 Wall 末尾；Wall 空后抛异常；`IsWallEmpty` 在 84→83→…→0 时正确。
- StateHash 无牌尾段，版本 2；固定 seed 的两遍哈希一致。
- 金标重录：Wall 84/首回合后 83、庄家 13/14；M0.1 T-4 旧拆区金标作废并记录。
- `dotnet test` 全绿；doc-gate 0 error；T-1 测试无回退。

## 实施记录

日期：2026-10-30

实际改动：

- 代码：`ZoneKind` 收为五值（Wall=0/Hand=1/Meld=2/Discard=3/Revealed=4）；`GameState` 只有一条 `Wall`，`Deal` 洗 136 张全部进墙、按 `(banker,+1,+2,+3) mod 4` 各发 13 张、不替庄家摸第 14，`DrawFromTail` 从 `Wall` 末尾取，`IsWallEmpty` 看整墙；`StateHash` 布局版本 2、去掉牌尾段。
- 测试：发牌金标按单牌墙重录（Wall 84 槽位 + 四家各 13 张手牌），StateHash Empty/Rich 升 v2 并同步 oracle；新增首回合 14/83、`DrawFromTail` 末尾、空墙异常、`IsWallEmpty` 84→0、`ZoneKind` 五值等用例；T-1 用例无回退。
- 文档：core.md §3、flow.md §7、DESIGN.md、M0.1 T-4/T-5、M0.2 master、T-2/T-5 规格与 M0.3 规格同步单牌墙口径；3 篇 2026-10-29 implemented Note（确定性发牌/状态哈希/区域副露）按事实更新；单牌墙 Note 转 implemented。

验证（命令 → 结果）：

| 命令 | 结果 |
|---|---|
| `dotnet build Mahjong.sln -warnaserror` | exit 0，0 警告 0 错误 |
| `dotnet test Mahjong.sln` | exit 0，530 通过 / 0 失败 / 0 跳过 / 530 总计；改动前 525 通过，净增 5；`[Fact]`+`[Theory]` 434→439 |
| `dotnet test Mahjong.sln --filter FullyQualifiedName~WinChecker` | exit 0，89 通过 / 0 失败（T-1 无回退） |
| `pwsh -File tools/doc-gate/doc-gate.ps1 -Fail` | exit 0，0 errors；100 files / 29 warnings（25 条既有 G12；4 条来自 `.reviews/` 审核报告：3×G7 未登记 + 1×G12，非 docs 改动新增） |

金标重录（独立 Python 推导，Lead 抽样复核一致）：

- 发牌：新金标 2 组。(seed 42, banker 0) `Wall` 首槽 11 / 末槽 58；(seed 20261029, banker 2) `Wall` 首槽 51 / 末槽 80；四家 13 张手牌全部重录（见 `RulesGoldenVectors`）。发牌后 `Wall` 84、四家各 13；庄家首回合 `DrawFromWall` 后庄家 14、`Wall` 83、非庄 13。
- StateHash：布局 v2 金标为空局 51B、富局面 111B（hex 见 `StateHashTests`）；旧 v1 金标（空局 53B、富局面 117B，含牌尾段）不再匹配。
- 作废登记：M0.1 T-4 的旧 `Wall` 69 / `WallTail` 14 发牌金标与旧 v1 StateHash 金标作废，由上述单牌墙/v2 金标替代。

敌对审核：

- review-core：PASS；P0=0、P1=0、P2=3。24 项契约逐条 PASS；独立 Python 重算发牌与 StateHash v2 的 10 个局面全部一致；13/13 变异 KILLED；Core diff 仅 3 文件、Core 内 `WallTail`/`tailCount` 0 命中。P2：StateHash 无 `offset == Measure` 自校验；`ZoneKind` 数值重排属发布前协议编码变更（已核实无旧字节消费方）；审核期间观察 tests 仍引用 `WallTail`、flow.md 的 v1 布局已由后续同步解决。
- review-tests：PASS；P0=0、P1=0、P2=6。独立重算 2 组 `Wall` 84 槽 + 四家手牌 + StateHash Empty/Rich 与字面量逐值一致（45 项断言）；旧 v1 金标可反演互证；530 用例数核实属实；17 个单点变异 KILLED 16/17（生产代码变异 15/15，唯一 SURVIVED 为等价变异且有组合变异反证）；测试作用域恰好 10 文件、无越界。P2 六项：Layout 用例不触达生产实现、oracle 独立性无守卫、「无尾字段」守卫只覆盖公开面、失败路径快照不含 Score/CurrentSeat/RoundNo、两处旧命名注释、跨作用域 M0.1 T-4 旧记录（已由 T-4 限定块处理）。
- review-docs：PASS；P0=0、P1=0、P2=2（均已裁定接受：T-4/T-5 预算余量 37B/43B 未提额；单牌墙 Note 的 Alternatives 保留旧方案）。0 信任复核：首轮 1×P1 与 6×P2 已修并逐项验证，文档事实与代码/测试一致（530/89、51B/111B、84/83 等经独立 Python 重算）；doc-gate 100 files / 0 errors / 29 warnings（25 条既有 G12 + 3×G7 + 1×G12 均来自 .reviews）、build 0 警告 0 错误、test 530/0、WinChecker 89/0；报告 .reviews/m02t1b-review-docs.md（§13 复审段）。

偏差与后续（不阻塞，后续加固）：

- review-core P2：`StateHash` 增加 `offset == Measure` 自校验；`ZoneKind` 数值重排是发布前协议编码变更，已核实无旧字节消费方。
- review-tests P2：oracle 独立性与测试守卫加固；两处旧命名注释清理。
- M0.1 T-4 的实施记录是 2026-10-29 当日历史证据，其墙数/金标与同文件 §2/§3 的现行口径不一致时以 §2/§3 与本文金标重录为准。
- AOT（L3）归 M0.2 T-7。

未跑项：AOT 完整对局（L3，归 M0.2 T-7）；Godot 客户端相关（不在本任务范围）。
