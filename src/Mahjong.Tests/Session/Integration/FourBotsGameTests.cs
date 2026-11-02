using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Integration;

/// <summary>
/// 四 Bot 8 局端到端：CreateFourBots 自动走完；断言 8 局结构、终局总分自洽、名次为总分降序且同分座位升序、
/// 换庄与每步牌数守恒；每个 seed 必须出现赢家局与终局非零总分，赢家结算金额为正且无杠位局精确。
/// 名次用测试侧独立排序计算，不复用被断言的生产排序实现。
/// </summary>
/// <remarks>
/// seed 由一次性四 Bot 扫描选定（3/5 全流局的 seed 不用于本用例）：7 有明杠+杠上开花赢家局（只做零和/符号断言），
/// 101 出现赢家为正、非赢家也为正的杠分分配，2024 是无杠位标准自摸精确金额局。用例内断言
/// 「winnerRounds ≥ 1 且总分非全 0」守护选种前置，避免赢家/金额断言在流局种子上空转。
/// </remarks>
[Collection("table-catalog")]
public sealed class FourBotsGameTests
{
    private readonly TableCatalogFixture _fixture;

    public FourBotsGameTests(TableCatalogFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(7)]
    [InlineData(101)]
    [InlineData(2024)]
    public void FourBots_CompletesEightRoundsWithLegalTotalsAndRanking(int seed)
    {
        IntegrationRun run = IntegrationSupport.RunFourBots(_fixture.Catalog, (ulong)seed);

        Assert.True(run.GameOver, "四 Bot 会话必须在最大步数内产出 GameEnded。");
        Assert.True(run.Session.IsGameOver);
        Assert.True(run.StepCount < IntegrationSupport.DefaultMaxSteps);

        // 每步牌数守恒。
        Assert.Equal(run.StepCount, run.TileCounts.Count);
        Assert.All(run.TileCounts, count => Assert.Equal(IntegrationSupport.TotalTileCount, count));

        // 8 局结构。
        RoundStartedPayload[] rounds = run.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted)
            .Select(SessionEventPayloadReader.ReadRoundStarted)
            .ToArray();
        Assert.Equal(8, rounds.Length);
        Assert.Equal(Enumerable.Range(1, 8).Select(roundNo => (byte)roundNo), rounds.Select(round => round.RoundNo));
        Assert.All(rounds, round => Assert.Equal((ushort)84, round.WallCount));
        Assert.Equal(8, run.Events.Count(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded));
        SessionEvent gameEndedEvent = Assert.Single(
            run.Events,
            sessionEvent => sessionEvent.Kind == SessionEventKind.GameEnded);
        Assert.Equal(SessionEventKind.GameEnded, run.Events[^1].Kind);
        Assert.DoesNotContain(run.Events, sessionEvent => sessionEvent.Kind == SessionEventKind.HandSync);

        GameEndedPayload gameEnded = SessionEventPayloadReader.ReadGameEnded(gameEndedEvent);
        Assert.Equal(GameState.PlayerCount, gameEnded.TotalScores.Length);
        Assert.Equal(0, gameEnded.TotalScores.Sum());

        // 选种前置：每个 seed 至少 1 个赢家局、终局总分非全 0，否则下面的赢家/金额断言会空转。
        RoundEndedPayload[] settlements = run.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)
            .Select(SessionEventPayloadReader.ReadRoundEnded)
            .ToArray();
        Assert.Contains(settlements, round => round.WinnerSeat >= 0);
        Assert.Contains(gameEnded.TotalScores, score => score != 0);

        // 每局结算：零和、赢家自摸置位且金额为正、无杠位局精确核算（含跟庄/连庄奖励）、流局全 0 + None。
        Assert.Equal(rounds.Length, settlements.Length);
        for (int index = 0; index < settlements.Length; index++)
        {
            IntegrationSupport.AssertSelfDrawnSettlement(
                settlements[index],
                rounds[index],
                SessionTestSupport.Content.Variants);
        }

        // 总分三口径一致。
        int[] accumulated = IntegrationSupport.AccumulateRoundDeltas(run.Events);
        Assert.Equal(accumulated, gameEnded.TotalScores);
        Assert.Equal(accumulated, run.MirrorScores.Select(score => (int)score).ToArray());

        // 名次合法：总分降序、同分座位号升序（独立排序，不复用 RoundRanking）。
        byte[] expectedRanking = Enumerable.Range(0, GameState.PlayerCount)
            .OrderByDescending(seat => gameEnded.TotalScores[seat])
            .ThenBy(seat => seat)
            .Select(seat => (byte)seat)
            .ToArray();
        Assert.Equal(expectedRanking, gameEnded.Ranking);
        Assert.Equal(
            Enumerable.Range(0, GameState.PlayerCount).Select(seat => (byte)seat).Order(),
            gameEnded.Ranking.Order());
        Assert.Equal(string.Empty, gameEnded.ReplayId);

        IntegrationSupport.AssertBankerRotation(
            IntegrationSupport.RoundBankers(run.Events),
            IntegrationSupport.RoundWinners(run.Events));

        // 终局后 Step 只推进时钟：不再产事件、不改局面与总分。
        string hash = SessionTestSupport.Hash(run.Session);
        short[] scores = IntegrationSupport.MirrorScores(run.Session);
        long tick = run.Session.Tick;
        run.Session.Step(tick + 1);
        Assert.Equal(tick + 1, run.Session.Tick);
        Assert.Equal(hash, SessionTestSupport.Hash(run.Session));
        Assert.Equal(scores, IntegrationSupport.MirrorScores(run.Session));
        Assert.Empty(run.Session.DrainEvents(255));
        Assert.Empty(run.Session.ActiveRequests);
    }
}
