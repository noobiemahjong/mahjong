using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;
using Mahjong.Tests.Content.Tables;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 与 M0.1 T-3 金标 fixture（src/Mahjong.Tests/fixtures/tables.json，语义见 docs/TABLES.md §2.4）的 4 个胡牌表 key 对齐：
/// 先断言真实表里这些 key 的行形状，再按「所选行的 NeedJoker / HasPair / HasWin 语义」构造 14 张折合手牌，
/// 验证 IsWin 结论与行语义一致（行可解释 → 胡；只有听牌行可解释 → 不胡）。
/// 构造依据写在各用例注释里；共享 <see cref="TableCatalogFixture"/>，不重复加载 15 MB 表。
/// </summary>
[Collection("table-catalog")]
public sealed class WinCheckerFixtureAlignmentTests
{
    private const ulong NormalKey1210220 = 1_210_220UL;
    private const ulong NormalKey101210221 = 101_210_221UL;
    private const ulong WindKey2 = 2UL;
    private const ulong DragonKey1 = 1UL;

    private readonly TableCatalog _catalog;
    private readonly WinChecker _checker;

    public WinCheckerFixtureAlignmentTests(TableCatalogFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _catalog = fixture.Catalog;
        _checker = new WinChecker(fixture.Catalog);
    }

    [Fact]
    public void NormalKey1210220_ConstructedHandsFollowRowSemantics()
    {
        // dictionary_normal key 1210220 = [0,0,1,2,1,0,2,2,0]：3/4/4/5/7/7/8/8（8 张）。
        AssertWinRows(
            SuitType.Character,
            NormalKey1210220,
            [
                (2, true, false),
                (3, false, false),
                (3, true, true),
                (4, false, true),
            ]);

        // 行 (4,false,true)：8 张万 + 4 鬼 = 12 张 = 4 面子（无将）；将取 wind key 2 的 (0,true,true)（北北）；
        // 剩余鬼 4-4=0 → 0%3 且恰一将 → 胡。
        List<Tile> needFourJokers = WinCheckerTestData.Hand(2, 3, 3, 4, 6, 6, 7, 7, 30, 30, 31, 31, 31, 31);
        Assert.True(_checker.IsWin(needFourJokers, Array.Empty<Meld>(), WinCheckerTestData.Joker));

        // 行 (3,true,true)：8 张万 + 3 鬼 = 11 张 = 3 面子 + 1 将；另 3 张为完整面子（123 筒），剩余鬼 0 → 胡。
        List<Tile> needThreeJokers = WinCheckerTestData.Hand(2, 3, 3, 4, 6, 6, 7, 7, 9, 10, 11, 31, 31, 31);
        Assert.True(_checker.IsWin(needThreeJokers, Array.Empty<Meld>(), WinCheckerTestData.Joker));

        // 对抗样例：2 鬼 + 2 对子。只有非 HasWin 行 (2,true,false) 的 NeedJoker=2 能对上，
        // 但该行是听牌分解（还差 1 张）；纳入它会伪胡。正确实现只取 HasWin 行 → 不胡。
        List<Tile> adversarial = WinCheckerTestData.Hand(2, 3, 3, 4, 6, 6, 7, 7, 30, 30, 28, 28, 31, 31);
        Assert.False(_checker.IsWin(adversarial, Array.Empty<Meld>(), WinCheckerTestData.Joker));
    }

    [Fact]
    public void NormalKey101210221_ConstructedHandsFollowRowSemantics()
    {
        // dictionary_normal key 101210221 = [1,0,1,2,1,0,2,2,1]：1/3/4/4/5/7/7/8/8/9（10 张）。
        AssertWinRows(
            SuitType.Character,
            NormalKey101210221,
            [
                (3, true, false),
                (4, true, true),
            ]);

        // 行 (4,true,true)：10 张万 + 4 鬼 = 14 张 = 4 面子 + 1 将，剩余鬼 0 → 胡。
        List<Tile> needFourJokers = WinCheckerTestData.Hand(0, 2, 3, 3, 4, 6, 6, 7, 7, 8, 31, 31, 31, 31);
        Assert.True(_checker.IsWin(needFourJokers, Array.Empty<Meld>(), WinCheckerTestData.Joker));

        // 只有 3 鬼时，唯一 HasWin 行要求 4 鬼；非 HasWin 行 (3,true,false) 是听牌分解，不参与成胡 → 不胡。
        List<Tile> threeJokers = WinCheckerTestData.Hand(0, 2, 3, 3, 4, 6, 6, 7, 7, 8, 31, 31, 31, 18);
        Assert.False(_checker.IsWin(threeJokers, Array.Empty<Meld>(), WinCheckerTestData.Joker));
    }

    [Fact]
    public void WindKey2_ConstructedHandsFollowRowSemantics()
    {
        // dictionary_wind key 2 = [0,0,0,2]：北北（2 张）。
        AssertWinRows(
            SuitType.Wind,
            WindKey2,
            [
                (0, true, true),
                (0, false, false),
                (1, false, true),
                (2, true, false),
                (3, true, true),
                (3, false, false),
                (4, false, true),
            ]);

        // 行 (0,true,true)：北北直接作将，其余 12 张为 4 组完整面子（123/456/789 万 + 111 筒）→ 胡。
        List<Tile> pairRow = WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 30, 30);
        Assert.True(_checker.IsWin(pairRow, Array.Empty<Meld>(), WinCheckerTestData.Joker));

        // 行 (4,false,true)：北北 + 4 鬼 = 6 张 = 2 面子（无将）；另 8 张 = 2 面子 + 1 将（123/456 万 + 99 筒）→ 胡。
        List<Tile> needFourJokers = WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 17, 17, 30, 30, 31, 31, 31, 31);
        Assert.True(_checker.IsWin(needFourJokers, Array.Empty<Meld>(), WinCheckerTestData.Joker));
    }

    [Fact]
    public void DragonKey1_ConstructedHandsFollowRowSemantics()
    {
        // dictionary_dragon key 1 = [0,0,1]：白 1 张。
        AssertWinRows(
            SuitType.Dragon,
            DragonKey1,
            [
                (0, true, false),
                (1, true, true),
                (1, false, false),
                (2, false, true),
                (3, true, false),
                (4, true, true),
                (4, false, false),
            ]);

        // 行 (1,true,true)：白 + 1 鬼 = 2 张 = 将；另 12 张为 4 组完整面子（123/456/789 万 + 111 筒）→ 胡（鬼作将）。
        List<Tile> needOneJoker = WinCheckerTestData.Hand(33, 31, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9);
        Assert.True(_checker.IsWin(needOneJoker, Array.Empty<Meld>(), WinCheckerTestData.Joker));

        // 行 (2,false,true)：白 + 2 鬼 = 3 张 = 1 面子（无将）；另 11 张 = 3 面子 + 1 将（123/456/789 万 + 99 筒）→ 胡。
        List<Tile> needTwoJokers = WinCheckerTestData.Hand(33, 31, 31, 0, 1, 2, 3, 4, 5, 6, 7, 8, 17, 17);
        Assert.True(_checker.IsWin(needTwoJokers, Array.Empty<Meld>(), WinCheckerTestData.Joker));
    }

    private void AssertWinRows(
        SuitType suit,
        ulong key,
        (int NeedJoker, bool HasPair, bool HasWin)[] expected)
    {
        Assert.True(
            _catalog.TryGetWinRows(suit, key, out IReadOnlyList<WinRow> rows),
            $"{suit} win table has no key {key}.");
        Assert.Equal(
            expected,
            rows.Select(row => (row.NeedJoker, row.HasPair, row.HasWin)).ToArray());
    }
}
