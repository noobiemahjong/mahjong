using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// T-2 §5 手算例 C-01…C-08 的账本组合断言：座 0 为庄，逐例钉死四座 <see cref="ScoreManager.RoundDelta"/>、
/// <see cref="ScoreManager.RoundActionType"/> 的精确位集与四座和 0；金额按 T-2 §2/§3 的表逐笔手算，不调用会话代码生成期望值。
/// C-09…C-12 依赖抢杠/承包/中马（T-3/T-4），本批只登记承接、不在本文件实现。
/// 第 8 局照常递增与变体关闭不付由会话组用例覆盖（会话是唯一载体）。
/// </summary>
public sealed class DealerScoringHandCalcTests
{
    private const ScoreActionType Standard = ScoreActionType.SelfDrawn | ScoreActionType.NoJoker;

    [Fact]
    public void C01_BankerSelfDrawn_NoJoker_StreakOnePaysNothing()
    {
        // 手算：无鬼自摸 per=4 → 庄 +12、其余三家各 −4；连庄 1 次查表得 0，不记账不置位。
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 0);

        AssertRound(scores, new short[] { 12, -4, -4, -4 }, Standard);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void C02_BankerSelfDrawn_NoJoker_StreakTwoAddsOneShare()
    {
        // 手算：12 + 3×1 = 15，三家 −4 −1 = −5；表 [0,1,2,3] 第 2 次连庄为 1。
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 1);

        AssertRound(scores, new short[] { 15, -5, -5, -5 }, Standard | ScoreActionType.DealerStreak);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void C03_BankerSelfDrawn_NoJoker_StreakFourAddsThreeShares()
    {
        // 手算：12 + 3×3 = 21，三家 −4 −3 = −7；第 4 次连庄为表尾 3。
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 3);

        AssertRound(scores, new short[] { 21, -7, -7, -7 }, Standard | ScoreActionType.DealerStreak);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void C04_BankerSelfDrawn_NoJoker_StreakFiveClampsToTableTailThree()
    {
        // 手算：streak 5 超出表长 4 → min(5,4)−1 = 3 → 奖励 3，金额与 C-03 相同。
        int bonus = DealerStreakBonus(streak: 5);

        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus);

        Assert.Equal(3, bonus);
        AssertRound(scores, new short[] { 21, -7, -7, -7 }, Standard | ScoreActionType.DealerStreak);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void C05_NonBankerSelfDrawn_NoJoker_StreakThreePaysFollowingBanker()
    {
        // 手算：座 2 无鬼自摸 +12、三家 −4；非庄胡庄家再付跟庄 1 → 座 2 +13、座 0 −5；连庄不付。
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 2, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);

        AssertRound(scores, new short[] { -5, -4, 13, -4 }, Standard | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void C06_DrawRound_AllZeroAndNoFlags()
    {
        // 手算：流局不记连庄、跟庄、中马；回滚暂存后四座全 0、聚合为 None。
        ScoreManager scores = new();
        scores.RollbackRound();

        AssertRound(scores, new short[] { 0, 0, 0, 0 }, ScoreActionType.None);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void C07_RoundEight_BankerSevenPairsNoJoker_StreakThree()
    {
        // 手算：七对无鬼 per = 4×2 = 8 → 庄 +24、三家 −8；第 8 局照常付第 3 次连庄（表第 3 项 = 2）
        // → 庄 +24+3×2 = +30、三家各 −8−2 = −10。
        ScoreManager scores = new();
        scores.ApplySevenPairsSelfDrawn(winnerSeat: 0, hasJoker: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 2);

        AssertRound(
            scores,
            new short[] { 30, -10, -10, -10 },
            Standard | ScoreActionType.SevenPairs | ScoreActionType.DealerStreak);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void C08_NonBankerSelfDrawn_HasJoker_FollowingBankerDisabledPaysNothing()
    {
        // 手算：有鬼自摸 per = 2 → 座 3 +6、三家 −2；跟庄变体关闭（金额 0）不付、不置位。
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 3, hasJoker: true, sevenPairs: false, kongUpFlower: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 3, bankerSeat: 0, amount: 0);

        AssertRound(scores, new short[] { -2, -2, -2, 6 }, ScoreActionType.SelfDrawn);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    /// <summary>C-04 的查表口径：<c>dealerStreakBonus[min(streak, 表长) - 1]</c>，表 [0,1,2,3]。</summary>
    private static int DealerStreakBonus(byte streak)
    {
        int[] table = [0, 1, 2, 3];
        int index = Math.Min(streak, table.Length) - 1;
        return index < 0 ? 0 : table[index];
    }

    /// <summary>断言四座 delta 与精确位集（四座和 0 由各用例内联断言）。</summary>
    private static void AssertRound(ScoreManager scores, short[] expectedDelta, ScoreActionType expectedType)
    {
        Assert.Equal(expectedDelta, scores.RoundDelta.ToArray());
        Assert.Equal(expectedType, scores.RoundActionType);
    }
}
