# Agent Note: 本地视图/回放视图不是在线 Mirror

Status: implemented

## Problem

DESIGN 规定「客户端 Mirror 跑同一套 Flow」，但 M0.2 单机与 M1 回放在同一进程内已有权威 `GameSession`/回放文件；再跑一套 Flow 会重复实现，且回放没有输入命令流，无法重建权威状态。M0.2 T-6 因此写了事件 apply 的 `GameMirror`，与 DESIGN 冲突。

## Decision

1. **M0.2 单机**：Client 直接读同进程的 `GameSession.State`，视图层只做投影（手牌排序/按钮可用性）；不另建第二套 Flow/Mirror。
2. **M1 回放**：`ReplayView` 应用已录制事件只做显示；权威终态来自回放文件 Trailer 的 `finalStateHash`，不用视图状态算哈希。
3. **M2+ 在线**：客户端 Mirror 仍按 DESIGN「跑同一套 Flow」实现；本 Note 只豁免本地/回放两种同进程场景。
4. 任何“可视状态”都不得成为权威；权威状态只在 `GameSession`（在线在服务器）。

## Alternatives considered

- 让 M0.2 也跑 Flow：重复实现、T-6 成本翻倍，且本地无网络收益。
- 让回放重跑 Flow：回放没有输入命令流，无法保证与权威一致；Trailer 已有权威哈希。
- 改 DESIGN 全面改成事件 apply：会推翻在线 Mirror 的省流量设计。

## Consequences

- M0.2 T-6 删除 `GameMirror/MirrorState`，改为 `GameSession.State` + ViewModel。
- M1 T-3 用 `ReplayView`，不叫 Mirror；金标比较 Trailer `finalStateHash`。
- 在线 Mirror（M2+）不受影响；若实现时选择事件 apply，需另写 Note 改 DESIGN。
