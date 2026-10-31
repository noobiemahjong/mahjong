using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 七对自摸 additive 结算口径：金额 (hasJoker ? 2 : 4) × 2、赢家收三份、四座和为 0；
/// 置位 SelfDrawn|SevenPairs|（无鬼 NoJoker）；不检查本局其他家的杠位（其他家先杠不阻断七对）；
/// 座位越界抛 <see cref="ArgumentOutOfRangeException"/> 且不改状态。
/// </summary>
public sealed class ScoreManagerSevenPairsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplySevenPairsSelfDrawn_ScoresWinnerAndThreePayers(bool hasJoker)
    {
        var scores = new ScoreManager();

        scores.ApplySevenPairsSelfDrawn(winnerSeat: 2, hasJoker: hasJoker);

        short per = (short)((hasJoker ? 2 : 4) * 2);
        short winner = (short)(3 * per);
        Assert.Equal(new short[] { (short)-per, (short)-per, winner, (short)-per }, scores.RoundDelta.ToArray());
        Assert.Equal(0, scores.RoundDelta.Sum(value => value));

        ScoreActionType expected = ScoreActionType.SelfDrawn | ScoreActionType.SevenPairs;
        if (!hasJoker)
        {
            expected |= ScoreActionType.NoJoker;
        }

        Assert.Equal(expected, scores.RoundActionType);
    }

    [Fact]
    public void ApplySevenPairsSelfDrawn_AfterAnotherSeatsKong_DoesNotThrowAndKeepsKongScores()
    {
        var scores = new ScoreManager();

        // B（座位 1）先暗杠：+6，其余各 -2；全局聚合带 ConcealedKong。
        scores.ApplyKong(seat: 1, MeldKind.KongConcealed);

        // A（座位 0）随后七对自摸：不因其他家的杠位抛异常，也不把杠上开花计入。
        scores.ApplySevenPairsSelfDrawn(winnerSeat: 0, hasJoker: false);

        Assert.Equal(new short[] { 22, -2, -10, -10 }, scores.RoundDelta.ToArray());
        Assert.Equal(
            ScoreActionType.SelfDrawn
            | ScoreActionType.SevenPairs
            | ScoreActionType.NoJoker
            | ScoreActionType.ConcealedKong,
            scores.RoundActionType);

        scores.Release();
        Assert.Equal(new short[] { 22, -2, -10, -10 }, scores.TotalScores.ToArray());
    }

    [Fact]
    public void ApplySevenPairsSelfDrawn_StacksWithExistingDelta()
    {
        var scores = new ScoreManager();
        scores.Apply(seat: 3, delta: 2, type: ScoreActionType.None);

        scores.ApplySevenPairsSelfDrawn(winnerSeat: 3, hasJoker: true);

        Assert.Equal(new short[] { -4, -4, -4, 14 }, scores.RoundDelta.ToArray());
        Assert.Equal(
            ScoreActionType.SelfDrawn | ScoreActionType.SevenPairs,
            scores.RoundActionType);
    }

    [Fact]
    public void ApplySevenPairsSelfDrawn_InvalidSeat_ThrowsAndLeavesStateUnchanged()
    {
        var scores = new ScoreManager();

        Assert.Throws<ArgumentOutOfRangeException>(() => scores.ApplySevenPairsSelfDrawn(winnerSeat: 4, hasJoker: false));

        Assert.Equal(new short[] { 0, 0, 0, 0 }, scores.RoundDelta.ToArray());
        Assert.Equal(ScoreActionType.None, scores.RoundActionType);
    }

    [Fact]
    public void ApplySelfDrawn_SevenPairsAfterAnotherSeatsKong_StillThrows()
    {
        // 既有方法口径不变：本局任一杠位与七对组合仍拒绝；T-5 七对分支改用 ApplySevenPairsSelfDrawn。
        var scores = new ScoreManager();
        scores.ApplyKong(seat: 1, MeldKind.KongConcealed);

        Assert.Throws<InvalidOperationException>(
            () => scores.ApplySelfDrawn(winnerSeat: 0, hasJoker: false, sevenPairs: true, kongUpFlower: false));
    }
}
