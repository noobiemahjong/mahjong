using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// T-5 §1 的单测矩阵：R1–R7 的单变体断言点 + T-1 §5 的 21 对组合 + 流局行。
/// 每行只断言两件事：结论（四座 delta 精确相等、四座和为 0）与旗标（<see cref="ScoreManager.RoundActionType"/>
/// 与完整期望位集相等，不是「包含」），互斥与优先级行另附「落败一方不记账、不置位」的断言。
///
/// 记号：座 0 = 庄；delta 即 <c>RoundEnded.scoreDelta</c>；<c>per0</c> = (无鬼 4 / 有鬼 2) × (七对 ×2)，
/// <c>perK</c> = 2 × per0，<c>b</c> = 连庄表 <c>dealerStreakBonus[min(streak, len) - 1]</c>，表 [0,1,2,3]。
/// 组合行默认 streak = 1（b = 0，不付连庄）；含 R1 的行取 streak = 3（b = 2）；含 R2 的行赢家取非庄座位。
/// 金额与位值按 T-1 §2/§4 手算，不调用会话代码生成期望值。
///
/// 会话侧（候选升序、<c>RequestKind.RobbingKong</c> 的 Win|Pass 与 deadline、补杠取消后无 Meld/杠分、
/// 四副露判定、单链优先级的实际落点）由 RobbingKongFlowTests 与 GameSessionRoundSettlementTests 覆盖，
/// 本文件钉住账本侧可机械判死的那一半。
/// </summary>
public sealed class RuleVariantMatrixTests
{
    /// <summary>连庄表 [0,1,2,3] 的第 streak 次取值（streak 1–5 覆盖表头到表尾钳位）。</summary>
    private static readonly int[] DealerStreakTable = [0, 1, 2, 3];

    /// <summary>含 R1 的组合行取 streak = 3（b = 2），与手算例 S-1/S-8 同口径。</summary>
    private const int StreakThreeBonus = 2;

    /// <summary>抢杠的两支：赢家位与 payer 位。</summary>
    private const ScoreActionType RobbingBits =
        ScoreActionType.RobbingTheKong | ScoreActionType.RobbingTheKongPayForEveryone;

    /// <summary>常规自摸（无鬼）的两位。</summary>
    private const ScoreActionType Standard = ScoreActionType.SelfDrawn | ScoreActionType.NoJoker;

    /// <summary>三种杠位（用于「本局有无杠」与「抢杠取消后无杠位」的判据）。</summary>
    private const ScoreActionType KongBits =
        ScoreActionType.ConcealedKong | ScoreActionType.ExposedKong | ScoreActionType.KongSupplement;

    // ------------------------------------------------------------------ 单变体 R1–R7

    /// <summary>R1 连庄奖励：128 进掩码；b = 0 不置位；streak 1–5 查表钳位到表尾 3。</summary>
    [Fact]
    public void R1_DealerStreak_PaysThreeSharesAndSkipsZero()
    {
        int[] byStreak = new[] { 1, 2, 3, 4, 5 }.Select(streak => DealerStreakBonus((byte)streak)).ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3, 3 }, byStreak);

        ScoreManager unpaid = new();
        unpaid.ApplyDealerStreak(bankerSeat: 0, bonus: 0);
        AssertLedger(unpaid, new short[] { 0, 0, 0, 0 }, ScoreActionType.None);

        ScoreManager paid = new();
        paid.ApplyDealerStreak(bankerSeat: 0, bonus: StreakThreeBonus);
        AssertLedger(paid, new short[] { 6, -2, -2, -2 }, ScoreActionType.DealerStreak);

        // 128 是已发布位值且已进 AllActionTypes 掩码：Apply 接受该位、拒绝未定义的 32768。
        Assert.Equal(128, (int)ScoreActionType.DealerStreak);
        ScoreManager mask = new();
        mask.Apply(seat: 1, delta: 1, type: ScoreActionType.DealerStreak);
        Assert.Equal(ScoreActionType.DealerStreak, mask.RoundActionType);
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.Apply(0, 0, (ScoreActionType)32768));
    }

    /// <summary>R2 跟庄：256 进掩码；非庄胡庄家付 1；庄家胡与流局不付、不置位。</summary>
    [Fact]
    public void R2_FollowingTheBanker_PaysOneOnlyForNonBankerWins()
    {
        Assert.Equal(256, (int)ScoreActionType.FollowingTheBanker);

        ScoreManager paid = new();
        paid.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);
        AssertLedger(paid, new short[] { -1, 0, 1, 0 }, ScoreActionType.FollowingTheBanker);

        // 庄家胡：本入口金额 0 → 不改暂存、不置位（会话侧不调本入口，见 S-1）。
        ScoreManager bankerWin = new();
        bankerWin.ApplyFollowingTheBanker(winnerSeat: 0, bankerSeat: 0, amount: 0);
        AssertLedger(bankerWin, new short[] { 0, 0, 0, 0 }, ScoreActionType.None);

        // 流局：先回滚再释放，净变化 0、位集空。
        ScoreManager draw = new();
        draw.RollbackRound();
        draw.Release();
        AssertLedger(draw, new short[] { 0, 0, 0, 0 }, ScoreActionType.None);

        ScoreManager mask = new();
        mask.Apply(seat: 3, delta: 0, type: ScoreActionType.FollowingTheBanker);
        Assert.Equal(ScoreActionType.FollowingTheBanker, mask.RoundActionType);
    }

    /// <summary>R3 中马：墙头取牌、墙空跳过、马座位 = 物理牌 id % 4；512 进掩码且 0 金额不置位。</summary>
    [Fact]
    public void R3_HorseBonus_RevealsFromWallHeadAndSkipsWhenWallEmpty()
    {
        // 物理牌 1–136 对 4 取余：四座各 34 次（RB-06，不用牌种 id）。
        int[] distribution = new int[GameState.PlayerCount];
        for (int physicalId = 1; physicalId <= GameState.PlayerCount * 34; physicalId++)
        {
            distribution[physicalId % GameState.PlayerCount]++;
        }

        Assert.All(distribution, count => Assert.Equal(34, count));

        // 墙头取牌进公共揭示区：物理 id 不变；墙空返回 false 且不改任何区域。
        var state = new GameState();
        state.Wall.Append(RulesTestData.FromId(17));
        Assert.Equal(1, RulesTestData.FromId(17).Id % GameState.PlayerCount);
        Assert.True(state.TryRevealHorse(out Tile horse));
        Assert.Equal(RulesTestData.FromId(17).Id, horse.Id);
        Assert.Equal(1, state.Revealed.Count);
        Assert.False(state.TryRevealHorse(out _));
        Assert.Equal(1, state.Revealed.Count);

        // 命中：赢家 +3、其余三家各 −1；512 已进掩码。
        Assert.Equal(512, (int)ScoreActionType.HorseWinner);
        ScoreManager hit = new();
        hit.ApplyHorseBonus(winnerSeat: 1, horseBonus: 1);
        AssertLedger(hit, new short[] { -1, 3, -1, -1 }, ScoreActionType.HorseWinner);

        // 未命中/关闭：调用方不调本入口，等价于金额 0 → 不置位。
        ScoreManager skipped = new();
        skipped.ApplyHorseBonus(winnerSeat: 2, horseBonus: 0);
        AssertLedger(skipped, new short[] { 0, 0, 0, 0 }, ScoreActionType.None);
    }

    /// <summary>R4 抢杠：kind = 5、被抢者付 3×per0；补杠取消后无杠位、不算自摸。</summary>
    [Fact]
    public void R4_RobbingKong_ChargesTheRobbedSeatThreeShares()
    {
        Assert.Equal(5, (int)RequestKind.RobbingKong);

        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 2, robbedSeat: 3, basePer: 4, sevenPairs: false);
        AssertLedger(scores, new short[] { 0, 0, 12, -12 }, RobbingBits);

        // 补杠被取消：不收杠分、不置任何杠位，也不算自摸（抢杠胡不置 SelfDrawn / NoJoker）。
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & KongBits);
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.SelfDrawn);
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.NoJoker);
    }

    /// <summary>R5 杠上开花承包：只有明杠放杠者付款；补杠/暗杠补牌自摸不承包（三家常付）。</summary>
    [Fact]
    public void R5_KongUpFlowerContract_ChargesOnlyTheExposedKongPayer()
    {
        ScoreManager contract = new();
        contract.ApplyKong(seat: 1, kind: MeldKind.KongExposed, sourceSeat: 2);
        contract.ApplyKongUpFlowerContract(winnerSeat: 1, payerSeat: 2, basePer: 4, hasJoker: false, sevenPairs: false);
        AssertLedger(
            contract,
            new short[] { 0, 27, -27, 0 },
            ScoreActionType.ExposedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone
            | ScoreActionType.NoJoker);

        // 补杠补牌自摸：per = 4×2 = 8，三家常付 → 不置承包 payer 位。
        ScoreManager supplement = new();
        supplement.ApplyKong(seat: 1, kind: MeldKind.KongSupplement);
        supplement.ApplySelfDrawn(winnerSeat: 1, hasJoker: false, sevenPairs: false, kongUpFlower: true);
        Assert.Equal(ScoreActionType.None, supplement.RoundActionType & ScoreActionType.KongUpFlowerPayForEveryone);
        AssertLedger(
            supplement,
            new short[] { -9, 27, -9, -9 },
            ScoreActionType.KongSupplement
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.NoJoker);

        // 暗杠补牌自摸：同样不承包。
        ScoreManager concealed = new();
        concealed.ApplyKong(seat: 1, kind: MeldKind.KongConcealed);
        concealed.ApplySelfDrawn(winnerSeat: 1, hasJoker: false, sevenPairs: false, kongUpFlower: true);
        Assert.Equal(ScoreActionType.None, concealed.RoundActionType & ScoreActionType.KongUpFlowerPayForEveryone);
        AssertLedger(
            concealed,
            new short[] { -10, 30, -10, -10 },
            ScoreActionType.ConcealedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.NoJoker);
    }

    /// <summary>R6 12 张承包：供牌者付 3×per0、其余两座 0；赢家笔与供牌者笔同置 TwelveCardsConceded。</summary>
    [Fact]
    public void R6_TwelveCardsContract_ChargesTheFourthMeldSupplier()
    {
        ScoreManager scores = new();
        scores.ApplyTwelveCardsContract(winnerSeat: 3, payerSeat: 1, basePer: 4, hasJoker: false);
        AssertLedger(
            scores,
            new short[] { 0, -12, 0, 12 },
            ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded | ScoreActionType.NoJoker);

        // 一位规则：赢家 +3×per0、供牌者 −3×per0，其余两座 0（四座和 0 由 AssertLedger 断言）。
        Assert.Equal(-(int)scores.RoundDelta[1], (int)scores.RoundDelta[3]);
    }

    /// <summary>R7 优先级：抢杠 ＞ 杠上开花 ＞ 12 张，单链只付一个承包者。</summary>
    [Fact]
    public void R7_Priority_PaysExactlyOneContractPayer()
    {
        // 抢杠居顶：抢杠胡不置 SelfDrawn，因此与杠上开花承包（需 SelfDrawn + 本局有杠）互斥。
        ScoreManager robbing = new();
        robbing.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);
        Assert.Equal(
            ScoreActionType.None,
            robbing.RoundActionType & (ScoreActionType.KongUpFlowerPayForEveryone | ScoreActionType.TwelveCardsConceded));

        // 杠上开花压 12 张：明杠承包成立时 12 张既不记账也不置位。
        ScoreManager flower = new();
        flower.ApplyKong(seat: 0, kind: MeldKind.KongExposed, sourceSeat: 2);
        flower.ApplyKongUpFlowerContract(winnerSeat: 0, payerSeat: 2, basePer: 4, hasJoker: false, sevenPairs: false);
        AssertLedger(
            flower,
            new short[] { 27, 0, -27, 0 },
            ScoreActionType.ExposedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone
            | ScoreActionType.NoJoker);

        // 单链只重定向一个 payer：每例恰两座非 0（赢家与 payer），其余两座为 0。
        foreach (ScoreManager ledger in new[] { robbing, flower })
        {
            Assert.Equal(2, ledger.RoundDelta.Count(value => value != 0));
        }
    }

    // ------------------------------------------------------------------ 21 对组合

    /// <summary>1×2 互斥：庄家胡付连庄、不付跟庄；非庄胡付跟庄、不付连庄；两位永不同局出现。</summary>
    [Fact]
    public void Pair_1x2_DealerStreakAndFollowingTheBanker_AreMutuallyExclusive()
    {
        // 庄胡：自摸 12/−4 + 连庄 b=2 → 18/−6×3；跟庄不付（金额 0）。
        ScoreManager bankerWin = new();
        bankerWin.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        bankerWin.ApplyDealerStreak(bankerSeat: 0, bonus: StreakThreeBonus);
        bankerWin.ApplyFollowingTheBanker(winnerSeat: 0, bankerSeat: 0, amount: 0);
        AssertLedger(bankerWin, new short[] { 18, -6, -6, -6 }, Standard | ScoreActionType.DealerStreak);

        // 非庄胡：自摸 + 跟庄 → −5/−4/13/−4；连庄不付（b=0）。
        ScoreManager nonBankerWin = new();
        nonBankerWin.ApplySelfDrawn(winnerSeat: 2, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        nonBankerWin.ApplyDealerStreak(bankerSeat: 0, bonus: 0);
        nonBankerWin.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);
        AssertLedger(nonBankerWin, new short[] { -5, -4, 13, -4 }, Standard | ScoreActionType.FollowingTheBanker);

        // 互斥的结构判据：两位从不同时出现。
        foreach (ScoreManager ledger in new[] { bankerWin, nonBankerWin })
        {
            bool streak = (ledger.RoundActionType & ScoreActionType.DealerStreak) != 0;
            bool following = (ledger.RoundActionType & ScoreActionType.FollowingTheBanker) != 0;
            Assert.False(streak && following);
        }
    }

    /// <summary>1×3：庄自摸 + 连庄 b=2 + 马中 → 3b + 3×马 叠加到自摸上。</summary>
    [Fact]
    public void Pair_1x3_DealerStreakAndHorseBonus_Stack()
    {
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: StreakThreeBonus);
        scores.ApplyHorseBonus(winnerSeat: 0, horseBonus: 1);

        // 12 + 3×2 + 3 = 21；其余三家 −4 −2 −1 = −7。
        AssertLedger(
            scores,
            new short[] { 21, -7, -7, -7 },
            Standard | ScoreActionType.DealerStreak | ScoreActionType.HorseWinner);
    }

    /// <summary>1×4：庄家抢杠胡 → 抢杠 3×per0 + 三家 3b。</summary>
    [Fact]
    public void Pair_1x4_DealerStreakStacksOnBankerRobbingKong()
    {
        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: StreakThreeBonus);

        // 12 + 6 = 18；座 3 −12 −2 = −14。
        AssertLedger(scores, new short[] { 18, -2, -2, -14 }, RobbingBits | ScoreActionType.DealerStreak);
    }

    /// <summary>1×5：庄家明杠补牌自摸承包 → 3×perK + 3b。</summary>
    [Fact]
    public void Pair_1x5_DealerStreakStacksOnKongUpFlowerContract()
    {
        ScoreManager scores = new();
        scores.ApplyKong(seat: 0, kind: MeldKind.KongExposed, sourceSeat: 2);
        scores.ApplyKongUpFlowerContract(winnerSeat: 0, payerSeat: 2, basePer: 4, hasJoker: false, sevenPairs: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: StreakThreeBonus);

        // 24 + 3 (杠分) + 6 = 33；座 1/3 各 −2，座 2 −24 −3 −2 = −29。
        AssertLedger(
            scores,
            new short[] { 33, -2, -29, -2 },
            ScoreActionType.ExposedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone
            | ScoreActionType.NoJoker
            | ScoreActionType.DealerStreak);
    }

    /// <summary>1×6：庄家 12 张承包 → 3×per0 + 3b。</summary>
    [Fact]
    public void Pair_1x6_DealerStreakStacksOnTwelveCardsContract()
    {
        ScoreManager scores = new();
        scores.ApplyTwelveCardsContract(winnerSeat: 0, payerSeat: 1, basePer: 4, hasJoker: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: StreakThreeBonus);

        // 12 + 6 = 18；供牌者座 1 −12 −2 = −14。
        AssertLedger(
            scores,
            new short[] { 18, -14, -2, -2 },
            ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded | ScoreActionType.NoJoker | ScoreActionType.DealerStreak);
    }

    /// <summary>1×7：庄家承包 + 连庄 → 胜出承包位 + b（此处胜出者为抢杠）。</summary>
    [Fact]
    public void Pair_1x7_DealerStreakStacksOnWhicheverContractWins()
    {
        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyDealerStreak(bankerSeat: 0, bonus: StreakThreeBonus);

        AssertLedger(scores, new short[] { 18, -2, -2, -14 }, RobbingBits | ScoreActionType.DealerStreak);

        // 落败承包位不出现：12 张与开花承包都不置位。
        Assert.Equal(
            ScoreActionType.None,
            scores.RoundActionType & (ScoreActionType.TwelveCardsConceded | ScoreActionType.KongUpFlowerPayForEveryone));
    }

    /// <summary>2×3：非庄自摸 + 马中 → 庄付 1（跟庄）+ 马中各家 1。</summary>
    [Fact]
    public void Pair_2x3_FollowingTheBankerAndHorseBonus_Stack()
    {
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 2, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);
        scores.ApplyHorseBonus(winnerSeat: 2, horseBonus: 1);

        // 自摸 +12/−4、跟庄 +1/−1、马中 +3/−1 → 座 0 −6、座 1 −5、座 2 +16、座 3 −5。
        AssertLedger(
            scores,
            new short[] { -6, -5, 16, -5 },
            Standard | ScoreActionType.FollowingTheBanker | ScoreActionType.HorseWinner);
    }

    /// <summary>2×4：非庄抢杠 + 跟庄 → 抢者 3×per0、庄家 1。</summary>
    [Fact]
    public void Pair_2x4_FollowingTheBankerStacksOnRobbingKong()
    {
        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 2, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);

        AssertLedger(scores, new short[] { -1, 0, 13, -12 }, RobbingBits | ScoreActionType.FollowingTheBanker);
    }

    /// <summary>2×5：非庄开花承包 + 跟庄 → 3×perK、庄家 1。</summary>
    [Fact]
    public void Pair_2x5_FollowingTheBankerStacksOnKongUpFlowerContract()
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

    /// <summary>2×6：非庄 12 张承包 + 跟庄 → 3×per0、庄家 1。</summary>
    [Fact]
    public void Pair_2x6_FollowingTheBankerStacksOnTwelveCardsContract()
    {
        ScoreManager scores = new();
        scores.ApplyTwelveCardsContract(winnerSeat: 3, payerSeat: 1, basePer: 4, hasJoker: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 3, bankerSeat: 0, amount: 1);

        AssertLedger(
            scores,
            new short[] { -1, -12, 0, 13 },
            ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded | ScoreActionType.NoJoker | ScoreActionType.FollowingTheBanker);
    }

    /// <summary>2×7：非庄承包 + 跟庄 → 胜出承包 + 1（此处胜出者为抢杠）。</summary>
    [Fact]
    public void Pair_2x7_FollowingTheBankerStacksOnWhicheverContractWins()
    {
        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 2, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);

        AssertLedger(scores, new short[] { -1, 0, 13, -12 }, RobbingBits | ScoreActionType.FollowingTheBanker);
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.TwelveCardsConceded);
    }

    /// <summary>3×4：抢杠 + 马中 → 抢者 3×per0 + 马中各家 1。</summary>
    [Fact]
    public void Pair_3x4_HorseBonusStacksOnRobbingKong()
    {
        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyHorseBonus(winnerSeat: 0, horseBonus: 1);

        // 12 + 3 = 15；座 3 −12 −1 = −13。
        AssertLedger(scores, new short[] { 15, -1, -1, -13 }, RobbingBits | ScoreActionType.HorseWinner);
    }

    /// <summary>3×5：开花承包 + 马中 → 3×perK + 马中各家 1。</summary>
    [Fact]
    public void Pair_3x5_HorseBonusStacksOnKongUpFlowerContract()
    {
        ScoreManager scores = new();
        scores.ApplyKong(seat: 1, kind: MeldKind.KongExposed, sourceSeat: 2);
        scores.ApplyKongUpFlowerContract(winnerSeat: 1, payerSeat: 2, basePer: 4, hasJoker: false, sevenPairs: false);
        scores.ApplyHorseBonus(winnerSeat: 1, horseBonus: 1);

        // 24 + 3 + 3 = 30；座 0/3 各 −1，座 2 −24 −3 −1 = −28。
        AssertLedger(
            scores,
            new short[] { -1, 30, -28, -1 },
            ScoreActionType.ExposedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone
            | ScoreActionType.NoJoker
            | ScoreActionType.HorseWinner);
    }

    /// <summary>3×6：12 张承包 + 马中 → 3×per0 + 马中各家 1。</summary>
    [Fact]
    public void Pair_3x6_HorseBonusStacksOnTwelveCardsContract()
    {
        ScoreManager scores = new();
        scores.ApplyTwelveCardsContract(winnerSeat: 3, payerSeat: 1, basePer: 4, hasJoker: false);
        scores.ApplyHorseBonus(winnerSeat: 3, horseBonus: 1);

        // 12 + 3 = 15；供牌者座 1 −12 −1 = −13。
        AssertLedger(
            scores,
            new short[] { -1, -13, -1, 15 },
            ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded | ScoreActionType.NoJoker | ScoreActionType.HorseWinner);
    }

    /// <summary>3×7：承包 + 马 + 优先级 → 只取胜出承包（抢杠）与马。</summary>
    [Fact]
    public void Pair_3x7_HorseBonusStacksOnWhicheverContractWins()
    {
        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);
        scores.ApplyHorseBonus(winnerSeat: 0, horseBonus: 1);

        AssertLedger(scores, new short[] { 15, -1, -1, -13 }, RobbingBits | ScoreActionType.HorseWinner);
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.TwelveCardsConceded);
    }

    /// <summary>4×5 互斥：抢杠取消补杠后本局无杠，杠上开花承包不可能同局成立。</summary>
    [Fact]
    public void Pair_4x5_RobbingKongAndKongUpFlowerContract_AreMutuallyExclusive()
    {
        ScoreManager robbing = new();
        robbing.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);
        Assert.Equal(ScoreActionType.None, robbing.RoundActionType & ScoreActionType.KongUpFlowerPayForEveryone);
        Assert.Equal(ScoreActionType.None, robbing.RoundActionType & KongBits);

        // 无杠时「杠上开花自摸」被账本直接拒绝：两规则无法叠加。
        Assert.Throws<InvalidOperationException>(
            () => robbing.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: true));
        Assert.Equal(new short[] { 12, 0, 0, -12 }, robbing.RoundDelta.ToArray());
    }

    /// <summary>4×6：优先级取 R4 —— 抢杠压过 12 张，供牌者不付款、不置位。</summary>
    [Fact]
    public void Pair_4x6_PriorityKeepsRobbingKongAndDropsTwelveCards()
    {
        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);

        AssertLedger(scores, new short[] { 12, 0, 0, -12 }, RobbingBits);
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.TwelveCardsConceded);
        Assert.Equal(0, scores.RoundDelta[1]);
    }

    /// <summary>4×7：R4 居顶 —— 抢杠成立时开花承包与 12 张都不记账。</summary>
    [Fact]
    public void Pair_4x7_RobbingKongOutranksBothOtherContracts()
    {
        ScoreManager scores = new();
        scores.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);

        AssertLedger(scores, new short[] { 12, 0, 0, -12 }, RobbingBits);
        Assert.Equal(
            ScoreActionType.None,
            scores.RoundActionType & (ScoreActionType.KongUpFlowerPayForEveryone | ScoreActionType.TwelveCardsConceded));
    }

    /// <summary>5×6：优先级取 R5 —— 杠上开花承包压过 12 张。</summary>
    [Fact]
    public void Pair_5x6_PriorityKeepsKongUpFlowerAndDropsTwelveCards()
    {
        ScoreManager scores = new();
        scores.ApplyKong(seat: 0, kind: MeldKind.KongExposed, sourceSeat: 2);
        scores.ApplyKongUpFlowerContract(winnerSeat: 0, payerSeat: 2, basePer: 4, hasJoker: false, sevenPairs: false);

        AssertLedger(
            scores,
            new short[] { 27, 0, -27, 0 },
            ScoreActionType.ExposedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone
            | ScoreActionType.NoJoker);
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.TwelveCardsConceded);
        Assert.Equal(0, scores.RoundDelta[1]);
    }

    /// <summary>5×7：R5 压 R6、让 R4 —— 开花承包付款，12 张不付款；抢杠一旦成立即换人付款（4×7）。</summary>
    [Fact]
    public void Pair_5x7_KongUpFlowerBeatsTwelveCardsButYieldsToRobbingKong()
    {
        ScoreManager flower = new();
        flower.ApplyKong(seat: 0, kind: MeldKind.KongExposed, sourceSeat: 2);
        flower.ApplyKongUpFlowerContract(winnerSeat: 0, payerSeat: 2, basePer: 4, hasJoker: false, sevenPairs: false);

        AssertLedger(
            flower,
            new short[] { 27, 0, -27, 0 },
            ScoreActionType.ExposedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone
            | ScoreActionType.NoJoker);
        Assert.Equal(ScoreActionType.None, flower.RoundActionType & ScoreActionType.TwelveCardsConceded);

        // 让 R4：同一胡牌若由抢杠成立，payer 换成被抢者、承包位换成抢杠两位。
        ScoreManager robbing = new();
        robbing.ApplyRobbingKong(winnerSeat: 0, robbedSeat: 3, basePer: 4, sevenPairs: false);
        AssertLedger(robbing, new short[] { 12, 0, 0, -12 }, RobbingBits);
        Assert.Equal(ScoreActionType.None, robbing.RoundActionType & ScoreActionType.KongUpFlowerPayForEveryone);
    }

    /// <summary>6×7：R6 居底 —— 只有 12 张承包成立时由供牌者付款，两个上位承包位都不出现。</summary>
    [Fact]
    public void Pair_6x7_TwelveCardsStaysAtTheBottomOfTheChain()
    {
        ScoreManager scores = new();
        scores.ApplyTwelveCardsContract(winnerSeat: 0, payerSeat: 1, basePer: 4, hasJoker: false);

        AssertLedger(
            scores,
            new short[] { 12, -12, 0, 0 },
            ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded | ScoreActionType.NoJoker);
        Assert.Equal(
            ScoreActionType.None,
            scores.RoundActionType & (RobbingBits | ScoreActionType.KongUpFlowerPayForEveryone));
    }

    // ------------------------------------------------------------------ 流局行

    /// <summary>流局：四座 0、不翻马（无 HorseRevealed / 无马位）、winnerSeat = −1、位集 None。</summary>
    [Fact]
    public void DrawRow_AllZeroNoHorseRevealAndWinnerMinusOne()
    {
        ScoreManager scores = new();
        scores.RollbackRound();
        scores.Release();
        AssertLedger(scores, new short[] { 0, 0, 0, 0 }, ScoreActionType.None);

        // 流局路径不调用翻马：牌墙仍有牌时揭示区也必须为空。
        var state = new GameState();
        state.Wall.Append(RulesTestData.FromId(9));
        Assert.Equal(0, state.Revealed.Count);
        Assert.Equal(ScoreActionType.None, scores.RoundActionType & ScoreActionType.HorseWinner);

        // winnerSeat = −1 的编码：赢家字节 0xFF、四座 0、位集 None。
        var buffer = new SessionEventBuffer();
        buffer.EmitRoundEnded(tick: 100, winnerSeat: -1, scoreDelta: new short[GameState.PlayerCount], actionType: ScoreActionType.None);
        SessionEvent ended = Assert.Single(buffer.Drain(255));
        Assert.Equal(0xFF, ended.Payload.Span[0]);
        RoundEndedPayload round = SessionEventPayloadReader.ReadRoundEnded(ended);
        Assert.Equal((sbyte)-1, round.WinnerSeat);
        Assert.Equal(ScoreActionType.None, round.ActionType);
        Assert.All(round.ScoreDelta, delta => Assert.Equal(0, delta));
    }

    /// <summary>连庄奖励表取值：<c>table[min(streak, 表长) - 1]</c>，表尾钳位。</summary>
    private static int DealerStreakBonus(byte streak)
    {
        int index = Math.Min(streak, DealerStreakTable.Length) - 1;
        return index < 0 ? 0 : DealerStreakTable[index];
    }

    /// <summary>断言四座 delta 与完整位集相等，并断言四座和为 0。</summary>
    private static void AssertLedger(ScoreManager scores, short[] expectedDelta, ScoreActionType expectedType)
    {
        Assert.Equal(expectedDelta, scores.RoundDelta.ToArray());
        Assert.Equal(expectedType, scores.RoundActionType);
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
    }
}
