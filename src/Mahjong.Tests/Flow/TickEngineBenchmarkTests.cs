using System.Diagnostics;
using System.Globalization;
using Mahjong.Core.Flow;
using Mahjong.Tests.Flow.Support;
using Xunit;
using Xunit.Abstractions;

namespace Mahjong.Tests.Flow;

/// <summary>
/// T-5 tick 微基准：只有 Continue 的动作树跑到 1,000,000 次 MoveNext（maxStepsPerTick 恰好设成该值，
/// 第 1,000,001 次前进前按防死循环约定抛 InvalidOperationException，这正是计数边界）。
/// Stopwatch 计 3 轮、取中位数，用 ITestOutputHelper 打印契约格式；不断言易抖动的绝对耗时。
/// </summary>
[Trait("Category", "Benchmark")]
public sealed class TickEngineBenchmarkTests
{
    private const int StepsPerRound = 1_000_000;
    private const int RoundCount = 3;

    private readonly ITestOutputHelper _output;

    public TickEngineBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ContinueOnlyTree_OneMillionMoveNext_ReportsNanosecondsPerStep()
    {
        var nanosecondsPerStep = new double[RoundCount];
        var totalMilliseconds = new double[RoundCount];

        for (int round = 0; round < RoundCount; round++)
        {
            var routine = new ContinueRoutine();
            ActionScheduler scheduler = FlowTestData.CreateScheduler(maxStepsPerTick: StepsPerRound);
            GameAction action = scheduler.CreateAction("bench", routine);
            scheduler.Start(action);

            long startedAt = Stopwatch.GetTimestamp();
            Assert.Throws<InvalidOperationException>(() => scheduler.Step(0));
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);

            Assert.Equal(StepsPerRound, routine.StepCount);
            nanosecondsPerStep[round] = elapsed.TotalNanoseconds / StepsPerRound;
            totalMilliseconds[round] = elapsed.TotalMilliseconds;
            _output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"M0.1-T5 tick bench round {round + 1}: {nanosecondsPerStep[round]:F2} ns/step ({totalMilliseconds[round]:F2} ms, {routine.StepCount} steps)"));
        }

        double medianNsPerStep = Median(nanosecondsPerStep);
        double medianTotalMs = Median(totalMilliseconds);

        _output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"M0.1-T5 tick bench: {medianNsPerStep:F2} ns/step (median of 3; total {medianTotalMs:F2} ms)"));
        Assert.All(nanosecondsPerStep, value => Assert.True(value > 0));
    }

    private static double Median(double[] values)
    {
        double[] sorted = [.. values];
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    /// <summary>最小开销的无限 Continue 枚举器：只做步数自增与返回，避免包装层影响测量。</summary>
    private sealed class ContinueRoutine : IEnumerator<ActionState>
    {
        public int StepCount { get; private set; }

        public ActionState Current => ActionState.Continue;

        object System.Collections.IEnumerator.Current => ActionState.Continue;

        public bool MoveNext()
        {
            StepCount++;
            return true;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
