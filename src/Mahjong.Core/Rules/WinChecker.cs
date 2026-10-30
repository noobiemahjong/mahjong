using Mahjong.Core.Content.Tables;

namespace Mahjong.Core.Rules;

/// <summary>
/// 基于 <c>dictionary_*</c> 查表的胡牌判定：常规胡（4 面子 + 1 将）与七对。
/// 判定只读入参与注入的 <see cref="TableCatalog"/> 只读快照，无静态可变状态、不分配跨调用缓存，可重复调用且结果一致。
/// </summary>
public sealed class WinChecker : IWinChecker
{
    /// <summary>牌种总数：万/筒/条各 9 张牌种，风 4，箭 3。</summary>
    private const int TileTypeCount = 34;

    /// <summary>常规胡的折算张数：手牌张数 + 3 × 副露组数。</summary>
    private const int WinningHandSize = 14;

    /// <summary>每组副露折算的张数；杠的第 4 张是补张，不参与折算。</summary>
    private const int TilesPerMeld = 3;

    /// <summary>单牌种的物理牌上限，用于识别非法输入。</summary>
    private const int MaxCopiesPerType = 4;

    /// <summary>查表花色组数：万/筒/条/风/箭，与 <see cref="TableCatalog"/> 的 5 个花色映射一致。</summary>
    private const int SuitGroupCount = 5;

    private const int NormalRankCount = 9;
    private const int WindRankCount = 4;
    private const int DragonRankCount = 3;

    private readonly TableCatalog _tables;

    /// <summary>用已加载的查表快照创建判定器。</summary>
    /// <param name="tables">已加载的 6 个查表；判定期只读。</param>
    /// <exception cref="ArgumentNullException"><paramref name="tables"/> 为 null。</exception>
    public WinChecker(TableCatalog tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        _tables = tables;
    }

    /// <inheritdoc />
    public bool IsWin(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds, TileType joker)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(melds);

        if (hand.Count + ((long)TilesPerMeld * melds.Count) != WinningHandSize)
        {
            return false;
        }

        Span<byte> rankCounts = stackalloc byte[TileTypeCount];
        if (!TryCountHand(hand, joker, rankCounts, out int jokerCount))
        {
            return false;
        }

        return TryMatch(rankCounts, jokerCount);
    }

    /// <inheritdoc />
    public IReadOnlyList<TileType> GetWinningTypes(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds, TileType joker)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(melds);

        // 加入一张后折算张数必须正好 14；其他张数没有命中可能，直接返回空列表。
        if (hand.Count + ((long)TilesPerMeld * melds.Count) != WinningHandSize - 1)
        {
            return Array.Empty<TileType>();
        }

        Span<byte> rankCounts = stackalloc byte[TileTypeCount];
        if (!TryCountHand(hand, joker, rankCounts, out int jokerCount))
        {
            return Array.Empty<TileType>();
        }

        // 副露组内牌种只用于“该牌种是否还有第 5 张”的可用性判断，不参与花色分解。
        Span<byte> meldCounts = stackalloc byte[TileTypeCount];
        if (!TryCountMeldTypes(melds, meldCounts))
        {
            return Array.Empty<TileType>();
        }

        bool jokerActive = IsJokerActive(joker);
        List<TileType> winningTypes = [];
        for (int typeId = 0; typeId < TileTypeCount; typeId++)
        {
            bool extraIsJoker = jokerActive && typeId == joker.Id;
            int existingCopies = (extraIsJoker ? jokerCount : rankCounts[typeId]) + meldCounts[typeId];
            if (existingCopies >= MaxCopiesPerType)
            {
                continue;
            }

            bool win;
            if (extraIsJoker)
            {
                win = TryMatch(rankCounts, jokerCount + 1);
            }
            else
            {
                rankCounts[typeId]++;
                win = TryMatch(rankCounts, jokerCount);
                rankCounts[typeId]--;
            }

            if (win)
            {
                winningTypes.Add(CreateTileType(typeId));
            }
        }

        // 按牌种 id 升序遍历；张数合法但无命中时返回本次新建的空列表（非空结果同样新建），失败早退返回共享只读空集合（Array.Empty）。
        return winningTypes;
    }

    /// <inheritdoc />
    public bool IsSevenPairs(IReadOnlyList<Tile> hand, TileType joker)
    {
        ArgumentNullException.ThrowIfNull(hand);

        if (hand.Count != WinningHandSize)
        {
            return false;
        }

        Span<byte> rankCounts = stackalloc byte[TileTypeCount];
        if (!TryCountHand(hand, joker, rankCounts, out int jokerCount))
        {
            return false;
        }

        // 鬼牌剔除后，每种牌按 2 张一对配；奇数张数的牌种各需 1 张鬼牌补对。
        // 手牌恰 14 张时奇偶自动守恒：(鬼牌余量 - 奇牌种数) 必为偶数，因此 s <= k 即能全部配成对。
        int oddTypeCount = 0;
        for (int typeId = 0; typeId < TileTypeCount; typeId++)
        {
            oddTypeCount += rankCounts[typeId] & 1;
        }

        return oddTypeCount <= jokerCount;
    }

    /// <summary>
    /// 按花色查表并做跨花色回溯：每个有实体牌的花色选一行，累加所需鬼牌并记录是否含将，
    /// 终态用剩余鬼牌补面子或将。
    /// </summary>
    /// <param name="rankCounts">按牌种 id 索引的非鬼牌张数，长度 34；花色段依次是万/筒/条 9、风 4、箭 3。</param>
    /// <param name="jokerCount">手牌中的鬼牌张数。</param>
    /// <returns>能分解为 4 面子 + 恰好 1 将时返回 true。</returns>
    private bool TryMatch(ReadOnlySpan<byte> rankCounts, int jokerCount)
    {
        Span<ulong> keys = stackalloc ulong[SuitGroupCount];
        Span<byte> present = stackalloc byte[SuitGroupCount];
        for (int group = 0; group < SuitGroupCount; group++)
        {
            (int offset, int length) = GroupRange(group);
            ReadOnlySpan<byte> ranks = rankCounts.Slice(offset, length);
            int tileCount = 0;
            for (int index = 0; index < ranks.Length; index++)
            {
                tileCount += ranks[index];
            }

            // key==0 表示该花色没有实体牌：不取表里 key0 的行，等价于该花色贡献 0 组面子且不含将。
            if (tileCount == 0)
            {
                continue;
            }

            ulong key = TableKey.Encode(ranks);
            if (!_tables.TryGetWinRows(SuitOf(group), key, out _))
            {
                // 该花色有牌但查表无候选行：任何分解都不可能，整手不胡。
                return false;
            }

            keys[group] = key;
            present[group] = 1;
        }

        return Search(0, 0, 0, jokerCount, keys, present);
    }

    /// <summary>跨花色回溯：逐花色选一行，累加所需鬼牌与将数，终态判定是否成胡。</summary>
    private bool Search(
        int group,
        int usedJokers,
        int pairCount,
        int jokerCount,
        ReadOnlySpan<ulong> keys,
        ReadOnlySpan<byte> present)
    {
        if (group == SuitGroupCount)
        {
            // 剩余鬼牌补面子（3 张一组）或补将（2 张）：成胡要求恰好一个将。
            int remainingJokers = jokerCount - usedJokers;
            return ((remainingJokers % TilesPerMeld == 0) && pairCount == 1)
                || ((remainingJokers % TilesPerMeld == 2) && pairCount == 0);
        }

        if (present[group] == 0)
        {
            return Search(group + 1, usedJokers, pairCount, jokerCount, keys, present);
        }

        _tables.TryGetWinRows(SuitOf(group), keys[group], out IReadOnlyList<WinRow> rows);
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            WinRow row = rows[rowIndex];
            // 只有 HasWin 行描述已完成的分解；未成胡行是等牌型（听牌）分解，不参与 14 张成胡判定。
            if (!row.HasWin)
            {
                continue;
            }

            int nextUsedJokers = usedJokers + row.NeedJoker;
            if (nextUsedJokers > jokerCount)
            {
                continue;
            }

            int nextPairCount = pairCount + (row.HasPair ? 1 : 0);
            if (nextPairCount > 1)
            {
                continue;
            }

            if (Search(group + 1, nextUsedJokers, nextPairCount, jokerCount, keys, present))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 统计手牌：鬼牌只计入 <paramref name="jokerCount"/>、不进入 rank 计数；
    /// 其余按牌种 id 计数，越界牌种或同牌种超过 4 张视为非法输入。
    /// </summary>
    private static bool TryCountHand(
        IReadOnlyList<Tile> hand,
        TileType joker,
        Span<byte> rankCounts,
        out int jokerCount)
    {
        rankCounts.Clear();
        jokerCount = 0;
        bool jokerActive = IsJokerActive(joker);
        for (int index = 0; index < hand.Count; index++)
        {
            ushort typeId = hand[index].Type.Id;
            if (typeId >= TileTypeCount)
            {
                return false;
            }

            if (jokerActive && typeId == joker.Id)
            {
                jokerCount++;
                if (jokerCount > MaxCopiesPerType)
                {
                    return false;
                }

                continue;
            }

            int count = rankCounts[typeId] + 1;
            if (count > MaxCopiesPerType)
            {
                return false;
            }

            rankCounts[typeId] = (byte)count;
        }

        return true;
    }

    /// <summary>统计副露组内各牌种的张数，只用于 <see cref="GetWinningTypes"/> 判断该牌种是否还有余量。</summary>
    private static bool TryCountMeldTypes(IReadOnlyList<Meld> melds, Span<byte> meldCounts)
    {
        meldCounts.Clear();
        for (int meldIndex = 0; meldIndex < melds.Count; meldIndex++)
        {
            Meld? meld = melds[meldIndex];
            if (meld is null)
            {
                return false;
            }

            IReadOnlyList<Tile> tiles = meld.Tiles;
            for (int tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
            {
                ushort typeId = tiles[tileIndex].Type.Id;
                if (typeId >= TileTypeCount)
                {
                    return false;
                }

                int count = meldCounts[typeId] + 1;
                if (count > MaxCopiesPerType)
                {
                    return false;
                }

                meldCounts[typeId] = (byte)count;
            }
        }

        return true;
    }

    /// <summary>
    /// 判断 <paramref name="joker"/> 是否指向本局的鬼牌牌种。
    /// <see cref="TileType"/> 没有 null 状态，无鬼牌的局调用方传 <c>default</c>：其 Suit 不在 1–5，
    /// 因此不会把牌种 id 0（一万）的实体牌误认成鬼牌；Id 越界同样视为无鬼牌。
    /// </summary>
    private static bool IsJokerActive(TileType joker) =>
        joker.Id < TileTypeCount && (byte)joker.Suit is >= (byte)SuitType.Character and <= (byte)SuitType.Dragon;

    /// <summary>花色组在按 id 索引的计数数组中的偏移与长度。</summary>
    private static (int Offset, int Length) GroupRange(int group) => group switch
    {
        0 => (0, NormalRankCount),
        1 => (NormalRankCount, NormalRankCount),
        2 => (2 * NormalRankCount, NormalRankCount),
        3 => (3 * NormalRankCount, WindRankCount),
        _ => ((3 * NormalRankCount) + WindRankCount, DragonRankCount),
    };

    /// <summary>花色组对应的查表花色。</summary>
    private static SuitType SuitOf(int group) => group switch
    {
        0 => SuitType.Character,
        1 => SuitType.Dot,
        2 => SuitType.Bamboo,
        3 => SuitType.Wind,
        _ => SuitType.Dragon,
    };

    /// <summary>
    /// 按内容格式的固定映射构造牌种：id 0–33 依次是万/筒/条 1–9、风 1–4、箭 1–3。
    /// 用于 <see cref="GetWinningTypes"/> 返回手牌与副露里不存在的牌种。
    /// </summary>
    private static TileType CreateTileType(int typeId) => typeId switch
    {
        < 9 => new TileType((ushort)typeId, SuitType.Character, (byte)(typeId + 1)),
        < 18 => new TileType((ushort)typeId, SuitType.Dot, (byte)(typeId - 8)),
        < 27 => new TileType((ushort)typeId, SuitType.Bamboo, (byte)(typeId - 17)),
        < 31 => new TileType((ushort)typeId, SuitType.Wind, (byte)(typeId - 26)),
        _ => new TileType((ushort)typeId, SuitType.Dragon, (byte)(typeId - 30)),
    };
}
