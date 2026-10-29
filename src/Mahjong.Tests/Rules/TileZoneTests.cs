using System.Collections.ObjectModel;
using System.Reflection;
using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// TileZone 契约：只允许尾部追加；槽位越界与重复物理牌 id 抛 InvalidOperationException；
/// Remove/IndexOf 按 Id 匹配且不存在时不抛；Items 是缓存的实时只读视图，不改顺序。
/// </summary>
public sealed class TileZoneTests
{
    private static readonly ZoneId HandZone = new(0, ZoneKind.Hand);

    [Fact]
    public void NewZone_IsEmptyAndKeepsId()
    {
        var zone = new TileZone(HandZone);

        Assert.Equal(HandZone, zone.Id);
        Assert.Equal(0, zone.Count);
        Assert.Empty(zone.Items);
    }

    [Fact]
    public void Append_AppendsToTailAndPreservesOrder()
    {
        var zone = new TileZone(HandZone);

        zone.Append(RulesTestData.Physical(33, 0));
        zone.Append(RulesTestData.Physical(0, 0));
        zone.Append(RulesTestData.Physical(17, 3));

        Assert.Equal(3, zone.Count);
        Assert.Equal(new ushort[] { 133, 1, 72 }, RulesTestData.Ids(zone.Items));
        Assert.Equal(133, zone[0].Id);
        Assert.Equal(1, zone[1].Id);
        Assert.Equal(72, zone[2].Id);
    }

    [Fact]
    public void Append_AllowsAllPhysicalTilesOnce()
    {
        var zone = new TileZone(HandZone);

        for (int physicalId = 1; physicalId <= RulesTestData.PhysicalTileCount; physicalId++)
        {
            zone.Append(RulesTestData.FromId(physicalId));
        }

        Assert.Equal(RulesTestData.PhysicalTileCount, zone.Count);
        Assert.Equal(Enumerable.Range(1, RulesTestData.PhysicalTileCount).Select(id => (ushort)id), RulesTestData.Ids(zone.Items));
    }

    [Fact]
    public void Append_DuplicateIdThrowsAndKeepsZoneUnchanged()
    {
        var zone = new TileZone(HandZone);
        Tile first = RulesTestData.Physical(5, 0);
        zone.Append(first);

        var duplicate = new Tile(first.Id, RulesTestData.TypeOf(6));

        Assert.Throws<InvalidOperationException>(() => zone.Append(duplicate));
        Assert.Equal(1, zone.Count);
        Assert.Equal(new ushort[] { first.Id }, RulesTestData.Ids(zone.Items));
    }

    [Fact]
    public void Indexer_OutOfRangeThrows()
    {
        var empty = new TileZone(HandZone);
        Assert.Throws<InvalidOperationException>(() => { _ = empty[0]; });
        Assert.Throws<InvalidOperationException>(() => { _ = empty[-1]; });

        var zone = new TileZone(HandZone);
        zone.Append(RulesTestData.Physical(0, 0));
        zone.Append(RulesTestData.Physical(0, 1));

        Assert.Throws<InvalidOperationException>(() => { _ = zone[2]; });
        Assert.Throws<InvalidOperationException>(() => { _ = zone[100]; });
        Assert.Throws<InvalidOperationException>(() => { _ = zone[-1]; });
    }

    [Fact]
    public void RemoveAt_ReturnsTileAndShiftsRemainingSlots()
    {
        var zone = new TileZone(HandZone);
        Tile first = RulesTestData.Physical(0, 0);
        Tile second = RulesTestData.Physical(1, 0);
        Tile third = RulesTestData.Physical(2, 0);
        zone.Append(first);
        zone.Append(second);
        zone.Append(third);

        Tile removed = zone.RemoveAt(1);

        Assert.Equal(second.Id, removed.Id);
        Assert.Equal(2, zone.Count);
        Assert.Equal(new ushort[] { first.Id, third.Id }, RulesTestData.Ids(zone.Items));
    }

    [Fact]
    public void RemoveAt_OutOfRangeThrows()
    {
        var empty = new TileZone(HandZone);
        Assert.Throws<InvalidOperationException>(() => empty.RemoveAt(0));

        var zone = new TileZone(HandZone);
        zone.Append(RulesTestData.Physical(0, 0));

        Assert.Throws<InvalidOperationException>(() => zone.RemoveAt(-1));
        Assert.Throws<InvalidOperationException>(() => zone.RemoveAt(1));
        Assert.Throws<InvalidOperationException>(() => zone.RemoveAt(50));
        Assert.Equal(1, zone.Count);
    }

    [Fact]
    public void Remove_MatchesById()
    {
        var zone = new TileZone(HandZone);
        Tile tile = RulesTestData.Physical(5, 0);
        zone.Append(tile);

        var sameIdDifferentType = new Tile(tile.Id, RulesTestData.TypeOf(31));

        Assert.True(zone.Remove(sameIdDifferentType));
        Assert.Equal(0, zone.Count);
        Assert.False(zone.Remove(sameIdDifferentType));
        Assert.False(zone.Remove(tile));
    }

    [Fact]
    public void Remove_UnknownIdReturnsFalseAndKeepsZone()
    {
        var zone = new TileZone(HandZone);
        zone.Append(RulesTestData.Physical(0, 0));
        zone.Append(RulesTestData.Physical(1, 0));

        Assert.False(zone.Remove(RulesTestData.Physical(9, 0)));
        Assert.Equal(2, zone.Count);
    }

    [Fact]
    public void IndexOf_MatchesByIdAndReturnsMinusOneWhenAbsent()
    {
        var zone = new TileZone(HandZone);
        zone.Append(RulesTestData.Physical(0, 0));
        zone.Append(RulesTestData.Physical(5, 0));
        zone.Append(RulesTestData.Physical(33, 3));

        Assert.Equal(1, zone.IndexOf(new Tile(RulesTestData.PhysicalId(5, 0), RulesTestData.TypeOf(17))));
        Assert.Equal(2, zone.IndexOf(RulesTestData.Physical(33, 3)));
        Assert.Equal(-1, zone.IndexOf(RulesTestData.Physical(9, 0)));
        Assert.Equal(-1, new TileZone(HandZone).IndexOf(RulesTestData.Physical(0, 0)));
    }

    [Fact]
    public void Items_IsCachedLiveReadOnlyView()
    {
        var zone = new TileZone(HandZone);
        zone.Append(RulesTestData.Physical(0, 0));

        IReadOnlyList<Tile> view = zone.Items;
        Assert.Same(view, zone.Items);
        Assert.IsType<ReadOnlyCollection<Tile>>(zone.Items);

        zone.Append(RulesTestData.Physical(1, 0));
        Assert.Equal(2, view.Count);

        Assert.Throws<InvalidCastException>(() => { _ = (List<Tile>)zone.Items; });

        IList<Tile> mutableView = (IList<Tile>)zone.Items;
        Assert.Throws<NotSupportedException>(() => mutableView.Add(RulesTestData.Physical(2, 0)));
        Assert.Throws<NotSupportedException>(() => mutableView[0] = RulesTestData.Physical(2, 0));
        Assert.Equal(2, zone.Count);
    }

    [Fact]
    public void Clear_EmptiesZoneAndAllowsFurtherAppend()
    {
        var zone = new TileZone(HandZone);
        zone.Append(RulesTestData.Physical(0, 0));
        zone.Append(RulesTestData.Physical(1, 0));

        zone.Clear();

        Assert.Equal(0, zone.Count);
        Assert.Empty(zone.Items);

        zone.Append(RulesTestData.Physical(2, 0));
        Assert.Equal(new ushort[] { RulesTestData.PhysicalId(2, 0) }, RulesTestData.Ids(zone.Items));
    }

    [Fact]
    public void Capacity_IsOnlyAnInitialCapacityHint()
    {
        var zone = new TileZone(HandZone, 2);

        zone.Append(RulesTestData.Physical(0, 0));
        zone.Append(RulesTestData.Physical(0, 1));
        zone.Append(RulesTestData.Physical(0, 2));

        Assert.Equal(3, zone.Count);
    }

    [Fact]
    public void Capacity_AcceptsZeroAndPositive()
    {
        Assert.Equal(0, new TileZone(HandZone, 0).Count);
        Assert.Equal(0, new TileZone(HandZone, 122).Count);
        Assert.Equal(0, new TileZone(HandZone, 136).Count);
    }

    [Fact]
    public void Capacity_NegativeThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileZone(HandZone, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileZone(HandZone, int.MinValue));
    }

    [Fact]
    public void Zones_WithEqualIdAreIndependent()
    {
        var first = new TileZone(HandZone);
        var second = new TileZone(HandZone);

        first.Append(RulesTestData.Physical(0, 0));

        Assert.Equal(1, first.Count);
        Assert.Equal(0, second.Count);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void ZoneId_PublicSeatIs255()
    {
        Assert.Equal(255, (int)ZoneId.PublicSeat);
        Assert.Equal(new ZoneId(ZoneId.PublicSeat, ZoneKind.Wall), new ZoneId(255, ZoneKind.Wall));
        Assert.NotEqual(new ZoneId(0, ZoneKind.Wall), new ZoneId(0, ZoneKind.WallTail));
        Assert.NotEqual(new ZoneId(1, ZoneKind.Hand), new ZoneId(2, ZoneKind.Hand));
    }

    [Fact]
    public void ZoneKind_DeclaresSixZonesInSpecifiedOrder()
    {
        Assert.Same(typeof(byte), Enum.GetUnderlyingType(typeof(ZoneKind)));
        Assert.Equal(
            new[] { "Wall", "WallTail", "Hand", "Meld", "Discard", "Revealed" },
            Enum.GetNames<ZoneKind>());
    }

    [Fact]
    public void TileZone_ExposesNoInsertOrSortApi()
    {
        string[] forbidden = ["Insert", "InsertRange", "Sort", "Reverse", "BinarySearch", "Move"];

        var declared = typeof(TileZone)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string name in forbidden)
        {
            Assert.DoesNotContain(name, declared);
        }
    }
}
