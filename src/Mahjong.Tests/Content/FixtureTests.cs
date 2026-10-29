using System.Diagnostics;
using System.Text.Json;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Mahjong.Tests.Content;

/// <summary>
/// 金标 fixture 测试。<c>fixtures/tables.json</c> 是唯一事实来源：由 python msgpack 对
/// <c>content/tables/*.bytes</c> 实读生成，测试只读 fixture，不从 docs/TABLES.md 抄值；
/// 改 .bytes 必须重生成 fixture，改 fixture 必须逐条对照 .bytes。
/// </summary>
[Collection("table-catalog")]
public sealed class FixtureTests
{
    private const string NormalTable = "dictionary_normal";
    private const string WindTable = "dictionary_wind";
    private const string DragonTable = "dictionary_dragon";
    private const string NormalAiTable = "ai_dictionary_normal";
    private const string WindAiTable = "ai_dictionary_wind";
    private const string DragonAiTable = "ai_dictionary_dragon";

    private readonly TableCatalogFixture _fixture;
    private readonly ITestOutputHelper _output;

    public FixtureTests(TableCatalogFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void FixtureFile_HasExpectedShape()
    {
        using var document = LoadFixtureDocument();
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(4, root.GetProperty("winAssertions").GetArrayLength());
        Assert.Equal(3, root.GetProperty("aiAssertions").GetArrayLength());
        Assert.Equal(6, root.GetProperty("keyCounts").EnumerateObject().Count());
    }

    [Fact]
    public void Assertions_CoverExactlyTheSpecifiedTableKeyPairs()
    {
        // T-3 §3 写死的 7 个 (table, key)：fixture 被重生成成别的 key 时这里必须红，
        // 否则 §3 的验收点会被「fixture 自洽但换了 key」绕过。
        using var document = LoadFixtureDocument();

        var expectedWinPairs = new (string Table, ulong Key)[]
        {
            (NormalTable, 1_210_220UL),
            (NormalTable, 101_210_221UL),
            (WindTable, 2UL),
            (DragonTable, 1UL),
        };
        var actualWinPairs = document.RootElement.GetProperty("winAssertions").EnumerateArray()
            .Select(assertion => (assertion.GetProperty("table").GetString()!, assertion.GetProperty("key").GetUInt64()))
            .ToArray();
        Assert.Equal(expectedWinPairs.Length, actualWinPairs.Length);
        for (var index = 0; index < expectedWinPairs.Length; index++)
        {
            Assert.Equal(expectedWinPairs[index], actualWinPairs[index]);
        }

        var expectedAiPairs = new (string Table, ulong Key)[]
        {
            (NormalAiTable, 0UL),
            (WindAiTable, 0UL),
            (DragonAiTable, 0UL),
        };
        var actualAiPairs = document.RootElement.GetProperty("aiAssertions").EnumerateArray()
            .Select(assertion => (assertion.GetProperty("table").GetString()!, assertion.GetProperty("key").GetUInt64()))
            .ToArray();
        Assert.Equal(expectedAiPairs.Length, actualAiPairs.Length);
        for (var index = 0; index < expectedAiPairs.Length; index++)
        {
            Assert.Equal(expectedAiPairs[index], actualAiPairs[index]);
        }
    }

    [Fact]
    public void WinRows_MatchGoldenFixture()
    {
        using var document = LoadFixtureDocument();
        var assertions = document.RootElement.GetProperty("winAssertions");
        var checkedAssertions = 0;

        foreach (var assertion in assertions.EnumerateArray())
        {
            var table = assertion.GetProperty("table").GetString()!;
            var suit = ParseSuit(assertion.GetProperty("suit").GetString()!);
            var key = assertion.GetProperty("key").GetUInt64();
            var expected = assertion.GetProperty("rows").EnumerateArray().Select(ParseWinRow).ToArray();

            // fixture 自身一致性：表名必须映射到它声明的花色。
            Assert.Equal(suit, WinTableSuit(table));

            Assert.True(
                _fixture.Catalog.TryGetWinRows(suit, key, out var actual),
                $"TryGetWinRows({suit}, {key}) returned false for {table}.");

            Assert.Equal(expected.Length, actual.Count);
            for (var index = 0; index < expected.Length; index++)
            {
                AssertWinRowEqual(expected[index], actual[index], table + "[" + key + "] row " + index);
            }

            checkedAssertions++;
        }

        Assert.Equal(4, checkedAssertions);
    }

    [Fact]
    public void AiRows_MatchGoldenFixture()
    {
        using var document = LoadFixtureDocument();
        var assertions = document.RootElement.GetProperty("aiAssertions");
        var checkedAssertions = 0;

        foreach (var assertion in assertions.EnumerateArray())
        {
            var table = assertion.GetProperty("table").GetString()!;
            var suit = ParseSuit(assertion.GetProperty("suit").GetString()!);
            var key = assertion.GetProperty("key").GetUInt64();
            var expected = assertion.GetProperty("rows").EnumerateArray().Select(ParseAiRow).ToArray();

            Assert.Equal(suit, AiTableSuit(table));

            Assert.True(
                _fixture.Catalog.TryGetAiRows(suit, key, out var actual),
                $"TryGetAiRows({suit}, {key}) returned false for {table}.");

            Assert.Equal(expected.Length, actual.Count);
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.Equal(expected[index].HasPair, actual[index].HasPair);
                // double 精确相等（不是容差比较）：fixture 值与 .bytes 里的 IEEE-754 位一致。
                Assert.Equal(expected[index].Probability, actual[index].Probability);
            }

            checkedAssertions++;
        }

        Assert.Equal(3, checkedAssertions);
    }

    [Fact]
    public void KeyCounts_MatchGoldenFixture()
    {
        using var document = LoadFixtureDocument();
        var counts = document.RootElement.GetProperty("keyCounts");
        var checkedTables = 0;

        foreach (var property in counts.EnumerateObject())
        {
            var table = property.Name;
            var expected = property.Value.GetInt32();
            var (isWinTable, suit) = MapTable(table);
            var actual = isWinTable
                ? _fixture.Catalog.WinRowKeyCount(suit)
                : _fixture.Catalog.AiRowKeyCount(suit);

            Assert.Equal(expected, actual);

            if (suit == SuitType.Character)
            {
                // 万/筒/条共用 normal 表：三种花色必须给出同一个计数。
                foreach (var sharedSuit in new[] { SuitType.Dot, SuitType.Bamboo })
                {
                    var sharedActual = isWinTable
                        ? _fixture.Catalog.WinRowKeyCount(sharedSuit)
                        : _fixture.Catalog.AiRowKeyCount(sharedSuit);
                    Assert.Equal(expected, sharedActual);
                }
            }

            checkedTables++;
        }

        Assert.Equal(6, checkedTables);
    }

    [Fact]
    public void ColdLoad_ThreeRunsMedianIsReportedAndSane()
    {
        var timings = new double[3];
        for (var run = 0; run < timings.Length; run++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var stopwatch = Stopwatch.StartNew();
            var catalog = TableCatalog.Load(RepoPaths.TablesDirectory);
            stopwatch.Stop();
            timings[run] = stopwatch.Elapsed.TotalMilliseconds;

            // 每次都是新实例且确实读到了真实表。
            Assert.Equal(227_494, catalog.WinRowKeyCount(SuitType.Character));
            Assert.Equal(286_550, catalog.AiRowKeyCount(SuitType.Character));
        }

        var median = timings.OrderBy(timing => timing).ElementAt(1);
        var message = "cold TableCatalog.Load ms: "
            + string.Join(", ", timings.Select(timing => timing.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))
            + "; median = " + median.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        _output.WriteLine(message);
        Console.WriteLine(message);

        Assert.True(median >= 0, "median must be a non-negative duration");
        Assert.True(median < 30_000, "cold load median sanity bound (< 30 s) exceeded: " + median.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms");
    }

    private static JsonDocument LoadFixtureDocument()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "tables.json");
        Assert.True(File.Exists(path), "golden fixture is missing from the test output: " + path);
        using var stream = File.OpenRead(path);
        return JsonDocument.Parse(stream);
    }

    private static SuitType ParseSuit(string value) => value switch
    {
        "Character" => SuitType.Character,
        "Dot" => SuitType.Dot,
        "Bamboo" => SuitType.Bamboo,
        "Wind" => SuitType.Wind,
        "Dragon" => SuitType.Dragon,
        _ => throw new InvalidOperationException("fixture declares unknown suit '" + value + "'."),
    };

    private static WinRow ParseWinRow(JsonElement row)
    {
        Assert.Equal(4, row.GetArrayLength());
        var winningRanks = row[3].EnumerateArray().Select(rank => rank.GetInt32()).ToArray();
        return new WinRow(row[0].GetInt32(), row[1].GetBoolean(), row[2].GetBoolean(), winningRanks);
    }

    private static AiRow ParseAiRow(JsonElement row)
    {
        Assert.Equal(2, row.GetArrayLength());
        return new AiRow(row[0].GetBoolean(), row[1].GetDouble());
    }

    private static void AssertWinRowEqual(WinRow expected, WinRow actual, string context)
    {
        Assert.Equal(expected.NeedJoker, actual.NeedJoker);
        Assert.Equal(expected.HasPair, actual.HasPair);
        Assert.Equal(expected.HasWin, actual.HasWin);
        Assert.Equal(expected.WinningRanks.Length, actual.WinningRanks.Length);
        for (var index = 0; index < expected.WinningRanks.Length; index++)
        {
            Assert.True(
                expected.WinningRanks[index] == actual.WinningRanks[index],
                context + ": WinningRanks[" + index + "] expected " + expected.WinningRanks[index] + " but was " + actual.WinningRanks[index]);
        }
    }

    private static SuitType WinTableSuit(string table) => table switch
    {
        NormalTable => SuitType.Character,
        WindTable => SuitType.Wind,
        DragonTable => SuitType.Dragon,
        _ => throw new InvalidOperationException("fixture declares unknown win table '" + table + "'."),
    };

    private static SuitType AiTableSuit(string table) => table switch
    {
        NormalAiTable => SuitType.Character,
        WindAiTable => SuitType.Wind,
        DragonAiTable => SuitType.Dragon,
        _ => throw new InvalidOperationException("fixture declares unknown ai table '" + table + "'."),
    };

    private static (bool IsWinTable, SuitType Suit) MapTable(string table) => table switch
    {
        NormalTable => (true, SuitType.Character),
        WindTable => (true, SuitType.Wind),
        DragonTable => (true, SuitType.Dragon),
        NormalAiTable => (false, SuitType.Character),
        WindAiTable => (false, SuitType.Wind),
        DragonAiTable => (false, SuitType.Dragon),
        _ => throw new InvalidOperationException("fixture declares unknown table '" + table + "'."),
    };
}
