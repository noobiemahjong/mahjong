using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 弃牌契约：Discard 的 slot 是手牌槽位（Hand.Items 下标），移除后追加到本家弃牌区尾部；
/// 手牌槽与弃牌槽是两个域，不得混用；越界抛 InvalidOperationException；座位越界抛 ArgumentOutOfRangeException。
/// </summary>
public sealed class GameStateDiscardTests
{
    [Fact]
    public void Discard_MovesHandTileToDiscardsTail()
    {
        var state = new GameState();
        RulesTestData.Fill(state.Players[1], RulesTestData.Physical(0, 0), RulesTestData.Physical(0, 1),
            RulesTestData.Physical(0, 2));

        state.Discard(1, 1);

        Assert.Equal(new ushort[] { 1, 3 }, RulesTestData.Ids(state.Players[1].Hand.Items));
        Assert.Equal(new ushort[] { 2 }, RulesTestData.Ids(state.Players[1].Discards.Items));
        Assert.Equal(2, (int)state.Players[1].Discards[0].Id);
    }

    [Fact]
    public void Discard_AppendsMultipleDiscardsInOrder()
    {
        var state = new GameState();
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(0, 0), RulesTestData.Physical(0, 1),
            RulesTestData.Physical(0, 2));

        state.Discard(0, 2);
        state.Discard(0, 0);

        // 先弃槽 2（id 3），手牌剩 [1,2]；再弃槽 0（id 1）。
        Assert.Equal(new ushort[] { 3, 1 }, RulesTestData.Ids(state.Players[0].Discards.Items));
        Assert.Equal(new ushort[] { 2 }, RulesTestData.Ids(state.Players[0].Hand.Items));
    }

    [Fact]
    public void Discard_UsesHandSlotDomainOnly()
    {
        var state = new GameState();
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(5, 0), RulesTestData.Physical(5, 1));
        RulesTestData.Discard(state.Players[0], RulesTestData.Physical(0, 0), RulesTestData.Physical(1, 0),
            RulesTestData.Physical(2, 0), RulesTestData.Physical(3, 0), RulesTestData.Physical(4, 0));
        string before = RulesTestData.Snapshot(state);

        // 弃牌区有 5 个槽位，但 slot 只按手牌解释：手牌只有 2 张。
        Assert.Throws<InvalidOperationException>(() => state.Discard(0, 4));

        Assert.Equal(before, RulesTestData.Snapshot(state));
    }

    [Fact]
    public void Discard_OutOfRangeSlotsThrow()
    {
        var empty = new GameState();
        Assert.Throws<InvalidOperationException>(() => empty.Discard(0, 0));

        var state = new GameState();
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(0, 0), RulesTestData.Physical(0, 1));

        Assert.Throws<InvalidOperationException>(() => state.Discard(0, -1));
        Assert.Throws<InvalidOperationException>(() => state.Discard(0, 2));
        Assert.Throws<InvalidOperationException>(() => state.Discard(0, 99));
        Assert.Equal(2, state.Players[0].Hand.Count);
        Assert.Empty(state.Players[0].Discards.Items);
    }

    [Fact]
    public void Discard_RejectsSeatAboveThree()
    {
        var state = new GameState();
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(0, 0));
        string before = RulesTestData.Snapshot(state);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.Discard(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Discard(255, 0));

        Assert.Equal(before, RulesTestData.Snapshot(state));
    }

    [Fact]
    public void Discard_FromEachSeatTargetsOwnZones()
    {
        var state = new GameState();
        for (byte seat = 0; seat < 4; seat++)
        {
            RulesTestData.Fill(state.Players[seat], RulesTestData.Physical(seat, 0));
        }

        for (byte seat = 0; seat < 4; seat++)
        {
            state.Discard(seat, 0);
        }

        for (int seat = 0; seat < 4; seat++)
        {
            Assert.Empty(state.Players[seat].Hand.Items);
            Assert.Equal(new ushort[] { RulesTestData.PhysicalId(seat, 0) },
                RulesTestData.Ids(state.Players[seat].Discards.Items));
        }
    }

    [Fact]
    public void Discard_PreservesRemainingHandOrderAfterMiddleRemoval()
    {
        var state = new GameState();
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(0, 0), RulesTestData.Physical(0, 1),
            RulesTestData.Physical(0, 2), RulesTestData.Physical(0, 3));

        state.Discard(0, 1);

        Assert.Equal(new ushort[] { 1, 3, 4 }, RulesTestData.Ids(state.Players[0].Hand.Items));
    }
}
