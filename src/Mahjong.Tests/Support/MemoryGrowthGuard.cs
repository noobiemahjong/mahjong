namespace Mahjong.Tests.Support;

/// <summary>
/// 连续两场内存增长守卫：第二场托管堆采样峰值允许超过第一场的上限是 <c>firstPeak / 4 + 4 MiB</c>。
/// 只用于 <see cref="GC.GetTotalMemory(bool)"/> 采样峰值对比；超限抛异常而不是返回布尔值，
/// 调用方不能忽略判定结果。
/// </summary>
internal static class MemoryGrowthGuard
{
    /// <summary>允许的增长额度（字节）：首场峰值的 1/4 再加 4 MiB（整数除法向下取整）。</summary>
    /// <param name="firstPeak">第一场峰值字节数。</param>
    /// <returns>允许第二场超过第一场的字节数。</returns>
    public static long Allowance(long firstPeak) => (firstPeak / 4) + (4 * 1024 * 1024);

    /// <summary>断言第二场峰值未超过「第一场峰值 + 允许额度」；相等算通过。</summary>
    /// <param name="firstPeak">第一场峰值字节数。</param>
    /// <param name="secondPeak">第二场峰值字节数。</param>
    /// <exception cref="InvalidOperationException">第二场峰值超限；消息携带两个峰值与允许额度。</exception>
    public static void AssertNoSustainedGrowth(long firstPeak, long secondPeak)
    {
        long allowance = Allowance(firstPeak);
        long allowedPeak = firstPeak + allowance;
        if (secondPeak > allowedPeak)
        {
            throw new InvalidOperationException(
                $"托管堆峰值持续增长：firstPeak={firstPeak} secondPeak={secondPeak} "
                + $"growth={secondPeak - firstPeak} allowance={allowance} allowedPeak={allowedPeak}。");
        }
    }
}
