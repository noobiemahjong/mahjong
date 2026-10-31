using Mahjong.Core.Content.Tables;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Integration;

/// <summary>
/// 两会话交错隔离：不同 seed 的两个会话交替 Step，终局 StateHash/总分/事件流与各自单独运行逐字节一致；
/// 每个会话的事件 seq 从 1 连续，Drain 只取本实例缓冲区，互不串号。
/// </summary>
/// <remarks>
/// seed 101/202 由四 Bot 扫描选定（各含 1 个赢家局）；隔离/等价断言与胜负无关，选种只为覆盖赢家结算事件。
/// </remarks>
[Collection("table-catalog")]
public sealed class InterleavedSessionTests
{
    private readonly TableCatalogFixture _fixture;

    public InterleavedSessionTests(TableCatalogFixture fixture) => _fixture = fixture;

    [Fact]
    public void TwoInterleavedSessions_MatchStandaloneRunsAndDoNotShareEventBuffers()
    {
        const ulong seedA = 101;
        const ulong seedB = 202;

        IntegrationRun standaloneA = IntegrationSupport.RunFourBots(_fixture.Catalog, seedA);
        IntegrationRun standaloneB = IntegrationSupport.RunFourBots(_fixture.Catalog, seedB);
        Assert.True(standaloneA.GameOver);
        Assert.True(standaloneB.GameOver);
        Assert.NotEqual(standaloneA.SerializedEvents, standaloneB.SerializedEvents);

        GameSession sessionA = SessionTestSupport.CreateFourBots(_fixture.Catalog, seedA);
        GameSession sessionB = SessionTestSupport.CreateFourBots(_fixture.Catalog, seedB);
        sessionA.Start();
        sessionB.Start();

        List<SessionEvent> eventsA = [.. sessionA.DrainEvents(IntegrationSupport.FullViewSeat)];
        List<SessionEvent> eventsB = [.. sessionB.DrainEvents(IntegrationSupport.FullViewSeat)];
        Assert.NotEmpty(eventsA);
        Assert.NotEmpty(eventsB);

        int steps = 0;
        while (!sessionA.IsGameOver || !sessionB.IsGameOver)
        {
            Assert.True(steps++ < IntegrationSupport.DefaultMaxSteps, "交错驱动未在最大步数内结束。");

            if (!sessionA.IsGameOver)
            {
                sessionA.Step(sessionA.Tick + 1);

                // B 的缓冲区不受 A 的 Step 与 Drain 影响。
                Assert.Empty(sessionB.DrainEvents(IntegrationSupport.FullViewSeat));
                eventsA.AddRange(sessionA.DrainEvents(IntegrationSupport.FullViewSeat));
            }

            if (!sessionB.IsGameOver)
            {
                sessionB.Step(sessionB.Tick + 1);
                Assert.Empty(sessionA.DrainEvents(IntegrationSupport.FullViewSeat));
                eventsB.AddRange(sessionB.DrainEvents(IntegrationSupport.FullViewSeat));
            }
        }

        Assert.Empty(sessionA.DrainEvents(IntegrationSupport.FullViewSeat));
        Assert.Empty(sessionB.DrainEvents(IntegrationSupport.FullViewSeat));

        // 各自 seq 从 1 连续、互不串。
        IntegrationSupport.AssertContiguousSeq(eventsA);
        IntegrationSupport.AssertContiguousSeq(eventsB);

        // 与各自单独运行逐字节一致（事件流、终态哈希、总分镜像）。
        Assert.Equal(standaloneA.SerializedEvents, IntegrationSupport.SerializeEventStream(eventsA));
        Assert.Equal(standaloneB.SerializedEvents, IntegrationSupport.SerializeEventStream(eventsB));
        Assert.Equal(standaloneA.FinalStateHash, SessionTestSupport.Hash(sessionA));
        Assert.Equal(standaloneB.FinalStateHash, SessionTestSupport.Hash(sessionB));
        Assert.Equal(standaloneA.MirrorScores, IntegrationSupport.MirrorScores(sessionA));
        Assert.Equal(standaloneB.MirrorScores, IntegrationSupport.MirrorScores(sessionB));
    }
}
