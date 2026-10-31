using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Flow;

/// <summary>
/// 超时托管（D9）：出牌请求超时自动打第一张合法牌；Win 请求超时按 Pass 继续；
/// 响应窗口到 deadline 未应答者 TimedOut 并关闭。全部用固定 tick 驱动，不 sleep、不读墙钟。
/// </summary>
[Collection("table-catalog")]
public sealed class GameSessionTimeoutTests
{
    private readonly TableCatalogFixture _fixture;

    public GameSessionTimeoutTests(TableCatalogFixture fixture) => _fixture = fixture;

    /// <summary>脚本：只应答自己的 Discard 请求，其余（Win/Pong 窗口）不提交，交给超时。</summary>
    private static SubmitResult? AnswerOwnDiscardsOnly(SessionDriver driver, RequestOpenedPayload request)
    {
        if (request.Kind != RequestKind.Discard)
        {
            return null;
        }

        GameSession session = driver.Session;
        int slot = SessionTestSupport.FirstLegalDiscardSlot(session, driver.Verifier, request.Seat);
        return session.Submit(SessionTestSupport.Command(request.Seat, CommandVerb.Discard, request.RequestId, slot));
    }

    [Fact]
    public void DiscardRequestTimeout_AutoDiscardsFirstLegalTile()
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed: 8);
        session.Start();
        session.DrainEvents(255);
        session.Step(1);
        IReadOnlyList<SessionEvent> first = session.DrainEvents(255);
        RequestOpenedPayload request = SessionEventPayloadReader.ReadRequestOpened(
            Assert.Single(first, sessionEvent => sessionEvent.Kind == SessionEventKind.RequestOpened));
        Assert.Equal(RequestKind.Discard, request.Kind);

        CardAndSkillVerifier verifier = SessionTestSupport.CreateVerifier(_fixture.Catalog);
        int expectedSlot = SessionTestSupport.FirstLegalDiscardSlot(session, verifier, seat: 0);
        Assert.True(expectedSlot >= 0);
        ushort expectedTileId = session.State.Players[0].Hand[expectedSlot].Id;
        long deadline = 1 + 600;
        Assert.Equal(deadline, request.DeadlineTick);

        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunUntil(() => session.Tick >= deadline, maxSteps: 2000));
        IReadOnlyList<SessionEvent> events = driver.Events;

        SessionEvent closedEvent = Assert.Single(
            events,
            sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed
                && SessionEventPayloadReader.ReadRequestClosed(sessionEvent).RequestId == request.RequestId);
        Assert.Equal(deadline, closedEvent.Tick);
        RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(closedEvent);
        Assert.Equal(RequestOutcomeKind.TimedOut, closed.Outcome);
        Assert.Null(closed.Answer);

        SessionEvent discardedEvent = Assert.Single(
            events,
            sessionEvent => sessionEvent.Kind == SessionEventKind.Discarded
                && SessionEventPayloadReader.ReadDiscarded(sessionEvent).Seat == 0);
        Assert.Equal(deadline, discardedEvent.Tick);
        DiscardedPayload discarded = SessionEventPayloadReader.ReadDiscarded(discardedEvent);
        Assert.Equal(expectedTileId, discarded.TileId);
        Assert.Equal(0, (int)discarded.Slot);
    }

    [Fact]
    public void WinRequestTimeout_IsTreatedAsPassAndOpensDiscardRequest()
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed: 29);
        session.Start();
        session.DrainEvents(255);
        SessionDriver driver = SessionTestSupport.CreateDriver(
            session,
            _fixture.Catalog,
            humanSeat: 0,
            policy: AnswerOwnDiscardsOnly);

        long? winRequestId = null;
        long winDeadline = 0;
        int cursor = 0;
        while (winRequestId is null && session.Tick < 5000)
        {
            driver.StepOnce();
            for (; cursor < driver.Events.Count; cursor++)
            {
                SessionEvent sessionEvent = driver.Events[cursor];
                if (sessionEvent.Kind != SessionEventKind.RequestOpened)
                {
                    continue;
                }

                RequestOpenedPayload opened = SessionEventPayloadReader.ReadRequestOpened(sessionEvent);
                if (opened.Seat == 0 && opened.Kind == RequestKind.Win)
                {
                    winRequestId = opened.RequestId;
                    winDeadline = opened.DeadlineTick;
                }
            }
        }

        Assert.NotNull(winRequestId);
        Assert.True(winDeadline > session.Tick);

        // 继续驱动到超时结算；期间窗口超时（若有）也按契约推进。
        Assert.True(driver.RunUntil(() => session.Tick >= winDeadline, maxSteps: 5000));

        SessionEvent closedEvent = Assert.Single(
            driver.Events,
            sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed
                && SessionEventPayloadReader.ReadRequestClosed(sessionEvent).RequestId == winRequestId!.Value);
        Assert.Equal(winDeadline, closedEvent.Tick);
        RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(closedEvent);
        Assert.Equal(RequestOutcomeKind.TimedOut, closed.Outcome);
        Assert.Null(closed.Answer);

        // Win 超时按 Pass：同 tick 继续开 Discard 请求，而不是结算本局。
        Assert.Contains(
            driver.Events,
            sessionEvent => sessionEvent.Tick == winDeadline
                && sessionEvent.Kind == SessionEventKind.RequestOpened
                && SessionEventPayloadReader.ReadRequestOpened(sessionEvent) is { Seat: 0, Kind: RequestKind.Discard });
        Assert.DoesNotContain(
            driver.Events,
            sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded);
    }

    [Fact]
    public void ResponseWindowTimeout_ClosesWithTimedOutForUnansweredSeat()
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed: 1);
        session.Start();
        session.DrainEvents(255);
        SessionDriver driver = SessionTestSupport.CreateDriver(
            session,
            _fixture.Catalog,
            humanSeat: 0,
            policy: AnswerOwnDiscardsOnly);

        RequestOpenedPayload? windowRequest = null;
        int cursor = 0;
        while (windowRequest is null && session.Tick < 5000)
        {
            driver.StepOnce();
            for (; cursor < driver.Events.Count; cursor++)
            {
                SessionEvent sessionEvent = driver.Events[cursor];
                if (sessionEvent.Kind != SessionEventKind.RequestOpened)
                {
                    continue;
                }

                RequestOpenedPayload opened = SessionEventPayloadReader.ReadRequestOpened(sessionEvent);
                if (opened.Seat == 0 && opened.Kind == RequestKind.Pong)
                {
                    windowRequest = opened;
                }
            }
        }

        Assert.NotNull(windowRequest);
        long deadline = windowRequest!.Value.DeadlineTick;
        Assert.True(driver.RunUntil(() => session.Tick >= deadline, maxSteps: 2000));

        SessionEvent closedEvent = Assert.Single(
            driver.Events,
            sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed
                && SessionEventPayloadReader.ReadRequestClosed(sessionEvent).RequestId == windowRequest.Value.RequestId);
        Assert.Equal(deadline, closedEvent.Tick);
        Assert.Equal(RequestOutcomeKind.TimedOut, SessionEventPayloadReader.ReadRequestClosed(closedEvent).Outcome);

        // 窗口关闭后流程继续推进（下一位摸牌或本局结束），不会卡死。
        Assert.True(driver.RunUntil(
            () => driver.Events.Any(sessionEvent =>
                sessionEvent.Tick > deadline
                && sessionEvent.Kind is SessionEventKind.TurnStarted or SessionEventKind.RoundEnded),
            maxSteps: 2000));
    }

    [Fact]
    public void TimeoutSettlesAtFirstStepWhoseTickReachesDeadline()
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed: 8);
        session.Start();
        session.DrainEvents(255);
        session.Step(1);
        RequestOpenedPayload request = SessionEventPayloadReader.ReadRequestOpened(
            Assert.Single(session.DrainEvents(255), sessionEvent => sessionEvent.Kind == SessionEventKind.RequestOpened));
        Assert.Equal(RequestKind.Discard, request.Kind);
        Assert.Equal(601L, request.DeadlineTick);

        CardAndSkillVerifier verifier = SessionTestSupport.CreateVerifier(_fixture.Catalog);
        int expectedSlot = SessionTestSupport.FirstLegalDiscardSlot(session, verifier, seat: 0);
        ushort expectedTileId = session.State.Players[0].Hand[expectedSlot].Id;

        // 直接跳到 700（超过 deadline）：应在第一个 tick >= deadline 的 Step 结算，而不是只在 == 601 时结算。
        session.Step(700);
        IReadOnlyList<SessionEvent> events = session.DrainEvents(255);
        SessionEvent closedEvent = Assert.Single(
            events,
            sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed
                && SessionEventPayloadReader.ReadRequestClosed(sessionEvent).RequestId == request.RequestId);
        Assert.Equal(700, closedEvent.Tick);
        Assert.Equal(RequestOutcomeKind.TimedOut, SessionEventPayloadReader.ReadRequestClosed(closedEvent).Outcome);

        SessionEvent discardedEvent = Assert.Single(
            events,
            sessionEvent => sessionEvent.Kind == SessionEventKind.Discarded
                && SessionEventPayloadReader.ReadDiscarded(sessionEvent).Seat == 0);
        Assert.Equal(700, discardedEvent.Tick);
        Assert.Equal(expectedTileId, SessionEventPayloadReader.ReadDiscarded(discardedEvent).TileId);
    }
}
