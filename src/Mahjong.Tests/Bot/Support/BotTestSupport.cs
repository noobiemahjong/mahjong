using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Tests.Rules;

namespace Mahjong.Tests.Bot;

/// <summary>
/// Bot 测试的共享支撑：按 content/tiles.json 固定公式（物理 id = typeId*4 + copy + 1）造牌、
/// 构造测试局面、提供状态快照/哈希与只替代外部依赖的 stub。被测的
/// <see cref="BotContext"/>、<see cref="SimpleBotPolicy"/> 与真实 <see cref="CardAndSkillVerifier"/> 不用替身。
/// </summary>
internal static class BotTestSupport
{
    /// <summary>content/rules.json 配置的鬼牌牌种 id（FixedType 31，红中）。</summary>
    public const int JokerTypeId = 31;

    /// <summary>content/rules.json 配置的鬼牌牌种。</summary>
    public static readonly TileType Joker = RulesTestData.TypeOf(JokerTypeId);

    /// <summary>填充牌：type id 18–30 各 copy 0（共 13 张），与 Bot 用例使用的 0–13、17、31 牌种不重叠。</summary>
    private static readonly Tile[] FillerTiles =
    [
        T(18, 0), T(19, 0), T(20, 0), T(21, 0), T(22, 0), T(23, 0), T(24, 0),
        T(25, 0), T(26, 0), T(27, 0), T(28, 0), T(29, 0), T(30, 0),
    ];

    /// <summary>创建空局（四家空手牌，牌墙为空，当前座位 0）。</summary>
    public static GameState NewState() => new();

    /// <summary>按物理牌公式构造一张牌。</summary>
    /// <param name="typeId">牌种 id 0–33。</param>
    /// <param name="copy">同牌种内第几张（0–3）。</param>
    public static Tile T(int typeId, int copy) => RulesTestData.Physical(typeId, copy);

    /// <summary>按牌种 id 序列构造手牌，每牌种 copy 从 0 起顺序分配，保证物理 id 在同一用例内唯一。</summary>
    /// <param name="typeIds">牌种 id 序列。</param>
    public static List<Tile> Hand(params int[] typeIds)
    {
        int[] copies = new int[RulesTestData.TileTypeCount];
        List<Tile> hand = new(typeIds.Length);
        foreach (int typeId in typeIds)
        {
            int copy = copies[typeId];
            if (copy >= 4)
            {
                throw new InvalidOperationException(
                    $"Tile type {typeId} already has 4 physical tiles in this hand.");
            }

            copies[typeId] = copy + 1;
            hand.Add(T(typeId, copy));
        }

        return hand;
    }

    /// <summary>取前 <paramref name="count"/> 张填充牌（0–13）。</summary>
    public static Tile[] Fillers(int count) => FillerTiles.Take(count).ToArray();

    /// <summary>向指定座位手牌尾部追加牌。</summary>
    public static void FillHand(GameState state, byte seat, params Tile[] tiles) =>
        RulesTestData.Fill(state.Players[seat], tiles);

    /// <summary>向指定座位弃牌区尾部追加牌。</summary>
    public static void FillDiscards(GameState state, byte seat, params Tile[] tiles) =>
        RulesTestData.Discard(state.Players[seat], tiles);

    /// <summary>向牌墙尾部追加牌。</summary>
    public static void FillWall(GameState state, params Tile[] tiles)
    {
        foreach (Tile tile in tiles)
        {
            state.Wall.Append(tile);
        }
    }

    /// <summary>全部区域的完整快照，用于断言调用不改状态。</summary>
    public static string Snapshot(GameState state) => RulesTestData.Snapshot(state);

    /// <summary>权威局面哈希，用于断言调用不改状态。</summary>
    public static string Hash(GameState state) => StateHash.Compute(state);

    /// <summary>局面上所有区域的物理牌总数（牌墙 + 手牌 + 弃牌 + 副露）。</summary>
    public static int TotalTiles(GameState state) => RulesTestData.TotalTileCount(state);

    /// <summary>自己回合、手牌 14 张（牌种 0–13 各一张），当前座位 0。</summary>
    public static GameState DiscardBoard14()
    {
        GameState state = NewState();
        FillHand(state, 0, [.. Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13)]);
        return state;
    }

    /// <summary>自己回合但手牌只有 13 张：出牌分支没有任何合法牌。</summary>
    public static GameState ThrowBoard13()
    {
        GameState state = NewState();
        FillHand(state, 0, [.. Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)]);
        return state;
    }

    /// <summary>
    /// 弃牌响应局面：座位 0 手牌 13 张（3 张 5万 + 10 张填充），座位 1 弃牌 [6万, 5万c3]，
    /// 当前座位 1；牌墙按 <paramref name="wallTile"/> 决定是否有杠补牌。
    /// 同一局面下碰与明杠都合法。
    /// </summary>
    public static GameState PongKongBoard(out Tile target, bool wallTile = true)
    {
        GameState state = NewState();
        FillHand(state, 0, [T(5, 0), T(5, 1), T(5, 2), .. Fillers(10)]);
        FillDiscards(state, 1, T(6, 0), T(5, 3));
        if (wallTile)
        {
            FillWall(state, T(29, 0));
        }

        state.CurrentSeat = 1;
        target = state.Players[1].Discards[1];
        return state;
    }

    /// <summary>弃牌响应局面：座位 0 手牌 13 张（1 张 5万 + 12 张填充），碰与杠都不合法。</summary>
    public static GameState PassBoard()
    {
        GameState state = NewState();
        FillHand(state, 0, [T(5, 0), .. Fillers(12)]);
        FillDiscards(state, 1, T(5, 1));
        state.CurrentSeat = 1;
        return state;
    }

    /// <summary>弃牌响应局面：座位 1 最后一张弃牌是鬼牌，碰与杠都不接受鬼牌。</summary>
    public static GameState JokerTargetBoard()
    {
        GameState state = NewState();
        FillHand(state, 0, Fillers(13));
        FillDiscards(state, 1, T(JokerTypeId, 0));
        state.CurrentSeat = 1;
        return state;
    }
}

/// <summary>
/// 可编程的胡牌判定替身：只返回构造后设定的两个布尔，并按调用顺序记录方法名；
/// 只替代外部依赖，用于钉住「七对优先于普通自摸」的分支顺序与门槛。
/// </summary>
internal sealed class ProgrammableWinChecker : IWinChecker
{
    /// <summary><see cref="IsWin"/> 的返回值。</summary>
    public bool Win { get; set; }

    /// <summary><see cref="IsSevenPairs"/> 的返回值。</summary>
    public bool SevenPairs { get; set; }

    /// <summary>按调用顺序记录的方法名。</summary>
    public List<string> Calls { get; } = [];

    /// <inheritdoc />
    public bool IsWin(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds, TileType joker)
    {
        Calls.Add("IsWin");
        return Win;
    }

    /// <inheritdoc />
    public IReadOnlyList<TileType> GetWinningTypes(
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Meld> melds,
        TileType joker) => throw new NotSupportedException("Bot 策略不调用 GetWinningTypes。");

    /// <inheritdoc />
    public bool IsSevenPairs(IReadOnlyList<Tile> hand, TileType joker)
    {
        Calls.Add("IsSevenPairs");
        return SevenPairs;
    }
}

/// <summary>计数随机源：记录 <see cref="NextUInt"/> 调用次数，用于证明策略不消费随机。</summary>
internal sealed class CountingRandomSource : IRandomSource
{
    private readonly Pcg32 _inner = new(1);

    /// <summary>被调用的次数。</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public uint NextUInt()
    {
        Calls++;
        return _inner.NextUInt();
    }
}
