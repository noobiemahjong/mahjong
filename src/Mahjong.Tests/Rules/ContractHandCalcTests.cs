using Mahjong.Core.Content;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// T-5 §1 手算例 S-1…S-8 的落地：座 0 = 庄；七变体全开（<c>dealerStreakBonus=[0,1,2,3]</c>、
/// <c>followingBanker=1</c>、<c>horseBonus=1</c>、抢杠与两种承包 true）；除 S-3（有鬼）外各例无鬼。
/// 每例钉死四座 <see cref="ScoreManager.RoundDelta"/> 精确相等、<see cref="ScoreManager.RoundActionType"/>
/// 的完整位集与四座和 0；金额按 T-1 §2 乘系数后逐笔手算，不调用会话代码生成期望值。
/// 例号沿用规格既有号段（不新造）；会话侧的事件序与窗口参数归 RobbingKongFlowTests / 会话结算用例。
/// 末条 <c>RobbingKong_NoJoker_...</c> 是 D-27 的补充用例：抢杠胡不置 <see cref="ScoreActionType.NoJoker"/>，
/// 因此「裸位反推有无鬼」会把无鬼抢杠算成 +6/−6；显式给 <c>winnerHasJoker: false</c> 时期望必须是 +12/−12。
/// </summary>
public sealed class ContractHandCalcTests
{
    /// <summary>抢杠的两支：赢家位与 payer 位。</summary>
    private const ScoreActionType RobbingBits =
        ScoreActionType.RobbingTheKong | ScoreActionType.RobbingTheKongPayForEveryone;

    /// <summary>常规自摸（无鬼）的两位。</summary>
    private const ScoreActionType Standard = ScoreActionType.SelfDrawn | ScoreActionType.NoJoker;

    /// <summary>手算前提：内容目录里的七变体确实是「全开」，否则下面的期望值失去依据。</summary>
    [Fact]
    public void HandCalcPreamble_SevenVariantsOnAndBonusTableMatchTheHandCalcBasis()
    {
        ContentVariants variants = SessionTestSupport.Content.Variants;

        Assert.Equal(new[] { 0, 1, 2, 3 }, variants.DealerStreakBonus.ToArray());
        Assert.Equal(1, variants.FollowingBanker);
        Assert.Equal(1, variants.HorseBonus);
        Assert.True(variants.RobbingKong);
        Assert.True(variants.KongFlowerContract);
        Assert.True(variants.TwelveCardsContract);
    }

    /// <summary>S-1 R1 庄自摸·streak 3（b=2）·无鬼：自摸 12/−4 + 连庄 +6/−2 → 18/−6×3。</summary>
    [Fact]
    public void S1_BankerSelfDrawn_StreakThree_StacksDealerStreak()
    {
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 2);

        AssertLedger(scores, new short[] { 18, -6, -6, -6 }, Standard | ScoreActionType.DealerStreak);
    }

    /// <summary>S-2 R2 非庄自摸·赢家座 2·无鬼：自摸 +12/−4 + 跟庄 +1/−1 → −5/−4/13/−4。</summary>
    [Fact]
    public void S2_NonBankerSelfDrawn_WinnerSeatTwo_PaysFollowingTheBanker()
    {
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 2, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);

        AssertLedger(scores, new short[] { -5, -4, 13, -4 }, Standard | ScoreActionType.FollowingTheBanker);
    }

    /// <summary>S-3 R3 中马·有鬼·赢家座 1·马 9（9 % 4 = 1 命中）：自摸 +6/−2、跟庄 +1/−1、马中 +3/−1。</summary>
    [Fact]
    public void S3_NonBankerSelfDrawn_HasJoker_HorseNineHitsSeatOne()
    {
        Assert.Equal(1, 9 % GameState.PlayerCount);

        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 1, hasJoker: true, sevenPairs: false, kongUpFlower: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 1, bankerSeat: 0, amount: 1);
        scores.ApplyHorseBonus(winnerSeat: 1, horseBonus: 1);

        AssertLedger(
            scores,
            new short[] { -4, 10, -3, -3 },
            ScoreActionType.SelfDrawn | ScoreActionType.FollowingTheBanker | ScoreActionType.HorseWinner);
    }

    /// <summary>S-4 R4 抢杠 base=4·赢家座 2·被抢座 3：抢杠 +12/−12 + 跟庄 +1/−1 → −1/0/13/−12。</summary>
    [Fact]
    public void S4_RobbingKongBaseFour_WinnerSeatTwo_RobbedSeatThree()
    {
        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 2, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);

        AssertLedger(scores, new short[] { -1, 0, 13, -12 }, RobbingBits | ScoreActionType.FollowingTheBanker);
    }

    /// <summary>S-5 R5 承包 perK=8·赢家座 1·放杠座 2：承包 +24/−24、明杠 +3/−3、跟庄 +1/−1。</summary>
    [Fact]
    public void S5_KongUpFlowerContract_PerKIsEight_PayerSeatTwo()
    {
        ScoreManager scores = new();
        scores.ApplyKong(seat: 1, kind: MeldKind.KongExposed, sourceSeat: 2);
        scores.ApplyKongUpFlowerContract(winnerSeat: 1, payerSeat: 2, basePer: 4, hasJoker: false, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 1, bankerSeat: 0, amount: 1);

        AssertLedger(
            scores,
            new short[] { -1, 28, -27, 0 },
            ScoreActionType.ExposedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone
            | ScoreActionType.NoJoker
            | ScoreActionType.FollowingTheBanker);
    }

    /// <summary>S-6 R6 12 张 base=4·赢家座 3·供牌座 1：承包 +12/−12 + 跟庄 +1/−1 → −1/−12/0/13。</summary>
    [Fact]
    public void S6_TwelveCardsContract_SupplierSeatOne_WinnerSeatThree()
    {
        ScoreManager scores = new();
        scores.ApplyTwelveCardsContract(winnerSeat: 3, payerSeat: 1, basePer: 4, hasJoker: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 3, bankerSeat: 0, amount: 1);

        AssertLedger(
            scores,
            new short[] { -1, -12, 0, 13 },
            ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded | ScoreActionType.NoJoker | ScoreActionType.FollowingTheBanker);
    }

    /// <summary>S-7 R7 抢杠压 12 张·赢家座 2·被抢座 3：只记抢杠 12 + 跟庄，12 张不记账不置位。</summary>
    [Fact]
    public void S7_RobbingKongOutranksTwelveCards_OnlyTheRobbedSeatPays()
    {
        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 2, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);

        AssertLedger(scores, new short[] { -1, 0, 13, -12 }, RobbingBits | ScoreActionType.FollowingTheBanker);

        // 12 张的供牌者（座 1）既未付款也未置位：一次胡牌只取一个承包者。
        Assert.Equal(0, scores.RoundDelta[1]);
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.TwelveCardsConceded);
    }

    /// <summary>S-8 连庄×抢杠·streak 3（b=2）·庄家抢杠·被抢座 3：抢杠 +12/−12 + 连庄 +6/−2。</summary>
    [Fact]
    public void S8_BankerRobbingKong_StreakThree_StacksDealerStreak()
    {
        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 2);

        AssertLedger(scores, new short[] { 18, -2, -2, -14 }, RobbingBits | ScoreActionType.DealerStreak);
    }

    /// <summary>
    /// D-27 补充：无鬼抢杠的每份基分是 4（赢家 +12、被抢者 −12），不是 2（+6/−6）。
    /// 抢杠胡不置 <see cref="ScoreActionType.NoJoker"/>，所以「无 NoJoker 位 ⇒ 有鬼」的反推一定会把它算错；
    /// 结算重算器必须由调用方显式给出有无鬼。
    /// </summary>
    [Fact]
    public void RobbingKong_NoJoker_ExpectationIsTwelveNotSix()
    {
        ScoreManager ledger = new();
        ledger.ApplyRobbingKong(winnerSeat: 2, robbedSeat: 3, basePer: 4, sevenPairs: false);
        ledger.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);
        AssertLedger(ledger, new short[] { -1, 0, 13, -12 }, RobbingBits | ScoreActionType.FollowingTheBanker);

        // 反推的陷阱：NoJoker 位不存在，但这一局确实无鬼（per0 = 4）。
        Assert.Equal(ScoreActionType.None, ledger.RoundActionType & ScoreActionType.NoJoker);
        bool hasJokerByBitOnly = (ledger.RoundActionType & ScoreActionType.NoJoker) == 0;
        Assert.True(hasJokerByBitOnly);
        Assert.Equal(12, 3 * 4);
        Assert.NotEqual(6, 3 * 4);

        // 显式传参：同一个无鬼抢杠结算，期望必须是 +12/−12。
        var round = new RoundEndedPayload(
            WinnerSeat: (sbyte)2,
            ScoreDelta: new[] { -1, 0, 13, -12 },
            ActionType: RobbingBits | ScoreActionType.FollowingTheBanker);
        var started = new RoundStartedPayload(RoundNo: 1, BankerSeat: 0, WallCount: 84, DealerStreak: 1);
        ContentVariants variants = SessionTestSupport.Content.Variants;
        IntegrationSupport.AssertSelfDrawnSettlement(round, started, variants, winnerHasJoker: false);

        // 同一份结算若被标成「有鬼」，期望变成 +6/−6，重算器必须判死。
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => IntegrationSupport.AssertSelfDrawnSettlement(round, started, variants, winnerHasJoker: true));

        // 不传显式值时只做「落在合法基分之一」的断言：+12/−12 仍是两个合法基分之一，必须通过。
        IntegrationSupport.AssertSelfDrawnSettlement(round, started, variants);
    }

    /// <summary>断言四座 delta 与完整位集相等，并断言四座和为 0。</summary>
    private static void AssertLedger(ScoreManager scores, short[] expectedDelta, ScoreActionType expectedType)
    {
        Assert.Equal(expectedDelta, scores.RoundDelta.ToArray());
        Assert.Equal(expectedType, scores.RoundActionType);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }
}
