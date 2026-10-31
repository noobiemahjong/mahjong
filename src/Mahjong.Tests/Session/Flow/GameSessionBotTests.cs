using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Flow;

/// <summary>
/// 本地组装与 Bot 驱动：工厂产物、座位状态、Bot 延迟 = max(1, ceil(TickRate×300/1000))、
/// Bot 走与人类相同的提交路径（RequestClosed Answered 携带应答命令）、同 tick 多请求按 requestId 升序结算。
/// </summary>
[Collection("table-catalog")]
public sealed class GameSessionBotTests
{
    private readonly TableCatalogFixture _fixture;

    public GameSessionBotTests(TableCatalogFixture fixture) => _fixture = fixture;

    [Fact]
    public void CreateSinglePlayer_HasSeatZeroOnlineAndOthersBot()
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed: 1);
        Assert.False(session.IsStarted);

        session.Start();
        IReadOnlyList<SeatStateChangedPayload> states = session.DrainEvents(255)
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.SeatStateChanged)
            .Select(SessionEventPayloadReader.ReadSeatStateChanged)
            .ToArray();

        Assert.Equal(4, states.Count);
        Assert.Equal(new byte[] { 0, 2, 2, 2 }, states.Select(state => state.State).ToArray());
    }

    [Fact]
    public void CreateFourBots_HasFourBotSeats()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 1);
        session.Start();

        byte[] states = session.DrainEvents(255)
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.SeatStateChanged)
            .Select(SessionEventPayloadReader.ReadSeatStateChanged)
            .Select(state => state.State)
            .ToArray();

        Assert.Equal(new byte[] { 2, 2, 2, 2 }, states);
    }

    [Fact]
    public void Create_WithCustomConfig_DoesNotStart()
    {
        GameSession session = SessionTestSupport.Create(
            new SessionConfig(HumanSeat: 2, BotCount: 3),
            _fixture.Catalog,
            seed: 5);

        Assert.False(session.IsStarted);
        session.Start();
        byte[] states = session.DrainEvents(255)
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.SeatStateChanged)
            .Select(SessionEventPayloadReader.ReadSeatStateChanged)
            .Select(state => state.State)
            .ToArray();

        Assert.Equal(new byte[] { 2, 2, 0, 2 }, states);
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(20, 6)]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(3, 1)]
    public void BotAnswersAfterExactlyCeilDelayTicks(int tickRate, int expectedDelay)
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 7, tickRate);
        session.Start();
        session.DrainEvents(255);
        session.Step(1);
        IReadOnlyList<SessionEvent> first = session.DrainEvents(255);
        RequestOpenedPayload request = SessionEventPayloadReader.ReadRequestOpened(
            Assert.Single(first, sessionEvent => sessionEvent.Kind == SessionEventKind.RequestOpened));
        long openedTick = 1;

        for (long tick = openedTick + 1; tick < openedTick + expectedDelay; tick++)
        {
            session.Step(tick);
            IReadOnlyList<SessionEvent> between = session.DrainEvents(255);
            Assert.DoesNotContain(
                between,
                sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed
                    && SessionEventPayloadReader.ReadRequestClosed(sessionEvent).RequestId == request.RequestId);
        }

        session.Step(openedTick + expectedDelay);
        IReadOnlyList<SessionEvent> closed = session.DrainEvents(255);
        SessionEvent closedEvent = Assert.Single(
            closed,
            sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed
                && SessionEventPayloadReader.ReadRequestClosed(sessionEvent).RequestId == request.RequestId);

        Assert.Equal(openedTick + expectedDelay, closedEvent.Tick);
        RequestClosedPayload payload = SessionEventPayloadReader.ReadRequestClosed(closedEvent);
        Assert.Equal(RequestOutcomeKind.Answered, payload.Outcome);
        Assert.NotNull(payload.Answer);
        Assert.True(IsAllowed(request.Allowed, payload.Answer!.Value.Verb));
    }

    [Fact]
    public void BotSubmitsAtFirstStepWhoseTickReachesDueTick()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 7);
        session.Start();
        session.DrainEvents(255);
        session.Step(1);
        RequestOpenedPayload request = SessionEventPayloadReader.ReadRequestOpened(
            Assert.Single(session.DrainEvents(255), sessionEvent => sessionEvent.Kind == SessionEventKind.RequestOpened));

        // 10Hz 下 Bot 到期 tick = 1 + 3 = 4；直接跳到 5，应在第一个 tick >= 到期值的 Step 提交，而不是 == 4。
        session.Step(5);
        IReadOnlyList<SessionEvent> events = session.DrainEvents(255);
        SessionEvent closedEvent = Assert.Single(
            events,
            sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed
                && SessionEventPayloadReader.ReadRequestClosed(sessionEvent).RequestId == request.RequestId);

        Assert.Equal(5, closedEvent.Tick);
        RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(closedEvent);
        Assert.Equal(RequestOutcomeKind.Answered, closed.Outcome);
        Assert.NotNull(closed.Answer);
    }

    [Fact]
    public void BotAnswersGoThroughSubmitPathAndProduceNormalEvents()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 7);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());

        // 每个 Answered 的 Bot 请求都携带命令；Discard 应答后同 tick 或之后必有 Discarded。
        foreach (SessionEvent sessionEvent in driver.Events.Where(
                     sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed))
        {
            RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(sessionEvent);
            if (closed.Outcome == RequestOutcomeKind.Answered)
            {
                Assert.NotNull(closed.Answer);
            }
        }

        Assert.Contains(driver.Events, sessionEvent => sessionEvent.Kind == SessionEventKind.Discarded);
        Assert.Contains(driver.Events, sessionEvent => sessionEvent.Kind == SessionEventKind.Meld);
    }

    [Fact]
    public void RequestsClosedInSameTick_AreOrderedByRequestId()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 7);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());

        var byTick = driver.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed)
            .GroupBy(sessionEvent => sessionEvent.Tick);
        foreach (var group in byTick)
        {
            long[] ids = group
                .Select(sessionEvent => SessionEventPayloadReader.ReadRequestClosed(sessionEvent).RequestId)
                .ToArray();
            Assert.Equal(ids.OrderBy(id => id).ToArray(), ids);
        }
    }

    private static bool IsAllowed(AllowedVerbs allowed, CommandVerb verb) => verb switch
    {
        CommandVerb.Discard => allowed.HasFlag(AllowedVerbs.Discard),
        CommandVerb.Pong => allowed.HasFlag(AllowedVerbs.Pong),
        CommandVerb.Kong => allowed.HasFlag(AllowedVerbs.Kong),
        CommandVerb.Win => allowed.HasFlag(AllowedVerbs.Win),
        CommandVerb.Pass => allowed.HasFlag(AllowedVerbs.Pass),
        _ => false,
    };
}
