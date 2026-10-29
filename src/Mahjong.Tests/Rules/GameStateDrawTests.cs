using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 摸牌契约：DrawFromWall 取牌墙头槽、DrawFromTail 取牌尾末槽，追加到指定座位手牌尾部；
/// 空牌墙/空牌尾抛 InvalidOperationException（流程层先用 IsWallEmpty 判流局，不靠异常驱动流程）；
/// 座位越界抛 ArgumentOutOfRangeException；失败调用不改状态。
/// </summary>
public sealed class GameStateDrawTests
{
    [Fact]
    public void DrawFromWall_TakesHeadSlotAndAppendsToHand()
    {
        var state = new GameState();
        state.Wall.Append(RulesTestData.Physical(0, 0));
        state.Wall.Append(RulesTestData.Physical(0, 1));
        RulesTestData.Fill(state.Players[2], RulesTestData.Physical(1, 0));

        Tile drawn = state.DrawFromWall(2);

        Assert.Equal(1, (int)drawn.Id);
        Assert.Equal(new ushort[] { 2 }, RulesTestData.Ids(state.Wall.Items));
        Assert.Equal(new ushort[] { 5, 1 }, RulesTestData.Ids(state.Players[2].Hand.Items));
    }

    [Fact]
    public void DrawFromWall_LeavesWallTailAndOtherSeatsUntouched()
    {
        var state = new GameState();
        state.Wall.Append(RulesTestData.Physical(0, 0));
        state.WallTail.Append(RulesTestData.Physical(1, 0));
        state.Players[3].Hand.Append(RulesTestData.Physical(2, 0));

        state.DrawFromWall(0);

        Assert.Equal(new ushort[] { 5 }, RulesTestData.Ids(state.WallTail.Items));
        Assert.Empty(state.Players[1].Hand.Items);
        Assert.Empty(state.Players[2].Hand.Items);
        Assert.Equal(new ushort[] { 9 }, RulesTestData.Ids(state.Players[3].Hand.Items));
    }

    [Fact]
    public void DrawFromWall_EmptyWallThrowsAndKeepsState()
    {
        var state = new GameState();
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(0, 0));
        state.WallTail.Append(RulesTestData.Physical(1, 0));
        string before = RulesTestData.Snapshot(state);

        Assert.Throws<InvalidOperationException>(() => state.DrawFromWall(0));

        Assert.Equal(before, RulesTestData.Snapshot(state));
    }

    [Fact]
    public void DrawFromWall_RejectsSeatAboveThree()
    {
        var state = new GameState();
        state.Wall.Append(RulesTestData.Physical(0, 0));
        string before = RulesTestData.Snapshot(state);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.DrawFromWall(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.DrawFromWall(255));

        Assert.Equal(before, RulesTestData.Snapshot(state));
    }

    [Fact]
    public void DrawFromTail_TakesLastSlotAndAppendsToHand()
    {
        var state = new GameState();
        state.WallTail.Append(RulesTestData.Physical(0, 0));
        state.WallTail.Append(RulesTestData.Physical(0, 1));
        state.WallTail.Append(RulesTestData.Physical(0, 2));

        Tile drawn = state.DrawFromTail(1);

        Assert.Equal(3, (int)drawn.Id);
        Assert.Equal(new ushort[] { 1, 2 }, RulesTestData.Ids(state.WallTail.Items));
        Assert.Equal(new ushort[] { 3 }, RulesTestData.Ids(state.Players[1].Hand.Items));
    }

    [Fact]
    public void DrawFromTail_EmptyTailThrowsEvenWhenWallHasTiles()
    {
        var state = new GameState();
        state.Wall.Append(RulesTestData.Physical(0, 0));
        string before = RulesTestData.Snapshot(state);

        Assert.Throws<InvalidOperationException>(() => state.DrawFromTail(0));

        Assert.Equal(before, RulesTestData.Snapshot(state));
        Assert.Equal(1, state.Wall.Count);
    }

    [Fact]
    public void DrawFromTail_RejectsSeatAboveThree()
    {
        var state = new GameState();
        state.WallTail.Append(RulesTestData.Physical(0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => state.DrawFromTail(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.DrawFromTail(255));
        Assert.Equal(1, state.WallTail.Count);
    }

    [Fact]
    public void DrawFromWall_DrainsDealtWallToEmpty()
    {
        var state = new GameState();
        state.Deal(new Pcg32(42), 0);

        for (int count = 0; count < 69; count++)
        {
            state.DrawFromWall(1);
        }

        Assert.True(state.IsWallEmpty);
        Assert.Equal(82, state.Players[1].Hand.Count);
        Assert.Equal(14, state.WallTail.Count);
        Assert.Throws<InvalidOperationException>(() => state.DrawFromWall(1));
    }

    [Fact]
    public void DrawFromTail_DoesNotTouchWall()
    {
        var state = new GameState();
        state.Deal(new Pcg32(7), 1);
        string wallBefore = string.Join(",", RulesTestData.Ids(state.Wall.Items));

        state.DrawFromTail(0);

        Assert.Equal(wallBefore, string.Join(",", RulesTestData.Ids(state.Wall.Items)));
        Assert.Equal(13, state.WallTail.Count);
        Assert.Equal(14, state.Players[0].Hand.Count);
    }
}
