using System.Diagnostics;
using System.Globalization;
using Mahjong.Core.Content;
using Mahjong.Core.Content.Tables;
using Mahjong.Session;
using Mahjong.Tests.Session.Support;
using Mahjong.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Mahjong.Tests.Session.Integration;

/// <summary>
/// M0.2 T-7 内存基准：同一进程连续两场完整 8 局四 Bot（同 seed、tickRate 10），
/// 每 25 tick 采样一次 <see cref="GC.GetTotalMemory(bool)"/>（不强制回收）记录本场峰值，
/// 第一场结束后清空会话引用并两次 GC，再跑第二场，最后用 <see cref="MemoryGrowthGuard"/> 判「无持续增长」。
/// 口径只含托管堆采样峰值，不含 GPU/引擎/进程私有内存；25 tick 的采样间隔会漏掉更短的瞬时峰值。
/// 另外读取 <see cref="Process.PeakWorkingSet64"/> 作为进程级参考，不参与断言。
/// </summary>
[Trait("Category", "Benchmark")]
[Collection("benchmark")]
public sealed class FourBotsMemoryBenchmarkTests
{
    private const ulong Seed = 20261031UL;
    private const int TickRate = 10;
    private const int SampleIntervalTicks = 25;
    private const int ExpectedRoundCount = 8;
    private const int MaxStepsPerGame = 200_000;

    private readonly ITestOutputHelper _output;

    public FourBotsMemoryBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TwoConsecutiveEightRoundGames_ManagedHeapPeak_ShowsNoSustainedGrowth()
    {
        ContentCatalog content = ContentCatalog.Load(RepoPaths.ContentDirectory);
        TableCatalog tables = TableCatalog.Load(RepoPaths.TablesDirectory);

        GameSession? firstSession = LocalSessionFactory.CreateFourBots(content, tables, Seed, TickRate);
        GameMeasurement first = DriveGame(firstSession!);
        firstSession = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        GameSession? secondSession = LocalSessionFactory.CreateFourBots(content, tables, Seed, TickRate);
        GameMeasurement second = DriveGame(secondSession!);
        secondSession = null;

        Assert.True(first.GameOver, "第一场必须跑满 8 局产出 GameEnded。");
        Assert.True(second.GameOver, "第二场必须跑满 8 局产出 GameEnded。");
        Assert.Equal(ExpectedRoundCount, first.RoundEndedCount);
        Assert.Equal(ExpectedRoundCount, second.RoundEndedCount);

        long workingSet = Process.GetCurrentProcess().PeakWorkingSet64;
        _output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"M0.2-T7 memory: game1Peak={first.ManagedPeak} game2Peak={second.ManagedPeak} "
            + $"growth={second.ManagedPeak - first.ManagedPeak} "
            + $"allowance={MemoryGrowthGuard.Allowance(first.ManagedPeak)} workingSet={workingSet}"));

        MemoryGrowthGuard.AssertNoSustainedGrowth(first.ManagedPeak, second.ManagedPeak);
    }

    /// <summary>启动会话并步进到 8 局结束，按 25 tick 的节奏采样托管堆峰值并统计 RoundEnded 事件数。</summary>
    /// <param name="session">未启动的四 Bot 会话。</param>
    /// <returns>本场的结束标记、RoundEnded 计数与托管堆采样峰值。</returns>
    /// <exception cref="InvalidOperationException">超过最大步数仍未结束（流程未收敛）。</exception>
    private static GameMeasurement DriveGame(GameSession session)
    {
        session.Start();
        long managedPeak = GC.GetTotalMemory(forceFullCollection: false);
        int roundEndedCount = 0;
        int steps = 0;

        while (!session.IsGameOver)
        {
            if (steps >= MaxStepsPerGame)
            {
                throw new InvalidOperationException($"四 Bot 会话在 {MaxStepsPerGame} 步内未产出 GameEnded。");
            }

            session.Step(session.Tick + 1);
            steps++;

            foreach (SessionEvent sessionEvent in session.DrainEvents(SessionTestSupport.FullViewSeat))
            {
                if (sessionEvent.Kind == SessionEventKind.RoundEnded)
                {
                    roundEndedCount++;
                }
            }

            if (steps % SampleIntervalTicks == 0)
            {
                managedPeak = Math.Max(managedPeak, GC.GetTotalMemory(forceFullCollection: false));
            }
        }

        managedPeak = Math.Max(managedPeak, GC.GetTotalMemory(forceFullCollection: false));
        return new GameMeasurement(session.IsGameOver, roundEndedCount, managedPeak);
    }

    /// <summary>一场的度量结果：是否结束、RoundEnded 事件数、托管堆采样峰值（字节）。</summary>
    private readonly record struct GameMeasurement(bool GameOver, int RoundEndedCount, long ManagedPeak);
}
