using Mahjong.Core.Rules;

namespace Mahjong.Tests.Rules;

/// <summary>
/// WinChecker 测试的牌/副露构造：按 content/tiles.json 的固定公式（物理 id = typeId*4 + copy + 1）分配物理牌，
/// 手牌与副露共用同一套 copy 计数，同一牌种累计超过 4 张立即失败（物理张数合法性守门）。
/// </summary>
internal static class WinCheckerTestData
{
    /// <summary>内容配置的鬼牌牌种 id（content/rules.json：FixedType 31，即红中）。</summary>
    public const int JokerTypeId = 31;

    /// <summary>content/rules.json 配置的鬼牌牌种。</summary>
    public static readonly TileType Joker = RulesTestData.TypeOf(JokerTypeId);

    /// <summary>无鬼牌时调用方传入的 <c>default(TileType)</c>。</summary>
    public static readonly TileType NoJoker = default;

    /// <summary>金标里的可空鬼牌 id 转成入参：null 表示无鬼牌（default）。</summary>
    /// <param name="jokerTypeId">金标中的鬼牌牌种 id，null 表示无鬼。</param>
    public static TileType JokerOrDefault(int? jokerTypeId) =>
        jokerTypeId.HasValue ? RulesTestData.TypeOf(jokerTypeId.Value) : default;

    /// <summary>按牌种 id 序列构造手牌，每牌种的 copy 从 0 起顺序分配。</summary>
    /// <param name="typeIds">牌种 id 序列，取值 0–33。</param>
    public static List<Tile> Hand(params int[] typeIds)
    {
        ArgumentNullException.ThrowIfNull(typeIds);
        int[] copies = new int[RulesTestData.TileTypeCount];
        List<Tile> hand = new(typeIds.Length);
        foreach (int typeId in typeIds)
        {
            hand.Add(Next(typeId, copies));
        }

        return hand;
    }

    /// <summary>按金标构造手牌与副露；副露张数由种类决定（碰 3 张、杠 4 张），全部物理 id 互不重复且同牌种 ≤ 4 张。</summary>
    /// <param name="goldenCase">金标用例。</param>
    public static (List<Tile> Hand, List<Meld> Melds) BuildCase(
        WinCheckerGoldenVectors.WinCheckerGoldenCase goldenCase)
    {
        ArgumentNullException.ThrowIfNull(goldenCase);
        int[] copies = new int[RulesTestData.TileTypeCount];
        List<Tile> hand = new(goldenCase.Hand.Length);
        foreach (int typeId in goldenCase.Hand)
        {
            hand.Add(Next(typeId, copies));
        }

        List<Meld> melds = new(goldenCase.Melds.Length);
        foreach (WinCheckerGoldenVectors.GoldenMeld spec in goldenCase.Melds)
        {
            int tileCount = spec.Kind == MeldKind.Pong ? 3 : 4;
            Tile[] tiles = new Tile[tileCount];
            for (int index = 0; index < tileCount; index++)
            {
                tiles[index] = Next(spec.TypeId, copies);
            }

            melds.Add(new Meld(spec.Kind, tiles, opened: true, targetSeat: Meld.NoTargetSeat));
        }

        return (hand, melds);
    }

    /// <summary>构造碰（3 张同牌种，copy 从 <paramref name="firstCopy"/> 起）。</summary>
    /// <param name="typeId">牌种 id。</param>
    /// <param name="firstCopy">起始 copy，默认 0。</param>
    public static Meld Pong(int typeId, int firstCopy = 0) => MeldOf(MeldKind.Pong, typeId, 3, firstCopy);

    /// <summary>构造杠（4 张同牌种，copy 从 <paramref name="firstCopy"/> 起）。</summary>
    /// <param name="kind">杠的种类。</param>
    /// <param name="typeId">牌种 id。</param>
    /// <param name="firstCopy">起始 copy，默认 0。</param>
    public static Meld Kong(MeldKind kind, int typeId, int firstCopy = 0) => MeldOf(kind, typeId, 4, firstCopy);

    /// <summary>把牌种 id 列表转成 id 序列，便于与 int[] 期望值比较。</summary>
    /// <param name="types">牌种序列。</param>
    public static int[] TypeIds(IEnumerable<TileType> types) =>
        types.Select(type => (int)type.Id).ToArray();

    /// <summary>手牌物理 id 快照，用于断言判定不修改入参。</summary>
    /// <param name="hand">手牌。</param>
    public static string Snapshot(IEnumerable<Tile> hand) => string.Join(",", hand.Select(tile => tile.Id));

    /// <summary>副露快照（种类、明暗、目标座位、物理 id 序列），用于断言判定不修改入参。</summary>
    /// <param name="melds">副露。</param>
    public static string Snapshot(IEnumerable<Meld> melds) =>
        string.Join("|", melds.Select(meld =>
            $"{(byte)meld.Kind}/{meld.Opened}/{meld.TargetSeat}/{Snapshot(meld.Tiles)}"));

    /// <summary>某牌种在手牌与副露中的实体牌总数（用于 GetWinningTypes 的 4 张上限）。</summary>
    /// <param name="hand">手牌。</param>
    /// <param name="melds">副露。</param>
    /// <param name="typeId">牌种 id。</param>
    public static int CountType(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds, int typeId)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(melds);
        int count = 0;
        for (int index = 0; index < hand.Count; index++)
        {
            if (hand[index].Type.Id == typeId)
            {
                count++;
            }
        }

        for (int meldIndex = 0; meldIndex < melds.Count; meldIndex++)
        {
            IReadOnlyList<Tile> tiles = melds[meldIndex].Tiles;
            for (int tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
            {
                if (tiles[tileIndex].Type.Id == typeId)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static Meld MeldOf(MeldKind kind, int typeId, int tileCount, int firstCopy)
    {
        Tile[] tiles = new Tile[tileCount];
        for (int index = 0; index < tileCount; index++)
        {
            tiles[index] = RulesTestData.Physical(typeId, firstCopy + index);
        }

        return new Meld(kind, tiles, opened: true, targetSeat: Meld.NoTargetSeat);
    }

    private static Tile Next(int typeId, int[] copies)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(typeId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(typeId, RulesTestData.TileTypeCount);
        int copy = copies[typeId];
        if (copy >= 4)
        {
            throw new InvalidOperationException(
                $"Tile type {typeId} already has 4 physical tiles in this case; a fifth copy is illegal.");
        }

        copies[typeId] = copy + 1;
        return RulesTestData.Physical(typeId, copy);
    }
}
