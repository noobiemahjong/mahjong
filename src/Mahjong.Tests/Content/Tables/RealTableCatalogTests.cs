using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;
using Xunit;
using Xunit.Abstractions;

namespace Mahjong.Tests.Content.Tables;

/// <summary>
/// 真实 6 张查表的加载、key 数与查询形状；行内容金标断言归 fixtures/tables.json（test-fixture）。
/// </summary>
[Collection("table-catalog")]
public sealed class RealTableCatalogTests
{
    private const int NormalWinKeyCount = 227_494;
    private const int WindWinKeyCount = 251;
    private const int DragonWinKeyCount = 64;
    private const int NormalAiKeyCount = 286_550;
    private const int WindAiKeyCount = 610;
    private const int DragonAiKeyCount = 125;

    private readonly TableCatalogFixture _fixture;
    private readonly ITestOutputHelper _output;

    public RealTableCatalogTests(TableCatalogFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void WinRowKeyCounts_MatchTableSizes()
    {
        Assert.Equal(NormalWinKeyCount, _fixture.Catalog.WinRowKeyCount(SuitType.Character));
        Assert.Equal(NormalWinKeyCount, _fixture.Catalog.WinRowKeyCount(SuitType.Dot));
        Assert.Equal(NormalWinKeyCount, _fixture.Catalog.WinRowKeyCount(SuitType.Bamboo));
        Assert.Equal(WindWinKeyCount, _fixture.Catalog.WinRowKeyCount(SuitType.Wind));
        Assert.Equal(DragonWinKeyCount, _fixture.Catalog.WinRowKeyCount(SuitType.Dragon));
    }

    [Fact]
    public void AiRowKeyCounts_MatchTableSizes()
    {
        Assert.Equal(NormalAiKeyCount, _fixture.Catalog.AiRowKeyCount(SuitType.Character));
        Assert.Equal(NormalAiKeyCount, _fixture.Catalog.AiRowKeyCount(SuitType.Dot));
        Assert.Equal(NormalAiKeyCount, _fixture.Catalog.AiRowKeyCount(SuitType.Bamboo));
        Assert.Equal(WindAiKeyCount, _fixture.Catalog.AiRowKeyCount(SuitType.Wind));
        Assert.Equal(DragonAiKeyCount, _fixture.Catalog.AiRowKeyCount(SuitType.Dragon));
    }

    [Fact]
    public void TryGetWinRows_KeyZero_HitsEverySuitWithConsistentRowShape()
    {
        (SuitType Suit, int RankCount)[] suits =
        [
            (SuitType.Character, 9),
            (SuitType.Dot, 9),
            (SuitType.Bamboo, 9),
            (SuitType.Wind, 4),
            (SuitType.Dragon, 3),
        ];

        foreach ((SuitType suit, int rankCount) in suits)
        {
            ulong key = TableKey.Encode(new byte[rankCount]);
            Assert.Equal(0UL, key);
            Assert.True(
                _fixture.Catalog.TryGetWinRows(suit, key, out IReadOnlyList<WinRow> rows),
                $"key 0 is missing from the {suit} win table");
            Assert.NotEmpty(rows);
            foreach (WinRow row in rows)
            {
                Assert.InRange(row.NeedJoker, 0, int.MaxValue);
                if (row.HasWin)
                {
                    Assert.Empty(row.WinningRanks);
                }
                else
                {
                    Assert.Equal(rankCount, row.WinningRanks.Length);
                    Assert.All(row.WinningRanks, count => Assert.InRange(count, 0, 4));
                }
            }
        }
    }

    [Fact]
    public void TryGetAiRows_KeyZero_HitsEverySuit()
    {
        SuitType[] suits = [SuitType.Character, SuitType.Dot, SuitType.Bamboo, SuitType.Wind, SuitType.Dragon];

        foreach (SuitType suit in suits)
        {
            Assert.True(
                _fixture.Catalog.TryGetAiRows(suit, 0, out IReadOnlyList<AiRow> rows),
                $"key 0 is missing from the {suit} ai table");
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.True(double.IsFinite(row.Probability)));
        }
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalseAndSharedEmptyList()
    {
        SuitType[] suits = [SuitType.Character, SuitType.Dot, SuitType.Bamboo, SuitType.Wind, SuitType.Dragon];

        foreach (SuitType suit in suits)
        {
            Assert.False(_fixture.Catalog.TryGetWinRows(suit, ulong.MaxValue, out IReadOnlyList<WinRow> winRows));
            Assert.Same(Array.Empty<WinRow>(), winRows);
            Assert.False(_fixture.Catalog.TryGetAiRows(suit, ulong.MaxValue, out IReadOnlyList<AiRow> aiRows));
            Assert.Same(Array.Empty<AiRow>(), aiRows);
        }
    }

    [Fact]
    public void UnmappedSuit_ReturnsFalseAndZeroCounts()
    {
        foreach (SuitType suit in new[] { (SuitType)0, (SuitType)6, (SuitType)255 })
        {
            Assert.False(_fixture.Catalog.TryGetWinRows(suit, 0, out IReadOnlyList<WinRow> winRows));
            Assert.Same(Array.Empty<WinRow>(), winRows);
            Assert.False(_fixture.Catalog.TryGetAiRows(suit, 0, out IReadOnlyList<AiRow> aiRows));
            Assert.Same(Array.Empty<AiRow>(), aiRows);
            Assert.Equal(0, _fixture.Catalog.WinRowKeyCount(suit));
            Assert.Equal(0, _fixture.Catalog.AiRowKeyCount(suit));
        }
    }

    [Fact]
    public void LoadElapsed_IsPlausibleForFifteenMegabytes()
    {
        _output.WriteLine($"[table-catalog] cold TableCatalog.Load = {_fixture.LoadElapsed.TotalMilliseconds:F1} ms");
        Assert.True(_fixture.LoadElapsed > TimeSpan.Zero);
        Assert.True(_fixture.LoadElapsed < TimeSpan.FromSeconds(30));
    }
}
