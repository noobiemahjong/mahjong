# Agent Note: 单牌墙，无预留牌尾

Status: proposed — 已拍板落点 M0.2 T-1b；当前代码仍是 Wall 69 + WallTail 14，未改。

## Problem

当前 [M0.1 T-4](../../../../docs/milestones/M0.1/T-4-牌与区域模型.md) 把洗牌后的牌拆成 `Wall` 69 张 + `WallTail` 14 张：正常摸牌从 `Wall` 头，杠后补牌从 `WallTail` 尾，流局只看 `Wall`，`WallTail` 允许剩余。这与旧 Unity 的广东麻将规则不一致：旧版只有一条牌墙，正常摸牌从头，杠后补牌从同一牌墙的末尾，没有预留 14 张的“牌尾”区。该偏差会直接影响发牌、流局、杠补、StateHash、事件里的墙数/尾数、金标和回放。

## Decision

目标模型为**单牌墙**：发牌后只剩一条牌墙；正常摸牌从墙头；杠后补牌从同一牌墙的墙尾；流局判据为牌墙空；不存在 `WallTail` 区，也不存在 `tailCount`。落点定为 **M0.2 T-1b**；庄家每人发 13 张、首回合摸第 14 张；中马从墙头翻；承包与连庄/跟庄/中马独立叠加。当前代码不变，按 M0.2 T-1b 落地。

## Alternatives considered

- 保留 Wall 69 + WallTail 14：与旧版规则不符，流局和杠补行为都会偏；只适合作为临时实现。
- 重开 M0.1 T-4：最严格，但 M0.1 已收口、M0.2 已开工，会打乱“已完成阶段的下一个”口径。
- 放在 M0.3：M0.2 会继续按错误墙模型写流程/事件/测试，返工最大。
- 不修，只在文档标注差异：不能接受，规则、回放、金标会长期不一致。

## Consequences

修正后要同步：`ZoneKind`/`ZoneId` 去掉 `WallTail`；`Deal` 不再拆 14 张；`DrawFromTail` 从牌墙末尾取；`IsWallEmpty` 看整墙；StateHash 去掉牌尾段；`RoundStarted`/`Drew` 只保留 `wallCount`；M0.1 T-4/T-5 金标与单测重录；M0.2 T-2/T-5 规格与事件字段改单墙；M0.3 规则表的墙/中马/12 张表述按单墙复核；DESIGN、core.md、architecture、protocol/replay 同步。

验收判据（M0.2 T-1b 落地时）：发牌后 `Wall.Count = 84`、四家各 13；庄家首回合摸牌后 `Wall.Count = 83`、庄家 14；不存在 `WallTail`；`DrawFromTail` 取走墙尾且 `Wall.Count` 减 1；`IsWallEmpty` 只在整墙空时为真；StateHash 版本 2 且不再含牌尾段；`RoundStarted`/`Drew` 只留 `wallCount`；`dotnet test` 全绿；doc-gate 0 error；金标重录并在 M0.2 结果记录。
