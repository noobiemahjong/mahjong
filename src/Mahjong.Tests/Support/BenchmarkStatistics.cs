namespace Mahjong.Tests.Support;

/// <summary>
/// 基准统计：把已升序排列的样本换算成百分位（nearest-rank），供 p99/p99.9 与中位数使用。
/// 不复制、不排序入参；null/空样本与非 (0, 1] 百分位直接抛异常，不返回哨兵值。
/// </summary>
internal static class BenchmarkStatistics
{
    /// <summary>nearest-rank 百分位：<c>index = ceil(p * n) - 1</c>，clamp 到 [0, n-1]。</summary>
    /// <param name="sortedAscending">已升序排列的样本；不得为 null 或空。</param>
    /// <param name="percentile">百分位，取值 (0, 1]；0、负数、NaN 与大于 1 都抛异常。</param>
    /// <returns>对应样本值。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sortedAscending"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="sortedAscending"/> 为空。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="percentile"/> 不在 (0, 1]。</exception>
    public static double Percentile(IReadOnlyList<double> sortedAscending, double percentile)
    {
        ArgumentNullException.ThrowIfNull(sortedAscending);
        if (sortedAscending.Count == 0)
        {
            throw new ArgumentException("样本不能为空。", nameof(sortedAscending));
        }

        if (!(percentile > 0.0) || percentile > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "百分位必须在 (0, 1] 内。");
        }

        int index = (int)Math.Ceiling(percentile * sortedAscending.Count) - 1;
        return sortedAscending[Math.Clamp(index, 0, sortedAscending.Count - 1)];
    }

    /// <summary>中位数 = p50（nearest-rank；奇数样本取正中项，偶数样本取下中位数）。</summary>
    /// <param name="sortedAscending">已升序排列的样本；不得为 null 或空。</param>
    /// <returns>对应样本值。</returns>
    public static double Median(IReadOnlyList<double> sortedAscending) => Percentile(sortedAscending, 0.5);
}
