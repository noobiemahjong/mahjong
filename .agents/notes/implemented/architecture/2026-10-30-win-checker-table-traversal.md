# Agent Note: 胡牌判定用查表跨花色回溯

Status: implemented — 常规胡与七对由 dictionary_* 表驱动，鬼牌只作万能补位，不做表外补偿。

## Problem

M0.2 需要常规胡（4 面子 + 1 将）、七对、胡牌张三件判定，且必须与 6 张查表逐字节语义一致：[dictionary_*](../../../../docs/TABLES.md) 的每个 key 是一组花色内 rank 计数，value 是若干分解行 `(needGui, jiang, hasWin, hupai)`，同一牌型多行、行间所需鬼牌数与是否含将不同。不用表而直接枚举 4 面子 + 1 将，会绕开唯一事实来源，使 M0.1 的 7 条 fixture 无法作为验收基线，鬼牌补位也容易与表内分解形成第二套口径；按 legacy 的 `GetWinningCardTypes` 用 `hupai` 反推胡牌张，则把听牌枚举与成胡判定混成一条实现。鬼牌（`content/rules.json` 的 FixedType，v1 为红中）既不进 rank 计数又要能补任意缺张；副露按每组 3 张折算、不解析组内牌面。这些口径需要一个可被 T-2/T-4/T-5 注入的接缝。

## Decision

1. `Mahjong.Core.Rules.IWinChecker` 与 `WinChecker(TableCatalog)`：判定只读入参与查表快照，无静态可变状态、无跨调用缓存，同输入重复调用结果一致；`hand` / `melds` 为 null 抛 `ArgumentNullException`，张数非法或同牌种超过 4 张返回 false / 空列表。
2. `IsWin` 只判常规胡：手牌张数 + 3 × 副露组数必须为 14；按 5 个花色组做 rank 计数，鬼牌只计张数、不进 rank 计数；每花色 `TableKey.Encode` 后 `TableCatalog.TryGetWinRows`，无候选该花色直接 false。
3. 只采用 `HasWin == true` 的行：未成胡行是等牌型（听牌）分解并带 `hupai`，不参与 14 张成胡判定。
4. 跨花色回溯：每个有实体牌的花色选一行，累加 `NeedJoker`、记录 `HasPair`，同一花色多行都试，终态恰好一个将。
5. 终态条件：剩余鬼牌 j 满足 `(j % 3 == 0 && 恰一将) || (j % 3 == 2 && 无将)`，即剩余鬼牌全部补成刻子（3 张一组）或将（2 张）。
6. key == 0（该花色无实体牌）直接跳过，不取表里 key0 的行；与 legacy `WinningCardDetector` 同口径。
7. 七对独立判定：手牌恰 14 张，剔除鬼牌后按牌种计数，奇数张数的牌种数 s ≤ 鬼牌数 k 即胡；4 张同牌种按两对计；不读表、不做贪心近似。
8. `GetWinningTypes` 对 34 种牌逐一尝试加入一张（该牌种在手牌 + 副露中现有张数 < 4），命中按牌种 id 升序、去重；与 `IsWin` 同口径，不含七对。非空结果每次新建列表；张数合法但无命中时为空列表；失败早退（张数不是 13 或输入非法）返回共享只读空集合。
9. `joker` 用 `TileType` 表示；无鬼牌传 `default(TileType)`，其 Suit 不在 1–5，牌种 id 0 的一万不会被误当鬼牌；有鬼牌时调用方传 `ContentCatalog.Joker.Type` 的真实牌种。
10. 热路径用 `stackalloc` 保存 rank 计数与 key 缓冲，不分配跨 tick 缓存。

## Alternatives considered

- **暴力枚举 4 面子 + 1 将（不查表）**：实现直接但绕开 6 张表这一唯一事实来源，M0.1 的 fixture 无法作验收基线，鬼牌补位还会与表内分解形成两套语义。
- **按 legacy `WinningCardDetector.GetWinningCardTypes` 用 hupai 反推胡牌张**：`hupai` 语义是「再补哪些张能成胡」，与「加一张后整手是否成胡」在副露与鬼牌分布下不是同一问题；T-2/T-4 需要后者，逐牌种 + `IsWin` 也可与测试交叉验证。
- **跨 tick 缓存或静态单例（legacy 用单例 + DontDestroyOnLoad）**：违反 Core 无静态可变状态与确定性要求；表在构造期一次加载后查询本就是字典查找，缓存没有收益。
- **把 `default(TileType)` 当作牌种 id 0 的鬼牌**：无鬼牌局会把一万误判为万能牌；因此加 Suit 合法性判活约定。
- **对 4 张同风/箭做表外补偿外推**：表 key 只覆盖 rank 计数 0–3，补偿规则会引入表外第二套语义；按规格 §2.3「无候选该花色不可胡」判 false，与 legacy 一致，缺口留给 M4 表重生成。

## Consequences

- T-2/T-4/T-5 通过 `IWinChecker` 注入判定；无鬼牌或其他鬼牌牌种只改 `joker` 实参，不改算法。
- 已知表缺口：`dictionary_wind` / `dictionary_dragon` 的 key 只覆盖 rank 计数 0–3，4 张同风/箭（以及 `dictionary_normal` 少数含 4 的缺失 pattern）判不胡；该类形态在合法规则下可成胡（鬼作将），本阶段表语义优先、会漏判，M4 重评；测试已钉住该行为（[TABLES](../../../../docs/TABLES.md) §4/§5）。
- 依赖方向：`Mahjong.Core.Rules` 依赖同程序集 `Mahjong.Core.Content.Tables`，无程序集环，与 T-1 规格 §1 的构造签名一致。
- 性能：Release 下 200k 次 `IsWin` 平均约 1.1 µs/次（开发机，标准 14 张胡牌样例），满足 10Hz tick 与 Bot 单次决策预算。
- 验收证据见 [T-1 实施记录](../../../../docs/milestones/M0.2/T-1-胡牌与七对.md)。
