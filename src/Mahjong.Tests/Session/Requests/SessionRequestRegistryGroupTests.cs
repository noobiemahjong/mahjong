using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Rules;
using Xunit;

namespace Mahjong.Tests.Session.Requests;

/// <summary>
/// 响应窗口登记：候选顺序占用连续 id、全体应答或到 deadline 关闭、仲裁复用 ResponseWindow
/// （杠 &gt; 碰 → arrivalTick 升序 → 座位升序）、逐座 Cancelled/Answered/TimedOut、
/// 空候选返回 null 且不分配 id、BeginTick 先单座后窗口。
/// </summary>
public sealed class SessionRequestRegistryGroupTests
{
    private static readonly Tile Discard = RulesTestData.FromId(21);

    [Fact]
    public void OpenGroup_AllocatesConsecutiveIdsInCandidateOrder()
    {
        (GameState state, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();

        long? groupId = registry.OpenGroup(Discard, Candidates(state, 3, 0, 2), openedTick: 0, deadlineTick: 10);

        Assert.NotNull(groupId);
        Assert.Equal(new long[] { 1, 2, 3 }, sink.Opened.Select(request => request.RequestId));
        Assert.Equal(new byte[] { 3, 0, 2 }, sink.Opened.Select(request => request.Seat));
        Assert.All(sink.Opened, request =>
        {
            Assert.Equal(RequestKind.Pong, request.Kind);
            Assert.Equal(AllowedVerbs.Pong | AllowedVerbs.Kong | AllowedVerbs.Pass, request.Allowed);
            Assert.Equal(10L, request.DeadlineTick);
        });
        Assert.Equal(new long[] { 1, 2, 3 }, registry.OpenRequests.Select(request => request.RequestId));
        Assert.True(registry.TryGetResponseTarget(2, out Tile target));
        Assert.Equal(Discard, target);
        Assert.False(registry.TryGetResponseTarget(99, out _));
        Assert.False(registry.IsGroupClosed(groupId!.Value));

        PendingRequest next = registry.OpenSingle(0, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);
        Assert.Equal(4L, next.RequestId);
    }

    [Fact]
    public void OpenGroup_EmptyCandidates_ReturnsNullAndAllocatesNoIds()
    {
        (_, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();

        long? groupId = registry.OpenGroup(Discard, Array.Empty<PlayerState>(), 0, 10);

        Assert.Null(groupId);
        Assert.Empty(sink.Opened);
        Assert.Empty(registry.OpenRequests);

        PendingRequest request = registry.OpenSingle(0, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);
        Assert.Equal(1L, request.RequestId);
    }

    [Fact]
    public void OpenGroup_RejectsInvalidArguments_AndAllocatesNoIds()
    {
        (GameState state, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();

        Assert.Throws<ArgumentNullException>(() => registry.OpenGroup(Discard, null!, 0, 10));
        Assert.Throws<ArgumentException>(() => registry.OpenGroup(Discard, Candidates(state, 1, 1), 0, 10));
        Assert.Throws<ArgumentException>(() => registry.OpenGroup(Discard, Candidates(state, 0, 1, 2, 3), 0, 10));
        Assert.Throws<ArgumentException>(() => registry.OpenGroup(default, Candidates(state, 1), 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.OpenGroup(Discard, Candidates(state, 1), 10, 9));

        Assert.Empty(sink.Opened);
        Assert.Equal(1L, registry.OpenSingle(0, RequestKind.Discard, AllowedVerbs.Discard, 0, 10).RequestId);
    }

    [Fact]
    public void AllAnswered_ClosesBeforeDeadline_WithPerSeatOutcomes()
    {
        (GameState state, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        long groupId = registry.OpenGroup(Discard, Candidates(state, 3, 0, 2), 0, 10)!.Value;
        PendingRequest request3 = Find(registry, 3);
        PendingRequest request0 = Find(registry, 0);
        PendingRequest request2 = Find(registry, 2);
        GameCommand pong3 = Command(request3, CommandVerb.Pong);
        GameCommand kong0 = Command(request0, CommandVerb.Kong);
        GameCommand pass2 = Command(request2, CommandVerb.Pass);

        Assert.True(registry.TryRecordAnswer(request3, pong3, arrivalTick: 2));
        Assert.True(registry.TryRecordAnswer(request0, kong0, arrivalTick: 3));
        Assert.True(registry.TryRecordAnswer(request2, pass2, arrivalTick: 1));
        Assert.False(registry.IsGroupClosed(groupId));

        BeginTick(registry, 2);

        Assert.True(registry.IsGroupClosed(groupId));
        ResponseResult result = registry.TakeGroupResult(groupId);
        Assert.True(result.HasWinner);
        Assert.Equal((byte)0, result.WinnerSeat);
        Assert.Equal(SkillId.Kong, result.WinnerSkill);
        Assert.Equal(Discard, result.TargetCard);
        Assert.Equal(new long[] { 1, 2, 3 }, result.Requests.Select(request => request.RequestId));
        Assert.Equal(
            new RequestOutcomeKind[] { RequestOutcomeKind.Cancelled, RequestOutcomeKind.Answered, RequestOutcomeKind.Answered },
            result.Outcomes.Select(outcome => outcome.Kind));
        Assert.Null(result.Outcomes[0].Payload);
        Assert.Equal(SkillId.Kong, Assert.IsType<SkillId>(result.Outcomes[1].Payload));
        Assert.Null(result.Outcomes[2].Payload);

        Assert.Equal(new long[] { 1, 2, 3 }, sink.Closed.Select(closed => closed.Request.RequestId));
        Assert.Equal(
            new RequestOutcomeKind[] { RequestOutcomeKind.Cancelled, RequestOutcomeKind.Answered, RequestOutcomeKind.Answered },
            sink.Closed.Select(closed => closed.Outcome));
        Assert.Null(sink.Closed[0].Answer);
        Assert.Equal(kong0, sink.Closed[1].Answer);
        Assert.Equal(pass2, sink.Closed[2].Answer);
        Assert.Empty(registry.OpenRequests);
    }

    [Fact]
    public void Deadline_ClosesWithAllTimedOut()
    {
        (GameState state, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        long groupId = registry.OpenGroup(Discard, Candidates(state, 1, 2), 0, 10)!.Value;

        BeginTick(registry, 9);
        Assert.False(registry.IsGroupClosed(groupId));
        Assert.Empty(sink.Closed);

        BeginTick(registry, 10);
        Assert.True(registry.IsGroupClosed(groupId));
        ResponseResult result = registry.TakeGroupResult(groupId);
        Assert.False(result.HasWinner);
        Assert.Equal(ResponseResult.NoWinnerSeat, result.WinnerSeat);
        Assert.All(result.Outcomes, outcome => Assert.Equal(RequestOutcomeKind.TimedOut, outcome.Kind));
        Assert.All(result.Outcomes, outcome => Assert.Null(outcome.Payload));
        Assert.All(sink.Closed, closed => Assert.Null(closed.Answer));
    }

    [Fact]
    public void PartialAnswers_CloseAtDeadline_WithAnsweredAndTimedOutPerSeat()
    {
        (GameState state, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        long groupId = registry.OpenGroup(Discard, Candidates(state, 1, 2, 3), 0, 10)!.Value;
        PendingRequest request1 = Find(registry, 1);
        PendingRequest request2 = Find(registry, 2);
        GameCommand pong1 = Command(request1, CommandVerb.Pong);
        GameCommand pass2 = Command(request2, CommandVerb.Pass);

        Assert.True(registry.TryRecordAnswer(request1, pong1, arrivalTick: 2));
        Assert.True(registry.TryRecordAnswer(request2, pass2, arrivalTick: 3));
        Assert.False(registry.IsGroupClosed(groupId));

        BeginTick(registry, 10);

        Assert.True(registry.IsGroupClosed(groupId));
        ResponseResult result = registry.TakeGroupResult(groupId);
        Assert.Equal((byte)1, result.WinnerSeat);
        Assert.Equal(SkillId.Pong, result.WinnerSkill);
        Assert.Equal(
            new RequestOutcomeKind[] { RequestOutcomeKind.Answered, RequestOutcomeKind.Answered, RequestOutcomeKind.TimedOut },
            result.Outcomes.Select(outcome => outcome.Kind));
        Assert.Equal(SkillId.Pong, Assert.IsType<SkillId>(result.Outcomes[0].Payload));
        Assert.Null(result.Outcomes[1].Payload);
        Assert.Null(result.Outcomes[2].Payload);

        Assert.Equal(
            new RequestOutcomeKind[] { RequestOutcomeKind.Answered, RequestOutcomeKind.Answered, RequestOutcomeKind.TimedOut },
            sink.Closed.Select(closed => closed.Outcome));
        Assert.Equal(pong1, sink.Closed[0].Answer);
        Assert.Equal(pass2, sink.Closed[1].Answer);
        Assert.Null(sink.Closed[2].Answer);
    }

    [Fact]
    public void Arbitration_KongBeatsPong()
    {
        (GameState state, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        long groupId = registry.OpenGroup(Discard, Candidates(state, 1, 3), 0, 10)!.Value;
        PendingRequest request1 = Find(registry, 1);
        PendingRequest request3 = Find(registry, 3);

        Assert.True(registry.TryRecordAnswer(request1, Command(request1, CommandVerb.Pong), 2));
        Assert.True(registry.TryRecordAnswer(request3, Command(request3, CommandVerb.Kong), 5));
        BeginTick(registry, 5);

        ResponseResult result = registry.TakeGroupResult(groupId);
        Assert.Equal((byte)3, result.WinnerSeat);
        Assert.Equal(SkillId.Kong, result.WinnerSkill);
        Assert.Equal(RequestOutcomeKind.Cancelled, result.Outcomes[0].Kind);
        Assert.Equal(RequestOutcomeKind.Answered, result.Outcomes[1].Kind);
        Assert.Equal(
            new RequestOutcomeKind[] { RequestOutcomeKind.Cancelled, RequestOutcomeKind.Answered },
            sink.Closed.Select(closed => closed.Outcome));
    }

    [Fact]
    public void Arbitration_EarlierArrivalTickWins()
    {
        (GameState state, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        long groupId = registry.OpenGroup(Discard, Candidates(state, 2, 0), 0, 10)!.Value;
        PendingRequest request2 = Find(registry, 2);
        PendingRequest request0 = Find(registry, 0);

        Assert.True(registry.TryRecordAnswer(request2, Command(request2, CommandVerb.Pong), 3));
        Assert.True(registry.TryRecordAnswer(request0, Command(request0, CommandVerb.Pong), 5));
        BeginTick(registry, 5);

        ResponseResult result = registry.TakeGroupResult(groupId);
        Assert.Equal((byte)2, result.WinnerSeat);
        Assert.Equal(RequestOutcomeKind.Answered, result.Outcomes[0].Kind);
        Assert.Equal(RequestOutcomeKind.Cancelled, result.Outcomes[1].Kind);
    }

    [Fact]
    public void Arbitration_SameArrivalTick_LowerSeatWins()
    {
        (GameState state, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        long groupId = registry.OpenGroup(Discard, Candidates(state, 2, 0), 0, 10)!.Value;
        PendingRequest request2 = Find(registry, 2);
        PendingRequest request0 = Find(registry, 0);

        Assert.True(registry.TryRecordAnswer(request2, Command(request2, CommandVerb.Pong), 5));
        Assert.True(registry.TryRecordAnswer(request0, Command(request0, CommandVerb.Pong), 5));
        BeginTick(registry, 5);

        ResponseResult result = registry.TakeGroupResult(groupId);
        Assert.Equal((byte)0, result.WinnerSeat);
        Assert.Equal(RequestOutcomeKind.Cancelled, result.Outcomes[0].Kind);
        Assert.Equal(RequestOutcomeKind.Answered, result.Outcomes[1].Kind);
    }

    [Fact]
    public void TryRecordAnswer_RejectsInvalidWindowAnswers()
    {
        (GameState state, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        long groupId = registry.OpenGroup(Discard, Candidates(state, 1, 2), 0, 10)!.Value;
        PendingRequest request1 = Find(registry, 1);
        PendingRequest request2 = Find(registry, 2);

        Assert.False(registry.TryRecordAnswer(request1, Command(request1, CommandVerb.Discard), 0));
        Assert.False(registry.TryRecordAnswer(request1, Command(request1, CommandVerb.Win), 0));
        Assert.True(registry.TryRecordAnswer(request1, Command(request1, CommandVerb.Pong), 0));
        Assert.False(registry.TryRecordAnswer(request1, Command(request1, CommandVerb.Pong), 1));
        Assert.False(registry.TryRecordAnswer(request2, Command(request2, CommandVerb.Pass), 11));

        BeginTick(registry, 10);
        Assert.True(registry.IsGroupClosed(groupId));
        Assert.False(registry.TryRecordAnswer(request2, Command(request2, CommandVerb.Pass), 10));
        Assert.Equal(RequestOutcomeKind.TimedOut, sink.Closed[1].Outcome);
    }

    [Fact]
    public void WindowRequestOutcome_IsConsumedOnce_AndTakeOutcomeRejectsIt()
    {
        (GameState state, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        long groupId = registry.OpenGroup(Discard, Candidates(state, 1, 2), 0, 10)!.Value;
        PendingRequest request1 = Find(registry, 1);
        PendingRequest request2 = Find(registry, 2);
        Assert.True(registry.TryRecordAnswer(request1, Command(request1, CommandVerb.Pass), 0));
        Assert.True(registry.TryRecordAnswer(request2, Command(request2, CommandVerb.Pass), 0));

        Assert.False(registry.IsSettled(request1.RequestId));
        BeginTick(registry, 1);
        Assert.True(registry.IsSettled(request1.RequestId));
        Assert.True(registry.IsGroupClosed(groupId));

        Assert.True(TryConsume(registry, request1.RequestId, out RequestOutcome outcome));
        Assert.Equal(RequestOutcomeKind.Answered, outcome.Kind);
        Assert.Null(outcome.Payload);
        Assert.False(TryConsume(registry, request1.RequestId, out _));
        Assert.Throws<InvalidOperationException>(() => registry.TakeOutcome(request1.RequestId));
        Assert.Equal(2, sink.Closed.Count);
    }

    [Fact]
    public void BeginTick_ClosesSingleRequestsBeforeResponseWindows()
    {
        (GameState state, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        PendingRequest single = registry.OpenSingle(0, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);
        _ = registry.OpenGroup(Discard, Candidates(state, 1, 2), 0, 10)!.Value;
        PendingRequest request1 = Find(registry, 1);
        PendingRequest request2 = Find(registry, 2);

        Assert.True(registry.TryRecordAnswer(single, Command(single, CommandVerb.Discard), 0));
        Assert.True(registry.TryRecordAnswer(request1, Command(request1, CommandVerb.Pass), 0));
        Assert.True(registry.TryRecordAnswer(request2, Command(request2, CommandVerb.Pass), 0));

        BeginTick(registry, 1);

        Assert.Equal(
            new[]
            {
                "open:1",
                "open:2",
                "open:3",
                "close:1:Answered",
                "close:2:Answered",
                "close:3:Answered",
            },
            sink.Log);
    }

    [Fact]
    public void TakeGroupResult_ThrowsUntilClosedOrUnknown()
    {
        (GameState state, _, SessionRequestRegistry registry) = Create();
        long groupId = registry.OpenGroup(Discard, Candidates(state, 1), 0, 10)!.Value;

        Assert.Throws<InvalidOperationException>(() => registry.TakeGroupResult(groupId));
        Assert.Throws<InvalidOperationException>(() => registry.TakeGroupResult(999));
        Assert.False(registry.IsGroupClosed(999));

        BeginTick(registry, 10);
        Assert.True(registry.IsGroupClosed(groupId));
        Assert.NotNull(registry.TakeGroupResult(groupId));
    }

    [Fact]
    public void TryGetResponseTarget_IsFalseForSingleRequest()
    {
        (_, _, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);

        Assert.False(registry.TryGetResponseTarget(request.RequestId, out _));
    }

    private static (GameState State, RecordingRequestSink Sink, SessionRequestRegistry Registry) Create()
    {
        var state = new GameState();
        var sink = new RecordingRequestSink();
        return (state, sink, new SessionRequestRegistry(state, sink));
    }

    private static PlayerState[] Candidates(GameState state, params byte[] seats) =>
        seats.Select(seat => state.Players[seat]).ToArray();

    private static PendingRequest Find(SessionRequestRegistry registry, byte seat)
    {
        Assert.True(registry.TryFindOpen(seat, 0, out PendingRequest request));
        return request;
    }

    private static GameCommand Command(PendingRequest request, CommandVerb verb) =>
        new(Seq: 1, ReceivedTick: 0, Seat: request.Seat, Verb: verb, Slot: 0, CardId: 0, RequestId: request.RequestId);

    private static void BeginTick(SessionRequestRegistry registry, long tick) =>
        ((IRequestRegistry)registry).BeginTick(tick);

    private static bool TryConsume(SessionRequestRegistry registry, long requestId, out RequestOutcome outcome) =>
        ((IRequestRegistry)registry).TryConsume(requestId, out outcome);
}
