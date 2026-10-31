using Xunit;

namespace Mahjong.Tests.Support;

/// <summary>
/// BenchmarkStatistics 的判死闸：用可手算的 1..100 精确断言 p50/p99/p100，覆盖单元素、百分位下界
/// 与非法入参。这些期望值就是 tick p99 基准的算法规格，分位实现偏移或 clamp 写错会立即变红。
/// </summary>
public sealed class BenchmarkStatisticsTests
{
    private static readonly double[] Hundred = Enumerable.Range(1, 100).Select(value => (double)value).ToArray();

    [Fact]
    public void Percentile_OneToHundred_MatchesNearestRank()
    {
        Assert.Equal(50.0, BenchmarkStatistics.Percentile(Hundred, 0.50));
        Assert.Equal(99.0, BenchmarkStatistics.Percentile(Hundred, 0.99));
        Assert.Equal(100.0, BenchmarkStatistics.Percentile(Hundred, 1.0));
    }

    [Fact]
    public void Percentile_SingleElement_ReturnsThatElement()
    {
        IReadOnlyList<double> single = [42.5];
        Assert.Equal(42.5, BenchmarkStatistics.Percentile(single, 0.001));
        Assert.Equal(42.5, BenchmarkStatistics.Percentile(single, 0.5));
        Assert.Equal(42.5, BenchmarkStatistics.Percentile(single, 1.0));
    }

    [Fact]
    public void Percentile_JustAboveZero_ClampsToFirstElement()
    {
        // ceil(0.005 * 100) - 1 = -1，clamp 到 0；再往上一点进入第 2 个样本，证明边界不是恒真。
        Assert.Equal(1.0, BenchmarkStatistics.Percentile(Hundred, 0.005));
        Assert.Equal(1.0, BenchmarkStatistics.Percentile(Hundred, 0.01));
        Assert.Equal(2.0, BenchmarkStatistics.Percentile(Hundred, 0.0101));
    }

    [Fact]
    public void Median_MatchesP50()
    {
        Assert.Equal(2.0, BenchmarkStatistics.Median([1.0, 2.0, 3.0]));
        Assert.Equal(1.0, BenchmarkStatistics.Median([1.0, 2.0]));
    }

    [Fact]
    public void Percentile_NullSamples_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BenchmarkStatistics.Percentile(null!, 0.5));
        Assert.Throws<ArgumentNullException>(() => BenchmarkStatistics.Median(null!));
    }

    [Fact]
    public void Percentile_EmptySamples_Throws()
    {
        Assert.Throws<ArgumentException>(() => BenchmarkStatistics.Percentile(Array.Empty<double>(), 0.5));
    }

    [Fact]
    public void Percentile_OutOfRangePercentile_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkStatistics.Percentile(Hundred, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkStatistics.Percentile(Hundred, -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkStatistics.Percentile(Hundred, 1.0001));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkStatistics.Percentile(Hundred, double.NaN));
    }
}
