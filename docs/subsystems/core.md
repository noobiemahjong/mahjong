# 子系统：内容、查表、牌模型与技能

> 本页只写类型、语义与契约；流程见 [architecture](../architecture.md)，决策理由见 [.agents/notes](../../.agents/notes/README.md)；查表文件格式见 [TABLES](../TABLES.md)。

## 1. 内容加载

- 入口 `ContentCatalog.Load(contentDirectory)` 校验 4 个 JSON、`manifest.json` 与 6 个查表 `.bytes` 的哈希并返回只读快照；查表字节只参与哈希、不解析；JSON 与 manifest 的版本必须是 `ContentFormatVersion`（1）。
- 固定清单是 10 个路径：4 个 JSON + `tables/{dictionary,ai_dictionary}_{normal,wind,dragon}.bytes`；`manifest.files` 缺项、多项或 `contentHash` 与重算值不一致都失败。
- `tiles.json` 必须恰好 34 条、id 0–33 依次是万/筒/条 1–9、风 1–4、箭 1–3；`suits.json` 必须恰好 5 条并等于固定值（万/筒/条 9/4/可顺；风 4/4/不可顺；箭 3/4/不可顺）；`skills.json` 必须恰好 4 条，id 1–4 对应 `Pong`、`Kong`、`SelfDrawn`、`SelfDrawnWith7Pairs`。
- `rules.json` 的 `joker.mode` 取 `None`/`FixedType`/`FlipIndicator`；`FixedType` 必须有存在的 `typeId`，`FlipIndicator` 抛 `NotSupportedException`，其余取值抛 `ContentLoadException`。
- contentHash：逐文件 SHA-256 小写 hex，按路径 ordinal 升序拼 `v1\n` + 每行 `path=hash\n`，整体再 SHA-256；`ContentHash` 是原始 32 字节，`ContentHashHex` 是 64 位小写 hex。
- 异常契约：参数 null/空白抛 `ArgumentException`；目录或文件缺失、JSON 不可解析、取值不符、未知字段、哈希不符抛 `ContentLoadException`；JSON 只走源生成上下文 `ContentJsonContext`（camelCase，未知成员报错），生产路径不用反射序列化。
- 查询：`TileTypes`（按 id 升序）、`Suits`（按枚举值升序）、`Skills`（按 id 升序，元素是 `SkillDefinition`）与 `Joker`；三个 `Get*` 在 id 越界或枚举值未定义时抛 `ArgumentOutOfRangeException`。
- `ContentPaths.Resolve()`：`MAHJONG_CONTENT_DIR` 非空白即权威（目录不存在直接失败、不回退）→ `AppContext.BaseDirectory/content` → 从当前目录逐级上找 `content/`，遇含 `global.json` 或 `.git` 的目录即停；失败抛 `ContentLoadException`，返回 content 根绝对路径（`tables` 由调用方拼）。

## 2. 查表

| 表 | 用途 | rank 数 |
|---|---|---|
| `dictionary_normal` | 胡牌（万/筒/条） | 9 |
| `dictionary_wind` | 胡牌（风） | 4 |
| `dictionary_dragon` | 胡牌（箭） | 3 |
| `ai_dictionary_normal` | 牌效（万/筒/条） | 9 |
| `ai_dictionary_wind` | 牌效（风） | 4 |
| `ai_dictionary_dragon` | 牌效（箭） | 3 |

- 文件格式、key 编码与 win/ai 行语义见 [TABLES](../TABLES.md) §2；`suit` 映射万/筒/条 → normal、风 → wind、箭 → dragon。
- `TableKey.Encode(rankCounts)` 的长度必须 3/4/9、每项 0–4，否则抛 `ArgumentException`。
- `WinRow`/`AiRow` 是只读 record struct；`WinRow.WinningRanks` 是 `TableCatalog` 缓存的数组实例，调用方不得修改；`AiRow.Probability` 必须是有限值。
- 加载校验（抛 `InvalidDataException`，消息带表路径，行级错误另带 key 与偏移）：顶层数组不是 1 元素、win 行不是 4 字段、ai 行不是 2 字段、空行列表、重复 key、尾随字节、`HasWin` 与 `WinningRanks` 长度矛盾（成胡时必须为空、未成胡时必须等于花色 rank 数）、补张数大于 4、概率非有限。
- `MsgPackLite` 只接受 positive fixint、uint8–64、fixarray/array16/32、fixmap/map16/32、bool、float64；其他类型码抛 `InvalidDataException` 不做跳过；数组/映射声明元素数超过剩余字节立即失败。
- `Load(tablesDirectory)` 一次读入 6 个文件进 `Dictionary<ulong, WinRow[]>`/`Dictionary<ulong, AiRow[]>`，之后只做字典查找；参数 null/空白抛 `ArgumentException`。
- `TryGetWinRows`/`TryGetAiRows(suit, key, out rows)` 返回 false 表示无表或无该 key（不区分）；未命中 `rows` 是 `Array.Empty`；`WinRowKeyCount`/`AiRowKeyCount` 返回花色的 key 数，无映射花色返回 0。

## 3. 牌与区域模型

- `SuitType`、`ZoneKind`、`MeldKind` 都是 `byte` 枚举，取值 1–5、0–4、1–4，这些数值参与内容、协议与回放编码。
- `TileType(ushort Id, SuitType Suit, byte Rank)`：`Id` 是 `tiles.json` 的 0–33，`Rank` 从 1 起；相等性与 `GetHashCode` 只比 `Id`。
- `Tile(ushort Id, TileType Type)`：物理 id 1–136，等于 `typeId*4+copy+1`；`UnknownId` 是 0，模型内的牌不用 0；相等性只比 `Id`。
- `Suit` 与 `SkillDefinition` 分别来自 `suits.json`/`skills.json`；`JokerRule(Mode, Type?)` 在 `FixedType` 时 `Type` 非空、`None` 时为空。
- `ZoneId(byte Seat, ZoneKind Kind)`：座位 0–3，公共区取 `ZoneId.PublicSeat`（255）。
- `TileZone(ZoneId id, int capacity = 0)` 只允许尾部 `Append`、按槽位 `RemoveAt`，没有插入与排序 API；`capacity` 是初始容量提示（负数抛 `ArgumentOutOfRangeException`），非上限。`Append` 遇到区域内已存在的物理 id、索引器与 `RemoveAt` 越界都抛 `InvalidOperationException` 且区域不变；`Remove` 找不到返回 false、`IndexOf` 找不到返回 -1；`Items` 是不复制的实时只读视图；非线程安全。
- `Meld(kind, tiles, opened, targetSeat)`：碰 3 张、三种杠 4 张；所有牌 `Type.Id` 相同、物理 id 互不相同；`targetSeat` 是 0–3 或 `Meld.NoTargetSeat`（255）；`opened` 与 `kind` 不做交叉校验；构造时复制传入集合，`Tiles` 是实时只读视图；`Kind` 只由碰原地升级为 `KongSupplement`，对象身份不变。
- `PlayerState(byte seat)`：`Hand`、`Discards`、`Melds`（`List<Meld>`，按声明顺序追加）、`Score`（short）、`IsBanker`。
- `GameState`：4 名玩家、物理牌 136 张。`Deal(rng, bankerSeat)` 用 Fisher-Yates 洗 1–136 全部进 `Wall`，按 `(bankerSeat,+1,+2,+3) mod 4` 各发 13 张、发牌时不替庄家摸第 14 张，发牌后 `Wall` 84 张、四家各 13 张，`CurrentSeat = bankerSeat` 且只有庄家 `IsBanker`；同 seed 同 `bankerSeat` 得同一全槽位；`rng` 为 null 抛 `ArgumentNullException`，`bankerSeat` 大于 3 抛 `ArgumentOutOfRangeException`。
- `DrawFromWall(seat)` 从 `Wall` 头部、`DrawFromTail(seat)` 从 `Wall` 末尾取牌并追加到该座位手牌尾部；`Wall` 空抛 `InvalidOperationException`。`IsWallEmpty` 看整条 `Wall`；单牌墙口径的背景见 [单牌墙 Note](../../.agents/notes/implemented/architecture/2026-10-30-single-wall-no-reserved-tail.md)。
- `Discard(seat, handSlot)` 把手牌槽位的牌移到弃牌区尾部，槽位越界抛 `InvalidOperationException`；`CurrentSeat` setter 取 0–3、`RoundNo` setter 取 1–8，越界抛 `ArgumentOutOfRangeException`。
- `DeclareMeld(seat, kind, handSlots, targetSeat, targetDiscardSlot = null)`：手牌槽数碰 2、明杠 3、暗杠 4、补杠 1 且互不重复；碰与明杠要求 `targetSeat` 在 0–3、不等于声明座位、`targetDiscardSlot` 非空，暗杠与补杠要求 255 且 null；校验先于状态修改，失败时状态不变；返回新建副露，补杠返回被原地升级的既有碰；副露本身不摸牌。

## 4. 随机与展示排序

- `IRandomSource` 只暴露 `uint NextUInt()`，实现只依赖实例状态；`Pcg32(ulong seed, ulong stream = 1)` 同 seed/stream 与同一调用序列得到同一序列，实例非线程安全。
- `HandOrdering.SortedForDisplay(tiles[, joker])` 返回新序列，按 `TileType.Id` 升序、鬼牌排最后、相同展示键保持输入顺序；不修改输入，`tiles` 为 null 抛 `ArgumentNullException`；展示序不参与规则与协议。

## 5. 技能与校验器

- `SkillId : uint` 取 Pong 1、Kong 2、SelfDrawn 3、SelfDrawnWith7Pairs 4，数值是协议常量；`Skill` 抽象类的公开成员是 `Id`、`Validate(SkillContext)`、`Commit(SkillContext)`；protected 成员是构造与 `Joker`/鬼牌/槽位工具，`Validate` 只读，`Commit` 先完整复校验再一次性改状态、失败抛 `InvalidOperationException` 且状态不变。
- `SkillContext(state, source, targetCard, drawnTile, targetSeat, requestId)`：`state`/`source` 为 null 抛 `ArgumentNullException`，`source` 与 `state.Players[source.Seat]` 非同引用抛 `ArgumentException`，`targetSeat` 只取 0–3 或 255，否则抛 `ArgumentOutOfRangeException`。
- 碰 `PongSkill`：目标是对方最后弃牌、手牌同 `Type` 非鬼 ≥2、`TargetSeat` 0–3 且不等于 `Source.Seat`；`Commit` 取手牌前两个匹配槽位 + 目标最后弃牌槽位声明 `MeldKind.Pong`，不摸牌，`CurrentSeat = Source.Seat`（强制出牌）。
- 杠 `KongSkill` 按上下文三态：明杠（`TargetCard` 非空 + 手牌 ≥3 + 对方最后弃牌 → `KongExposed`）、暗杠（`TargetCard` 空、无同 `Type` 碰、手牌 ≥4 → `KongConcealed`）、补杠（`TargetCard` 空、有同 `Type` 碰、手牌 ≥1 → `KongSupplement`）；判定顺序固定为明杠 → 补杠 → 暗杠，`DrawnTile` 的牌种优先用于补杠匹配；三种都在声明副露后从墙尾补 1 张并回到 `Source.Seat`，墙空时 `Validate=false` 且 `Commit` 抛异常、状态不变。
- 自摸 `SelfDrawnSkill` 要求 `IsWin` 且非七对，`SelfDrawnWith7PairsSkill` 只要求 `IsSevenPairs`（七对优先，防与普通自摸双算）；两者 `Commit` 只复校验、不改状态。`DrawnTile` 是刚摸到的牌（可空），碰忽略它、杠用其牌种作补杠提示；鬼牌由构造注入，`default(TileType)` 表示无鬼，鬼牌不可作碰/杠材料、不可打出。
- `CardAndSkillVerifier(IWinChecker wins, TileType joker = default).Verify(state, player, skill?, card?, targetCard?)` 单点收口跨动作口径：`state`/`player` 为 null 抛 `ArgumentNullException`；`player` 非同引用、技能值未定义、目标牌不是某座位 `Discards` 最后一张、张数不符都返回 false（出牌 14 且轮到他，碰 13，杠 13–14，自摸与七对 14）；最后以 `card` 作 `DrawnTile` 构造 `SkillContext` 调技能 `Validate`，不改状态。
