using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// Pcg32 契约：PCG32 参考算法的实例状态实现；同 seed/stream 必得同序列，不同 seed/stream 改变序列；
/// 默认 stream 为 1；实例之间不共享状态。
/// </summary>
public sealed class Pcg32Tests
{
    [Fact]
    public void NextUInt_MatchesGoldenSequenceForSeed42Stream54()
    {
        var rng = new Pcg32(42, 54);

        Assert.Equal(RulesGoldenVectors.Pcg32Seed42Stream54, Take(rng, RulesGoldenVectors.Pcg32Seed42Stream54.Length));
    }

    [Fact]
    public void NextUInt_MatchesGoldenSequenceForSeed0Stream1()
    {
        var rng = new Pcg32(0, 1);

        Assert.Equal(RulesGoldenVectors.Pcg32Seed0Stream1, Take(rng, RulesGoldenVectors.Pcg32Seed0Stream1.Length));
    }

    [Fact]
    public void NextUInt_MatchesGoldenSequenceForSeed42Stream1()
    {
        var rng = new Pcg32(42, 1);

        Assert.Equal(RulesGoldenVectors.Pcg32Seed42Stream1, Take(rng, RulesGoldenVectors.Pcg32Seed42Stream1.Length));
    }

    [Fact]
    public void Constructor_DefaultStreamIsOne()
    {
        var withDefault = new Pcg32(42);
        var explicitOne = new Pcg32(42, 1);

        Assert.Equal(Take(withDefault, 16), Take(explicitOne, 16));
    }

    [Fact]
    public void NextUInt_IsDeterministicForSameSeedAndStream()
    {
        Assert.Equal(Take(new Pcg32(20261029, 7), 128), Take(new Pcg32(20261029, 7), 128));
    }

    [Fact]
    public void NextUInt_ChangesWithSeed()
    {
        Assert.False(Take(new Pcg32(42, 1), 16).SequenceEqual(Take(new Pcg32(43, 1), 16)));
    }

    [Fact]
    public void NextUInt_ChangesWithStream()
    {
        Assert.False(Take(new Pcg32(42, 1), 16).SequenceEqual(Take(new Pcg32(42, 2), 16)));
    }

    [Fact]
    public void Instances_DoNotShareState()
    {
        var first = new Pcg32(42, 54);
        var second = new Pcg32(42, 54);

        // 两个实例交错推进，各自序列必须与单独推进的参考实例一致。
        uint firstValue = first.NextUInt();
        uint secondValue = second.NextUInt();
        Assert.Equal(firstValue, secondValue);

        uint secondNext = second.NextUInt();
        uint firstNext = first.NextUInt();
        Assert.Equal(firstNext, secondNext);

        var reference = new Pcg32(42, 54);
        Assert.Equal(firstValue, reference.NextUInt());
        Assert.Equal(firstNext, reference.NextUInt());
        Assert.Equal(reference.NextUInt(), second.NextUInt());
    }

    [Fact]
    public void NextUInt_IsNotConstant()
    {
        uint[] values = Take(new Pcg32(7, 3), 64);

        Assert.True(values.Distinct().Count() > 1);
    }

    private static uint[] Take(IRandomSource rng, int count)
    {
        var values = new uint[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = rng.NextUInt();
        }

        return values;
    }
}
