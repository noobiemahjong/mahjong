using Mahjong.Core.Flow;
using Mahjong.Core.Rules;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 计分与轮次测试的共享支撑：分数复制、失败路径的账本/局面快照与状态哈希，
/// 以及只替代外部依赖（<see cref="IWinChecker"/>、<see cref="IRandomSource"/>）的 stub。
/// 被测的 <see cref="ScoreManager"/>、<see cref="RoundController"/> 与 <see cref="GameState"/> 不用替身。
/// </summary>
internal static class RoundTestSupport
{
    /// <summary>content/rules.json 配置的鬼牌牌种 id（FixedType 31，红中）。</summary>
    public const int JokerTypeId = 31;

    /// <summary>content/rules.json 配置的鬼牌牌种。</summary>
    public static readonly TileType Joker = RulesTestData.TypeOf(JokerTypeId);

    /// <summary>无鬼牌时调用方传入的 <c>default(TileType)</c>。</summary>
    public static readonly TileType NoJoker = default;

    /// <summary>把只读分数序列复制为数组，便于按具体数值断言。</summary>
    /// <param name="scores">分数序列。</param>
    public static short[] CopyScores(IReadOnlyList<short> scores) => scores.ToArray();

    /// <summary>账本的完整快照：本局暂存、番种聚合与累计总分，用于断言失败调用不改状态。</summary>
    /// <param name="scores">账本。</param>
    public static string Book(ScoreManager scores) =>
        $"delta={string.Join(",", scores.RoundDelta)};action={(ushort)scores.RoundActionType};"
        + $"total={string.Join(",", scores.TotalScores)}";

    /// <summary>全部区域的完整快照（牌墙、手牌、弃牌、副露、庄家标记）。</summary>
    /// <param name="state">局面。</param>
    public static string Snapshot(GameState state) => RulesTestData.Snapshot(state);

    /// <summary>权威局面哈希。</summary>
    /// <param name="state">局面。</param>
    public static string Hash(GameState state) => StateHash.Compute(state);

    /// <summary>局面上所有区域的物理牌总数。</summary>
    /// <param name="state">局面。</param>
    public static int TotalTiles(GameState state) => RulesTestData.TotalTileCount(state);

    /// <summary>
    /// 尝试通过 <see cref="IList{T}"/> 写只读分数返回值。契约允许「返回拷贝」或「只读包装」两种实现，
    /// 因此不绑定异常类型：只读包装抛 <see cref="NotSupportedException"/> 属于允许行为；
    /// 调用方负责断言写入尝试后的值/内部状态未变。
    /// </summary>
    /// <param name="scores">被测的只读分数序列。</param>
    /// <param name="index">要写的下标。</param>
    /// <param name="value">要写入的值。</param>
    public static void TryWriteScore(IReadOnlyList<short> scores, int index, short value)
    {
        var view = (IList<short>)scores;
        try
        {
            view[index] = value;
        }
        catch (NotSupportedException)
        {
            // 只读包装实现：写入被拒是契约允许的选择，内部值由调用方断言未变。
        }
    }
}

/// <summary>
/// 一次胡牌判定的入参快照：方法名、手牌张数、副露组数、鬼牌牌种与手牌首牌，
/// 用于钉住轮次驱动传给 <see cref="IWinChecker"/> 的 hand/melds/joker。
/// </summary>
/// <param name="Method">被调用的方法名：<c>IsWin</c> 或 <c>IsSevenPairs</c>。</param>
/// <param name="HandCount">入参手牌张数。</param>
/// <param name="MeldCount">入参副露组数；<c>IsSevenPairs</c> 没有副露入参，记 null。</param>
/// <param name="JokerId">入参鬼牌的 <see cref="TileType.Id"/>；<c>default(TileType)</c> 为 0。</param>
/// <param name="FirstTileId">入参手牌首牌的物理 id；手牌为空记 0。</param>
internal readonly record struct WinCheckerCall(
    string Method,
    int HandCount,
    int? MeldCount,
    ushort JokerId,
    ushort FirstTileId);

/// <summary>
/// 可编程的胡牌判定替身：只返回构造后设定的两个布尔，并按调用顺序记录 <c>IsWin</c>/<c>IsSevenPairs</c>，
/// 用于钉住轮次驱动「七对优先」的判定顺序与调用次数。
/// </summary>
internal sealed class StubWinChecker : IWinChecker
{
    /// <summary><see cref="IsWin"/> 的返回值。</summary>
    public bool Win { get; set; }

    /// <summary><see cref="IsSevenPairs"/> 的返回值。</summary>
    public bool SevenPairs { get; set; }

    /// <summary>按调用顺序记录的方法名：<c>IsSevenPairs</c> 或 <c>IsWin</c>。</summary>
    public List<string> Calls { get; } = [];

    /// <summary>按调用顺序记录每次判定的完整入参，用于钉住 hand/melds/joker 没有被传错。</summary>
    public List<WinCheckerCall> Arguments { get; } = [];

    /// <inheritdoc />
    public bool IsWin(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds, TileType joker)
    {
        Calls.Add("IsWin");
        Arguments.Add(new WinCheckerCall("IsWin", hand.Count, melds.Count, joker.Id, FirstTileId(hand)));
        return Win;
    }

    /// <inheritdoc />
    public IReadOnlyList<TileType> GetWinningTypes(
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Meld> melds,
        TileType joker) => throw new NotSupportedException("轮次驱动只调用 IsWin/IsSevenPairs。");

    /// <inheritdoc />
    public bool IsSevenPairs(IReadOnlyList<Tile> hand, TileType joker)
    {
        Calls.Add("IsSevenPairs");
        Arguments.Add(new WinCheckerCall("IsSevenPairs", hand.Count, null, joker.Id, FirstTileId(hand)));
        return SevenPairs;
    }

    private static ushort FirstTileId(IReadOnlyList<Tile> hand) => hand.Count > 0 ? hand[0].Id : (ushort)0;
}

/// <summary>
/// 一旦被调用就抛异常的胡牌判定替身：用于证明某条非法命令在应用前就被拒绝，没有走到胡牌判定。
/// </summary>
internal sealed class ThrowingWinChecker : IWinChecker
{
    /// <inheritdoc />
    public bool IsWin(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds, TileType joker) =>
        throw new InvalidOperationException("The win checker must not be called for this command.");

    /// <inheritdoc />
    public IReadOnlyList<TileType> GetWinningTypes(
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Meld> melds,
        TileType joker) =>
        throw new InvalidOperationException("The win checker must not be called for this command.");

    /// <inheritdoc />
    public bool IsSevenPairs(IReadOnlyList<Tile> hand, TileType joker) =>
        throw new InvalidOperationException("The win checker must not be called for this command.");
}

/// <summary>
/// 首次返回值可控、其余委托给 <see cref="Pcg32"/> 的随机源：第 1 局起庄取首次调用的固定值，
/// 后续洗牌与真实牌序使用固定 seed 的 PCG32，从而同时钉住「构造不消耗 RNG」与可复现的牌序。
/// </summary>
internal sealed class FirstValueRandomSource : IRandomSource
{
    private readonly uint _firstValue;
    private readonly Pcg32 _rest;

    /// <summary>创建随机源。</summary>
    /// <param name="firstValue">第一次 <see cref="NextUInt"/> 返回的值（起庄用）。</param>
    /// <param name="seed">其后的 PCG32 种子。</param>
    public FirstValueRandomSource(uint firstValue, ulong seed)
    {
        _firstValue = firstValue;
        _rest = new Pcg32(seed);
    }

    /// <summary>总调用次数。</summary>
    public int Calls { get; private set; }

    /// <summary>委托给 <see cref="Pcg32"/> 的调用次数（总次数减 1）。</summary>
    public int DelegateCalls { get; private set; }

    /// <inheritdoc />
    public uint NextUInt()
    {
        Calls++;
        if (Calls == 1)
        {
            return _firstValue;
        }

        DelegateCalls++;
        return _rest.NextUInt();
    }
}
