using Xunit;

namespace Mahjong.Tests.Support;

/// <summary>
/// MemoryGrowthGuard 的判死闸：相等与 5% 增长通过，2 倍增长抛；边界值恰好等于允许额度通过、
/// 再超 1 字节抛。阈值公式与边界都由用户可手算的常量锁定，反转比较符或改额度会立即变红。
/// </summary>
public sealed class MemoryGrowthGuardTests
{
    private const long Mib = 1024 * 1024;

    [Fact]
    public void Allowance_IsQuarterOfFirstPeakPlusFourMebibytes()
    {
        Assert.Equal((100 * Mib / 4) + (4 * Mib), MemoryGrowthGuard.Allowance(100 * Mib));
        Assert.Equal(4 * Mib, MemoryGrowthGuard.Allowance(0));
    }

    [Fact]
    public void EqualPeaks_Pass()
    {
        MemoryGrowthGuard.AssertNoSustainedGrowth(100 * Mib, 100 * Mib);
    }

    [Fact]
    public void FivePercentGrowth_Pass()
    {
        MemoryGrowthGuard.AssertNoSustainedGrowth(100 * Mib, 105 * Mib);
    }

    [Fact]
    public void DoubledPeak_Throws()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => MemoryGrowthGuard.AssertNoSustainedGrowth(100 * Mib, 200 * Mib));
        Assert.Contains("firstPeak=" + (100 * Mib), exception.Message, StringComparison.Ordinal);
        Assert.Contains("secondPeak=" + (200 * Mib), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactlyAtAllowanceBoundary_Pass()
    {
        long firstPeak = 100 * Mib;
        MemoryGrowthGuard.AssertNoSustainedGrowth(firstPeak, firstPeak + MemoryGrowthGuard.Allowance(firstPeak));
    }

    [Fact]
    public void OneByteAboveAllowanceBoundary_Throws()
    {
        long firstPeak = 100 * Mib;
        Assert.Throws<InvalidOperationException>(
            () => MemoryGrowthGuard.AssertNoSustainedGrowth(firstPeak, firstPeak + MemoryGrowthGuard.Allowance(firstPeak) + 1));
    }
}
