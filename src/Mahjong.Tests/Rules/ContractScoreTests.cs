using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// T-3 §5 手算例 K-01…K-15 的账本侧断言：座 0 为庄，逐例钉死 <see cref="ScoreManager.RoundDelta"/> 的四座金额、
/// <see cref="ScoreManager.RoundActionType"/> 的精确位集与四座和 0。金额与位集按 T-1 §2、T-3 §2–§3 手算，
/// 不调用会话代码生成期望值。
/// 只覆盖 <see cref="ScoreManager"/> 能直接表达的部分：抢杠、杠上开花承包、12 张承包、杠分、跟庄、连庄。
/// K-08 的中马段（HorseWinner）归 T-4，本文件只断言中马之前的账本；
/// 事件序、窗口参数、Cancelled/TimedOut 归会话组的 RobbingKongFlowTests。
/// 文件末尾另有 T3-06 补杠预判只读接口（KongSkill.TryCreateSupplementPlan / KongSupplementPlan）的契约用例，
/// 覆盖「摸到的第 4 张」与「手牌持有的第 4 张」两种来源，并逐例断言预判给出的牌 == Commit 实际加进碰的牌；
/// 因 KongSkillTests 不在本单元可写文件内，随本文件落地。
/// </summary>
public sealed class ContractScoreTests
{
    /// <summary>抢杠的两位：赢家位 RobbingTheKong 与 payer 位 RobbingTheKongPayForEveryone。</summary>
    private const ScoreActionType Robbing =
        ScoreActionType.RobbingTheKong | ScoreActionType.RobbingTheKongPayForEveryone;

    [Fact]
    public void K01_RobbingKong_NoJoker_PaysThreeSharesFromRobbedSeat()
    {
        // 手算：per0 = 无鬼 4 × 非七对 1 = 4；赢家 +3×4 = +12、被抢杠者 −12，其余两座 0。
        ScoreManager scores = new();

        scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);

        AssertRound(scores, new short[] { 12, 0, 0, -12 }, Robbing);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K02_RobbingKong_WithJoker_PaysThreeSharesFromRobbedSeat()
    {
        // 手算：per0 = 有鬼 2 × 1 = 2；赢家 +6、被抢杠者 −6。
        ScoreManager scores = new();

        scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 2, basePer: 2, sevenPairs: false);

        AssertRound(scores, new short[] { 6, 0, -6, 0 }, Robbing);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K03_SevenPairsRobbingKong_AddsSevenPairsBit()
    {
        // 手算：per0 = 4 × 七对 2 = 8；赢家 +24、被抢杠者 −24；七对并置 SevenPairs，不置 NoJoker。
        ScoreManager scores = new();

        scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 8, sevenPairs: true);

        AssertRound(scores, new short[] { 24, 0, 0, -24 }, Robbing | ScoreActionType.SevenPairs);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K04_RobbingKongDoesNotCancelTheExposedKong_AndStacksFollowingTheBanker()
    {
        // 手算：抢杠 3×4 = 12（座 1 +12、座 3 −12）；明杠照收（座 1 +3、座 2 −3）；
        // 跟庄（座 0 −1、座 1 +1）→ [−1, +16, −3, −12]。
        ScoreManager scores = new();
        scores.ApplyKong(seat: 1, MeldKind.KongExposed, sourceSeat: 2);

        scores.ApplyRobbingKong(winnerSeat: 1, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 1, bankerSeat: 0, amount: 1);

        AssertRound(
            scores,
            new short[] { -1, 16, -3, -12 },
            Robbing | ScoreActionType.ExposedKong | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K05_RobbingKongDeclined_SupplementKongAndKongUpFlowerSelfDrawn()
    {
        // 手算：补杠 [3, −1, −1, −1]；杠上开花无鬼自摸 per = 4×2 = 8 → [+24, −8, −8, −8]；
        // 合计 [+27, −9, −9, −9]。
        ScoreManager scores = new();
        scores.ApplyKong(seat: 0, MeldKind.KongSupplement);

        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: true);

        AssertRound(
            scores,
            new short[] { 27, -9, -9, -9 },
            ScoreActionType.KongSupplement
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.NoJoker);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K06_RobbingKongTimedOut_SupplementKongAndKongUpFlowerWithJoker()
    {
        // 手算：补杠 [3, −1, −1, −1]；杠上开花有鬼自摸 per = 2×2 = 4 → [+12, −4, −4, −4]；
        // 合计 [+15, −5, −5, −5]，不置 NoJoker。
        ScoreManager scores = new();
        scores.ApplyKong(seat: 0, MeldKind.KongSupplement);

        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: true, sevenPairs: false, kongUpFlower: true);

        AssertRound(
            scores,
            new short[] { 15, -5, -5, -5 },
            ScoreActionType.KongSupplement | ScoreActionType.SelfDrawn | ScoreActionType.KongUpFlower);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K07_TwoCandidates_LowestSeatWinsAndPaysThreeShares()
    {
        // 手算：赢家取座位升序首个（座 1），抢杠 3×4 = 12（座 1 +12、座 3 −12）；
        // 座 2 记 Cancelled 不记账；跟庄（座 0 −1、座 1 +1）→ [−1, +13, 0, −12]。
        ScoreManager scores = new();

        scores.ApplyRobbingKong(winnerSeat: 1, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 1, bankerSeat: 0, amount: 1);

        AssertRound(scores, new short[] { -1, 13, 0, -12 }, Robbing | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K08_RobbingKongWithFollowingTheBanker_StatesLedgerBeforeHorseReveal()
    {
        // 手算（中马之前）：抢杠 12（座 2 +12、座 3 −12）；跟庄（座 0 −1、座 2 +1）→ [−1, 0, +13, −12]。
        // streak=2 但赢家非庄，本局不付连庄；马中座 2 的 +3/−1 属 T-4，本文件不断言。
        ScoreManager scores = new();

        scores.ApplyRobbingKong(winnerSeat: 2, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);

        AssertRound(scores, new short[] { -1, 0, 13, -12 }, Robbing | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K09_KongUpFlowerContract_NoJoker_PayerCoversAllThreeShares()
    {
        // 手算：明杠（座 1 +3、座 2 −3）；承包 per0 = 4、perK = 8 → 座 1 +24、座 2 −24，座 0/座 3 记 0；
        // 跟庄（座 0 −1、座 1 +1）→ [−1, +28, −27, 0]。
        ScoreManager scores = new();
        scores.ApplyKong(seat: 1, MeldKind.KongExposed, sourceSeat: 2);

        scores.ApplyKongUpFlowerContract(
            winnerSeat: 1, payerSeat: 2, basePer: 4, hasJoker: false, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 1, bankerSeat: 0, amount: 1);

        AssertRound(
            scores,
            new short[] { -1, 28, -27, 0 },
            ScoreActionType.ExposedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone
            | ScoreActionType.NoJoker
            | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K10_SupplementKongReplacementSelfDrawn_IsNotAContract()
    {
        // 手算：补杠（座 1 +3、其余 −1）；杠上开花无鬼自摸 per = 4×2 = 8 → 座 1 +24、其余 −8；
        // 跟庄（座 0 −1、座 1 +1）→ [−10, +28, −9, −9]；补杠补牌不承包，无 KongUpFlowerPayForEveryone。
        ScoreManager scores = new();
        scores.ApplyKong(seat: 1, MeldKind.KongSupplement);

        scores.ApplySelfDrawn(winnerSeat: 1, hasJoker: false, sevenPairs: false, kongUpFlower: true);
        scores.ApplyFollowingTheBanker(winnerSeat: 1, bankerSeat: 0, amount: 1);

        AssertRound(
            scores,
            new short[] { -10, 28, -9, -9 },
            ScoreActionType.KongSupplement
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.NoJoker
            | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K11_TwelveCardsContract_SuppliedBySeatOne()
    {
        // 手算：per0 = 4 → 座 3 +12、供牌者座 1 −12，座 0/座 2 记 0；
        // 跟庄（座 0 −1、座 3 +1）→ [−1, −12, 0, +13]。
        ScoreManager scores = new();

        scores.ApplyTwelveCardsContract(winnerSeat: 3, payerSeat: 1, basePer: 4, hasJoker: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 3, bankerSeat: 0, amount: 1);

        AssertRound(
            scores,
            new short[] { -1, -12, 0, 13 },
            ScoreActionType.SelfDrawn
            | ScoreActionType.TwelveCardsConceded
            | ScoreActionType.NoJoker
            | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K12_TwelveCardsContract_FourthMeldIsSupplementKongKeepsOriginalPongSource()
    {
        // 手算：第 4 副露是补杠，供牌者仍取原碰放牌者座 2 → 座 3 +12、座 2 −12；
        // 跟庄（座 0 −1、座 3 +1）→ [−1, 0, −12, +13]。
        ScoreManager scores = new();

        scores.ApplyTwelveCardsContract(winnerSeat: 3, payerSeat: 2, basePer: 4, hasJoker: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 3, bankerSeat: 0, amount: 1);

        AssertRound(
            scores,
            new short[] { -1, 0, -12, 13 },
            ScoreActionType.SelfDrawn
            | ScoreActionType.TwelveCardsConceded
            | ScoreActionType.NoJoker
            | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void K13_RobbingKongBeatsTwelveCards_OnlyOnePayerIsCharged()
    {
        // 手算：只取一个承包者。抢杠 3×4 = 12（座 2 +12、座 3 −12）；12 张的供牌者座 1 付 0 且不置位；
        // 跟庄（座 0 −1、座 2 +1）→ [−1, 0, +13, −12]。
        ScoreManager scores = new();

        scores.ApplyRobbingKong(winnerSeat: 2, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);

        AssertRound(scores, new short[] { -1, 0, 13, -12 }, Robbing | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));

        // 12 张的供牌者（座 1）未付、未置位：承包只重定向 payer，且一次胡牌只取一个承包者。
        Assert.Equal(0, scores.RoundDelta[1]);
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.TwelveCardsConceded);
    }

    [Fact]
    public void K14_KongUpFlowerContractBeatsTwelveCards_WithJoker()
    {
        // 手算：明杠（座 1 +3、座 2 −3）；承包 per0 = 2、perK = 4 → 座 1 +12、座 2 −12；
        // 跟庄（座 0 −1、座 1 +1）→ [−1, +16, −15, 0]；12 张不记账，无 NoJoker。
        ScoreManager scores = new();
        scores.ApplyKong(seat: 1, MeldKind.KongExposed, sourceSeat: 2);

        scores.ApplyKongUpFlowerContract(
            winnerSeat: 1, payerSeat: 2, basePer: 2, hasJoker: true, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 1, bankerSeat: 0, amount: 1);

        AssertRound(
            scores,
            new short[] { -1, 16, -15, 0 },
            ScoreActionType.ExposedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone
            | ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));

        // 12 张让位于杠上开花承包：既不记账也不置位。
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.TwelveCardsConceded);
    }

    [Fact]
    public void K15_BankerRobbingKong_StacksDealerStreak()
    {
        // 手算：抢杠 12（庄座 0 +12、座 3 −12）；连庄 streak=3 → b=2，庄 +6、三家各 −2；
        // 合计 [+18, −2, −2, −14]。
        ScoreManager scores = new();

        scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 2);

        AssertRound(scores, new short[] { 18, -2, -2, -14 }, Robbing | ScoreActionType.DealerStreak);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void ApplyRobbingKong_ZeroBase_SkipsWithoutSettingFlags()
    {
        ScoreManager scores = new();
        scores.Apply(1, 3, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        scores.ApplyRobbingKong(winnerSeat: 2, robbedSeat: 3, basePer: 0, sevenPairs: true);

        Assert.Equal(before, RoundTestSupport.Book(scores));
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Fact]
    public void ApplyRobbingKong_SetsWinnerBitAndPayerBitOnly()
    {
        // 抢杠胡不算自摸：不置 SelfDrawn / NoJoker，两位分别落在赢家笔与 payer 笔。
        ScoreManager scores = new();

        scores.ApplyRobbingKong(winnerSeat: 3, robbedSeat: 0, basePer: 2, sevenPairs: false);

        Assert.Equal(Robbing, scores.RoundActionType);
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.SelfDrawn);
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.NoJoker);
        Assert.Equal(new short[] { -6, 0, 0, 6 }, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void ApplyKongUpFlowerContract_PaysDoubleBaseWithJoker()
    {
        // perK = 2×basePer = 4，赢家 +12、payer −12；有鬼不置 NoJoker。
        ScoreManager scores = new();

        scores.ApplyKongUpFlowerContract(winnerSeat: 2, payerSeat: 3, basePer: 2, hasJoker: true, sevenPairs: false);

        AssertRound(
            scores,
            new short[] { 0, 0, 12, -12 },
            ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void ApplyKongUpFlowerContract_SevenPairsFlagAddsSevenPairsBit()
    {
        // 七对与杠上开花在实战互斥（七对须 0 副露），入口按入参置位、不重复校验。
        ScoreManager scores = new();

        scores.ApplyKongUpFlowerContract(winnerSeat: 0, payerSeat: 1, basePer: 4, hasJoker: false, sevenPairs: true);

        AssertRound(
            scores,
            new short[] { 24, -24, 0, 0 },
            ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.NoJoker
            | ScoreActionType.SevenPairs
            | ScoreActionType.KongUpFlowerPayForEveryone);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void ApplyTwelveCardsContract_WithJoker_NoNoJokerBit()
    {
        ScoreManager scores = new();

        scores.ApplyTwelveCardsContract(winnerSeat: 0, payerSeat: 1, basePer: 2, hasJoker: true);

        AssertRound(
            scores,
            new short[] { 6, -6, 0, 0 },
            ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }

    [Fact]
    public void ApplyKongUpFlowerContract_ZeroBase_SkipsWithoutSettingFlags()
    {
        ScoreManager scores = new();
        scores.Apply(0, -2, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        scores.ApplyKongUpFlowerContract(winnerSeat: 1, payerSeat: 2, basePer: 0, hasJoker: false, sevenPairs: false);

        Assert.Equal(before, RoundTestSupport.Book(scores));
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Fact]
    public void ApplyTwelveCardsContract_ZeroBase_SkipsWithoutSettingFlags()
    {
        ScoreManager scores = new();
        scores.Apply(2, 1, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        scores.ApplyTwelveCardsContract(winnerSeat: 3, payerSeat: 1, basePer: 0, hasJoker: false);

        Assert.Equal(before, RoundTestSupport.Book(scores));
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-4)]
    public void ApplyRobbingKong_NegativeBase_ThrowsAndLeavesStateUnchanged(int basePer)
    {
        ScoreManager scores = new();
        scores.Apply(3, 2, ScoreActionType.NoJoker);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 1, basePer, sevenPairs: false));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    public void ApplyKongUpFlowerContract_NegativeBase_ThrowsAndLeavesStateUnchanged(int basePer)
    {
        ScoreManager scores = new();
        scores.Apply(1, 1, ScoreActionType.NoJoker);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scores.ApplyKongUpFlowerContract(
                winnerSeat: 1, payerSeat: 2, basePer, hasJoker: false, sevenPairs: false));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-3)]
    public void ApplyTwelveCardsContract_NegativeBase_ThrowsAndLeavesStateUnchanged(int basePer)
    {
        ScoreManager scores = new();
        scores.Apply(0, -1, ScoreActionType.NoJoker);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scores.ApplyTwelveCardsContract(winnerSeat: 2, payerSeat: 3, basePer, hasJoker: false));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(0, 4)]
    [InlineData(255, 255)]
    public void ApplyRobbingKong_SeatOutOfRange_ThrowsAndLeavesStateUnchanged(byte winnerSeat, byte robbedSeat)
    {
        ScoreManager scores = new();
        scores.Apply(0, 1, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scores.ApplyRobbingKong(winnerSeat, robbedSeat, basePer: 4, sevenPairs: false));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(0, 4)]
    [InlineData(255, 255)]
    public void ApplyKongUpFlowerContract_SeatOutOfRange_ThrowsAndLeavesStateUnchanged(byte winnerSeat, byte payerSeat)
    {
        ScoreManager scores = new();
        scores.Apply(1, 2, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scores.ApplyKongUpFlowerContract(winnerSeat, payerSeat, basePer: 4, hasJoker: false, sevenPairs: false));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(0, 4)]
    [InlineData(255, 255)]
    public void ApplyTwelveCardsContract_SeatOutOfRange_ThrowsAndLeavesStateUnchanged(byte winnerSeat, byte payerSeat)
    {
        ScoreManager scores = new();
        scores.Apply(2, -1, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scores.ApplyTwelveCardsContract(winnerSeat, payerSeat, basePer: 4, hasJoker: false));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Fact]
    public void ContractEntries_SameSeat_NetZeroAndFlagsStillSet()
    {
        // 赢家与 payer 不同座由调用方保证；同座时两笔相抵为 0，仍按位规则置位（与跟庄入口同口径）。
        ScoreManager robbing = new();
        robbing.ApplyRobbingKong(winnerSeat: 2, robbedSeat: 2, basePer: 4, sevenPairs: false);
        Assert.Equal(new short[] { 0, 0, 0, 0 }, robbing.RoundDelta.ToArray());
        Assert.Equal(Robbing, robbing.RoundActionType);

        ScoreManager kongUpFlower = new();
        kongUpFlower.ApplyKongUpFlowerContract(
            winnerSeat: 2, payerSeat: 2, basePer: 4, hasJoker: false, sevenPairs: false);
        Assert.Equal(new short[] { 0, 0, 0, 0 }, kongUpFlower.RoundDelta.ToArray());
        Assert.Equal(
            ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.NoJoker
            | ScoreActionType.KongUpFlowerPayForEveryone,
            kongUpFlower.RoundActionType);

        ScoreManager twelveCards = new();
        twelveCards.ApplyTwelveCardsContract(winnerSeat: 2, payerSeat: 2, basePer: 4, hasJoker: false);
        Assert.Equal(new short[] { 0, 0, 0, 0 }, twelveCards.RoundDelta.ToArray());
        Assert.Equal(
            ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded | ScoreActionType.NoJoker,
            twelveCards.RoundActionType);
    }

    [Fact]
    public void ContractEntries_ReleaseMergesAndRollbackRoundWithdraws()
    {
        ScoreManager scores = new();

        scores.ApplyRobbingKong(winnerSeat: 1, robbedSeat: 2, basePer: 4, sevenPairs: false);
        scores.Release();

        Assert.Equal(new short[] { 0, 12, -12, 0 }, scores.TotalScores.ToArray());
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);

        scores.ApplyTwelveCardsContract(winnerSeat: 3, payerSeat: 0, basePer: 4, hasJoker: false);
        scores.RollbackRound();

        Assert.Equal(new short[] { 0, 12, -12, 0 }, scores.TotalScores.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
    }

    [Fact]
    public void TryCreateSupplementPlan_ReturnsFourthTileSlotAndNoTargetSeat()
    {
        GameState state = SupplementBoard(out Meld pong);
        string snapshotBefore = SkillTestData.Snapshot(state);
        string hashBefore = SkillTestData.Hash(state);

        bool created = new KongSkill().TryCreateSupplementPlan(
            state.Players[0],
            SkillTestData.T(5, 3),
            out KongSkill.KongSupplementPlan plan);

        Assert.True(created);
        Assert.Equal(MeldKind.KongSupplement, plan.Kind);
        Assert.Equal(Meld.NoTargetSeat, plan.TargetSeat);
        Assert.Equal(new int[] { 0 }, plan.HandSlots.ToArray());
        Assert.Equal(SkillTestData.T(5, 3).Id, plan.Tile.Id);
        Assert.Same(pong, state.Players[0].Melds[0]);
        Assert.Equal(snapshotBefore, SkillTestData.Snapshot(state));
        Assert.Equal(hashBefore, SkillTestData.Hash(state));
    }

    [Fact]
    public void TryCreateSupplementPlan_FalseWithoutMatchingPong()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 3), .. SkillTestData.Fillers(10)]);

        bool created = new KongSkill().TryCreateSupplementPlan(
            state.Players[0],
            SkillTestData.T(5, 3),
            out KongSkill.KongSupplementPlan plan);

        Assert.False(created);
        // 失败时 plan 为 default：Kind 0、HandSlots null、Tile.Id 0。
        Assert.Equal((MeldKind)0, plan.Kind);
        Assert.Null(plan.HandSlots);
        Assert.Equal((ushort)0, plan.Tile.Id);
    }

    [Fact]
    public void TryCreateSupplementPlan_FalseWhenHandHasNoFourthTile()
    {
        GameState state = SkillTestData.NewState();
        state.Players[0].Melds.Add(PongOf(5, targetSeat: 1));
        SkillTestData.FillHand(state, 0, SkillTestData.Fillers(11));

        bool created = new KongSkill().TryCreateSupplementPlan(
            state.Players[0],
            SkillTestData.T(5, 3),
            out KongSkill.KongSupplementPlan plan);

        Assert.False(created);
        Assert.Null(plan.HandSlots);
    }

    [Fact]
    public void TryCreateSupplementPlan_RejectsJokerTile_ButAcceptsWithoutJoker()
    {
        // 红中碰 + 手牌第 4 张红中：无鬼时可补杠，红中为鬼时不可（鬼牌不是杠材料）。
        GameState state = SkillTestData.NewState();
        state.Players[0].Melds.Add(PongOf(31, targetSeat: 1));
        SkillTestData.FillHand(state, 0, [SkillTestData.T(31, 3), .. SkillTestData.Fillers(10)]);

        Assert.True(new KongSkill().TryCreateSupplementPlan(
            state.Players[0], SkillTestData.T(31, 3), out KongSkill.KongSupplementPlan noJokerPlan));
        Assert.Equal(SkillTestData.T(31, 3).Id, noJokerPlan.Tile.Id);

        Assert.False(new KongSkill(SkillTestData.Joker).TryCreateSupplementPlan(
            state.Players[0], SkillTestData.T(31, 3), out KongSkill.KongSupplementPlan jokerPlan));
        Assert.Null(jokerPlan.HandSlots);
    }

    [Fact]
    public void TryCreateSupplementPlan_UnknownOrNullDrawnTileFallsBackToHandScan()
    {
        // 未知牌与 null 都只表示「没有摸牌提示」，不等于不可补杠：仍按手牌槽位升序扫描。
        GameState withSupplement = SupplementBoard(out _);

        Assert.True(new KongSkill().TryCreateSupplementPlan(
            withSupplement.Players[0], default(Tile), out KongSkill.KongSupplementPlan unknownPlan));
        Assert.Equal(SkillTestData.T(5, 3).Id, unknownPlan.Tile.Id);

        Assert.True(new KongSkill().TryCreateSupplementPlan(
            withSupplement.Players[0], null, out KongSkill.KongSupplementPlan nullPlan));
        Assert.Equal(new int[] { 0 }, nullPlan.HandSlots.ToArray());
        Assert.Equal(unknownPlan.Tile.Id, nullPlan.Tile.Id);

        // 无补杠局面下两种「无提示」输入都返回 false，plan 仍为 default。
        GameState plain = SkillTestData.NewState();
        SkillTestData.FillHand(plain, 0, SkillTestData.Fillers(5));

        Assert.False(new KongSkill().TryCreateSupplementPlan(
            plain.Players[0], default(Tile), out KongSkill.KongSupplementPlan plainPlan));
        Assert.Null(plainPlan.HandSlots);
        Assert.Equal((ushort)0, plainPlan.Tile.Id);
    }

    [Fact]
    public void TryCreateSupplementPlan_PlanHandSlotsAreReadOnly()
    {
        GameState state = SupplementBoard(out _);

        Assert.True(new KongSkill().TryCreateSupplementPlan(
            state.Players[0], SkillTestData.T(5, 3), out KongSkill.KongSupplementPlan plan));

        Assert.Throws<NotSupportedException>(() => ((IList<int>)plan.HandSlots)[0] = 9);
        Assert.Equal(new int[] { 0 }, plan.HandSlots.ToArray());
    }

    [Fact]
    public void TryCreateSupplementPlan_DrawnFourthTileMatchesCommittedTile()
    {
        // 两个碰都在手边：摸到 6万c3 时预判必须优先用它（摸牌优先于手牌升序的 5万c3），
        // 且 Commit 实际加进 6万碰的第 4 张就是预判给出的同一张牌。
        GameState state = SkillTestData.NewState();
        Meld pong5 = PongOf(5, targetSeat: 1);
        Meld pong6 = PongOf(6, targetSeat: 2);
        state.Players[0].Melds.Add(pong5);
        state.Players[0].Melds.Add(pong6);
        SkillTestData.FillHand(
            state, 0, [SkillTestData.T(5, 3), SkillTestData.T(6, 3), .. SkillTestData.Fillers(2)]);
        SkillTestData.FillWall(state, SkillTestData.T(33, 0), SkillTestData.T(30, 0));

        bool predicted = new KongSkill().TryCreateSupplementPlan(
            state.Players[0], SkillTestData.T(6, 3), out KongSkill.KongSupplementPlan plan);

        Assert.True(predicted);
        Assert.Equal(new int[] { 1 }, plan.HandSlots.ToArray());
        Assert.Equal(SkillTestData.T(6, 3).Id, plan.Tile.Id);
        Assert.Equal(state.Players[0].Hand[plan.HandSlots[0]].Id, plan.Tile.Id);

        new KongSkill().Commit(SkillTestData.Context(state, 0, drawnTile: SkillTestData.T(6, 3)));

        Assert.Equal(MeldKind.KongSupplement, pong6.Kind);
        Assert.Equal(4, pong6.Tiles.Count);
        Assert.Equal(plan.Tile.Id, pong6.Tiles[3].Id);
        Assert.Equal(MeldKind.Pong, pong5.Kind);
        Assert.Equal(3, pong5.Tiles.Count);
    }

    [Fact]
    public void TryCreateSupplementPlan_HandHeldFourthTileMatchesCommittedTile()
    {
        // 摸到的是无关牌（9万c0，无碰）：预判仍要按手牌槽位升序返回 5万c3，Commit 加进碰的也是它。
        GameState state = SkillTestData.NewState();
        Meld pong = PongOf(5, targetSeat: 1);
        state.Players[0].Melds.Add(pong);
        SkillTestData.FillHand(
            state, 0, [SkillTestData.T(9, 0), SkillTestData.T(5, 3), .. SkillTestData.Fillers(2)]);
        SkillTestData.FillWall(state, SkillTestData.T(33, 0), SkillTestData.T(30, 0));

        bool predicted = new KongSkill().TryCreateSupplementPlan(
            state.Players[0], SkillTestData.T(9, 0), out KongSkill.KongSupplementPlan plan);

        Assert.True(predicted);
        Assert.Equal(new int[] { 1 }, plan.HandSlots.ToArray());
        Assert.Equal(SkillTestData.T(5, 3).Id, plan.Tile.Id);

        // 无摸牌提示时与「摸到无关牌」等价：同一张牌、同一槽位。
        Assert.True(new KongSkill().TryCreateSupplementPlan(
            state.Players[0], null, out KongSkill.KongSupplementPlan noHintPlan));
        Assert.Equal(plan.Tile.Id, noHintPlan.Tile.Id);
        Assert.Equal(plan.HandSlots.ToArray(), noHintPlan.HandSlots.ToArray());

        // 会话侧 Validate 与 Commit 走同一判定：校验通过才会执行，两者结论必须一致。
        Assert.True(new KongSkill().Validate(SkillTestData.Context(state, 0, drawnTile: SkillTestData.T(9, 0))));

        new KongSkill().Commit(SkillTestData.Context(state, 0, drawnTile: SkillTestData.T(9, 0)));

        Assert.Equal(MeldKind.KongSupplement, pong.Kind);
        Assert.Equal(4, pong.Tiles.Count);
        Assert.Equal(plan.Tile.Id, pong.Tiles[3].Id);
        Assert.Equal((ushort)24, pong.Tiles[3].Id);
    }

    [Fact]
    public void TryCreateSupplementPlan_FalseWithoutPongOrWithoutFourthTile()
    {
        // 无碰：摸到同牌种也 false。
        GameState noPong = SkillTestData.NewState();
        SkillTestData.FillHand(noPong, 0, [SkillTestData.T(5, 3), .. SkillTestData.Fillers(5)]);

        Assert.False(new KongSkill().TryCreateSupplementPlan(
            noPong.Players[0], SkillTestData.T(5, 3), out KongSkill.KongSupplementPlan noPongPlan));
        Assert.Null(noPongPlan.HandSlots);

        // 有碰但手牌没有第 4 张：摸牌提示与手牌扫描都落空。
        GameState noFourth = SkillTestData.NewState();
        noFourth.Players[0].Melds.Add(PongOf(5, targetSeat: 1));
        SkillTestData.FillHand(noFourth, 0, [SkillTestData.T(9, 0), .. SkillTestData.Fillers(5)]);

        Assert.False(new KongSkill().TryCreateSupplementPlan(
            noFourth.Players[0], SkillTestData.T(9, 0), out KongSkill.KongSupplementPlan noFourthPlan));
        Assert.Null(noFourthPlan.HandSlots);
        Assert.False(new KongSkill().TryCreateSupplementPlan(
            noFourth.Players[0], null, out KongSkill.KongSupplementPlan noHintPlan));
        Assert.Null(noHintPlan.HandSlots);
    }

    /// <summary>断言四座 delta 与精确位集（四座和 0 由各用例内联断言）。</summary>
    private static void AssertRound(ScoreManager scores, short[] expectedDelta, ScoreActionType expectedType)
    {
        Assert.Equal(expectedDelta, scores.RoundDelta.ToArray());
        Assert.Equal(expectedType, scores.RoundActionType);
    }

    /// <summary>构造一组指定牌种的碰（物理牌 copy 0–2，目标座位由调用方给）。</summary>
    private static Meld PongOf(int typeId, byte targetSeat) =>
        new(
            MeldKind.Pong,
            [SkillTestData.T(typeId, 0), SkillTestData.T(typeId, 1), SkillTestData.T(typeId, 2)],
            opened: true,
            targetSeat: targetSeat);

    /// <summary>补杠局面：座位 0 已有 5万碰，手牌槽 0 是第 4 张 5万（物理 id 24）。</summary>
    private static GameState SupplementBoard(out Meld pong)
    {
        GameState state = SkillTestData.NewState();
        pong = PongOf(5, targetSeat: 1);
        state.Players[0].Melds.Add(pong);
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 3), .. SkillTestData.Fillers(10)]);
        return state;
    }
}
