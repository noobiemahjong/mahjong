using System.Reflection;
using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// PlayerState 契约：座位 0–3；手牌/弃牌 ZoneId 按座位与区域区分；Melds 为空 List；分与庄家默认值；
/// 不提供排序 API。
/// </summary>
public sealed class PlayerStateTests
{
    [Fact]
    public void Constructor_InitializesZonesMeldsAndDefaults()
    {
        foreach (byte seat in new byte[] { 0, 1, 2, 3 })
        {
            var player = new PlayerState(seat);

            Assert.Equal(seat, player.Seat);
            Assert.Equal(new ZoneId(seat, ZoneKind.Hand), player.Hand.Id);
            Assert.Equal(new ZoneId(seat, ZoneKind.Discard), player.Discards.Id);
            Assert.Equal(0, player.Hand.Count);
            Assert.Equal(0, player.Discards.Count);
            Assert.Empty(player.Melds);
            Assert.IsType<List<Meld>>(player.Melds);
            Assert.Equal(0, player.Score);
            Assert.False(player.IsBanker);
        }
    }

    [Fact]
    public void Constructor_RejectsSeatAboveThree()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerState(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerState(255));
    }

    [Fact]
    public void Score_SupportsFullShortRange()
    {
        var player = new PlayerState(1);

        player.Score = short.MinValue;
        Assert.Equal(short.MinValue, player.Score);

        player.Score = short.MaxValue;
        Assert.Equal(short.MaxValue, player.Score);
    }

    [Fact]
    public void IsBanker_IsMutable()
    {
        var player = new PlayerState(2);

        player.IsBanker = true;

        Assert.True(player.IsBanker);
    }

    [Fact]
    public void Melds_IsAMutableList()
    {
        var player = new PlayerState(0);
        var meld = new Meld(MeldKind.Pong, RulesTestData.Copies(5).Take(3).ToArray(), true, 1);

        player.Melds.Add(meld);

        Assert.Single(player.Melds);
        Assert.Same(meld, player.Melds[0]);
    }

    [Fact]
    public void PlayerState_ExposesNoSortingApi()
    {
        var declared = typeof(PlayerState)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain(declared, name => name.Contains("Sort", StringComparison.Ordinal));
        Assert.DoesNotContain(declared, name => name.Contains("Order", StringComparison.Ordinal));
    }
}
