using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 摸牌契约：DrawFromWall 取牌墙头槽、DrawFromTail 取同一牌墙的末槽，追加到指定座位手牌尾部；
/// 牌墙为空时两者都抛 InvalidOperationException（流程层先用 IsWallEmpty 判流局，不靠异常驱动流程）；
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
    public void DrawFromWall_LeavesOtherSeatsUntouched()
    {
        var state = new GameState();
        state.Wall.Append(RulesTestData.Physical(0, 0));
        state.Wall.Append(RulesTestData.Physical(1, 0));
        state.Players[3].Hand.Append(RulesTestData.Physical(2, 0));

        state.DrawFromWall(0);

        Assert.Equal(new ushort[] { 5 }, RulesTestData.Ids(state.Wall.Items));
        Assert.Empty(state.Players[1].Hand.Items);
        Assert.Empty(state.Players[2].Hand.Items);
        Assert.Equal(new ushort[] { 9 }, RulesTestData.Ids(state.Players[3].Hand.Items));
    }

    [Fact]
    public void DrawFromWall_EmptyWallThrowsAndKeepsState()
    {
        var state = new GameState();
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(0, 0));
        state.Wall.Append(RulesTestData.Physical(1, 0));
        _ = state.DrawFromWall(0);
        string before = RulesTestData.Snapshot(state);

        Assert.Throws<InvalidOperationException>(() => state.DrawFromWall(0));

        Assert.Equal(before, RulesTestData.Snapshot(state));
        Assert.True(state.IsWallEmpty);
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
        state.Wall.Append(RulesTestData.Physical(0, 0));
        state.Wall.Append(RulesTestData.Physical(0, 1));
        state.Wall.Append(RulesTestData.Physical(0, 2));

        Tile drawn = state.DrawFromTail(1);

        Assert.Equal(3, (int)drawn.Id);
        Assert.Equal(new ushort[] { 1, 2 }, RulesTestData.Ids(state.Wall.Items));
        Assert.Equal(new ushort[] { 3 }, RulesTestData.Ids(state.Players[1].Hand.Items));
    }

    [Fact]
    public void DrawFromTail_EmptyWallThrowsAndKeepsState()
    {
        var state = new GameState();
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(0, 0));
        string before = RulesTestData.Snapshot(state);

        Assert.Throws<InvalidOperationException>(() => state.DrawFromTail(0));

        Assert.Equal(before, RulesTestData.Snapshot(state));
    }

    [Fact]
    public void DrawFromTail_RejectsSeatAboveThree()
    {
        var state = new GameState();
        state.Wall.Append(RulesTestData.Physical(0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => state.DrawFromTail(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.DrawFromTail(255));
        Assert.Equal(1, state.Wall.Count);
    }

    [Fact]
    public void DrawFromWall_DrainsDealtWallToEmpty()
    {
        var state = new GameState();
        state.Deal(new Pcg32(42), 0);

        for (int count = 0; count < 84; count++)
        {
            _ = state.DrawFromWall(1);
        }

        Assert.True(state.IsWallEmpty);
        Assert.Equal(97, state.Players[1].Hand.Count);
        Assert.Throws<InvalidOperationException>(() => state.DrawFromWall(1));
        Assert.Throws<InvalidOperationException>(() => state.DrawFromTail(1));
    }

    [Fact]
    public void DrawFromTail_TakesDealtWallLastSlotAndLeavesHeadOrder()
    {
        var state = new GameState();
        state.Deal(new Pcg32(20261029), 2);
        ushort[] wallBefore = RulesTestData.Ids(state.Wall.Items);
        ushort expected = wallBefore[^1];

        Tile drawn = state.DrawFromTail(3);

        Assert.Equal(expected, drawn.Id);
        Assert.Equal(83, state.Wall.Count);
        Assert.Equal(wallBefore[..83], RulesTestData.Ids(state.Wall.Items));
        Assert.Equal(14, state.Players[3].Hand.Count);
        Assert.Equal(expected, state.Players[3].Hand.Items[^1].Id);
    }

    [Fact]
    public void DrawFromWall_BankerFirstTurnProducesFourteenAnd83()
    {
        var state = new GameState();
        state.Deal(new Pcg32(42), 0);
        ushort[] dealtWall = RulesGoldenVectors.DealSeed42Banker0Wall;
        ushort expected = dealtWall[0];

        Tile drawn = state.DrawFromWall(state.CurrentSeat);

        Assert.Equal(expected, drawn.Id);

        // 首回合后的金标：庄家 14 张 = 发牌手牌 + 牌墙头槽；牌墙 83 槽 = 发牌牌墙去掉头槽。
        ushort[] expectedHand = [.. RulesGoldenVectors.DealSeed42Banker0Hand0, expected];
        Assert.Equal(expectedHand, RulesTestData.Ids(state.Players[0].Hand.Items));
        Assert.Equal(dealtWall[1..], RulesTestData.Ids(state.Wall.Items));
        Assert.Equal(83, state.Wall.Count);
        Assert.Equal(0, (int)state.CurrentSeat);
        for (int seat = 1; seat < GameState.PlayerCount; seat++)
        {
            Assert.Equal(13, state.Players[seat].Hand.Count);
        }
    }

    [Fact]
    public void IsWallEmpty_IsFalseForEveryCountFrom84DownToZero()
    {
        var state = new GameState();
        state.Deal(new Pcg32(42), 0);
        Assert.False(state.IsWallEmpty);

        for (int remaining = 84; remaining > 0; remaining--)
        {
            Assert.Equal(remaining, state.Wall.Count);
            Assert.False(state.IsWallEmpty);
            _ = state.DrawFromWall(0);
        }

        Assert.Equal(0, state.Wall.Count);
        Assert.True(state.IsWallEmpty);
        Assert.Equal(97, state.Players[0].Hand.Count);
    }
}
