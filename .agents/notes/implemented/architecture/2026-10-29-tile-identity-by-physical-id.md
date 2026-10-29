# Agent Note: 牌的身份只由物理 id 决定

Status: implemented — 相等性只比 `Id`，让 zone/副露/状态哈希共享同一身份语义。

## Problem

`Tile`/`TileType` 原先用 `readonly record struct` 的逐字段相等：`Tile(Id, Type)` 的 `Type` 里又含 `Suit/Rank`。模型里到处需要"同一张牌"的判断——区域去重、`Remove/IndexOf`、`HashSet`/`Dictionary` 键、状态哈希、T-5 的动作树重放——而物理牌的身份只有 `Id`（136 张，1–136）。逐字段相等会让"id 相同但 `Type` 拷贝过期/构造方式不同"的两张牌判不相等，在去重和移除处产生难查的重复/漏删；反过来，若调用方用自造 `Type` 构造牌，身份判断也会被内容细节污染。

## Decision

1. `Tile`/`TileType` 改为 `readonly struct : IEquatable<T>`，手写 `Equals(T)`、`Equals(object?)`、`GetHashCode`、`==`/`!=`，全部只比 `Id`。
2. `TileType` 的 `Suit/Rank` 与 `Tile` 的 `Type` 只作数据，不参与身份；需要牌种比较时显式比 `Type.Id`。
3. 放弃 `record struct`：C# 编译器为 record struct 合成 `==`/`!=`，自定义会 CS0111，无法同时满足"手写相等性"与 record struct。
4. 内容加载负责保证 `id ↔ Suit/Rank` 一致（T-3 校验）；模型不重复校验，也不为不一致数据兜底。

## Alternatives considered

- **保留 record struct 的逐字段相等**：实现零成本，但把内容字段卷进身份；`HashSet` 去重、`Remove(Tile)` 需要在每个调用点手工传同一份 `Type`，容易写出"按 id 找不到"的 bug。
- **用独立 `IEqualityComparer<Tile>`**：能定制，但集合与 API 必须处处显式传 comparer，漏传就静默退回逐字段相等，比改类型更危险。
- **把 `Tile` 做成 class**：身份稳定，但 136 张牌的模型层会有引用相等/分配/可空噪音，且状态哈希与回放想要值语义。
- **在 `Tile` 里去掉 `Type`**：身份干净，但 ViewModel/胡牌判定每次都要回查内容表，模型与内容耦合更重。

## Consequences

- `TileZone` 的重复检测、`Remove/IndexOf`、`Meld` 校验都以 `Id` 为准；测试显式覆盖"同 id 不同 `Type` 相等且同 HashCode"。
- `GetHashCode` 只用 `Id`，`HashSet<Tile>`/`Dictionary<Tile,...>` 可直接用。
- 牌种相等用 `a.Type == b.Type`（同样只比 `Id`）；不要比较 `Suit/Rank` 字段来判断是否同一牌种。
- 与 [确定性发牌 Note](2026-10-29-deterministic-deal-and-pcg32.md) 相关：发牌按物理 id 还原牌种，牌种字段只作数据；与 [区域/副露 Note](2026-10-29-zone-append-only-and-meld-upgrade.md) 相关：zone 去重/移除按 `Id` 匹配。
- `TileType` 的相等性同样只比 `Id`：内容表校验后的 `TileType` 是规范值，任何"同 id 不同花色"的构造只可能来自测试或错误数据。
- T-3 规格 §0 的类型声明同步指向 [TileTypes.cs](../../../../src/Mahjong.Core/Rules/TileTypes.cs)。
