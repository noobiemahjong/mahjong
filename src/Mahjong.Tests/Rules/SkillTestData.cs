using Mahjong.Core.Flow;
using Mahjong.Core.Rules;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 技能/校验器测试的共享构造：按 content/tiles.json 固定公式（物理 id = typeId*4 + copy + 1）造牌，
/// 统一 <see cref="SkillContext"/> 构造入参（独立入参 TargetSeat 默认 <see cref="Meld.NoTargetSeat"/>），
/// 并提供「状态不变」断言所需的完整快照与状态哈希。
/// </summary>
internal static class SkillTestData
{
    /// <summary>content/rules.json 配置的鬼牌牌种 id（FixedType 31，红中）。</summary>
    public const int JokerTypeId = 31;

    /// <summary>content/rules.json 配置的鬼牌牌种。</summary>
    public static readonly TileType Joker = RulesTestData.TypeOf(JokerTypeId);

    /// <summary>无鬼牌时调用方传入的 <c>default(TileType)</c>。</summary>
    public static readonly TileType NoJoker = default;

    /// <summary>
    /// 填充牌：type id 18–30 各 copy 0（共 13 张，物理 id 73/77/81/…/121），
    /// 与技能用例里用到的 0–9、17、31–33 牌种不重叠，避免物理 id 冲突。
    /// </summary>
    private static readonly Tile[] FillerTiles =
    [
        RulesTestData.Physical(18, 0),
        RulesTestData.Physical(19, 0),
        RulesTestData.Physical(20, 0),
        RulesTestData.Physical(21, 0),
        RulesTestData.Physical(22, 0),
        RulesTestData.Physical(23, 0),
        RulesTestData.Physical(24, 0),
        RulesTestData.Physical(25, 0),
        RulesTestData.Physical(26, 0),
        RulesTestData.Physical(27, 0),
        RulesTestData.Physical(28, 0),
        RulesTestData.Physical(29, 0),
        RulesTestData.Physical(30, 0),
    ];

    /// <summary>按物理牌公式构造一张牌。</summary>
    /// <param name="typeId">牌种 id 0–33。</param>
    /// <param name="copy">同牌种内第几张（0–3）。</param>
    public static Tile T(int typeId, int copy) => RulesTestData.Physical(typeId, copy);

    /// <summary>取前 <paramref name="count"/> 张填充牌（0–13）。</summary>
    /// <param name="count">张数。</param>
    public static Tile[] Fillers(int count) => FillerTiles.Take(count).ToArray();

    /// <summary>创建空局（牌墙为空，四家空手牌，CurrentSeat 0）。</summary>
    public static GameState NewState() => new();

    /// <summary>向指定座位手牌尾部追加牌。</summary>
    /// <param name="state">局面。</param>
    /// <param name="seat">座位 0–3。</param>
    /// <param name="tiles">要追加的牌。</param>
    public static void FillHand(GameState state, byte seat, params Tile[] tiles) =>
        RulesTestData.Fill(state.Players[seat], tiles);

    /// <summary>向指定座位弃牌区尾部追加牌。</summary>
    /// <param name="state">局面。</param>
    /// <param name="seat">座位 0–3。</param>
    /// <param name="tiles">要追加的牌。</param>
    public static void FillDiscards(GameState state, byte seat, params Tile[] tiles) =>
        RulesTestData.Discard(state.Players[seat], tiles);

    /// <summary>向牌墙尾部追加牌。</summary>
    /// <param name="state">局面。</param>
    /// <param name="tiles">要追加的牌。</param>
    public static void FillWall(GameState state, params Tile[] tiles)
    {
        foreach (Tile tile in tiles)
        {
            state.Wall.Append(tile);
        }
    }

    /// <summary>用默认入参构造技能上下文：无目标牌/摸牌、无目标座位、requestId 0。</summary>
    /// <param name="state">局面。</param>
    /// <param name="seat">发起技能的座位 0–3。</param>
    /// <param name="targetCard">目标弃牌。</param>
    /// <param name="drawnTile">自摸/补杠刚摸到的牌。</param>
    /// <param name="targetSeat">目标座位 0–3 或 <see cref="Meld.NoTargetSeat"/>。</param>
    /// <param name="requestId">请求 id。</param>
    public static SkillContext Context(
        GameState state,
        byte seat,
        Tile? targetCard = null,
        Tile? drawnTile = null,
        byte targetSeat = Meld.NoTargetSeat,
        long requestId = 0) =>
        new(state, state.Players[seat], targetCard, drawnTile, targetSeat, requestId);

    /// <summary>全部区域的完整快照，用于断言调用失败不改状态。</summary>
    /// <param name="state">局面。</param>
    public static string Snapshot(GameState state) => RulesTestData.Snapshot(state);

    /// <summary>权威局面哈希，用于断言失败调用不产生任何半改状态。</summary>
    /// <param name="state">局面。</param>
    public static string Hash(GameState state) => StateHash.Compute(state);

    /// <summary>手牌物理 id 序列。</summary>
    /// <param name="player">玩家。</param>
    public static ushort[] HandIds(PlayerState player) => RulesTestData.Ids(player.Hand.Items);

    /// <summary>弃牌物理 id 序列。</summary>
    /// <param name="player">玩家。</param>
    public static ushort[] DiscardIds(PlayerState player) => RulesTestData.Ids(player.Discards.Items);

    /// <summary>牌墙物理 id 序列。</summary>
    /// <param name="state">局面。</param>
    public static ushort[] WallIds(GameState state) => RulesTestData.Ids(state.Wall.Items);

    /// <summary>副露物理 id 序列。</summary>
    /// <param name="meld">副露。</param>
    public static ushort[] MeldIds(Meld meld) => RulesTestData.Ids(meld.Tiles);
}
