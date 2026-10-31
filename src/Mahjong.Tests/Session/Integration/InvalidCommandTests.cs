using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Integration;

/// <summary>
/// 非法命令端到端：未 Start、无开放请求、错座位、错 requestId、动词越权、槽位越界、牌 id 不一致与未定义动词
/// 一律返回 false + 非空原因，且 StateHash、事件数、Tick 三口径不变；请求保持开放、随后合法命令仍可被接受。
/// </summary>
/// <remarks>
/// 固定 seed 8 的前置是 RNG 决定的（首局庄家与首个请求种类），不是契约常量：一次性 1–100 扫描选定它
/// 使「首局庄家=0 且首个请求=Discard（无 Kong/Win 位）」。下面的前置断言把这两个假设变成显式失败点，
/// 洗牌/起庄语义一改就在此处报错，而不是让后续「动词越权」断言以误导性原因失败。
/// </remarks>
[Collection("table-catalog")]
public sealed class InvalidCommandTests
{
    private readonly TableCatalogFixture _fixture;

    public InvalidCommandTests(TableCatalogFixture fixture) => _fixture = fixture;

    [Fact]
    public void SubmitBeforeStart_IsRejectedWithoutAnyStateChange()
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed: 8);
        string hash = SessionTestSupport.Hash(session);

        SubmitResult result = session.Submit(IntegrationSupport.Command(0, CommandVerb.Discard, 0));

        Assert.False(result.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Equal(0, result.RequestId);
        Assert.False(session.IsStarted);
        Assert.Equal(0, session.Tick);
        Assert.Equal(hash, SessionTestSupport.Hash(session));
        Assert.Empty(session.DrainEvents(IntegrationSupport.FullViewSeat));
        Assert.Empty(session.ActiveRequests);
    }

    [Fact]
    public void SubmitAfterStartWithoutOpenRequest_IsRejected()
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed: 8);
        session.Start();
        session.DrainEvents(IntegrationSupport.FullViewSeat);
        Assert.Empty(session.ActiveRequests);

        string hash = SessionTestSupport.Hash(session);
        SubmitResult result = session.Submit(IntegrationSupport.Command(0, CommandVerb.Discard, 0));

        AssertRejectedUnchanged(session, result, hash, baselineTick: 0);
        Assert.Empty(session.ActiveRequests);
    }

    [Fact]
    public void AllIllegalCommandShapes_AreRejectedAndLeaveTheOpenRequestIntact()
    {
        (GameSession session, RequestOpenedPayload request) = OpenFirstHumanDiscardRequest();
        string hash = SessionTestSupport.Hash(session);
        long tick = session.Tick;
        PendingRequest active = Assert.Single(session.ActiveRequests);
        Assert.Equal(request.RequestId, active.RequestId);
        Assert.Equal((byte)0, active.Seat);

        // 错座位：该座位没有开放请求。
        byte wrongSeat = (byte)((request.Seat + 1) % GameState.PlayerCount);
        Assert.DoesNotContain(session.ActiveRequests, open => open.Seat == wrongSeat);
        AssertRejectedUnchanged(
            session,
            session.Submit(IntegrationSupport.Command(wrongSeat, CommandVerb.Discard, request.RequestId)),
            hash,
            tick);

        // requestId 不匹配。
        AssertRejectedUnchanged(
            session,
            session.Submit(IntegrationSupport.Command(request.Seat, CommandVerb.Discard, request.RequestId + 1)),
            hash,
            tick);

        // 动词越权：首回合出牌请求不允许胡。
        Assert.False(request.Allowed.HasFlag(AllowedVerbs.Win));
        AssertRejectedUnchanged(
            session,
            session.Submit(IntegrationSupport.Command(request.Seat, CommandVerb.Win, request.RequestId)),
            hash,
            tick);

        // 槽位越界。
        AssertRejectedUnchanged(
            session,
            session.Submit(IntegrationSupport.Command(
                request.Seat,
                CommandVerb.Discard,
                request.RequestId,
                slot: 99)),
            hash,
            tick);

        // 牌 id 与槽位不一致。
        ushort mismatchedCardId = (ushort)(session.State.Players[request.Seat].Hand[0].Id + 1);
        AssertRejectedUnchanged(
            session,
            session.Submit(IntegrationSupport.Command(
                request.Seat,
                CommandVerb.Discard,
                request.RequestId,
                slot: 0,
                cardId: mismatchedCardId)),
            hash,
            tick);

        // 未定义动词。
        AssertRejectedUnchanged(
            session,
            session.Submit(IntegrationSupport.Command(request.Seat, (CommandVerb)99, request.RequestId)),
            hash,
            tick);

        // 非法提交没有关闭/消费请求：请求仍以同一 id 开放。
        Assert.Equal(request.RequestId, Assert.Single(session.ActiveRequests).RequestId);

        // 合法命令仍可被接受；接受本身只记录应答，不改局面/事件/Tick。
        int slot = SessionTestSupport.FirstLegalDiscardSlot(session, SessionTestSupport.CreateVerifier(_fixture.Catalog), request.Seat);
        Assert.True(slot >= 0);
        SubmitResult accepted = session.Submit(IntegrationSupport.Command(
            request.Seat,
            CommandVerb.Discard,
            request.RequestId,
            slot,
            session.State.Players[request.Seat].Hand[slot].Id));
        Assert.True(accepted.Accepted, accepted.Reason);
        Assert.Equal(request.RequestId, accepted.RequestId);
        Assert.Equal(hash, SessionTestSupport.Hash(session));
        Assert.Equal(tick, session.Tick);
        Assert.Empty(session.DrainEvents(IntegrationSupport.FullViewSeat));

        // 下一次 Step 才结算：RequestClosed(Answered) → Discarded，请求列表清空。
        session.Step(tick + 1);
        IReadOnlyList<SessionEvent> events = session.DrainEvents(IntegrationSupport.FullViewSeat);
        RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(
            Assert.Single(
                events,
                sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed
                    && SessionEventPayloadReader.ReadRequestClosed(sessionEvent).RequestId == request.RequestId));
        Assert.Equal(RequestOutcomeKind.Answered, closed.Outcome);
        Assert.NotNull(closed.Answer);
        Assert.Equal(CommandVerb.Discard, closed.Answer!.Value.Verb);
        Assert.Contains(events, sessionEvent => sessionEvent.Kind == SessionEventKind.Discarded);

        // 只断言该座位请求已结算：下家（Bot）会在同一 tick 打开自己的出牌请求，不能期望请求列表为空。
        Assert.DoesNotContain(session.ActiveRequests, open => open.RequestId == request.RequestId);
        Assert.DoesNotContain(session.ActiveRequests, open => open.Seat == request.Seat);
        Assert.NotEqual(hash, SessionTestSupport.Hash(session));
    }

    /// <summary>
    /// 打开 seed 8 的首个人类请求，并把「首局庄家=0、首请求= Discard 且无 Kong/Win 位」写成前置断言；
    /// 这些是 RNG 决定的可变前置（见类注释），断言失败即选种假设失效。
    /// </summary>
    private (GameSession Session, RequestOpenedPayload Request) OpenFirstHumanDiscardRequest()
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed: 8);
        session.Start();
        session.DrainEvents(IntegrationSupport.FullViewSeat);
        session.Step(1);
        IReadOnlyList<SessionEvent> events = session.DrainEvents(IntegrationSupport.FullViewSeat);

        RoundStartedPayload roundStarted = SessionEventPayloadReader.ReadRoundStarted(
            Assert.Single(events, sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted));
        Assert.Equal((byte)1, roundStarted.RoundNo);
        Assert.Equal((ushort)84, roundStarted.WallCount);
        Assert.Equal((byte)0, roundStarted.BankerSeat);

        RequestOpenedPayload request = SessionEventPayloadReader.ReadRequestOpened(
            Assert.Single(events, sessionEvent => sessionEvent.Kind == SessionEventKind.RequestOpened));

        Assert.Equal(1L, request.RequestId);
        Assert.Equal((byte)0, request.Seat);
        Assert.Equal(RequestKind.Discard, request.Kind);
        Assert.False(request.Allowed.HasFlag(AllowedVerbs.Kong));
        Assert.False(request.Allowed.HasFlag(AllowedVerbs.Win));
        return (session, request);
    }

    private static void AssertRejectedUnchanged(
        GameSession session,
        SubmitResult result,
        string baselineHash,
        long baselineTick)
    {
        Assert.False(result.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Equal(0, result.RequestId);
        Assert.Equal(baselineHash, SessionTestSupport.Hash(session));
        Assert.Equal(baselineTick, session.Tick);
        Assert.Empty(session.DrainEvents(IntegrationSupport.FullViewSeat));
    }
}
