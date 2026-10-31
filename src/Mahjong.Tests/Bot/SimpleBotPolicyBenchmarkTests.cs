using System.Diagnostics;
using Mahjong.Core.Bot;
using Mahjong.Core.Rules;
using Mahjong.Tests.Content.Tables;
using Xunit;
using Xunit.Abstractions;

namespace Mahjong.Tests.Bot;

/// <summary>
/// 单次 Decide 延迟门槛：真实发牌局面（自己回合、手牌 14、LastDrawn=手牌末张、可用技能为空），
/// 预热 200 次后采样 1000 次 <see cref="Stopwatch.GetTimestamp"/>；断言 p99 与中位数 &lt; 1 ms。
/// 只打印 max 不断言：CI 偶发调度抖动的单次尖峰不可重复，门槛只对分布主体生效。
/// 真实查表由 <see cref="TableCatalogFixture"/> 共享。
/// </summary>
[Collection("table-catalog")]
public sealed class SimpleBotPolicyBenchmarkTests
{
    private const int WarmupIterations = 200;
    private const int SampleIterations = 1000;
    private const double OneMillisecondNanoseconds = 1_000_000.0;

    private readonly TableCatalogFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SimpleBotPolicyBenchmarkTests(TableCatalogFixture fixture, ITestOutputHelper output)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(output);
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Decide_RealDealtPosition_P99AndMedianUnderOneMillisecond()
    {
        var wins = new WinChecker(_fixture.Catalog);
        var verifier = new CardAndSkillVerifier(wins, BotTestSupport.Joker);
        var policy = new SimpleBotPolicy(wins, verifier);

        GameState state = BotTestSupport.NewState();
        state.Deal(new Pcg32(20261029), bankerSeat: 0);
        state.DrawFromWall(0);
        PlayerState self = state.Players[0];
        Tile lastDrawn = self.Hand[self.Hand.Count - 1];
        var context = new BotContext(state, self, [], new Pcg32(1), lastDrawn: lastDrawn);

        BotDecision firstDecision = policy.Decide(context);
        Assert.Null(firstDecision.Skill);
        Assert.True(firstDecision.Card.HasValue);

        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = policy.Decide(context);
        }

        long[] samples = new long[SampleIterations];
        for (int iteration = 0; iteration < SampleIterations; iteration++)
        {
            long start = Stopwatch.GetTimestamp();
            _ = policy.Decide(context);
            samples[iteration] = Stopwatch.GetTimestamp() - start;
        }

        Array.Sort(samples);
        double nanosecondsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;
        double medianNs =
            ((samples[(SampleIterations / 2) - 1] + samples[SampleIterations / 2]) / 2.0) * nanosecondsPerTick;
        int p99Index = (int)Math.Ceiling(SampleIterations * 0.99) - 1;
        double p99Ns = samples[p99Index] * nanosecondsPerTick;
        double maxNs = samples[SampleIterations - 1] * nanosecondsPerTick;

        _output.WriteLine(
            $"SimpleBotPolicy.Decide 真实发牌局面 warmup={WarmupIterations} samples={SampleIterations} "
            + $"median={medianNs:F1} ns p99={p99Ns:F1} ns max={maxNs:F1} ns");

        Assert.True(medianNs < OneMillisecondNanoseconds, $"median {medianNs:F1} ns is not under 1 ms");
        Assert.True(p99Ns < OneMillisecondNanoseconds, $"p99 {p99Ns:F1} ns is not under 1 ms");
    }
}
