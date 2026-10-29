using Mahjong.Core.Content;
using Mahjong.Core.Rules;
using Mahjong.Tests.Support;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 规则层与内容的跨层对齐守卫：Deal 发到各区域的每张牌，其 Type 必须与仓库 content/tiles.json 的牌型布局一致。
/// 规则层按固定公式（typeId = (物理 id - 1) / 4）还原 Type，内容布局一旦变更必须在此变红。
/// 本文件是 Rules 下唯一允许依赖仓库 content/ 的测试；其余 Rules 测试保持不依赖内容目录。
/// TileType 相等性只比 Id，因此逐字段断言 Id/Suit/Rank，避免“只比 Id”把 Suit/Rank 漂移静默放行。
/// </summary>
public sealed class RulesContentAlignmentTests
{
    private static readonly Lazy<ContentCatalog> RepoContent =
        new(() => ContentCatalog.Load(RepoPaths.ContentDirectory));

    [Fact]
    public void Deal_TileTypesMatchContentLayoutForAllPhysicalTiles()
    {
        ContentCatalog catalog = RepoContent.Value;
        Tile[] tiles = CollectTiles(DealtState());

        Assert.Equal(RulesTestData.PhysicalTileCount, tiles.Length);
        Assert.Equal(
            Enumerable.Range(1, RulesTestData.PhysicalTileCount),
            tiles.Select(tile => (int)tile.Id).Order());

        foreach (Tile tile in tiles)
        {
            TileType expected = catalog.GetTileType((ushort)((tile.Id - 1) / 4));

            Assert.Equal(expected.Id, tile.Type.Id);
            Assert.Equal(expected.Suit, tile.Type.Suit);
            Assert.Equal(expected.Rank, tile.Type.Rank);
        }
    }

    [Fact]
    public void Deal_CoversEveryTileTypeIncludingRedDragon()
    {
        ContentCatalog catalog = RepoContent.Value;
        Tile[] tiles = CollectTiles(DealtState());

        ushort[] typeIds = tiles.Select(tile => tile.Type.Id).Distinct().Order().ToArray();
        Assert.Equal(
            Enumerable.Range(0, RulesTestData.TileTypeCount).Select(typeId => (ushort)typeId),
            typeIds);

        foreach (ushort typeId in typeIds)
        {
            Assert.Equal(4, tiles.Count(tile => tile.Type.Id == typeId));
        }

        TileType redDragon = catalog.GetTileType(31);
        Assert.Equal(SuitType.Dragon, redDragon.Suit);
        Assert.Equal(1, (int)redDragon.Rank);
        Assert.Equal(4, tiles.Count(tile => tile.Type.Id == 31));
    }

    [Fact]
    public void Deal_AssignsNoUnknownOrDefaultTileType()
    {
        Tile[] tiles = CollectTiles(DealtState());

        foreach (Tile tile in tiles)
        {
            // 物理牌 id 0 是 Unknown，Deal 产出的牌不得出现。
            Assert.NotEqual(0, (int)tile.Id);

            // TileType 相等性只比 Id，typeId 0（1 万）本身合法，与 default(TileType).Id 相同；
            // 因此“未初始化/default”必须按 Suit/Rank 落位判断，不能直接与 default 比较。
            Assert.Equal((ushort)((tile.Id - 1) / 4), tile.Type.Id);
            Assert.NotEqual(default, tile.Type.Suit);
            Assert.True(tile.Type.Rank >= 1);
        }
    }

    private static GameState DealtState()
    {
        var state = new GameState();
        state.Deal(new Pcg32(42), 2);
        return state;
    }

    private static Tile[] CollectTiles(GameState state) =>
        state.Wall.Items
            .Concat(state.WallTail.Items)
            .Concat(state.Players.SelectMany(player => player.Hand.Items))
            .ToArray();
}
