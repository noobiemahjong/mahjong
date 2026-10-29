using System.Collections.ObjectModel;
using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// Meld 构造契约：未定义种类/空牌列表/张数不符/牌型不一致/物理牌重复/目标座位越界均拒绝；
/// Tiles 是 Meld 自有存储的缓存只读视图（构造时防御性拷贝，不与调用方 List 共享）；
/// 明暗标志不与种类交叉校验（由 DeclareMeld 决定）。
/// </summary>
public sealed class MeldTests
{
    [Fact]
    public void Pong_ExposesKindTilesOpenedAndTargetSeat()
    {
        Tile[] tiles = RulesTestData.Copies(5).Take(3).ToArray();

        var meld = new Meld(MeldKind.Pong, tiles, opened: true, targetSeat: 2);

        Assert.Equal(MeldKind.Pong, meld.Kind);
        Assert.Equal(RulesTestData.Ids(tiles), RulesTestData.Ids(meld.Tiles));
        Assert.True(meld.Opened);
        Assert.Equal(2, (int)meld.TargetSeat);
    }

    [Fact]
    public void KongKinds_AcceptFourDistinctTiles()
    {
        foreach (MeldKind kind in new[] { MeldKind.KongExposed, MeldKind.KongConcealed, MeldKind.KongSupplement })
        {
            var meld = new Meld(kind, RulesTestData.Copies(7), opened: true, targetSeat: Meld.NoTargetSeat);

            Assert.Equal(kind, meld.Kind);
            Assert.Equal(4, meld.Tiles.Count);
            Assert.Equal(255, (int)meld.TargetSeat);
        }
    }

    [Fact]
    public void NoTargetSeatIs255() => Assert.Equal(255, (int)Meld.NoTargetSeat);

    [Fact]
    public void MeldKind_NumericValuesAreStable()
    {
        Assert.Equal(1, (int)MeldKind.Pong);
        Assert.Equal(2, (int)MeldKind.KongExposed);
        Assert.Equal(3, (int)MeldKind.KongConcealed);
        Assert.Equal(4, (int)MeldKind.KongSupplement);
    }

    [Fact]
    public void Constructor_RejectsUndefinedKind()
    {
        foreach (MeldKind kind in new[] { (MeldKind)0, (MeldKind)5, (MeldKind)255 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Meld(kind, RulesTestData.Copies(5).Take(3).ToArray(), true, 0));
        }
    }

    [Fact]
    public void Constructor_RejectsNullTileList()
    {
        Assert.Throws<ArgumentNullException>(() => new Meld(MeldKind.Pong, null!, true, 0));
    }

    [Fact]
    public void Constructor_RejectsWrongTileCounts()
    {
        foreach (MeldKind kind in new[] { MeldKind.Pong, MeldKind.KongExposed, MeldKind.KongConcealed, MeldKind.KongSupplement })
        {
            int expected = ExpectedTileCount(kind);
            foreach (int count in new[] { 0, 1, 2, 3, 4, 5 })
            {
                if (count == expected)
                {
                    continue;
                }

                Tile[] tiles = TilesWithCount(count);
                Assert.Throws<ArgumentException>(() => new Meld(kind, tiles, true, Meld.NoTargetSeat));
            }
        }
    }

    [Fact]
    public void Constructor_AcceptsExactlyThreeForPongAndFourForKong()
    {
        Assert.Equal(3, new Meld(MeldKind.Pong, RulesTestData.Copies(5).Take(3).ToArray(), true, 1).Tiles.Count);

        foreach (MeldKind kind in new[] { MeldKind.KongExposed, MeldKind.KongConcealed, MeldKind.KongSupplement })
        {
            Assert.Equal(4, new Meld(kind, RulesTestData.Copies(5), false, Meld.NoTargetSeat).Tiles.Count);
        }
    }

    [Fact]
    public void Constructor_RejectsMixedTypeIds()
    {
        Tile[] tiles = [RulesTestData.Physical(5, 0), RulesTestData.Physical(6, 0), RulesTestData.Physical(5, 1)];

        Assert.Throws<ArgumentException>(() => new Meld(MeldKind.Pong, tiles, true, 0));
    }

    [Fact]
    public void Constructor_RejectsDuplicatePhysicalTiles()
    {
        Tile repeated = RulesTestData.Physical(5, 0);
        Tile[] tiles = [repeated, repeated, RulesTestData.Physical(5, 1)];

        Assert.Throws<ArgumentException>(() => new Meld(MeldKind.Pong, tiles, true, 0));
    }

    [Fact]
    public void Constructor_AcceptsTargetSeatsZeroToThreeAndNoTarget()
    {
        Tile[] tiles = RulesTestData.Copies(5).Take(3).ToArray();

        foreach (int seat in new[] { 0, 1, 2, 3, 255 })
        {
            var meld = new Meld(MeldKind.Pong, tiles, true, (byte)seat);
            Assert.Equal(seat, (int)meld.TargetSeat);
        }
    }

    [Fact]
    public void Constructor_RejectsTargetSeatOutsideRange()
    {
        Tile[] tiles = RulesTestData.Copies(5).Take(3).ToArray();

        foreach (int seat in new[] { 4, 5, 200, 254 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Meld(MeldKind.Pong, tiles, true, (byte)seat));
        }
    }

    [Fact]
    public void Constructor_DoesNotCrossValidateOpenedWithKind()
    {
        var closedPong = new Meld(MeldKind.Pong, RulesTestData.Copies(5).Take(3).ToArray(), false, Meld.NoTargetSeat);
        Assert.False(closedPong.Opened);

        var openedConcealedKong = new Meld(MeldKind.KongConcealed, RulesTestData.Copies(5), true, 1);
        Assert.True(openedConcealedKong.Opened);
    }

    [Fact]
    public void Tiles_IsCachedReadOnlyViewOfOwnStorage()
    {
        var source = new List<Tile>(RulesTestData.Copies(5).Take(3));
        var meld = new Meld(MeldKind.Pong, source, true, 1);

        Assert.Same(meld.Tiles, meld.Tiles);
        Assert.IsType<ReadOnlyCollection<Tile>>(meld.Tiles);

        // 防御性拷贝：构造后外部 List 的增删不影响 Meld 自身存储。
        source.Add(RulesTestData.Physical(5, 3));
        Assert.Equal(3, meld.Tiles.Count);

        source.RemoveAt(0);
        Assert.Equal(new ushort[] { 21, 22, 23 }, RulesTestData.Ids(meld.Tiles));

        Assert.Throws<InvalidCastException>(() => { _ = (List<Tile>)meld.Tiles; });
        Assert.Throws<NotSupportedException>(() => ((IList<Tile>)meld.Tiles).Add(RulesTestData.Physical(6, 0)));
    }

    private static int ExpectedTileCount(MeldKind kind) => kind switch
    {
        MeldKind.Pong => 3,
        _ => 4,
    };

    private static Tile[] TilesWithCount(int count)
    {
        var tiles = new Tile[count];
        for (int index = 0; index < count; index++)
        {
            tiles[index] = RulesTestData.Physical(5 + (index / 4), index % 4);
        }

        return tiles;
    }
}
