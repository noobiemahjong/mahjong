# Agent Note: 洗牌与发牌的确定性契约

Status: implemented — 规则层只接受注入的 `IRandomSource`，发牌步骤与切分点固定，同 seed 同全槽位。

## Problem

回放、断线重连、Bot 与状态哈希都要求"同一 seed 得到同一局面"。`System.Random` 的实现随 .NET 版本/平台变化，不能作为确定性来源；规则层又不允许静态全局 RNG、时钟或网络。发牌细节（谁先摸、牌尾留几张、是否拒绝采样）只要有一个自由度，跨端/跨版本就会分叉。

## Decision

1. 规则层只依赖 `IRandomSource.NextUInt()`；具体实现 `Pcg32(seed, stream)` 放 `Mahjong.Core.Rules`，状态为实例字段、无静态。
2. 发牌严格按 T-4 §3.1：`order = 1..136` 做 Fisher-Yates（`i` 从 135 递减到 1，`j = NextUInt() % (i+1)`），`WallTail = order[122..136)`、`Wall = order[0..122)`；按 `(banker, +1, +2, +3) % 4` 每人从牌墙头连续取 13 张，庄家再取 1 张。
3. 取模不做拒绝采样：跨实现的一致性优先于统计完美；modulo bias 记录在此，不作为缺陷修。
4. `Deal` 先重置牌墙/牌尾/四家手牌/副露/弃牌与庄家标记（保留 `Score/RoundNo`），再发牌；可重复调用，同 seed/banker 必得同全槽位。
5. 物理牌 `Id = typeId*4+copy+1` 的牌种映射按 `content/tiles.json` v1 固定布局在规则层还原（万/筒/条 1–9、风 1–4、箭 1–3）；`GameState` 不依赖 Content 程序集，内容布局变更按基线变更流程处理。
6. 洗牌只发生一次；之后只经 `DrawFromWall`（头）/`DrawFromTail`（尾）取牌。

## Alternatives considered

- **`System.Random`**：不用写代码，但序列不跨版本保证，直接违背回放/哈希确定性。
- **加密随机 + 记录种子**：种子仍要传给回放，且实现跨平台差异不可控；PCG32 足够且可复现。
- **拒绝采样消除 modulo bias**：统计更干净，但会让"j = NextUInt() % (i+1)"的规格不可按步验证，跨端实现容易分叉。
- **轮转发牌（真实麻将庄家先拿 4 张等）**：更贴近真人手感，但规格已固定为"每人连续 13 张"；改成轮发要同时改金标与回放契约。
- **`Deal(rng, bankerSeat, ContentCatalog)`**：牌种映射可复用内容表，但把 Core 规则层绑到内容加载器，且 M0.1 的 Core 不引用 Content 目录；代价大于收益。

## Consequences

- 同 seed + 同 bankerSeat 的 `Deal` 全槽位可作金标；测试用独立实现的跨语言向量锁定。
- 改算法/切分点/座位顺序 = 改回放契约：必须写新 Note 并按基线变更流程处理，不能静默改。
- `Deal` 后 `Wall=69`、`WallTail=14`；流局只看 `Wall` 空，牌尾允许余额。
- 牌种映射与 `content/tiles.json` 的布局耦合：换牌表布局需同步改规则层映射与金标。
- `Pcg32.NextUInt` 的溢出依赖 unchecked 环绕：项目未开启 `CheckForOverflowUnderflow`（`Directory.Build.props` 未设置）；改该构建配置会改变 `Pcg32` 语义，必须复跑 PCG32/Deal 金标。
- 与 [牌身份 Note](2026-10-29-tile-identity-by-physical-id.md) 相关：发牌按物理 id 还原牌种，身份只比 `Id`；与 [区域/副露 Note](2026-10-29-zone-append-only-and-meld-upgrade.md) 相关：牌墙切分点与 zone 只追加的槽位序共同决定回放顺序。
