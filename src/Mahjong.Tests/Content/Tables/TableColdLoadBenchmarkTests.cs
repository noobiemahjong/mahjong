using System.Diagnostics;
using System.Globalization;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;
using Mahjong.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Mahjong.Tests.Content.Tables;

/// <summary>
/// T-6 查表冷加载基准：每轮都新建一次 <see cref="TableCatalog.Load"/>，读入仓库 <c>content/tables</c> 的 6 个文件，
/// Stopwatch 计 3 轮取中位数，用 ITestOutputHelper 打印契约格式。口径是「新进程里的新 Load」：
/// 进程内第 1 轮是真正的冷启动，第 2/3 轮只复用 OS 文件缓存，不缓存已解析的表。
/// 不断言绝对耗时（只断言加载成功且耗时为正）。
/// </summary>
[Trait("Category", "Benchmark")]
[Collection("benchmark")]
public sealed class TableColdLoadBenchmarkTests
{
    private const int RoundCount = 3;

    private const int NormalWinKeyCount = 227_494;

    private const int NormalAiKeyCount = 286_550;

    private readonly ITestOutputHelper _output;

    public TableColdLoadBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void NewLoadPerRound_ReportsMedianMilliseconds()
    {
        var milliseconds = new double[RoundCount];

        for (int round = 0; round < RoundCount; round++)
        {
            long startedAt = Stopwatch.GetTimestamp();
            TableCatalog catalog = TableCatalog.Load(RepoPaths.TablesDirectory);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            milliseconds[round] = elapsed.TotalMilliseconds;

            Assert.Equal(NormalWinKeyCount, catalog.WinRowKeyCount(SuitType.Character));
            Assert.Equal(NormalAiKeyCount, catalog.AiRowKeyCount(SuitType.Character));
            Assert.True(milliseconds[round] > 0);

            _output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"M0.1-T6 table cold load round {round + 1}: {milliseconds[round]:F1} ms (new TableCatalog.Load)"));
        }

        double medianMilliseconds = Median(milliseconds);
        _output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"M0.1-T6 table cold load: {medianMilliseconds:F1} ms (median of 3)"));
    }

    private static double Median(double[] values)
    {
        double[] sorted = [.. values];
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }
}
