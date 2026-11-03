using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Rules;
using Xunit;

namespace Mahjong.Tests.Session.Events;

/// <summary>
/// 读取器契约：与写入端回读一致；Kind 不匹配抛 <see cref="ArgumentException"/>；
/// 截断、计数与长度矛盾、answer 长度与 outcome 矛盾、UTF-8 非法、名次越界抛 <see cref="InvalidDataException"/>。
/// 读取返回的数组是新副本，修改它不影响事件 payload。
/// </summary>
public sealed class PayloadReaderTests
{
    [Fact]
    public void AllKinds_RoundTripThroughBufferAndReader()
    {
        var buffer = new SessionEventBuffer();
        var request = new PendingRequest(5, 2, RequestKind.Discard, AllowedVerbs.Discard | AllowedVerbs.Kong, 615);
        var answer = new GameCommand(1, 3, 2, CommandVerb.Kong, 4, 21, 5);
        buffer.EmitRoundStarted(10, 3, 2, 84, 4);
        buffer.EmitTurnStarted(11, 2, 700);
        buffer.EmitDeal(12, 1, Tiles(1, 2, 3));
        buffer.EmitHandSync(13, 1, Tiles(4, 5));
        buffer.EmitDrew(14, 2, 21, 83);
        buffer.EmitDiscarded(15, 1, 4, 21);
        buffer.EmitMeld(16, 3, MeldKind.KongSupplement, Tiles(17, 18, 19, 20), 255);
        buffer.EmitRequestOpened(17, request);
        buffer.EmitRequestClosed(18, request, RequestOutcomeKind.Answered, answer);
        buffer.EmitRoundEnded(19, 2, new short[] { 10, -5, -3, -2 }, ScoreActionType.SelfDrawn);
        buffer.EmitGameEnded(20, new short[] { 100, -20, -30, -50 }, new byte[] { 0, 1, 2, 3 }, "replay");
        buffer.EmitSeatStateChanged(21, 0, 2);
        buffer.EmitHorseRevealed(22, 3, 71);

        IReadOnlyList<SessionEvent> events = buffer.Drain(255);

        RoundStartedPayload roundStarted = SessionEventPayloadReader.ReadRoundStarted(events[0]);
        Assert.Equal(new RoundStartedPayload(3, 2, 84, 4), roundStarted);

        TurnStartedPayload turnStarted = SessionEventPayloadReader.ReadTurnStarted(events[1]);
        Assert.Equal(new TurnStartedPayload(2, 700), turnStarted);

        DealPayload deal = SessionEventPayloadReader.ReadDeal(events[2]);
        Assert.Equal((byte)1, deal.Seat);
        Assert.Equal((byte)3, deal.Count);
        Assert.Equal(new ushort[] { 1, 2, 3 }, deal.TileIds);

        HandSyncPayload handSync = SessionEventPayloadReader.ReadHandSync(events[3]);
        Assert.Equal((byte)1, handSync.Seat);
        Assert.Equal((byte)2, handSync.Count);
        Assert.Equal(new ushort[] { 4, 5 }, handSync.TileIds);

        DrewPayload drew = SessionEventPayloadReader.ReadDrew(events[4]);
        Assert.Equal(new DrewPayload(2, 21, 83), drew);

        DiscardedPayload discarded = SessionEventPayloadReader.ReadDiscarded(events[5]);
        Assert.Equal(new DiscardedPayload(1, 4, 21), discarded);

        MeldPayload meld = SessionEventPayloadReader.ReadMeld(events[6]);
        Assert.Equal((byte)3, meld.Seat);
        Assert.Equal(MeldKind.KongSupplement, meld.Kind);
        Assert.Equal(new ushort[] { 17, 18, 19, 20 }, meld.TileIds);
        Assert.Equal((byte)255, meld.TargetSeat);

        RequestOpenedPayload opened = SessionEventPayloadReader.ReadRequestOpened(events[7]);
        Assert.Equal(5L, opened.RequestId);
        Assert.Equal((byte)2, opened.Seat);
        Assert.Equal(RequestKind.Discard, opened.Kind);
        Assert.Equal(AllowedVerbs.Discard | AllowedVerbs.Kong, opened.Allowed);
        Assert.Equal(615L, opened.DeadlineTick);

        RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(events[8]);
        Assert.Equal(5L, closed.RequestId);
        Assert.Equal(RequestOutcomeKind.Answered, closed.Outcome);
        RequestAnswer answerPayload = Assert.IsType<RequestAnswer>(closed.Answer);
        Assert.Equal(CommandVerb.Kong, answerPayload.Verb);
        Assert.Equal((ushort)4, answerPayload.Slot);
        Assert.Equal((ushort)21, answerPayload.CardId);

        RoundEndedPayload roundEnded = SessionEventPayloadReader.ReadRoundEnded(events[9]);
        Assert.Equal((sbyte)2, roundEnded.WinnerSeat);
        Assert.Equal(new int[] { 10, -5, -3, -2 }, roundEnded.ScoreDelta);
        Assert.Equal(ScoreActionType.SelfDrawn, roundEnded.ActionType);

        GameEndedPayload gameEnded = SessionEventPayloadReader.ReadGameEnded(events[10]);
        Assert.Equal(new int[] { 100, -20, -30, -50 }, gameEnded.TotalScores);
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, gameEnded.Ranking);
        Assert.Equal("replay", gameEnded.ReplayId);

        SeatStateChangedPayload seatStateChanged = SessionEventPayloadReader.ReadSeatStateChanged(events[11]);
        Assert.Equal(new SeatStateChangedPayload(0, 2), seatStateChanged);

        HorseRevealedPayload horseRevealed = SessionEventPayloadReader.ReadHorseRevealed(events[12]);
        Assert.Equal(new HorseRevealedPayload(3, 71), horseRevealed);
    }

    [Fact]
    public void RequestClosed_TimedOut_HasNullAnswer()
    {
        var buffer = new SessionEventBuffer();
        var request = new PendingRequest(6, 1, RequestKind.Pong, AllowedVerbs.Pong | AllowedVerbs.Pass, 10);
        buffer.EmitRequestClosed(10, request, RequestOutcomeKind.TimedOut, null);

        RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(Assert.Single(buffer.Drain(255)));

        Assert.Equal(RequestOutcomeKind.TimedOut, closed.Outcome);
        Assert.Null(closed.Answer);
    }

    [Fact]
    public void Readers_RejectKindMismatch_WithArgumentException()
    {
        SessionEvent sessionEvent = Raw(SessionEventKind.SeatStateChanged, 0, 2);
        Action[] readers =
        [
            () => SessionEventPayloadReader.ReadRoundStarted(sessionEvent),
            () => SessionEventPayloadReader.ReadTurnStarted(sessionEvent),
            () => SessionEventPayloadReader.ReadDeal(sessionEvent),
            () => SessionEventPayloadReader.ReadHandSync(sessionEvent),
            () => SessionEventPayloadReader.ReadDrew(sessionEvent),
            () => SessionEventPayloadReader.ReadDiscarded(sessionEvent),
            () => SessionEventPayloadReader.ReadMeld(sessionEvent),
            () => SessionEventPayloadReader.ReadRequestOpened(sessionEvent),
            () => SessionEventPayloadReader.ReadRequestClosed(sessionEvent),
            () => SessionEventPayloadReader.ReadRoundEnded(sessionEvent),
            () => SessionEventPayloadReader.ReadGameEnded(sessionEvent),
            () => SessionEventPayloadReader.ReadHorseRevealed(sessionEvent),
        ];

        foreach (Action read in readers)
        {
            Assert.Throws<ArgumentException>(read);
        }
    }

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public void Readers_RejectInvalidPayload_WithInvalidDataException(SessionEventKind kind, byte[] payload)
    {
        SessionEvent sessionEvent = Raw(kind, payload);

        Assert.Throws<InvalidDataException>(() => ReadByKind(sessionEvent));
    }

    [Fact]
    public void ReadRoundStarted_AcceptsFiveBytePayload_AndRejectsLegacyFourBytes()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitRoundStarted(10, 3, 2, 84, 5);
        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        // 合法：5B 精确长度，第 5 字节是本局连庄次数。
        Assert.Equal(5, sessionEvent.Payload.Length);
        RoundStartedPayload parsed = SessionEventPayloadReader.ReadRoundStarted(sessionEvent);
        Assert.Equal(new RoundStartedPayload(3, 2, 84, 5), parsed);

        // 不兼容：旧 4B 载荷与更短的 3B 载荷一律按精确长度拒绝。
        Assert.Throws<InvalidDataException>(() => SessionEventPayloadReader.ReadRoundStarted(Raw(SessionEventKind.RoundStarted, 3, 2, 84, 0)));
        Assert.Throws<InvalidDataException>(() => SessionEventPayloadReader.ReadRoundStarted(Raw(SessionEventKind.RoundStarted, 3, 2, 84)));
        Assert.Throws<InvalidDataException>(() => SessionEventPayloadReader.ReadRoundStarted(Raw(SessionEventKind.RoundStarted, 3, 2)));
    }

    public static TheoryData<SessionEventKind, byte[]> InvalidPayloads => new()
    {
        { SessionEventKind.RoundStarted, new byte[] { 1, 2, 3 } },
        { SessionEventKind.RoundStarted, new byte[] { 1, 2, 3, 4 } },
        { SessionEventKind.TurnStarted, new byte[8] },
        { SessionEventKind.Deal, new byte[] { 1 } },
        { SessionEventKind.Deal, new byte[] { 1, 3, 1, 0, 2, 0 } },
        { SessionEventKind.HandSync, new byte[] { 1, 3, 1, 0 } },
        { SessionEventKind.Drew, new byte[9] },
        { SessionEventKind.Discarded, new byte[4] },
        { SessionEventKind.Meld, new byte[] { 3, 1, 2 } },
        { SessionEventKind.Meld, new byte[] { 3, 1, 2, 1, 0 } },
        { SessionEventKind.RequestOpened, new byte[19] },
        { SessionEventKind.RequestClosed, new byte[10] },
        { SessionEventKind.RequestClosed, new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 } },
        { SessionEventKind.RequestClosed, new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 1, 5, 0, 3, 2, 0, 21, 0 } },
        { SessionEventKind.RoundEnded, new byte[18] },
        { SessionEventKind.GameEnded, new byte[20] },
        { SessionEventKind.GameEnded, GameEndedWithReplayLength(2) },
        { SessionEventKind.GameEnded, GameEndedWithRanking(4) },
        { SessionEventKind.GameEnded, GameEndedWithInvalidUtf8() },
        { SessionEventKind.SeatStateChanged, new byte[] { 0 } },
        { SessionEventKind.HorseRevealed, new byte[] { 2, 21 } },
        { SessionEventKind.HorseRevealed, new byte[] { 2, 21, 0, 0 } },
    };

    [Fact]
    public void ReadDeal_ReturnsFreshArray_SoPayloadStaysImmutable()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitDeal(0, 1, Tiles(1, 2, 3));
        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        DealPayload first = SessionEventPayloadReader.ReadDeal(sessionEvent);
        first.TileIds[0] = 99;

        DealPayload second = SessionEventPayloadReader.ReadDeal(sessionEvent);
        Assert.Equal(new ushort[] { 1, 2, 3 }, second.TileIds);
    }

    private static SessionEvent Raw(SessionEventKind kind, params byte[] payload) =>
        new(1, 0, kind, payload);

    private static object ReadByKind(SessionEvent sessionEvent) => sessionEvent.Kind switch
    {
        SessionEventKind.RoundStarted => SessionEventPayloadReader.ReadRoundStarted(sessionEvent),
        SessionEventKind.TurnStarted => SessionEventPayloadReader.ReadTurnStarted(sessionEvent),
        SessionEventKind.Deal => SessionEventPayloadReader.ReadDeal(sessionEvent),
        SessionEventKind.HandSync => SessionEventPayloadReader.ReadHandSync(sessionEvent),
        SessionEventKind.Drew => SessionEventPayloadReader.ReadDrew(sessionEvent),
        SessionEventKind.Discarded => SessionEventPayloadReader.ReadDiscarded(sessionEvent),
        SessionEventKind.Meld => SessionEventPayloadReader.ReadMeld(sessionEvent),
        SessionEventKind.RequestOpened => SessionEventPayloadReader.ReadRequestOpened(sessionEvent),
        SessionEventKind.RequestClosed => SessionEventPayloadReader.ReadRequestClosed(sessionEvent),
        SessionEventKind.RoundEnded => SessionEventPayloadReader.ReadRoundEnded(sessionEvent),
        SessionEventKind.GameEnded => SessionEventPayloadReader.ReadGameEnded(sessionEvent),
        SessionEventKind.SeatStateChanged => SessionEventPayloadReader.ReadSeatStateChanged(sessionEvent),
        SessionEventKind.HorseRevealed => SessionEventPayloadReader.ReadHorseRevealed(sessionEvent),
        _ => throw new InvalidOperationException(),
    };

    private static byte[] GameEndedWithRanking(byte outOfRangeSeat)
    {
        byte[] payload = new byte[21];
        payload[16] = 0;
        payload[17] = 1;
        payload[18] = 2;
        payload[19] = outOfRangeSeat;
        return payload;
    }

    private static byte[] GameEndedWithReplayLength(byte replayLength)
    {
        byte[] payload = new byte[21];
        payload[16] = 0;
        payload[17] = 1;
        payload[18] = 2;
        payload[19] = 3;
        payload[20] = replayLength;
        return payload;
    }

    private static byte[] GameEndedWithInvalidUtf8()
    {
        byte[] payload = new byte[22];
        payload[16] = 0;
        payload[17] = 1;
        payload[18] = 2;
        payload[19] = 3;
        payload[20] = 1;
        payload[21] = 0xFF;
        return payload;
    }

    private static Tile[] Tiles(params int[] physicalIds) =>
        physicalIds.Select(RulesTestData.FromId).ToArray();
}
