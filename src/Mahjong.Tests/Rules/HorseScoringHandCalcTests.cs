using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// T-4 §5 手算例 M-01…M-10 的账本组合断言：座 0 为庄，逐例钉死四座 <see cref="ScoreManager.RoundDelta"/>、
/// <see cref="ScoreManager.RoundActionType"/> 的精确位集与四座和 0；金额按 T-4 §2 与 T-1 §2 的表逐笔手算，
/// 不调用会话代码生成期望值。马座位口径为 <c>tile.Id % 4</c>（物理牌 1–136），不是牌种 id。
/// 事件序、墙空整步跳过与帧级翻马由会话组用例覆盖（会话是唯一载体）。
/// </summary>
public sealed class HorseScoringHandCalcTests
{
    private const ScoreActionType Standard = ScoreActionType.SelfDrawn | ScoreActionType.NoJoker;

    /// <summary>M-01 庄自摸·无鬼·马中（tileId=4→座0）：自摸 +12/−4，马中 +3/−1，合计 +15/−5×3。</summary>
    [Fact]
    public void M01_BankerSelfDrawn_NoJoker_HorseHitsWinner()
    {
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyHorseBonus(winnerSeat: 0, horseBonus: 1);

        AssertRound(scores, new short[] { 15, -5, -5, -5 }, Standard | ScoreActionType.HorseWinner);

        // 512 是第 7 位，且已进 AllActionTypes 掩码：Apply 接受该位，不接受未定义的 16384。
        Assert.Equal(512, (int)ScoreActionType.HorseWinner);
        ScoreManager mask = new();
        mask.Apply(seat: 0, delta: 0, type: ScoreActionType.HorseWinner);
        Assert.Equal(ScoreActionType.HorseWinner, mask.RoundActionType);
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.Apply(0, 0, (ScoreActionType)16384));
    }

    /// <summary>M-02 非庄自摸·无鬼·马不中（tileId=9→座1，赢家座2）：自摸 +12/−4、跟庄 +1/−1，马不中无支付无位。</summary>
    [Fact]
    public void M02_NonBankerSelfDrawn_NoJoker_HorseMissesWinner()
    {
        int horseSeat = HorseSeatOf(tileId: 9);
        Assert.Equal(1, horseSeat);
        Assert.NotEqual(2, horseSeat);

        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 2, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);

        // 马不中：调用方不调 ApplyHorseBonus，位集不得含 HorseWinner。
        AssertRound(scores, new short[] { -5, -4, 13, -4 }, Standard | ScoreActionType.FollowingTheBanker);
    }

    /// <summary>M-03 非庄自摸·有鬼·马中（tileId=5→座1，赢家座1）：自摸 +6/−2、跟庄 +1/−1、马中 +3/−1，合计 +10/−4×2/−3。</summary>
    [Fact]
    public void M03_NonBankerSelfDrawn_HasJoker_HorseHitsWinner()
    {
        int horseSeat = HorseSeatOf(tileId: 5);
        Assert.Equal(1, horseSeat);

        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 1, hasJoker: true, sevenPairs: false, kongUpFlower: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 1, bankerSeat: 0, amount: 1);
        scores.ApplyHorseBonus(winnerSeat: 1, horseBonus: 1);

        AssertRound(
            scores,
            new short[] { -4, 10, -3, -3 },
            ScoreActionType.SelfDrawn | ScoreActionType.FollowingTheBanker | ScoreActionType.HorseWinner);
    }

    /// <summary>M-04 非庄自摸·无鬼·墙空整步跳过（赢家座3）：自摸 +12/−4、跟庄 +1/−1，无马事件、无马支付、无马位。</summary>
    [Fact]
    public void M04_NonBankerSelfDrawn_WallEmpty_NoHorsePaymentNorBit()
    {
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 3, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 3, bankerSeat: 0, amount: 1);

        AssertRound(scores, new short[] { -5, -4, -4, 13 }, Standard | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, (int)(scores.RoundActionType & ScoreActionType.HorseWinner));
    }

    /// <summary>M-05 抢杠×马中·无鬼 base=4（tileId=6→座2，赢家座2、被抢座3）：抢杠 +12/−12、跟庄 +1/−1、马中 +3/−1。</summary>
    [Fact]
    public void M05_RobbingKong_NoJoker_HorseHitsWinner()
    {
        int horseSeat = HorseSeatOf(tileId: 6);
        Assert.Equal(2, horseSeat);

        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 2, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);
        scores.ApplyHorseBonus(winnerSeat: 2, horseBonus: 1);

        AssertRound(
            scores,
            new short[] { -2, -1, 16, -13 },
            ScoreActionType.RobbingTheKong
                | ScoreActionType.RobbingTheKongPayForEveryone
                | ScoreActionType.FollowingTheBanker
                | ScoreActionType.HorseWinner);
    }

    /// <summary>
    /// M-06 明杠开花承包×马不中（perK=8，tileId=7→座3，赢家座1、放杠座2）：
    /// 承包 +24/−24、明杠 +3/−3、跟庄 +1/−1，马不中无支付。
    /// </summary>
    [Fact]
    public void M06_KongUpFlowerContract_HorseMissesWinner()
    {
        int horseSeat = HorseSeatOf(tileId: 7);
        Assert.Equal(3, horseSeat);
        Assert.NotEqual(1, horseSeat);

        ScoreManager scores = new();
        scores.ApplyKongUpFlowerContract(winnerSeat: 1, payerSeat: 2, basePer: 4, hasJoker: false, sevenPairs: false);
        scores.ApplyKong(seat: 1, kind: MeldKind.KongExposed, sourceSeat: 2);
        scores.ApplyFollowingTheBanker(winnerSeat: 1, bankerSeat: 0, amount: 1);

        AssertRound(
            scores,
            new short[] { -1, 28, -27, 0 },
            ScoreActionType.SelfDrawn
                | ScoreActionType.KongUpFlower
                | ScoreActionType.KongUpFlowerPayForEveryone
                | ScoreActionType.ExposedKong
                | ScoreActionType.NoJoker
                | ScoreActionType.FollowingTheBanker);
    }

    /// <summary>M-07 庄七对自摸·有鬼·马中（tileId=8→座0）·streak=2（b=1）：七对 +12/−4、连庄 +3/−1、马中 +3/−1。</summary>
    [Fact]
    public void M07_BankerSevenPairs_HasJoker_HorseHits_StreakTwo()
    {
        int horseSeat = HorseSeatOf(tileId: 8);
        Assert.Equal(0, horseSeat);

        ScoreManager scores = new();
        scores.ApplySevenPairsSelfDrawn(winnerSeat: 0, hasJoker: true);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 1);
        scores.ApplyHorseBonus(winnerSeat: 0, horseBonus: 1);

        AssertRound(
            scores,
            new short[] { 18, -6, -6, -6 },
            ScoreActionType.DealerStreak | ScoreActionType.SelfDrawn | ScoreActionType.SevenPairs | ScoreActionType.HorseWinner);
    }

    /// <summary>M-08 庄自摸·无鬼·streak 表尾（b=3）·马不中（tileId=10→座2）：自摸 +12/−4、连庄 +9/−3。</summary>
    [Fact]
    public void M08_BankerSelfDrawn_StreakTableTail_HorseMisses()
    {
        int horseSeat = HorseSeatOf(tileId: 10);
        Assert.Equal(2, horseSeat);

        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 3);

        AssertRound(scores, new short[] { 21, -7, -7, -7 }, Standard | ScoreActionType.DealerStreak);
    }

    /// <summary>
    /// M-09 非庄自摸·无鬼·horseBonus=0 关闭（赢家座2）：自摸 +12/−4、跟庄 +1/−1；
    /// 金额 0 不改暂存、不置位，负金额与越界座位在校验阶段就被拒绝（先校验后写）。
    /// </summary>
    [Fact]
    public void M09_HorseBonusZero_PaysNothingAndSetsNoBit()
    {
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 2, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);
        scores.ApplyHorseBonus(winnerSeat: 2, horseBonus: 0);

        AssertRound(scores, new short[] { -5, -4, 13, -4 }, Standard | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, (int)(scores.RoundActionType & ScoreActionType.HorseWinner));

        short[] before = scores.RoundDelta.ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => scores.ApplyHorseBonus(winnerSeat: 0, horseBonus: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => scores.ApplyHorseBonus(winnerSeat: 4, horseBonus: 1));
        Assert.Equal(before, scores.RoundDelta.ToArray());
    }

    /// <summary>M-10 抢杠七对×马不中 base=8（tileId=12→座0，赢家座3、被抢座1）：抢杠 +24/−24、跟庄 +1/−1。</summary>
    [Fact]
    public void M10_RobbingKongSevenPairs_HorseMissesWinner()
    {
        int horseSeat = HorseSeatOf(tileId: 12);
        Assert.Equal(0, horseSeat);
        Assert.NotEqual(3, horseSeat);

        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 3, robbedSeat: 1, basePer: 8, sevenPairs: true);
        scores.ApplyFollowingTheBanker(winnerSeat: 3, bankerSeat: 0, amount: 1);

        AssertRound(
            scores,
            new short[] { -1, -24, 0, 25 },
            ScoreActionType.RobbingTheKong
                | ScoreActionType.RobbingTheKongPayForEveryone
                | ScoreActionType.SevenPairs
                | ScoreActionType.FollowingTheBanker);
    }

    /// <summary>马座位口径：物理牌 id 1–136 对 4 取余，落在 0–3（不是牌种 id 0–33）。</summary>
    private static int HorseSeatOf(int tileId) => tileId % GameState.PlayerCount;

    /// <summary>断言四座 delta、精确位集与四座和 0。</summary>
    private static void AssertRound(ScoreManager scores, short[] expectedDelta, ScoreActionType expectedType)
    {
        Assert.Equal(expectedDelta, scores.RoundDelta.ToArray());
        Assert.Equal(expectedType, scores.RoundActionType);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }
}
