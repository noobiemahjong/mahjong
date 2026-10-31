using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Xunit;

namespace Mahjong.Tests.Session.Requests;

/// <summary>
/// 单座请求登记：id 从 1 单调、Open 立即上报、应答在下一个 BeginTick 结算为 Answered（Payload=boxed 命令）、
/// 到 deadline 结算为 TimedOut、每个请求只产一次结果、结果只消费一次、重复/不匹配/越权/迟到应答被拒。
/// </summary>
public sealed class SessionRequestRegistrySingleTests
{
    [Fact]
    public void OpenSingle_AllocatesMonotonicIds_AndRaisesRequestOpened()
    {
        (_, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();

        PendingRequest first = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, openedTick: 0, deadlineTick: 10);
        PendingRequest second = registry.OpenSingle(2, RequestKind.Win, AllowedVerbs.Win | AllowedVerbs.Pass, 0, 10);

        Assert.Equal(1L, first.RequestId);
        Assert.Equal(2L, second.RequestId);
        Assert.Equal(new long[] { 1, 2 }, sink.Opened.Select(request => request.RequestId));
        Assert.Equal(new byte[] { 1, 2 }, sink.Opened.Select(request => request.Seat));
        Assert.Equal(new long[] { 1, 2 }, registry.OpenRequests.Select(request => request.RequestId));
        Assert.Empty(sink.Closed);
    }

    [Fact]
    public void OpenSingle_RejectsInvalidSeatAndDeadline()
    {
        (_, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => registry.OpenSingle(4, RequestKind.Discard, AllowedVerbs.Discard, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => registry.OpenSingle(0, RequestKind.Discard, AllowedVerbs.Discard, 10, 9));

        Assert.Empty(sink.Opened);
        Assert.Empty(registry.OpenRequests);
    }

    [Fact]
    public void AnsweredRequest_SettlesOnNextBeginTick_WithAnsweredOutcome()
    {
        (_, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);
        GameCommand command = Command(request, CommandVerb.Discard);

        Assert.True(registry.TryRecordAnswer(request, command, arrivalTick: 5));
        Assert.False(registry.IsSettled(request.RequestId));

        BeginTick(registry, 5);

        Assert.True(registry.IsSettled(request.RequestId));
        ClosedRecord closed = Assert.Single(sink.Closed);
        Assert.Equal(RequestOutcomeKind.Answered, closed.Outcome);
        Assert.Equal(command, closed.Answer);
        Assert.Empty(registry.OpenRequests);

        RequestOutcome outcome = registry.TakeOutcome(request.RequestId);
        Assert.Equal(RequestOutcomeKind.Answered, outcome.Kind);
        Assert.Equal(command, Assert.IsType<GameCommand>(outcome.Payload));
    }

    [Fact]
    public void PassAnswer_OnWinRequest_IsAnsweredWithPassCommand()
    {
        (_, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(2, RequestKind.Win, AllowedVerbs.Win | AllowedVerbs.Pass, 0, 10);
        GameCommand pass = Command(request, CommandVerb.Pass);

        Assert.True(registry.TryRecordAnswer(request, pass, 1));
        BeginTick(registry, 1);

        ClosedRecord closed = Assert.Single(sink.Closed);
        Assert.Equal(RequestOutcomeKind.Answered, closed.Outcome);
        Assert.Equal(pass, closed.Answer);
        Assert.Equal(pass, Assert.IsType<GameCommand>(registry.TakeOutcome(request.RequestId).Payload));
    }

    [Fact]
    public void UnansweredRequest_TimesOutAtDeadline()
    {
        (_, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(2, RequestKind.Win, AllowedVerbs.Win | AllowedVerbs.Pass, 0, 10);

        BeginTick(registry, 9);
        Assert.False(registry.IsSettled(request.RequestId));
        Assert.Empty(sink.Closed);

        BeginTick(registry, 10);
        Assert.True(registry.IsSettled(request.RequestId));
        ClosedRecord closed = Assert.Single(sink.Closed);
        Assert.Equal(RequestOutcomeKind.TimedOut, closed.Outcome);
        Assert.Null(closed.Answer);

        RequestOutcome outcome = registry.TakeOutcome(request.RequestId);
        Assert.Equal(RequestOutcomeKind.TimedOut, outcome.Kind);
        Assert.Null(outcome.Payload);
    }

    [Fact]
    public void Request_SettlesOnlyOnce()
    {
        (_, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);
        Assert.True(registry.TryRecordAnswer(request, Command(request, CommandVerb.Discard), 0));

        BeginTick(registry, 1);
        BeginTick(registry, 2);
        BeginTick(registry, 11);

        Assert.Single(sink.Closed);
        _ = registry.TakeOutcome(request.RequestId);
        Assert.False(TryConsume(registry, request.RequestId, out _));
    }

    [Fact]
    public void DuplicateAnswer_IsRejected()
    {
        (_, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);

        Assert.True(registry.TryRecordAnswer(request, Command(request, CommandVerb.Discard), 0));
        Assert.False(registry.TryRecordAnswer(request, Command(request, CommandVerb.Discard), 1));

        BeginTick(registry, 1);
        Assert.Single(sink.Closed);
    }

    [Fact]
    public void AnswerWithMismatchedRequestId_IsRejected()
    {
        (_, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);
        var unknown = new PendingRequest(request.RequestId + 5, request.Seat, request.Kind, request.Allowed, request.DeadlineTick);
        var commandWithWrongId = new GameCommand(1, 0, 1, CommandVerb.Discard, 0, 21, request.RequestId + 5);

        Assert.False(registry.TryRecordAnswer(unknown, commandWithWrongId, 0));
        Assert.False(registry.TryRecordAnswer(request, commandWithWrongId, 0));

        BeginTick(registry, 10);
        Assert.Equal(RequestOutcomeKind.TimedOut, Assert.Single(sink.Closed).Outcome);
    }

    [Fact]
    public void AnswerWithVerbOutsideAllowed_IsRejected()
    {
        (_, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);

        Assert.False(registry.TryRecordAnswer(request, Command(request, CommandVerb.Pong), 0));
        Assert.False(registry.TryRecordAnswer(request, Command(request, CommandVerb.Win), 0));

        BeginTick(registry, 10);
        Assert.Equal(RequestOutcomeKind.TimedOut, Assert.Single(sink.Closed).Outcome);
    }

    [Fact]
    public void AnswerAfterDeadline_IsRejected()
    {
        (_, RecordingRequestSink sink, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);

        Assert.False(registry.TryRecordAnswer(request, Command(request, CommandVerb.Discard), arrivalTick: 11));

        BeginTick(registry, 10);
        Assert.Equal(RequestOutcomeKind.TimedOut, Assert.Single(sink.Closed).Outcome);
    }

    [Fact]
    public void TakeOutcome_CanBeTakenOnlyOnce()
    {
        (_, _, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);
        Assert.True(registry.TryRecordAnswer(request, Command(request, CommandVerb.Discard), 0));
        BeginTick(registry, 1);

        _ = registry.TakeOutcome(request.RequestId);

        Assert.Throws<InvalidOperationException>(() => registry.TakeOutcome(request.RequestId));
        Assert.False(TryConsume(registry, request.RequestId, out _));
    }

    [Fact]
    public void TryConsume_ConsumesOnce_AndBlocksTakeOutcome()
    {
        (_, _, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);
        Assert.True(registry.TryRecordAnswer(request, Command(request, CommandVerb.Discard), 0));
        BeginTick(registry, 1);

        Assert.True(TryConsume(registry, request.RequestId, out RequestOutcome first));
        Assert.Equal(RequestOutcomeKind.Answered, first.Kind);
        Assert.False(TryConsume(registry, request.RequestId, out _));
        Assert.Throws<InvalidOperationException>(() => registry.TakeOutcome(request.RequestId));
    }

    [Fact]
    public void TryFindOpen_BySeatAndById()
    {
        (_, _, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);

        Assert.True(registry.TryFindOpen(1, 0, out PendingRequest bySeat));
        Assert.Equal(request, bySeat);

        Assert.True(registry.TryFindOpen(1, request.RequestId, out PendingRequest byId));
        Assert.Equal(request, byId);

        Assert.False(registry.TryFindOpen(0, 0, out _));
        Assert.False(registry.TryFindOpen(1, request.RequestId + 1, out _));
    }

    [Fact]
    public void TryFindOpen_AfterSettlement_ReturnsFalse()
    {
        (_, _, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);
        Assert.True(registry.TryRecordAnswer(request, Command(request, CommandVerb.Discard), 0));
        BeginTick(registry, 1);

        Assert.False(registry.TryFindOpen(1, 0, out _));
        Assert.False(registry.TryFindOpen(1, request.RequestId, out _));
    }

    [Fact]
    public void TakeOutcome_UnknownOrUnsettled_Throws()
    {
        (_, _, SessionRequestRegistry registry) = Create();
        PendingRequest request = registry.OpenSingle(1, RequestKind.Discard, AllowedVerbs.Discard, 0, 10);

        Assert.Throws<InvalidOperationException>(() => registry.TakeOutcome(request.RequestId));
        Assert.Throws<InvalidOperationException>(() => registry.TakeOutcome(999));
    }

    private static (GameState State, RecordingRequestSink Sink, SessionRequestRegistry Registry) Create()
    {
        var state = new GameState();
        var sink = new RecordingRequestSink();
        return (state, sink, new SessionRequestRegistry(state, sink));
    }

    private static GameCommand Command(PendingRequest request, CommandVerb verb) =>
        new(Seq: 1, ReceivedTick: 0, Seat: request.Seat, Verb: verb, Slot: 0, CardId: 0, RequestId: request.RequestId);

    private static void BeginTick(SessionRequestRegistry registry, long tick) =>
        ((IRequestRegistry)registry).BeginTick(tick);

    private static bool TryConsume(SessionRequestRegistry registry, long requestId, out RequestOutcome outcome) =>
        ((IRequestRegistry)registry).TryConsume(requestId, out outcome);
}
