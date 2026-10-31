using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 终局排名纯函数契约：总分降序、同分座位号升序、返回长度 4 的只读列表；
/// null 抛 <see cref="ArgumentNullException"/>，长度不是 4 抛 <see cref="ArgumentException"/>，且不修改入参。
/// <see cref="RoundController.Rank"/> 与该实现共用同一排序口径。
/// </summary>
public sealed class RoundRankingTests
{
    [Fact]
    public void Rank_OrdersByTotalDescending()
    {
        IReadOnlyList<byte> rank = RoundRanking.Rank([-8, 12, 0, 4]);

        Assert.Equal(4, rank.Count);
        Assert.Equal(new byte[] { 1, 3, 2, 0 }, rank);
    }

    [Fact]
    public void Rank_TiesBreakBySeatAscending()
    {
        // 总分 [5, -5, 0, 0]：0 最高，座位 2、3 同分按座位升序，1 最低。
        IReadOnlyList<byte> rank = RoundRanking.Rank([5, -5, 0, 0]);

        Assert.Equal(new byte[] { 0, 2, 3, 1 }, rank);
    }

    [Fact]
    public void Rank_AllTied_OrdersBySeatAscending()
    {
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, RoundRanking.Rank([0, 0, 0, 0]));
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, RoundRanking.Rank([-3, -3, -3, -3]));
    }

    [Fact]
    public void Rank_HandlesShortExtremes()
    {
        IReadOnlyList<byte> rank = RoundRanking.Rank([short.MinValue, short.MaxValue, 0, -1]);

        Assert.Equal(new byte[] { 1, 2, 3, 0 }, rank);
    }

    [Fact]
    public void Rank_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => RoundRanking.Rank(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Rank_RejectsLengthOtherThanFour(int length)
    {
        short[] scores = new short[length];
        Assert.Throws<ArgumentException>(() => RoundRanking.Rank(scores));
    }

    [Fact]
    public void Rank_ReturnsReadOnlyList()
    {
        IReadOnlyList<byte> rank = RoundRanking.Rank([1, 2, 3, 4]);

        Assert.IsAssignableFrom<System.Collections.Generic.IList<byte>>(rank);
        var writable = (System.Collections.Generic.IList<byte>)rank;
        Assert.Throws<NotSupportedException>(() => writable[0] = 9);
        Assert.Equal(new byte[] { 3, 2, 1, 0 }, rank);
    }

    [Fact]
    public void Rank_DoesNotModifyInputAndRepeatCallsAreStable()
    {
        short[] scores = [5, -5, 0, 0];

        IReadOnlyList<byte> first = RoundRanking.Rank(scores);
        IReadOnlyList<byte> second = RoundRanking.Rank(scores);

        Assert.Equal(new short[] { 5, -5, 0, 0 }, scores);
        Assert.Equal(first, second);
    }

    [Fact]
    public void RoundController_Rank_UsesSameOrdering()
    {
        var controller = new RoundController(new StubWinChecker(), new Pcg32(1));
        controller.Scores.Apply(0, -7, ScoreActionType.None);
        controller.Scores.Apply(1, 9, ScoreActionType.None);
        controller.Scores.Apply(2, 9, ScoreActionType.None);
        controller.Scores.Apply(3, 0, ScoreActionType.None);
        controller.Scores.Release();

        IReadOnlyList<byte> expected = RoundRanking.Rank(controller.Scores.TotalScores);

        Assert.Equal(expected, controller.Rank());
        Assert.Equal(new byte[] { 1, 2, 3, 0 }, controller.Rank());
    }
}
