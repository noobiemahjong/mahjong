using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// GameState 契约：四家 + 公共牌墙/牌尾；默认值；CurrentSeat/RoundNo 取值校验；IsWallEmpty 只由 Wall 决定。
/// </summary>
public sealed class GameStateTests
{
    [Fact]
    public void Constructor_InitializesFourSeatsAndEmptyWalls()
    {
        var state = new GameState();

        Assert.Equal(4, (int)GameState.PlayerCount);
        Assert.Equal(4, state.Players.Length);
        Assert.Equal(0, state.CurrentSeat);
        Assert.Equal(1, state.RoundNo);
        Assert.True(state.IsWallEmpty);

        Assert.Equal(new ZoneId(ZoneId.PublicSeat, ZoneKind.Wall), state.Wall.Id);
        Assert.Equal(new ZoneId(ZoneId.PublicSeat, ZoneKind.WallTail), state.WallTail.Id);
        Assert.Equal(0, state.Wall.Count);
        Assert.Equal(0, state.WallTail.Count);

        for (int seat = 0; seat < state.Players.Length; seat++)
        {
            PlayerState player = state.Players[seat];
            Assert.Equal(seat, (int)player.Seat);
            Assert.Equal(new ZoneId((byte)seat, ZoneKind.Hand), player.Hand.Id);
            Assert.Equal(new ZoneId((byte)seat, ZoneKind.Discard), player.Discards.Id);
            Assert.False(player.IsBanker);
        }

        Assert.NotSame(state.Players[0], state.Players[1]);
    }

    [Fact]
    public void CurrentSeat_AcceptsZeroToThree()
    {
        var state = new GameState();

        foreach (byte seat in new byte[] { 0, 1, 2, 3 })
        {
            state.CurrentSeat = seat;
            Assert.Equal(seat, state.CurrentSeat);
        }
    }

    [Fact]
    public void CurrentSeat_RejectsAboveThree()
    {
        var state = new GameState();

        Assert.Throws<ArgumentOutOfRangeException>(() => state.CurrentSeat = 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.CurrentSeat = 255);
        Assert.Equal(0, state.CurrentSeat);
    }

    [Fact]
    public void RoundNo_AcceptsOneToEight()
    {
        var state = new GameState();

        foreach (byte round in new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            state.RoundNo = round;
            Assert.Equal(round, state.RoundNo);
        }
    }

    [Fact]
    public void RoundNo_RejectsOutsideOneToEight()
    {
        var state = new GameState();

        Assert.Throws<ArgumentOutOfRangeException>(() => state.RoundNo = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.RoundNo = 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.RoundNo = 255);
        Assert.Equal(1, state.RoundNo);
    }

    [Fact]
    public void IsWallEmpty_TracksWallOnly()
    {
        var state = new GameState();

        state.WallTail.Append(RulesTestData.Physical(0, 0));
        Assert.True(state.IsWallEmpty);

        state.Wall.Append(RulesTestData.Physical(1, 0));
        Assert.False(state.IsWallEmpty);

        state.Wall.Clear();
        Assert.True(state.IsWallEmpty);
        Assert.Equal(1, state.WallTail.Count);
    }
}
