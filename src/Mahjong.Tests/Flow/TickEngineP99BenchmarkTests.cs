using System.Diagnostics;
using System.Globalization;
using Mahjong.Core.Flow;
using Mahjong.Tests.Flow.Support;
using Mahjong.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Mahjong.Tests.Flow;

/// <summary>
/// M0.2 T-7 tick p99 基准：复用 T-5 的 Continue-only 动作树形态（1,000,000 次 MoveNext，
/// <c>maxStepsPerTick</c> 恰为该值，第 1,000,001 次前进前抛 <see cref="InvalidOperationException"/>），
/// 每次 MoveNext 前后各取一次 <see cref="Stopwatch.GetTimestamp"/> 把纳秒差值写入样本数组，3 轮取各轮分位后中位 p99。
/// 单步样本含两次 GetTimestamp 调用开销，因此数字是步耗时的上界；阈值按规格固定 1 ms，Debug 本地与 Release CI 都可能跑。
/// 时间戳精度受平台计时器限制（Windows QPC 约 100 ns），单步常被量化成 0 或 1 个计时 tick，p50 可能为 0，分位数字按计时器精度解读。
/// </summary>
[Trait("Category", "Benchmark")]
[Collection("benchmark")]
public sealed class TickEngineP99BenchmarkTests
{
    private const int StepsPerRound = 1_000_000;
    private const int RoundCount = 3;
    private const double ThresholdNanoseconds = 1_000_000.0;

    private readonly ITestOutputHelper _output;

    public TickEngineP99BenchmarkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ContinueOnlyTree_OneMillionTimedSteps_ReportsPercentilesAndAssertsP99UnderOneMillisecond()
    {
        var p50 = new double[RoundCount];
        var p99 = new double[RoundCount];
        var p999 = new double[RoundCount];
        var mean = new double[RoundCount];
        var max = new double[RoundCount];
        double nanosecondsPerTimestamp = 1_000_000_000.0 / Stopwatch.Frequency;

        for (int round = 0; round < RoundCount; round++)
        {
            var samples = new double[StepsPerRound];
            var routine = new TimedContinueRoutine(samples, nanosecondsPerTimestamp);
            ActionScheduler scheduler = FlowTestData.CreateScheduler(maxStepsPerTick: StepsPerRound);
            GameAction action = scheduler.CreateAction("bench", routine);
            scheduler.Start(action);

            Assert.Throws<InvalidOperationException>(() => scheduler.Step(0));
            Assert.Equal(StepsPerRound, routine.StepCount);

            Array.Sort(samples);
            p50[round] = BenchmarkStatistics.Percentile(samples, 0.50);
            p99[round] = BenchmarkStatistics.Percentile(samples, 0.99);
            p999[round] = BenchmarkStatistics.Percentile(samples, 0.999);
            mean[round] = samples.Sum() / StepsPerRound;
            max[round] = samples[^1];

            _output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"M0.2-T7 tick p99 round {round + 1}: p50={p50[round]:F1} p99={p99[round]:F1} p99.9={p999[round]:F1} ns ({StepsPerRound} steps)"));
            _output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"M0.2-T7 tick p99 round {round + 1} extras: mean={mean[round]:F1} max={max[round]:F1} ns"));

            Assert.True(
                p99[round] < ThresholdNanoseconds,
                $"第 {round + 1} 轮 p99={p99[round]:F1} ns 必须小于 {ThresholdNanoseconds:F0} ns（1 ms）。");
        }

        _output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"M0.2-T7 tick p99: {BenchmarkStatistics.Median(p99):F1} ns (median of 3 rounds; threshold 1000000 ns)"));
    }

    /// <summary>最小开销的无限 Continue 枚举器：每次 MoveNext 前后取时间戳，把本步纳秒值写入调用方提供的样本数组。</summary>
    private sealed class TimedContinueRoutine : IEnumerator<ActionState>
    {
        private readonly double[] _samples;
        private readonly double _nanosecondsPerTimestamp;
        private int _index = -1;

        public TimedContinueRoutine(double[] samples, double nanosecondsPerTimestamp)
        {
            _samples = samples;
            _nanosecondsPerTimestamp = nanosecondsPerTimestamp;
        }

        /// <summary>调度器实际调用的 MoveNext 次数（含返回 false 的那次，本树恒为 true）。</summary>
        public int StepCount { get; private set; }

        public ActionState Current => ActionState.Continue;

        object System.Collections.IEnumerator.Current => ActionState.Continue;

        public bool MoveNext()
        {
            long startedAt = Stopwatch.GetTimestamp();
            StepCount++;
            long finishedAt = Stopwatch.GetTimestamp();
            _samples[_index += 1] = (finishedAt - startedAt) * _nanosecondsPerTimestamp;
            return true;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
