using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;
using Mahjong.Tests.Content.Tables;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// WinChecker 契约测试：常规胡只认折合 14 张（hand + 3×副露）、鬼牌万能且不进 rank 计数、副露只按组计张、
/// 七对按「奇数张数牌种数 ≤ 鬼牌数」精确判定；null 入参抛 <see cref="ArgumentNullException"/>，
/// 其余非法张数返回 false/空列表；GetWinningTypes 升序去重、每次新列表、不改入参、
/// 4 张上限含副露占用，并与「逐牌种加一张后 IsWin」交叉一致。
/// 真实查表由 <see cref="TableCatalogFixture"/> 共享，整个进程只加载一次 15 MB 数据。
/// </summary>
[Collection("table-catalog")]
public sealed class WinCheckerTests
{
    private readonly WinChecker _checker;

    public WinCheckerTests(TableCatalogFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _checker = new WinChecker(fixture.Catalog);
    }

    [Fact]
    public void Constructor_RejectsNullTable()
    {
        Assert.Throws<ArgumentNullException>(() => new WinChecker(null!));
    }

    [Fact]
    public void NullArguments_ThrowArgumentNullException()
    {
        List<Tile> hand = WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 17, 17);

        Assert.Throws<ArgumentNullException>(
            () => _checker.IsWin(null!, Array.Empty<Meld>(), WinCheckerTestData.Joker));
        Assert.Throws<ArgumentNullException>(() => _checker.IsWin(hand, null!, WinCheckerTestData.Joker));
        Assert.Throws<ArgumentNullException>(
            () => _checker.GetWinningTypes(null!, Array.Empty<Meld>(), WinCheckerTestData.Joker));
        Assert.Throws<ArgumentNullException>(() => _checker.GetWinningTypes(hand, null!, WinCheckerTestData.Joker));
        Assert.Throws<ArgumentNullException>(() => _checker.IsSevenPairs(null!, WinCheckerTestData.Joker));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(13, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 0)]
    [InlineData(10, 1)]
    [InlineData(12, 1)]
    [InlineData(9, 2)]
    [InlineData(8, 3)]
    public void IsWin_IllegalTileCounts_ReturnFalse(int handCount, int meldCount)
    {
        List<Tile> hand = PatternHand(handCount);
        List<Meld> melds = PatternMelds(meldCount);

        Assert.False(_checker.IsWin(hand, melds, WinCheckerTestData.Joker));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]
    [InlineData(14, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 0)]
    [InlineData(11, 1)]
    [InlineData(12, 2)]
    public void GetWinningTypes_NonTenpaiCounts_ReturnEmpty(int handCount, int meldCount)
    {
        List<Tile> hand = PatternHand(handCount);
        List<Meld> melds = PatternMelds(meldCount);

        Assert.Empty(_checker.GetWinningTypes(hand, melds, WinCheckerTestData.Joker));
    }

    [Fact]
    public void GetWinningTypes_AlreadyWinningFourteenTileHand_ReturnsEmpty()
    {
        List<Tile> hand = WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 17, 17);

        Assert.True(_checker.IsWin(hand, Array.Empty<Meld>(), WinCheckerTestData.Joker));
        Assert.Empty(_checker.GetWinningTypes(hand, Array.Empty<Meld>(), WinCheckerTestData.Joker));
    }

    [Fact]
    public void IsWin_RejectsHandWithMoreThanFourCopiesOfOneType()
    {
        // 1 万 5 张 + 9 张其它牌：张数恰好 14，但物理牌合法性不成立，必须判 false 而不是抛异常。
        // 判定只读 Type.Id，因此第 5 张复用物理 id 不影响本用例要覆盖的非法牌种计数分支。
        List<Tile> hand = WinCheckerTestData.Hand(1, 2, 3, 4, 5, 6, 7, 8, 9);
        AddFiveCopiesOfOneType(hand);

        Assert.Equal(14, hand.Count);
        Assert.False(_checker.IsWin(hand, Array.Empty<Meld>(), WinCheckerTestData.Joker));
        Assert.False(_checker.IsWin(hand, Array.Empty<Meld>(), WinCheckerTestData.NoJoker));

        // 13 张的同类非法输入：GetWinningTypes 同样走非法输入分支，返回空列表而不是抛异常。
        List<Tile> thirteenTiles = WinCheckerTestData.Hand(1, 2, 3, 4, 5, 6, 7, 8);
        AddFiveCopiesOfOneType(thirteenTiles);
        Assert.Equal(13, thirteenTiles.Count);
        Assert.Empty(_checker.GetWinningTypes(thirteenTiles, Array.Empty<Meld>(), WinCheckerTestData.Joker));
    }

    [Fact]
    public void DefaultJoker_MeansNoJoker_AndTypeZeroStaysAnOrdinaryTile()
    {
        // 1 万 ×4 只有在 1 万是鬼牌时才能补成「刻子 + 将」；default 表示无鬼，1 万是普通牌，因此不胡。
        List<Tile> fourOnes = WinCheckerTestData.Hand(0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 9, 9, 9, 17);
        Assert.False(_checker.IsWin(fourOnes, Array.Empty<Meld>(), WinCheckerTestData.NoJoker));
        Assert.True(_checker.IsWin(fourOnes, Array.Empty<Meld>(), RulesTestData.TypeOf(0)));

        // default 下含一万的普通 14 张胡牌与七对按普通牌种计数。
        List<Tile> standardHand = WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 17, 17);
        Assert.True(_checker.IsWin(standardHand, Array.Empty<Meld>(), WinCheckerTestData.NoJoker));

        List<Tile> sevenPairsHand = WinCheckerTestData.Hand(0, 0, 1, 1, 2, 2, 9, 9, 10, 10, 18, 18, 27, 27);
        Assert.True(_checker.IsSevenPairs(sevenPairsHand, WinCheckerTestData.NoJoker));
    }

    [Fact]
    public void JokerType_TilesAreTreatedAsWild_OnlyWhenThatTypeIsActive()
    {
        // 东 ×4 + 223344 万 + 111 筒 + 9 筒：东为鬼时 4 张鬼 = 刻子 + 将；东为普通牌只是刻子 + 单张，不成胡。
        List<Tile> hand = WinCheckerTestData.Hand(27, 27, 27, 27, 1, 2, 3, 4, 5, 6, 9, 9, 9, 17);

        Assert.True(_checker.IsWin(hand, Array.Empty<Meld>(), RulesTestData.TypeOf(27)));
        Assert.False(_checker.IsWin(hand, Array.Empty<Meld>(), WinCheckerTestData.NoJoker));
    }

    [Fact]
    public void IsWin_MeldKindsDoNotParticipateInDecomposition_OnlyGroupCountMatters()
    {
        // 2 张手牌（将）+ 4 组副露 = 14 张折合；副露只按每组 3 张计，杠的第 4 张不影响张数。
        List<Tile> hand = WinCheckerTestData.Hand(17, 17);
        List<Meld> melds =
        [
            WinCheckerTestData.Kong(MeldKind.KongExposed, 27),
            WinCheckerTestData.Kong(MeldKind.KongConcealed, 28),
            WinCheckerTestData.Pong(29),
            WinCheckerTestData.Pong(30),
        ];

        Assert.True(_checker.IsWin(hand, melds, WinCheckerTestData.Joker));

        // 同 2 张手牌换成 3 组副露（折合 11 张）则不是 14 张，必须 false。
        Assert.False(_checker.IsWin(hand, melds.Take(3).ToList(), WinCheckerTestData.Joker));
    }

    [Fact]
    public void RepeatedCalls_AreStable_AndInputsAreNotMutated()
    {
        List<Tile> hand = WinCheckerTestData.Hand(0, 1, 3, 4, 5, 6, 7, 8, 17, 17);
        List<Meld> melds = [WinCheckerTestData.Pong(27)];
        string handBefore = WinCheckerTestData.Snapshot(hand);
        string meldsBefore = WinCheckerTestData.Snapshot(melds);

        bool firstWin = _checker.IsWin(hand, melds, WinCheckerTestData.Joker);
        bool secondWin = _checker.IsWin(hand, melds, WinCheckerTestData.Joker);
        Assert.Equal(firstWin, secondWin);

        IReadOnlyList<TileType> firstTypes = _checker.GetWinningTypes(hand, melds, WinCheckerTestData.Joker);
        IReadOnlyList<TileType> secondTypes = _checker.GetWinningTypes(hand, melds, WinCheckerTestData.Joker);
        Assert.NotSame(firstTypes, secondTypes);
        Assert.Equal(
            WinCheckerTestData.TypeIds(firstTypes),
            WinCheckerTestData.TypeIds(secondTypes));

        bool firstSevenPairs = _checker.IsSevenPairs(hand, WinCheckerTestData.Joker);
        bool secondSevenPairs = _checker.IsSevenPairs(hand, WinCheckerTestData.Joker);
        Assert.Equal(firstSevenPairs, secondSevenPairs);

        Assert.Equal(handBefore, WinCheckerTestData.Snapshot(hand));
        Assert.Equal(meldsBefore, WinCheckerTestData.Snapshot(melds));
    }

    [Fact]
    public void GetWinningTypes_ReturnsFreshAscendingDistinctList_AndKeepsInputsUnchanged()
    {
        List<Tile> hand = WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 17, 17);
        List<Meld> melds = [WinCheckerTestData.Pong(27)];
        string handBefore = WinCheckerTestData.Snapshot(hand);
        string meldsBefore = WinCheckerTestData.Snapshot(melds);

        IReadOnlyList<TileType> first = _checker.GetWinningTypes(hand, melds, WinCheckerTestData.Joker);
        IReadOnlyList<TileType> second = _checker.GetWinningTypes(hand, melds, WinCheckerTestData.Joker);

        int[] ids = WinCheckerTestData.TypeIds(first);
        Assert.NotEmpty(ids);
        Assert.NotSame(first, second);
        Assert.Equal(ids.Order(), ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal(handBefore, WinCheckerTestData.Snapshot(hand));
        Assert.Equal(meldsBefore, WinCheckerTestData.Snapshot(melds));
    }

    [Fact]
    public void GetWinningTypes_AgreesWithPerTypeIsWin()
    {
        AssertWinningTypesAgreeWithIsWin(
            WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 17),
            new List<Meld>());

        AssertWinningTypesAgreeWithIsWin(
            WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 17, 17),
            new List<Meld> { WinCheckerTestData.Pong(27) });

        AssertWinningTypesAgreeWithIsWin(
            WinCheckerTestData.Hand(0, 0, 0, 0, 1, 2, 3, 9, 10, 11, 18, 19, 20),
            new List<Meld>());

        // 手牌 1 张东 + 东碰：4 张上限要按手牌 + 副露合并计算。
        List<Tile> fourEastCopies = WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8);
        fourEastCopies.Add(RulesTestData.Physical(27, 3));
        AssertWinningTypesAgreeWithIsWin(
            fourEastCopies,
            new List<Meld> { WinCheckerTestData.Pong(27) });
    }

    [Fact]
    public void GetWinningTypes_FourCopyLimit_IncludesMeldTiles()
    {
        // 手牌 1 张东 + 东碰（3 张）= 东共 4 张：东风牌种不能作为补张返回，即使加第 5 张在牌型上能胡。
        List<Tile> hand = [.. WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8), RulesTestData.Physical(27, 3)];
        List<Meld> melds = [WinCheckerTestData.Pong(27)];

        int[] result = WinCheckerTestData.TypeIds(_checker.GetWinningTypes(hand, melds, WinCheckerTestData.Joker));

        Assert.DoesNotContain(27, result);
        Assert.Equal(new[] { 31 }, result);
    }

    [Fact]
    public void IsSevenPairs_RequiresExactlyFourteenTiles()
    {
        List<Tile> fourteen = WinCheckerTestData.Hand(0, 0, 1, 1, 2, 2, 9, 9, 10, 10, 18, 18, 27, 27);
        List<Tile> thirteen = WinCheckerTestData.Hand(0, 0, 1, 1, 2, 2, 9, 9, 10, 10, 18, 18, 27);
        List<Tile> fifteen = WinCheckerTestData.Hand(0, 0, 1, 1, 2, 2, 9, 9, 10, 10, 18, 18, 27, 27, 28);

        Assert.True(_checker.IsSevenPairs(fourteen, WinCheckerTestData.Joker));
        Assert.False(_checker.IsSevenPairs(thirteen, WinCheckerTestData.Joker));
        Assert.False(_checker.IsSevenPairs(fifteen, WinCheckerTestData.Joker));

        // 4 张同牌种按两对计；normal 表本身也覆盖 rank 计数 4。
        List<Tile> fourCopies = WinCheckerTestData.Hand(0, 0, 0, 0, 1, 1, 2, 2, 9, 9, 10, 10, 18, 18);
        Assert.True(_checker.IsSevenPairs(fourCopies, WinCheckerTestData.Joker));
    }

    [Fact]
    public void IsSevenPairs_OddSingleTypesMustBeCoveredByJokers()
    {
        // 4 个奇数张牌种 + 4 张鬼：可全部配对；减 1 张鬼则不够，必须 false。
        List<Tile> fourJokers = WinCheckerTestData.Hand(0, 0, 1, 1, 2, 2, 3, 4, 5, 6, 31, 31, 31, 31);
        List<Tile> threeJokers = WinCheckerTestData.Hand(0, 0, 1, 1, 2, 2, 3, 4, 5, 6, 7, 31, 31, 31);

        Assert.True(_checker.IsSevenPairs(fourJokers, WinCheckerTestData.Joker));
        Assert.False(_checker.IsSevenPairs(threeJokers, WinCheckerTestData.Joker));
    }

    [Fact]
    public void IsWin_HonorRank4_TableHasNoKey_ReturnsFalseAsSpecified()
    {
        // M0.2 T-1 已知缺口（Lead 裁定：维持规格 §2.3 字面语义，不是 T-1 缺陷，M4 重生成表后重评）：
        // dictionary_wind 只覆盖每 rank 0–3（251 个 key，最大 digit=3），dictionary_dragon 只覆盖 0–3（64 个 key），
        // 表结构无法表达「4 张同风/箭 = 刻子 3 张 + 1 张配鬼成将」的分解。因此该花色 key 无候选行 → 按规格整手不胡；
        // 同样的 14 张在暴力回溯下本可成胡（刻子 + 鬼作将 + 3 组面子）。
        List<Tile> fourEast = WinCheckerTestData.Hand(27, 27, 27, 27, 31, 0, 1, 2, 10, 11, 12, 18, 19, 20);
        Assert.Equal(14, fourEast.Count);
        Assert.False(_checker.IsWin(fourEast, Array.Empty<Meld>(), WinCheckerTestData.Joker));

        List<Tile> fourGreenDragon = WinCheckerTestData.Hand(32, 32, 32, 32, 31, 0, 1, 2, 10, 11, 12, 18, 19, 20);
        Assert.Equal(14, fourGreenDragon.Count);
        Assert.False(_checker.IsWin(fourGreenDragon, Array.Empty<Meld>(), WinCheckerTestData.Joker));

        // 对照：同结构只留 3 张东（普通刻子）+ 白配鬼成将，按表可胡，说明差异只来自 4 张同风/箭的 key 覆盖缺口。
        List<Tile> threeEast = WinCheckerTestData.Hand(27, 27, 27, 31, 33, 0, 1, 2, 10, 11, 12, 18, 19, 20);
        Assert.True(_checker.IsWin(threeEast, Array.Empty<Meld>(), WinCheckerTestData.Joker));
    }

    [Fact]
    public void IsWin_MoreThanFourJokers_IllegalInput_ReturnsFalse()
    {
        // 鬼牌也是物理牌种：同牌种超过 4 张属非法输入，契约要求 false 而不是抛异常。
        // 该 14 张若不做非法校验，5 张鬼可拆成「1 组面子 + 1 个将」，与 3 组自然面子成胡，因此本用例可钉住鬼牌上限分支。
        List<Tile> hand = WinCheckerTestData.Hand(0, 1, 2, 9, 10, 11, 18, 19, 20);
        AddCopies(hand, WinCheckerTestData.JokerTypeId, 5);

        Assert.Equal(14, hand.Count);
        Assert.False(_checker.IsWin(hand, Array.Empty<Meld>(), WinCheckerTestData.Joker));

        // 13 张同形非法输入：GetWinningTypes 返回空列表且不抛异常。
        List<Tile> thirteenTiles = WinCheckerTestData.Hand(0, 1, 2, 9, 10, 11, 18, 19);
        AddCopies(thirteenTiles, WinCheckerTestData.JokerTypeId, 5);
        Assert.Equal(13, thirteenTiles.Count);
        Assert.Empty(_checker.GetWinningTypes(thirteenTiles, Array.Empty<Meld>(), WinCheckerTestData.Joker));
    }

    [Fact]
    public void GetWinningTypes_FourJokersInHand_DoesNotSuggestJokerType()
    {
        // 手牌已含 4 张红中：不可能再摸到第 5 张，GetWinningTypes 必须排除 id 31；
        // 其余 33 种牌种逐一加一张都能与 4 鬼组成 4 面子 + 1 将（期望由独立 Python 参考推导）。
        List<Tile> hand = WinCheckerTestData.Hand(0, 1, 2, 9, 10, 11, 18, 19, 20, 31, 31, 31, 31);
        int[] expected =
        [
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
            21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 32, 33,
        ];

        int[] actual = WinCheckerTestData.TypeIds(_checker.GetWinningTypes(hand, Array.Empty<Meld>(), WinCheckerTestData.Joker));

        Assert.DoesNotContain(WinCheckerTestData.JokerTypeId, actual);
        Assert.Equal(expected, actual);
    }

    private void AssertWinningTypesAgreeWithIsWin(List<Tile> hand, List<Meld> melds)
    {
        Assert.Equal(13, hand.Count + (3 * melds.Count));
        Assert.Equal(
            ExpectedWinningTypesByIsWin(hand, melds, WinCheckerTestData.Joker),
            WinCheckerTestData.TypeIds(_checker.GetWinningTypes(hand, melds, WinCheckerTestData.Joker)));
    }

    private static void AddCopies(List<Tile> hand, int typeId, int count)
    {
        for (int index = 0; index < count; index++)
        {
            // 判定只读 Type.Id；构造非法输入时复用物理 id 不影响所测的「同牌种 >4」分支。
            hand.Add(RulesTestData.Physical(typeId, index % 4));
        }
    }

    private static void AddFiveCopiesOfOneType(List<Tile> hand)
    {
        for (int index = 0; index < 5; index++)
        {
            hand.Add(RulesTestData.Physical(0, index % 4));
        }
    }

    private int[] ExpectedWinningTypesByIsWin(
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Meld> melds,
        TileType joker)
    {
        List<int> expected = [];
        for (int typeId = 0; typeId < RulesTestData.TileTypeCount; typeId++)
        {
            if (WinCheckerTestData.CountType(hand, melds, typeId) >= 4)
            {
                continue;
            }

            List<Tile> extended = new(hand) { RulesTestData.Physical(typeId, 0) };
            if (_checker.IsWin(extended, melds, joker))
            {
                expected.Add(typeId);
            }
        }

        return expected.ToArray();
    }

    private static List<Tile> PatternHand(int count)
    {
        // 17 个牌种循环，保证同牌种最多 2 张，避免触发「超过 4 张」的非法输入分支。
        int[] typeIds = new int[count];
        for (int index = 0; index < count; index++)
        {
            typeIds[index] = index % 17;
        }

        return WinCheckerTestData.Hand(typeIds);
    }

    private static List<Meld> PatternMelds(int count)
    {
        List<Meld> melds = new(count);
        for (int index = 0; index < count; index++)
        {
            melds.Add(WinCheckerTestData.Pong(27 + index));
        }

        return melds;
    }
}
