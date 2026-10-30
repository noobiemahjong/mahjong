using Mahjong.Core.Rules;
using Mahjong.Tests.Content.Tables;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// T-1 金标回归：逐条比对 <see cref="WinCheckerGoldenVectors"/> 的独立期望；听牌用例再用
/// 「逐牌种加一张后调用 IsWin」交叉验证 GetWinningTypes（期望不来自 GetWinningTypes 自身）。
/// 真实查表由 <see cref="TableCatalogFixture"/> 共享，整个进程只加载一次。
/// </summary>
[Collection("table-catalog")]
public sealed class WinCheckerGoldenVectorTests
{
    private readonly WinChecker _checker;

    public WinCheckerGoldenVectorTests(TableCatalogFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _checker = new WinChecker(fixture.Catalog);
    }

    /// <summary>金标用例名，作为 <see cref="GoldenCase_MatchesIndependentExpectations"/> 的数据行。</summary>
    public static IEnumerable<object[]> CaseNames =>
        WinCheckerGoldenVectors.Cases.Select(goldenCase => new object[] { goldenCase.Name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void GoldenCase_MatchesIndependentExpectations(string caseName)
    {
        WinCheckerGoldenVectors.WinCheckerGoldenCase goldenCase =
            Array.Find(WinCheckerGoldenVectors.Cases, candidate => candidate.Name == caseName)
            ?? throw new InvalidOperationException($"Unknown golden case '{caseName}'.");
        (List<Tile> hand, List<Meld> melds) = WinCheckerTestData.BuildCase(goldenCase);
        TileType joker = WinCheckerTestData.JokerOrDefault(goldenCase.JokerTypeId);

        Assert.Equal(goldenCase.IsWin, _checker.IsWin(hand, melds, joker));
        Assert.Equal(goldenCase.IsSevenPairs, _checker.IsSevenPairs(hand, joker));
        Assert.Equal(
            goldenCase.WinningTypes,
            WinCheckerTestData.TypeIds(_checker.GetWinningTypes(hand, melds, joker)));
    }

    [Fact]
    public void WinningTypes_AgreeWithPerTypeIsWin_ForEveryTenpaiGoldenCase()
    {
        int checkedCases = 0;
        foreach (WinCheckerGoldenVectors.WinCheckerGoldenCase goldenCase in WinCheckerGoldenVectors.Cases)
        {
            (List<Tile> hand, List<Meld> melds) = WinCheckerTestData.BuildCase(goldenCase);
            if (hand.Count + (3 * melds.Count) != 13)
            {
                continue;
            }

            checkedCases++;
            TileType joker = WinCheckerTestData.JokerOrDefault(goldenCase.JokerTypeId);
            List<int> expected = [];
            for (int typeId = 0; typeId < RulesTestData.TileTypeCount; typeId++)
            {
                if (WinCheckerTestData.CountType(hand, melds, typeId) >= 4)
                {
                    continue;
                }

                // 交叉验证只依赖 IsWin（其本身由金标逐条钉住）；GetWinningTypes 的主体实现不参与期望推导。
                List<Tile> extended = new(hand) { RulesTestData.Physical(typeId, 0) };
                if (_checker.IsWin(extended, melds, joker))
                {
                    expected.Add(typeId);
                }
            }

            int[] actual = WinCheckerTestData.TypeIds(_checker.GetWinningTypes(hand, melds, joker));
            Assert.Equal(expected, actual);
            Assert.Equal(goldenCase.WinningTypes, actual);
        }

        Assert.True(checkedCases >= 6, $"Expected at least 6 tenpai golden cases, found {checkedCases}.");
    }
}
