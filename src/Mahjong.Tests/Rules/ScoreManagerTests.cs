using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 计分账本契约：单次动作先校验再写暂存（失败不改状态）、每次计分动作四座净 0、
/// Release/RollbackRound 的分层语义与幂等、只读视图不能改内部状态。
/// 数值锚点按手算：无鬼自摸 +12/-4×3；有鬼 +6/-2×3；七对无鬼 +24/-8×3；七对有鬼 +12/-4×3；
/// 杠上开花再 ×2；明杠 +3/-3、暗杠 +6/-2×3、补杠 +3/-1×3；连庄奖励 +3b/-b×3；跟庄 +1/-1；
/// 抢杠 +3×base/-3×base、杠上开花承包 +3×2×base/-3×2×base、12 张承包 +3×base/-3×base（均单 payer、四座和 0）。
/// </summary>
public sealed class ScoreManagerTests
{
    [Fact]
    public void NewManager_StartsEmpty()
    {
        ScoreManager scores = new();

        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.TotalScores.ToArray());
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Fact]
    public void Apply_AccumulatesDeltaAndActionType()
    {
        ScoreManager scores = new();

        scores.Apply(1, 5, ScoreActionType.SevenPairs);
        scores.Apply(1, -3, ScoreActionType.None);

        Assert.Equal(new short[] { 0, 2, 0, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(ScoreActionType.SevenPairs, scores.RoundActionType);
    }

    [Fact]
    public void Apply_AcceptsDefinedCombinationBitsAndAggregatesThem()
    {
        // 已定义位的组合（如 SelfDrawn|NoJoker）必须被接受，不能被误判成「未定义位」。
        ScoreManager scores = new();

        scores.Apply(0, 4, ScoreActionType.SelfDrawn | ScoreActionType.NoJoker);
        scores.Apply(1, -4, ScoreActionType.SevenPairs);

        Assert.Equal(new short[] { 4, -4, 0, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(
            ScoreActionType.SelfDrawn | ScoreActionType.NoJoker | ScoreActionType.SevenPairs,
            scores.RoundActionType);
    }

    [Fact]
    public void Apply_None_IsPureAmountAdjustment()
    {
        ScoreManager scores = new();

        scores.Apply(2, -7, ScoreActionType.None);

        Assert.Equal(new short[] { 0, 0, -7, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(255)]
    public void Apply_SeatOutOfRange_ThrowsAndLeavesStateUnchanged(byte seat)
    {
        ScoreManager scores = new();
        scores.Apply(0, 9, ScoreActionType.SelfDrawn);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(() => scores.Apply(seat, 1, ScoreActionType.None));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Theory]
    [InlineData(16384)]
    [InlineData(32768)]
    [InlineData(49152)]
    [InlineData(16385)]
    [InlineData(24576)]
    public void Apply_UndefinedFlagBits_ThrowsAndLeavesStateUnchanged(int rawType)
    {
        // 未定义位取 16384、32768 及其组合（含「已定义位 + 未定义位」的混用：16385 = 1 + 16384、
        // 24576 = 8192 + 16384）。128–256 与 1024–8192 已是规则位，定义后不再是非法输入。
        ScoreManager scores = new();
        scores.Apply(3, 4, ScoreActionType.NoJoker);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scores.Apply(1, 1, (ScoreActionType)rawType));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Fact]
    public void ApplySelfDrawn_NoJoker_PaysFourEach()
    {
        ScoreManager scores = new();

        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);

        short[] expected = [12, -4, -4, -4];
        Assert.Equal(expected, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(ScoreActionType.SelfDrawn | ScoreActionType.NoJoker, scores.RoundActionType);
    }

    [Fact]
    public void ApplySelfDrawn_WithJoker_PaysTwoEach()
    {
        ScoreManager scores = new();

        scores.ApplySelfDrawn(winnerSeat: 2, hasJoker: true, sevenPairs: false, kongUpFlower: false);

        short[] expected = [-2, -2, 6, -2];
        Assert.Equal(expected, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(ScoreActionType.SelfDrawn, scores.RoundActionType);
    }

    [Fact]
    public void ApplySelfDrawn_SevenPairsNoJoker_PaysEightEach()
    {
        ScoreManager scores = new();

        scores.ApplySelfDrawn(winnerSeat: 3, hasJoker: false, sevenPairs: true, kongUpFlower: false);

        short[] expected = [-8, -8, -8, 24];
        Assert.Equal(expected, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(
            ScoreActionType.SelfDrawn | ScoreActionType.SevenPairs | ScoreActionType.NoJoker,
            scores.RoundActionType);
    }

    [Fact]
    public void ApplySelfDrawn_SevenPairsWithJoker_PaysFourEach()
    {
        ScoreManager scores = new();

        scores.ApplySelfDrawn(winnerSeat: 1, hasJoker: true, sevenPairs: true, kongUpFlower: false);

        short[] expected = [-4, 12, -4, -4];
        Assert.Equal(expected, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(
            ScoreActionType.SelfDrawn | ScoreActionType.SevenPairs,
            scores.RoundActionType);
    }

    [Fact]
    public void ApplySelfDrawn_KongUpFlowerAfterConcealedKong_DoublesNoJoker()
    {
        ScoreManager scores = new();
        scores.ApplyKong(1, MeldKind.KongConcealed);

        scores.ApplySelfDrawn(winnerSeat: 1, hasJoker: false, sevenPairs: false, kongUpFlower: true);

        // 暗杠 [ -2, 6, -2, -2 ] + 杠上开花无鬼自摸 [ -8, 24, -8, -8 ]。
        short[] expected = [-10, 30, -10, -10];
        Assert.Equal(expected, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(
            ScoreActionType.ConcealedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower
            | ScoreActionType.NoJoker,
            scores.RoundActionType);
    }

    [Fact]
    public void ApplySelfDrawn_KongUpFlowerAfterExposedKong_DoublesWithJoker()
    {
        ScoreManager scores = new();
        scores.ApplyKong(0, MeldKind.KongExposed, sourceSeat: 3);

        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: true, sevenPairs: false, kongUpFlower: true);

        // 明杠 [ 3, 0, 0, -3 ] + 杠上开花有鬼自摸 [ 12, -4, -4, -4 ]。
        short[] expected = [15, -4, -4, -7];
        Assert.Equal(expected, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(
            ScoreActionType.ExposedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower,
            scores.RoundActionType);
    }

    [Fact]
    public void ApplySelfDrawn_SevenPairsWithKongUpFlower_ThrowsAndLeavesStateUnchanged()
    {
        ScoreManager scores = new();
        scores.Apply(0, 5, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<InvalidOperationException>(
            () => scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: true, kongUpFlower: true));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Fact]
    public void ApplySelfDrawn_SevenPairsAfterKong_ThrowsAndLeavesStateUnchanged()
    {
        (MeldKind Kind, byte SourceSeat)[] kongForms =
        [
            (MeldKind.KongExposed, 2),
            (MeldKind.KongConcealed, Meld.NoTargetSeat),
            (MeldKind.KongSupplement, Meld.NoTargetSeat),
        ];

        foreach ((MeldKind kind, byte sourceSeat) in kongForms)
        {
            ScoreManager scores = new();
            scores.ApplyKong(0, kind, sourceSeat);
            string before = RoundTestSupport.Book(scores);

            Assert.Throws<InvalidOperationException>(
                () => scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: true, sevenPairs: true, kongUpFlower: false));

            Assert.Equal(before, RoundTestSupport.Book(scores));
        }
    }

    [Fact]
    public void ApplySelfDrawn_KongUpFlowerWithoutKong_ThrowsAndLeavesStateUnchanged()
    {
        ScoreManager scores = new();
        scores.Apply(2, 3, ScoreActionType.SelfDrawn);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<InvalidOperationException>(
            () => scores.ApplySelfDrawn(winnerSeat: 2, hasJoker: false, sevenPairs: false, kongUpFlower: true));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Fact]
    public void ApplySelfDrawn_SeatOutOfRange_ThrowsAndLeavesStateUnchanged()
    {
        ScoreManager scores = new();
        scores.Apply(1, -3, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scores.ApplySelfDrawn(winnerSeat: 4, hasJoker: false, sevenPairs: false, kongUpFlower: false));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Fact]
    public void ApplyKong_Exposed_TransfersThreeFromSource()
    {
        ScoreManager scores = new();

        scores.ApplyKong(seat: 0, MeldKind.KongExposed, sourceSeat: 2);

        short[] expected = [3, 0, -3, 0];
        Assert.Equal(expected, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(ScoreActionType.ExposedKong, scores.RoundActionType);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 4)]
    [InlineData(0, Meld.NoTargetSeat)]
    public void ApplyKong_ExposedInvalidSource_ThrowsAndLeavesStateUnchanged(byte seat, byte sourceSeat)
    {
        ScoreManager scores = new();
        scores.Apply(3, -2, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentException>(() => scores.ApplyKong(seat, MeldKind.KongExposed, sourceSeat));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Fact]
    public void ApplyKong_Concealed_ChargesTwoToEachOtherSeat()
    {
        ScoreManager scores = new();

        scores.ApplyKong(seat: 3, MeldKind.KongConcealed);

        short[] expected = [-2, -2, -2, 6];
        Assert.Equal(expected, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(ScoreActionType.ConcealedKong, scores.RoundActionType);
    }

    [Fact]
    public void ApplyKong_Supplement_ChargesOneToEachOtherSeat()
    {
        ScoreManager scores = new();

        scores.ApplyKong(seat: 2, MeldKind.KongSupplement);

        short[] expected = [-1, -1, 3, -1];
        Assert.Equal(expected, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(ScoreActionType.KongSupplement, scores.RoundActionType);
    }

    [Theory]
    [InlineData(MeldKind.KongConcealed)]
    [InlineData(MeldKind.KongSupplement)]
    public void ApplyKong_NoTargetKongWithSourceSeat_ThrowsAndLeavesStateUnchanged(MeldKind kind)
    {
        ScoreManager scores = new();
        scores.Apply(0, 1, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentException>(() => scores.ApplyKong(seat: 0, kind, sourceSeat: 1));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Theory]
    [InlineData(MeldKind.Pong)]
    [InlineData((MeldKind)0)]
    [InlineData((MeldKind)5)]
    public void ApplyKong_UndefinedKind_ThrowsAndLeavesStateUnchanged(MeldKind kind)
    {
        ScoreManager scores = new();
        scores.Apply(1, 2, ScoreActionType.SelfDrawn);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(() => scores.ApplyKong(seat: 0, kind));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Fact]
    public void ApplyKong_SeatOutOfRange_ThrowsAndLeavesStateUnchanged()
    {
        ScoreManager scores = new();
        scores.Apply(0, 1, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(() => scores.ApplyKong(seat: 4, MeldKind.KongConcealed));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Fact]
    public void ScoreActionType_DealerAndFollowingBitsKeepPublishedValues()
    {
        // 位值参与回放与协议编码：128 / 256 已定稿，既有 1–64 不动，两位互不重叠。
        Assert.Equal(128, (ushort)ScoreActionType.DealerStreak);
        Assert.Equal(256, (ushort)ScoreActionType.FollowingTheBanker);
        Assert.Equal(0, (ushort)(ScoreActionType.DealerStreak & ScoreActionType.FollowingTheBanker));
        Assert.Equal(384, (ushort)(ScoreActionType.DealerStreak | ScoreActionType.FollowingTheBanker));
    }

    [Fact]
    public void ScoreActionType_RobbingKongAndContractBitsKeepPublishedValues()
    {
        // T-1 §4 定稿位值：1024/2048/4096/8192；四位与 1–256 既有位两两不重叠，并集 15360。
        Assert.Equal(1024, (ushort)ScoreActionType.RobbingTheKong);
        Assert.Equal(2048, (ushort)ScoreActionType.RobbingTheKongPayForEveryone);
        Assert.Equal(4096, (ushort)ScoreActionType.KongUpFlowerPayForEveryone);
        Assert.Equal(8192, (ushort)ScoreActionType.TwelveCardsConceded);

        ScoreActionType[] newBits =
        [
            ScoreActionType.RobbingTheKong,
            ScoreActionType.RobbingTheKongPayForEveryone,
            ScoreActionType.KongUpFlowerPayForEveryone,
            ScoreActionType.TwelveCardsConceded,
        ];

        for (int left = 0; left < newBits.Length; left++)
        {
            for (int right = left + 1; right < newBits.Length; right++)
            {
                Assert.Equal(0, (ushort)(newBits[left] & newBits[right]));
            }

            Assert.Equal(0, (ushort)(newBits[left] & ScoreActionType.FollowingTheBanker));
            Assert.Equal(0, (ushort)(newBits[left] & ScoreActionType.DealerStreak));
        }

        Assert.Equal(15360, newBits.Sum(bit => (int)(ushort)bit));
        Assert.Equal(
            15744,
            (ushort)(ScoreActionType.DealerStreak | ScoreActionType.FollowingTheBanker
                | ScoreActionType.RobbingTheKong | ScoreActionType.RobbingTheKongPayForEveryone
                | ScoreActionType.KongUpFlowerPayForEveryone | ScoreActionType.TwelveCardsConceded));
    }

    [Fact]
    public void Apply_AcceptsDealerStreakAndFollowingTheBankerBits()
    {
        // 掩码扩容后两位都是合法输入；未并入掩码时 Apply 会以未定义位抛 ArgumentOutOfRangeException。
        ScoreManager scores = new();

        scores.Apply(0, 1, ScoreActionType.DealerStreak);
        scores.Apply(1, -1, ScoreActionType.FollowingTheBanker);

        Assert.Equal(
            ScoreActionType.DealerStreak | ScoreActionType.FollowingTheBanker,
            scores.RoundActionType);
        Assert.Equal(new short[] { 1, -1, 0, 0 }, scores.RoundDelta.ToArray());
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    [InlineData(8192)]
    public void Apply_AcceptsRobbingKongAndContractBits(int rawType)
    {
        // 掩码扩容后四位都是合法输入；未并入掩码时 Apply 会以未定义位抛 ArgumentOutOfRangeException。
        ScoreActionType type = (ScoreActionType)rawType;
        ScoreManager scores = new();

        scores.Apply(seat: 2, delta: 3, type);

        Assert.Equal(type, scores.RoundActionType);
        Assert.Equal(new short[] { 0, 0, 3, 0 }, scores.RoundDelta.ToArray());
    }

    [Fact]
    public void Apply_AcceptsAllRobbingAndContractBitsTogether()
    {
        ScoreManager scores = new();

        scores.Apply(
            seat: 1,
            delta: -2,
            ScoreActionType.RobbingTheKong
            | ScoreActionType.RobbingTheKongPayForEveryone
            | ScoreActionType.KongUpFlowerPayForEveryone
            | ScoreActionType.TwelveCardsConceded);

        Assert.Equal(
            ScoreActionType.RobbingTheKong
            | ScoreActionType.RobbingTheKongPayForEveryone
            | ScoreActionType.KongUpFlowerPayForEveryone
            | ScoreActionType.TwelveCardsConceded,
            scores.RoundActionType);
        Assert.Equal(new short[] { 0, -2, 0, 0 }, scores.RoundDelta.ToArray());
    }

    [Fact]
    public void ApplyDealerStreak_PaysThreeSharesToBanker()
    {
        ScoreManager scores = new();

        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 2);

        // 三家各付 2、庄家收 3×2。
        Assert.Equal(new short[] { 6, -2, -2, -2 }, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(ScoreActionType.DealerStreak, scores.RoundActionType);
    }

    [Fact]
    public void ApplyDealerStreak_TableTailBonusThreeStacksOnSelfDrawn()
    {
        // 庄家无鬼自摸 +12/-4×3 之后，streak=4（每家 3）再 +9/-3×3。
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);

        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 3);

        Assert.Equal(new short[] { 21, -7, -7, -7 }, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(
            ScoreActionType.DealerStreak | ScoreActionType.SelfDrawn | ScoreActionType.NoJoker,
            scores.RoundActionType);
    }

    [Fact]
    public void ApplyDealerStreak_ZeroBonus_SkipsWithoutSettingFlag()
    {
        ScoreManager scores = new();
        scores.Apply(1, 3, ScoreActionType.SelfDrawn);
        string before = RoundTestSupport.Book(scores);

        scores.ApplyDealerStreak(bankerSeat: 2, bonus: 0);

        Assert.Equal(before, RoundTestSupport.Book(scores));
        Assert.Equal(ScoreActionType.SelfDrawn, scores.RoundActionType);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-3)]
    public void ApplyDealerStreak_NegativeBonus_ThrowsAndLeavesStateUnchanged(int bonus)
    {
        ScoreManager scores = new();
        scores.ApplyKong(0, MeldKind.KongConcealed);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(() => scores.ApplyDealerStreak(bankerSeat: 0, bonus));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(255)]
    public void ApplyDealerStreak_SeatOutOfRange_ThrowsAndLeavesStateUnchanged(byte bankerSeat)
    {
        ScoreManager scores = new();
        scores.Apply(3, -2, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(() => scores.ApplyDealerStreak(bankerSeat, bonus: 1));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Fact]
    public void ApplyFollowingTheBanker_TransfersAmountFromBankerToWinner()
    {
        ScoreManager scores = new();

        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);

        Assert.Equal(new short[] { -1, 0, 1, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(ScoreActionType.FollowingTheBanker, scores.RoundActionType);
    }

    [Fact]
    public void ApplyFollowingTheBanker_ZeroAmount_SkipsWithoutSettingFlag()
    {
        ScoreManager scores = new();
        scores.Apply(2, 4, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 1, amount: 0);

        Assert.Equal(before, RoundTestSupport.Book(scores));
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    public void ApplyFollowingTheBanker_NegativeAmount_ThrowsAndLeavesStateUnchanged(int amount)
    {
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 1, hasJoker: true, sevenPairs: false, kongUpFlower: false);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scores.ApplyFollowingTheBanker(winnerSeat: 1, bankerSeat: 3, amount));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(0, 4)]
    [InlineData(255, 255)]
    public void ApplyFollowingTheBanker_SeatOutOfRange_ThrowsAndLeavesStateUnchanged(byte winnerSeat, byte bankerSeat)
    {
        ScoreManager scores = new();
        scores.Apply(0, 1, ScoreActionType.None);
        string before = RoundTestSupport.Book(scores);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scores.ApplyFollowingTheBanker(winnerSeat, bankerSeat, amount: 1));

        Assert.Equal(before, RoundTestSupport.Book(scores));
    }

    [Fact]
    public void ApplyFollowingTheBanker_SameSeat_TransfersNothingAndSetsFlag()
    {
        // 赢家与庄家不同座由调用方保证，入口不做该检查：同座时两笔相抵为 0，仍按一位规则置位。
        ScoreManager scores = new();

        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 2, amount: 1);

        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(ScoreActionType.FollowingTheBanker, scores.RoundActionType);
    }

    [Fact]
    public void ApplyDealerStreakAndFollowingTheBanker_StackWithZeroSum()
    {
        ScoreManager scores = new();

        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 2);
        scores.ApplyFollowingTheBanker(winnerSeat: 2, bankerSeat: 0, amount: 1);

        // 连庄 [ 6, -2, -2, -2 ] 之上，赢家 2 收 1、庄家 0 付 1。
        Assert.Equal(new short[] { 5, -2, -1, -2 }, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(
            ScoreActionType.DealerStreak | ScoreActionType.FollowingTheBanker,
            scores.RoundActionType);
    }

    [Fact]
    public void DealerStreakAndFollowingTheBanker_UnionWithSelfDrawnAndKongBits()
    {
        ScoreManager scores = new();
        scores.ApplyKong(seat: 0, MeldKind.KongConcealed);
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);

        scores.ApplyDealerStreak(bankerSeat: 0, bonus: 2);

        // 暗杠 [ 6, -2, -2, -2 ] + 无鬼自摸 [ 12, -4, -4, -4 ] + 连庄 2 [ 6, -2, -2, -2 ]。
        Assert.Equal(new short[] { 24, -8, -8, -8 }, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => (int)value));
        Assert.Equal(
            ScoreActionType.ConcealedKong
            | ScoreActionType.SelfDrawn
            | ScoreActionType.NoJoker
            | ScoreActionType.DealerStreak,
            scores.RoundActionType);
    }

    [Fact]
    public void RollbackRound_ClearsDealerStreakAndFollowingTheBanker()
    {
        ScoreManager scores = new();
        scores.ApplyDealerStreak(bankerSeat: 1, bonus: 1);
        scores.ApplyFollowingTheBanker(winnerSeat: 3, bankerSeat: 1, amount: 1);

        scores.RollbackRound();

        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.TotalScores.ToArray());
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Fact]
    public void Release_MergesDealerStreakAndFollowingTheBankerThenClears()
    {
        ScoreManager scores = new();
        scores.ApplyDealerStreak(bankerSeat: 2, bonus: 1);
        scores.ApplyFollowingTheBanker(winnerSeat: 0, bankerSeat: 2, amount: 1);

        scores.Release();

        // 连庄 [ -1, -1, 3, -1 ] 之上，赢家 0 收 1、庄家 2 付 1。
        Assert.Equal(new short[] { 0, -1, 2, -1 }, scores.TotalScores.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Fact]
    public void RollbackRound_ClearsStagingAndActionButKeepsTotals()
    {
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        scores.Release();
        scores.ApplyKong(1, MeldKind.KongConcealed);
        scores.Apply(2, 5, ScoreActionType.SelfDrawn);

        scores.RollbackRound();

        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(new short[] { 12, -4, -4, -4 }, scores.TotalScores.ToArray());
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Fact]
    public void RollbackRound_IsIdempotent()
    {
        ScoreManager scores = new();
        scores.Apply(0, 7, ScoreActionType.SevenPairs);

        scores.RollbackRound();
        scores.RollbackRound();

        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Fact]
    public void Release_MergesStagingIntoTotalAndClears()
    {
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 1, hasJoker: true, sevenPairs: false, kongUpFlower: false);
        scores.ApplyKong(2, MeldKind.KongSupplement);

        scores.Release();

        // 有鬼自摸 [ -2, 6, -2, -2 ] + 补杠 [ -1, -1, 3, -1 ]。
        Assert.Equal(new short[] { -3, 5, 1, -3 }, scores.TotalScores.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Fact]
    public void Release_IsIdempotent()
    {
        ScoreManager scores = new();
        scores.Apply(0, 9, ScoreActionType.None);

        scores.Release();
        scores.Release();

        Assert.Equal(new short[] { 9, 0, 0, 0 }, scores.TotalScores.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
    }

    [Fact]
    public void DrawRound_RollbackThenRelease_LeavesTotalsUnchanged()
    {
        ScoreManager scores = new();
        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: true, kongUpFlower: false);
        scores.Release();
        short[] totalsBefore = scores.TotalScores.ToArray();

        scores.ApplyKong(1, MeldKind.KongConcealed);
        scores.Apply(2, 9, ScoreActionType.SelfDrawn);
        scores.RollbackRound();
        scores.Release();

        Assert.Equal(totalsBefore, scores.TotalScores.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
    }

    [Fact]
    public void ConsecutiveRounds_StagingRestartsAtZeroAndTotalsAccumulate()
    {
        ScoreManager scores = new();

        scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: false, kongUpFlower: false);
        Assert.Equal(new short[] { 12, -4, -4, -4 }, scores.RoundDelta.ToArray());
        scores.Release();

        scores.ApplySelfDrawn(winnerSeat: 3, hasJoker: true, sevenPairs: true, kongUpFlower: false);
        Assert.Equal(new short[] { -4, -4, -4, 12 }, scores.RoundDelta.ToArray());
        scores.Release();

        // 两局各自四座净 0，累计总分等于两局 Release 净和。
        Assert.Equal(new short[] { 8, -8, -8, 8 }, scores.TotalScores.ToArray());
        Assert.Equal(0, scores.TotalScores.Sum(value => (int)value));
    }

    [Fact]
    public void RoundDelta_IsLengthFourAndWriteAttemptDoesNotChangeInternalState()
    {
        ScoreManager scores = new();
        scores.Apply(0, 3, ScoreActionType.None);

        Assert.Equal(4, scores.RoundDelta.Count);

        // 契约允许「返回拷贝」或「只读包装」；无论哪种，外部写入尝试都不能改到账本内部值。
        RoundTestSupport.TryWriteScore(scores.RoundDelta, index: 0, value: 99);

        Assert.Equal(new short[] { 3, 0, 0, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.TotalScores.ToArray());
    }

    [Fact]
    public void TotalScores_IsLengthFourAndWriteAttemptDoesNotChangeInternalState()
    {
        ScoreManager scores = new();
        scores.Apply(1, 4, ScoreActionType.None);
        scores.Release();

        Assert.Equal(4, scores.TotalScores.Count);

        RoundTestSupport.TryWriteScore(scores.TotalScores, index: 1, value: 99);

        Assert.Equal(new short[] { 0, 4, 0, 0 }, scores.TotalScores.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
    }
}
