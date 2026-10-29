using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// HandOrdering 契约：返回新的展示序列；普通牌按 Type.Id 升序；固定鬼牌规则的鬼牌排最后；
/// 稳定排序（同 Type.Id 保持输入顺序）；不改输入集合或 zone 顺序；null 入参抛 ArgumentNullException。
/// </summary>
public sealed class HandOrderingTests
{
    private static readonly JokerRule FixedJoker = new(JokerMode.FixedType, RulesTestData.TypeOf(31));

    [Fact]
    public void SortedForDisplay_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => HandOrdering.SortedForDisplay(null!));
        Assert.Throws<ArgumentNullException>(() => HandOrdering.SortedForDisplay(null!, FixedJoker));
    }

    [Fact]
    public void SortedForDisplay_EmptyInputReturnsEmptySequence()
    {
        Assert.Empty(HandOrdering.SortedForDisplay(Array.Empty<Tile>()));
        Assert.Empty(HandOrdering.SortedForDisplay(Array.Empty<Tile>(), FixedJoker));
    }

    [Fact]
    public void SortedForDisplay_SortsByTypeId()
    {
        Tile[] tiles =
        [
            RulesTestData.Physical(33, 0),
            RulesTestData.Physical(0, 0),
            RulesTestData.Physical(17, 0),
            RulesTestData.Physical(5, 0),
        ];

        Assert.Equal(new ushort[] { 1, 21, 69, 133 }, RulesTestData.Ids(HandOrdering.SortedForDisplay(tiles)));
    }

    [Fact]
    public void SortedForDisplay_IsStableForEqualTypeIds()
    {
        Tile[] tiles = [RulesTestData.Physical(5, 3), RulesTestData.Physical(5, 1), RulesTestData.Physical(5, 0)];

        Assert.Equal(new ushort[] { 24, 22, 21 }, RulesTestData.Ids(HandOrdering.SortedForDisplay(tiles)));
        Assert.Equal(new ushort[] { 24, 22, 21 }, RulesTestData.Ids(HandOrdering.SortedForDisplay(tiles, FixedJoker)));
    }

    [Fact]
    public void SortedForDisplay_PlacesJokersLastWhenRuleGiven()
    {
        Tile[] tiles =
        [
            RulesTestData.Physical(31, 0),
            RulesTestData.Physical(0, 0),
            RulesTestData.Physical(31, 1),
            RulesTestData.Physical(33, 0),
        ];

        Assert.Equal(new ushort[] { 1, 133, 125, 126 }, RulesTestData.Ids(HandOrdering.SortedForDisplay(tiles, FixedJoker)));
    }

    [Fact]
    public void SortedForDisplay_WithoutRuleSortsJokerTypeByTypeId()
    {
        Tile[] tiles = [RulesTestData.Physical(31, 0), RulesTestData.Physical(0, 0), RulesTestData.Physical(33, 0)];

        Assert.Equal(new ushort[] { 1, 125, 133 }, RulesTestData.Ids(HandOrdering.SortedForDisplay(tiles)));
    }

    [Fact]
    public void SortedForDisplay_WithNoneModeSortsJokerTypeByTypeId()
    {
        var none = new JokerRule(JokerMode.None, null);
        Tile[] tiles = [RulesTestData.Physical(31, 0), RulesTestData.Physical(0, 0), RulesTestData.Physical(33, 0)];

        Assert.Equal(new ushort[] { 1, 125, 133 }, RulesTestData.Ids(HandOrdering.SortedForDisplay(tiles, none)));
    }

    [Fact]
    public void SortedForDisplay_MultipleJokersKeepRelativeOrder()
    {
        Tile[] tiles =
        [
            RulesTestData.Physical(31, 3),
            RulesTestData.Physical(0, 0),
            RulesTestData.Physical(31, 0),
            RulesTestData.Physical(31, 2),
        ];

        Assert.Equal(new ushort[] { 1, 128, 125, 127 }, RulesTestData.Ids(HandOrdering.SortedForDisplay(tiles, FixedJoker)));
    }

    [Fact]
    public void SortedForDisplay_DoesNotModifyInputSequence()
    {
        var input = new List<Tile>
        {
            RulesTestData.Physical(33, 0),
            RulesTestData.Physical(0, 0),
            RulesTestData.Physical(31, 0),
        };
        ushort[] before = RulesTestData.Ids(input);

        IReadOnlyList<Tile> sorted = HandOrdering.SortedForDisplay(input, FixedJoker);

        Assert.Equal(before, RulesTestData.Ids(input));
        Assert.NotSame(input, sorted);
        Assert.Equal(new ushort[] { 1, 133, 125 }, RulesTestData.Ids(sorted));
    }

    [Fact]
    public void SortedForDisplay_DoesNotReorderTileZone()
    {
        var zone = new TileZone(new ZoneId(0, ZoneKind.Hand));
        zone.Append(RulesTestData.Physical(33, 0));
        zone.Append(RulesTestData.Physical(0, 0));
        zone.Append(RulesTestData.Physical(31, 0));
        ushort[] before = RulesTestData.Ids(zone.Items);

        _ = HandOrdering.SortedForDisplay(zone.Items, FixedJoker);

        Assert.Equal(before, RulesTestData.Ids(zone.Items));
    }

    [Fact]
    public void SortedForDisplay_ReturnsCompleteIndexableSequence()
    {
        Tile[] tiles = [RulesTestData.Physical(5, 0), RulesTestData.Physical(5, 1), RulesTestData.Physical(0, 0)];

        IReadOnlyList<Tile> sorted = HandOrdering.SortedForDisplay(tiles);

        Assert.Equal(3, sorted.Count);
        Assert.Equal(1, (int)sorted[0].Id);
        Assert.Equal(21, (int)sorted[1].Id);
        Assert.Equal(22, (int)sorted[2].Id);
    }
}
