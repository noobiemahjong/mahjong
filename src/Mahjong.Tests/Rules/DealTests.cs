using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// Deal 契约：按规格 §3.1 洗牌一次并发牌；固定 seed/bankerSeat 的全槽位由独立 Python 推导的金标断言；
/// 同 seed 可重复；重置局面（保留 Score/RoundNo）；bankerSeat 越界与 rng 为空拒绝且不改状态。
/// </summary>
public sealed class DealTests
{
    [Fact]
    public void Deal_ProducesSpecifiedCounts()
    {
        var state = new GameState();

        state.Deal(new Pcg32(42), 0);

        Assert.Equal(69, state.Wall.Count);
        Assert.Equal(14, state.WallTail.Count);
        Assert.Equal(14, state.Players[0].Hand.Count);
        Assert.Equal(13, state.Players[1].Hand.Count);
        Assert.Equal(13, state.Players[2].Hand.Count);
        Assert.Equal(13, state.Players[3].Hand.Count);
        Assert.Equal(136, RulesTestData.TotalTileCount(state));
    }

    [Fact]
    public void Deal_MatchesGoldenSlotsForSeed42Banker0()
    {
        var state = new GameState();

        state.Deal(new Pcg32(42), 0);

        AssertGolden(
            state,
            RulesGoldenVectors.DealSeed42Banker0Wall,
            RulesGoldenVectors.DealSeed42Banker0WallTail,
            [
                RulesGoldenVectors.DealSeed42Banker0Hand0,
                RulesGoldenVectors.DealSeed42Banker0Hand1,
                RulesGoldenVectors.DealSeed42Banker0Hand2,
                RulesGoldenVectors.DealSeed42Banker0Hand3,
            ]);
    }

    [Fact]
    public void Deal_MatchesGoldenSlotsForSeed20261029Banker2()
    {
        var state = new GameState();

        state.Deal(new Pcg32(20261029), 2);

        AssertGolden(
            state,
            RulesGoldenVectors.DealSeed20261029Banker2Wall,
            RulesGoldenVectors.DealSeed20261029Banker2WallTail,
            [
                RulesGoldenVectors.DealSeed20261029Banker2Hand0,
                RulesGoldenVectors.DealSeed20261029Banker2Hand1,
                RulesGoldenVectors.DealSeed20261029Banker2Hand2,
                RulesGoldenVectors.DealSeed20261029Banker2Hand3,
            ]);
    }

    [Fact]
    public void Deal_SameSeedProducesIdenticalSlots()
    {
        var first = new GameState();
        first.Deal(new Pcg32(42), 0);

        var second = new GameState();
        second.Deal(new Pcg32(42), 0);

        Assert.Equal(RulesTestData.Snapshot(first), RulesTestData.Snapshot(second));
        Assert.Equal(RulesTestData.Ids(first.Wall.Items), RulesTestData.Ids(second.Wall.Items));
        Assert.Equal(RulesTestData.Ids(first.WallTail.Items), RulesTestData.Ids(second.WallTail.Items));
    }

    [Fact]
    public void Deal_DifferentSeedChangesLayout()
    {
        var first = new GameState();
        first.Deal(new Pcg32(42), 0);

        var second = new GameState();
        second.Deal(new Pcg32(43), 0);

        Assert.False(RulesTestData.Ids(first.Wall.Items).SequenceEqual(RulesTestData.Ids(second.Wall.Items)));
        Assert.NotEqual(RulesTestData.Snapshot(first), RulesTestData.Snapshot(second));
    }

    [Fact]
    public void Deal_UsesEveryPhysicalTileExactlyOnce()
    {
        var state = new GameState();
        state.Deal(new Pcg32(42), 0);

        ushort[] ids = state.Wall.Items
            .Concat(state.WallTail.Items)
            .Concat(state.Players.SelectMany(player => player.Hand.Items))
            .Select(tile => tile.Id)
            .ToArray();

        Assert.Equal(RulesTestData.PhysicalTileCount, ids.Length);
        Assert.Equal(RulesTestData.PhysicalTileCount, ids.Distinct().Count());
        Assert.Equal(1, ids.Min());
        Assert.Equal(RulesTestData.PhysicalTileCount, ids.Max());
    }

    [Fact]
    public void Deal_SetsCurrentSeatAndOnlyBankerFlag()
    {
        var state = new GameState();

        state.Deal(new Pcg32(20261029), 2);

        Assert.Equal(2, (int)state.CurrentSeat);

        foreach (PlayerState player in state.Players)
        {
            if (player.Seat == 2)
            {
                Assert.True(player.IsBanker);
            }
            else
            {
                Assert.False(player.IsBanker);
            }
        }

        Assert.Equal(14, state.Players[2].Hand.Count);
    }

    [Fact]
    public void Deal_ResetsBoardButKeepsScoreAndRoundNo()
    {
        var state = new GameState();
        state.Players[0].Hand.Append(RulesTestData.Physical(0, 0));
        state.Players[0].Discards.Append(RulesTestData.Physical(1, 0));
        state.Players[0].Melds.Add(new Meld(MeldKind.Pong, RulesTestData.Copies(2).Take(3).ToArray(), true, 1));
        state.Players[1].IsBanker = true;
        state.Players[2].Score = -12;
        state.CurrentSeat = 3;
        state.RoundNo = 5;
        state.Wall.Append(RulesTestData.Physical(3, 0));
        state.WallTail.Append(RulesTestData.Physical(4, 0));

        state.Deal(new Pcg32(42), 2);

        var fresh = new GameState();
        fresh.Deal(new Pcg32(42), 2);

        Assert.Equal(RulesTestData.Snapshot(fresh), RulesTestData.Snapshot(state));
        Assert.Equal(5, (int)state.RoundNo);
        Assert.Equal(-12, state.Players[2].Score);
        Assert.Equal(2, (int)state.CurrentSeat);
    }

    [Fact]
    public void Deal_RejectsNullRandomSource()
    {
        var state = new GameState();
        state.Wall.Append(RulesTestData.Physical(0, 0));
        string before = RulesTestData.Snapshot(state);

        Assert.Throws<ArgumentNullException>(() => state.Deal(null!, 0));

        Assert.Equal(before, RulesTestData.Snapshot(state));
    }

    [Fact]
    public void Deal_RejectsBankerSeatAboveThree()
    {
        var state = new GameState();
        state.Wall.Append(RulesTestData.Physical(0, 0));
        string before = RulesTestData.Snapshot(state);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.Deal(new Pcg32(1), 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Deal(new Pcg32(1), 255));

        Assert.Equal(before, RulesTestData.Snapshot(state));
    }

    private static void AssertGolden(GameState state, ushort[] wall, ushort[] wallTail, ushort[][] hands)
    {
        Assert.Equal(wall, RulesTestData.Ids(state.Wall.Items));
        Assert.Equal(wallTail, RulesTestData.Ids(state.WallTail.Items));

        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            Assert.Equal(hands[seat], RulesTestData.Ids(state.Players[seat].Hand.Items));
        }
    }
}
