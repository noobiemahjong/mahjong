using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Flow;

/// <summary>
/// Submit 校验：座位/请求 id/动词位/动作合法性全部通过才接受；失败不抛、Reason 非空、RequestId=0，
/// 且 StateHash、事件、Tick 三口径不变。接受路径只记录应答，不推进局面、不产事件。
/// </summary>
[Collection("table-catalog")]
public sealed class GameSessionSubmitTests
{
    private readonly TableCatalogFixture _fixture;

    public GameSessionSubmitTests(TableCatalogFixture fixture) => _fixture = fixture;

    /// <summary>单人会话的第一回合请求：seed 8 首局庄家为真人 0，首个请求为 Discard。</summary>
    private (GameSession Session, RequestOpenedPayload Request) OpenFirstHumanDiscardRequest(ulong seed = 8)
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed);
        session.Start();
        session.DrainEvents(255);
        session.Step(1);
        IReadOnlyList<SessionEvent> events = session.DrainEvents(255);
        RequestOpenedPayload request = SessionEventPayloadReader.ReadRequestOpened(
            Assert.Single(events, sessionEvent => sessionEvent.Kind == SessionEventKind.RequestOpened));
        Assert.Equal((byte)0, request.Seat);
        Assert.Equal(RequestKind.Discard, request.Kind);
        return (session, request);
    }

    /// <summary>单人会话的首回合请求：seed 9 手牌含鬼牌（牌种 31）。</summary>
    private (GameSession Session, RequestOpenedPayload Request) OpenFirstHumanDiscardRequestWithJoker()
    {
        (GameSession session, RequestOpenedPayload request) = OpenFirstHumanDiscardRequest(seed: 9);
        IReadOnlyList<Tile> hand = session.State.Players[0].Hand.Items;
        Assert.Contains(hand, tile => tile.Type.Id == SessionTestSupport.Joker.Id);
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
        Assert.Empty(session.DrainEvents(255));
    }

    [Fact]
    public void Submit_BeforeStartOrWithoutOpenRequest_IsRejected()
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed: 8);
        SubmitResult beforeStart = session.Submit(SessionTestSupport.Command(0, CommandVerb.Discard, 0));
        Assert.False(beforeStart.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(beforeStart.Reason));

        session.Start();
        session.DrainEvents(255);
        string hash = SessionTestSupport.Hash(session);
        SubmitResult withoutRequest = session.Submit(SessionTestSupport.Command(0, CommandVerb.Discard, 0));
        AssertRejectedUnchanged(session, withoutRequest, hash, baselineTick: 0);
    }

    [Fact]
    public void Submit_RejectsWrongSeat()
    {
        (GameSession session, RequestOpenedPayload request) = OpenFirstHumanDiscardRequest();
        string hash = SessionTestSupport.Hash(session);
        long tick = session.Tick;

        SubmitResult result = session.Submit(
            SessionTestSupport.Command((byte)((request.Seat + 1) % GameState.PlayerCount), CommandVerb.Discard, request.RequestId));

        AssertRejectedUnchanged(session, result, hash, tick);
    }

    [Fact]
    public void Submit_RejectsWrongRequestId()
    {
        (GameSession session, RequestOpenedPayload request) = OpenFirstHumanDiscardRequest();
        string hash = SessionTestSupport.Hash(session);
        long tick = session.Tick;

        SubmitResult result = session.Submit(
            SessionTestSupport.Command(request.Seat, CommandVerb.Discard, request.RequestId + 1));

        AssertRejectedUnchanged(session, result, hash, tick);
    }

    [Fact]
    public void Submit_RejectsVerbNotAllowedByRequest()
    {
        (GameSession session, RequestOpenedPayload request) = OpenFirstHumanDiscardRequest();
        string hash = SessionTestSupport.Hash(session);
        long tick = session.Tick;

        // 首回合 Discard 请求只允许 Discard（本 seed 不含杠位），Win 越权。
        Assert.False(request.Allowed.HasFlag(AllowedVerbs.Win));
        SubmitResult result = session.Submit(
            SessionTestSupport.Command(request.Seat, CommandVerb.Win, request.RequestId));

        AssertRejectedUnchanged(session, result, hash, tick);
    }

    [Fact]
    public void Submit_RejectsUndefinedVerb()
    {
        (GameSession session, RequestOpenedPayload request) = OpenFirstHumanDiscardRequest();
        string hash = SessionTestSupport.Hash(session);
        long tick = session.Tick;

        SubmitResult result = session.Submit(
            SessionTestSupport.Command(request.Seat, (CommandVerb)99, request.RequestId));

        AssertRejectedUnchanged(session, result, hash, tick);
    }

    [Fact]
    public void Submit_RejectsOutOfRangeSlot()
    {
        (GameSession session, RequestOpenedPayload request) = OpenFirstHumanDiscardRequest();
        string hash = SessionTestSupport.Hash(session);
        long tick = session.Tick;

        SubmitResult result = session.Submit(
            SessionTestSupport.Command(request.Seat, CommandVerb.Discard, request.RequestId, slot: 99));

        AssertRejectedUnchanged(session, result, hash, tick);
    }

    [Fact]
    public void Submit_RejectsCardIdMismatch()
    {
        (GameSession session, RequestOpenedPayload request) = OpenFirstHumanDiscardRequest();
        string hash = SessionTestSupport.Hash(session);
        long tick = session.Tick;
        ushort firstTileId = session.State.Players[0].Hand[0].Id;
        ushort mismatched = (ushort)(firstTileId == ushort.MaxValue ? firstTileId - 1 : firstTileId + 1);

        SubmitResult result = session.Submit(
            SessionTestSupport.Command(request.Seat, CommandVerb.Discard, request.RequestId, slot: 0, cardId: mismatched));

        AssertRejectedUnchanged(session, result, hash, tick);
    }

    [Fact]
    public void Submit_RejectsJokerDiscard()
    {
        (GameSession session, RequestOpenedPayload request) = OpenFirstHumanDiscardRequestWithJoker();
        string hash = SessionTestSupport.Hash(session);
        long tick = session.Tick;
        IReadOnlyList<Tile> hand = session.State.Players[0].Hand.Items;
        int jokerSlot = -1;
        for (int slot = 0; slot < hand.Count; slot++)
        {
            if (hand[slot].Type.Id == SessionTestSupport.Joker.Id)
            {
                jokerSlot = slot;
                break;
            }
        }

        Assert.True(jokerSlot >= 0);
        SubmitResult result = session.Submit(
            SessionTestSupport.Command(
                request.Seat,
                CommandVerb.Discard,
                request.RequestId,
                slot: jokerSlot,
                cardId: hand[jokerSlot].Id));

        AssertRejectedUnchanged(session, result, hash, tick);
    }

    [Fact]
    public void Submit_AcceptsLegalDiscardButOnlyRecordsAnswer()
    {
        (GameSession session, RequestOpenedPayload request) = OpenFirstHumanDiscardRequest();
        CardAndSkillVerifier verifier = SessionTestSupport.CreateVerifier(_fixture.Catalog);
        int slot = SessionTestSupport.FirstLegalDiscardSlot(session, verifier, seat: 0);
        Assert.True(slot >= 0);
        Tile discarded = session.State.Players[0].Hand[slot];
        string hash = SessionTestSupport.Hash(session);
        long tick = session.Tick;

        SubmitResult accepted = session.Submit(
            SessionTestSupport.Command(request.Seat, CommandVerb.Discard, request.RequestId, slot: slot, cardId: discarded.Id));

        Assert.True(accepted.Accepted);
        Assert.Null(accepted.Reason);
        Assert.Equal(request.RequestId, accepted.RequestId);

        // 只记录应答：局面、ticks、事件都不变。
        Assert.Equal(hash, SessionTestSupport.Hash(session));
        Assert.Equal(tick, session.Tick);
        Assert.Empty(session.DrainEvents(255));
        Assert.Single(session.ActiveRequests);

        // 下一次 Step 才产出 RequestClosed(Answered) → Discarded，顺序固定。
        session.Step(tick + 1);
        IReadOnlyList<SessionEvent> events = session.DrainEvents(255);
        int closedIndex = -1;
        int discardedIndex = -1;
        for (int index = 0; index < events.Count; index++)
        {
            if (events[index].Kind == SessionEventKind.RequestClosed)
            {
                RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(events[index]);
                if (closed.RequestId == request.RequestId)
                {
                    Assert.Equal(RequestOutcomeKind.Answered, closed.Outcome);
                    Assert.NotNull(closed.Answer);
                    Assert.Equal(CommandVerb.Discard, closed.Answer!.Value.Verb);
                    closedIndex = index;
                }
            }
            else if (events[index].Kind == SessionEventKind.Discarded)
            {
                DiscardedPayload discardedPayload = SessionEventPayloadReader.ReadDiscarded(events[index]);
                Assert.Equal((byte)0, discardedPayload.Seat);
                Assert.Equal(discarded.Id, discardedPayload.TileId);
                discardedIndex = index;
            }
        }

        Assert.True(closedIndex >= 0);
        Assert.True(discardedIndex > closedIndex);
    }
}
