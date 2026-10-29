# Agent Note: 区域只追加、副露原地升级

Status: implemented — zone 保插入序、排序留给展示层；补杠替换同型碰而不新增副露。

## Problem

牌墙/手牌/弃牌需要在流程与回放里有唯一顺序：发牌、摸牌、出牌、碰杠都按发生顺序落槽。若模型层提供插入/排序，规则层与展示层会各自排序，回放与状态哈希对不上。副露方面，补杠（碰 + 手牌第 4 张）若新增一条副露，会破坏"副露数 = 手牌折算"的记账，并在重放时产生两种表示。

## Decision

1. `TileZone` 只有尾部 `Append`、按槽位 `RemoveAt`、按 `Id` `Remove/IndexOf`；不提供插入与排序 API。
2. 槽位域固定：手牌槽位 = `Hand.Items` 下标；弃牌槽位 = `Discards.Items` 下标；`DeclareMeld` 只接受手牌槽位，`Discarded` 类事件才用弃牌槽位。
3. 展示排序是纯函数 [HandOrdering](../../../../src/Mahjong.Core/Rules/HandOrdering.cs)：普通牌按 `Type.Id`、鬼牌最后、稳定；返回新序列，不改 zone。
4. 补杠在既有 `Meld` 上原地升级：`Kind` 改 `KongSupplement`、追加第 4 张、保留对象身份与 `Opened/TargetSeat`，不新增副露。
5. `Meld` 构造防御性拷贝传入集合；`Tiles` 是 Meld 自身存储的实时只读视图（补杠后可见第 4 张，但调用方集合的后续修改不泄漏）。
6. `DeclareMeld` 先完成全部校验与预取，再改状态；失败路径不改任何 zone/副露（原子性）。副露本身不摸牌，杠的补牌由流程层 `DrawFromTail` 负责。

## Alternatives considered

- **区域内置排序**：手牌展示方便，但流程顺序与展示顺序绑死，回放/哈希要依赖排序规则版本，且不同座位视角会想按不同键排。
- **单独的 `Hand` 类型带排序**：多一层类型，规则层仍然需要无序槽位，收益只是把`TileZone`的少量能力改名字。
- **补杠新建 Meld 替换列表元素**：语义也可行，但持有旧 Meld 引用的动作树/请求需要重绑，补杠"不新增副露"的判据也要额外维护。
- **`DeclareMeld` 顺便摸补牌**：调用方少一步，但把"摸打"和"副露"两个事务揉进一个方法，流程层无法在杠后插入请求/事件。

## Consequences

- T-5 状态哈希按槽位顺序编码；zone 顺序即流程事实，不需要额外排序。
- ViewModel 负责手牌排序；协议/回放只传槽位与 id，不传展示序。
- 补杠后手牌折算仍是 3 张（4 张里 1 张视为补张），摸打流程按 `Hand.Count` 记账时必须配合"杠 = 3 折算"的规则（M0.2 胡牌判定读取副露）。
- `Meld.Tiles` 不能强转 `List` 修改；补杠只能经 `DeclareMeld` 触发。
- 与 [牌身份 Note](2026-10-29-tile-identity-by-physical-id.md) 相关：槽位去重与移除按物理 `Id`；与 [确定性发牌 Note](2026-10-29-deterministic-deal-and-pcg32.md) 相关：发牌切分点决定牌墙/牌尾的初始槽位序。
