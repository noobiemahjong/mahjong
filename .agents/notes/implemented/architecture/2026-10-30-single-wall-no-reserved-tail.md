# Agent Note: 单牌墙，无预留牌尾

Status: implemented — 牌墙只有一条：正常摸头、杠补墙尾，发牌后 84 张、四家各 13 张。

## Problem

回放、断线重连、Bot 与状态哈希都要求牌墙只有一种顺序、一种发牌口径。广东麻将只有一条牌墙：正常摸牌从墙头，杠后补牌从同一牌墙的末尾，没有预留的「牌尾」区；发牌口径见 [M0.1 T-4](../../../../docs/milestones/M0.1/T-4-牌与区域模型.md)。把洗出的牌拆成主墙加预留区，会让发牌、流局、杠补、StateHash、事件里的墙数与尾数、金标和回放各自多带一个自由度，跨端与跨版本就会分叉。

## Decision

1. 洗牌后 136 张按序全部进 `Wall`；`Deal(rng, bankerSeat)` 按 `(bankerSeat, +1, +2, +3) mod 4` 各发 13 张，发牌时不替庄家摸第 14 张；发牌后 `Wall` 84 张、四家各 13 张、`CurrentSeat = bankerSeat`、只有庄家 `IsBanker`。
2. 庄家首回合从 `Wall` 头摸第 14 张（`Wall` 83、庄家手牌 14），由流程层在回合循环里产生，规则层发牌不代劳。
3. 正常摸牌从 `Wall` 头，杠后补牌从同一 `Wall` 末尾；`IsWallEmpty` 与流局判据都看这一条墙。
4. `ZoneKind` 五值：`Wall=0`、`Hand=1`、`Meld=2`、`Discard=3`、`Revealed=4`；公共区 `ZoneId` 只有 `Wall`，座位区 `Hand`/`Discard` 等仍用 `ZoneId`；不存在牌尾区与墙尾计数字段。
5. `StateHash` 布局版本 2，布局里没有牌尾段；中马从墙头翻。

## Alternatives considered

- 保留主墙 69 + 预留牌尾 14：与广东麻将规则不符，流局和杠补行为都会偏，只适合作为临时实现。
- 重开 M0.1 T-4：返工最小，但 M0.1 已收口、M0.2 已开工，会打乱「已完成阶段的下一个」口径。
- 放到 M0.3 再做：M0.2 会继续按拆区模型写流程/事件/测试，返工最大。
- 不修，只在文档标注差异：规则、回放、金标会长期不一致，不能接受。

## Consequences

- `Deal` 后 `Wall` 84、四家各 13、`CurrentSeat = bankerSeat`；同 seed 同 `bankerSeat` 全槽位一致，是发牌金标。
- 庄家首回合摸牌后 `Wall` 83、庄家 14，非庄仍 13；`DrawFromTail` 取走 `Wall` 末尾一张、`Wall` 减 1；`Wall` 空时摸牌抛 `InvalidOperationException` 且状态不变。
- 发牌金标按单牌墙重录（`Wall` 84 槽位 + 四家各 13 张手牌），StateHash 布局说明为 v2。
- 牌墙相关事件只保留 `wallCount`，不引入尾数。
- 区域与副露语义不变，见 [区域/副露 Note](2026-10-29-zone-append-only-and-meld-upgrade.md)；发牌确定性见 [确定性发牌 Note](2026-10-29-deterministic-deal-and-pcg32.md)；哈希布局见 [状态哈希 Note](2026-10-29-state-hash-byte-layout.md)。
