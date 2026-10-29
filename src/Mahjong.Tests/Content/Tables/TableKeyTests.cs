using System.Globalization;
using Mahjong.Core.Content.Tables;
using Xunit;

namespace Mahjong.Tests.Content.Tables;

/// <summary>TableKey.Encode：十进制拼接语义、长度 3/4/9 与每项 0–4 的入参校验。</summary>
public sealed class TableKeyTests
{
    [Fact]
    public void Encode_MatchesSpecExamples()
    {
        Assert.Equal(2UL, TableKey.Encode([0, 0, 2]));
        Assert.Equal(2UL, TableKey.Encode([0, 0, 0, 2]));
        Assert.Equal(1UL, TableKey.Encode([0, 0, 1]));
        Assert.Equal(1210220UL, TableKey.Encode([0, 0, 1, 2, 1, 0, 2, 2, 0]));
    }

    [Fact]
    public void Encode_DropsLeadingZeros()
    {
        Assert.Equal(0UL, TableKey.Encode(new byte[9]));
        Assert.Equal(1UL, TableKey.Encode([0, 0, 0, 0, 0, 0, 0, 0, 1]));
        Assert.Equal(100UL, TableKey.Encode([0, 0, 0, 0, 0, 0, 1, 0, 0]));
    }

    [Fact]
    public void Encode_AcceptsMaximumCounts()
    {
        Assert.Equal(444UL, TableKey.Encode([4, 4, 4]));
        Assert.Equal(4444UL, TableKey.Encode([4, 4, 4, 4]));
        Assert.Equal(444444444UL, TableKey.Encode([4, 4, 4, 4, 4, 4, 4, 4, 4]));
    }

    [Fact]
    public void Encode_EqualsDecimalConcatenation()
    {
        Random random = new(20261029);
        foreach (int length in new[] { 3, 4, 9 })
        {
            for (int trial = 0; trial < 50; trial++)
            {
                byte[] counts = new byte[length];
                random.NextBytes(counts);
                for (int index = 0; index < counts.Length; index++)
                {
                    counts[index] %= 5;
                }

                ulong expected = ulong.Parse(string.Concat(counts), CultureInfo.InvariantCulture);
                Assert.Equal(expected, TableKey.Encode(counts));
            }
        }
    }

    [Fact]
    public void Encode_RejectsUnsupportedLengths()
    {
        foreach (int length in new[] { 0, 1, 2, 5, 6, 7, 8, 10 })
        {
            byte[] counts = new byte[length];
            ArgumentException exception = Assert.Throws<ArgumentException>(() => { _ = TableKey.Encode(counts); });
            Assert.Contains("length", exception.Message, StringComparison.Ordinal);
            Assert.Equal("rankCounts", exception.ParamName);
        }
    }

    [Fact]
    public void Encode_ChecksLengthBeforeElementValues()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => { _ = TableKey.Encode([5]); });
        Assert.Contains("length", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Encode_RejectsCountsAboveFour()
    {
        foreach (int length in new[] { 3, 4, 9 })
        {
            for (int index = 0; index < length; index++)
            {
                foreach (byte bad in new byte[] { 5, 255 })
                {
                    byte[] counts = new byte[length];
                    counts[index] = bad;
                    ArgumentException exception = Assert.Throws<ArgumentException>(() => { _ = TableKey.Encode(counts); });
                    Assert.Contains($"index {index}", exception.Message, StringComparison.Ordinal);
                    Assert.Equal("rankCounts", exception.ParamName);
                }
            }
        }
    }
}
