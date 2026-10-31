using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Flow;

/// <summary>
/// 整场流程：四 Bot 自动走完 8 局、终局 payload 与 IsGameOver、终局后 Step/Submit 行为、
/// 同 seed 同脚本两遍的状态哈希与事件流逐字节一致。
/// </summary>
[Collection("table-catalog")]
public sealed class GameSessionGameLoopTests
{
    private readonly TableCatalogFixture _fixture;

    public GameSessionGameLoopTests(TableCatalogFixture fixture) => _fixture = fixture;

    [Fact]
    public void FourBots_CompleteEightRoundsAndEmitGameEnded()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 7);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());

        Assert.True(session.IsGameOver);
        IReadOnlyList<SessionEvent> events = driver.Events;
        Assert.Equal(8, events.Count(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded));
        SessionEvent gameEnded = Assert.Single(events, sessionEvent => sessionEvent.Kind == SessionEventKind.GameEnded);
        Assert.Equal(8, events.Count(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted));

        GameEndedPayload payload = SessionEventPayloadReader.ReadGameEnded(gameEnded);
        Assert.Equal(4, payload.TotalScores.Length);
        Assert.Equal(4, payload.Ranking.Length);
        Assert.Equal(0, payload.TotalScores.Sum());
        Assert.Equal(4, payload.Ranking.Distinct().Count());

        // 排名与总分一致：总分降序、同分座位升序。
        short[] totals = payload.TotalScores.Select(value => (short)value).ToArray();
        byte[] expectedRanking = RoundRanking.Rank(totals).ToArray();
        Assert.Equal(expectedRanking, payload.Ranking);
        Assert.Equal(string.Empty, payload.ReplayId);

        // 第 8 局结束后总分写回镜像，且与 GameEnded 的总分一致。
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            Assert.Equal(payload.TotalScores[seat], (int)session.State.Players[seat].Score);
        }

        // 每局结算后四座 delta 和为 0。
        foreach (SessionEvent roundEnded in events.Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded))
        {
            RoundEndedPayload round = SessionEventPayloadReader.ReadRoundEnded(roundEnded);
            Assert.Equal(0, round.ScoreDelta.Sum());
            Assert.InRange(round.WinnerSeat, -1, 3);
        }
    }

    [Fact]
    public void AfterGameEnded_StepProducesNoEventsAndSubmitIsRejected()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 11);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());
        session.DrainEvents(255);

        long tick = session.Tick;
        session.Step(tick + 1);

        Assert.Equal(tick + 1, session.Tick);
        Assert.Empty(session.DrainEvents(255));
        Assert.Empty(session.ActiveRequests);
        SubmitResult rejected = session.Submit(SessionTestSupport.Command(0, CommandVerb.Discard, 0));
        Assert.False(rejected.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(rejected.Reason));
        Assert.Equal(0, rejected.RequestId);
    }

    [Fact]
    public void SameSeedAndScript_ProducesIdenticalStateHashAndEventStream()
    {
        (string hash, string events) first = RunToEnd(seed: 21);
        (string hash, string events) second = RunToEnd(seed: 21);

        Assert.Equal(first.hash, second.hash);
        Assert.Equal(first.events, second.events);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentEventStreams()
    {
        (string hash, string events) first = RunToEnd(seed: 21);
        (string hash, string events) second = RunToEnd(seed: 22);

        Assert.NotEqual(first.hash, second.hash);
        Assert.NotEqual(first.events, second.events);
    }

    [Fact]
    public void TileCount_IsConservedThroughTheWholeGame()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 3);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);

        driver.StepOnce();
        while (!session.IsGameOver)
        {
            Assert.Equal(136, SessionTestSupport.CountTiles(session.State));
            driver.StepOnce();
        }

        Assert.Equal(136, SessionTestSupport.CountTiles(session.State));
    }

    private (string Hash, string Events) RunToEnd(ulong seed)
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());
        return (SessionTestSupport.Hash(session), Serialize(driver.Events));
    }

    private static string Serialize(IReadOnlyList<SessionEvent> events)
    {
        var builder = new System.Text.StringBuilder();
        foreach (SessionEvent sessionEvent in events)
        {
            builder
                .Append(sessionEvent.Seq).Append('|')
                .Append(sessionEvent.Tick).Append('|')
                .Append((byte)sessionEvent.Kind).Append('|')
                .Append(Convert.ToHexString(sessionEvent.Payload.Span))
                .Append('\n');
        }

        return builder.ToString();
    }
}
