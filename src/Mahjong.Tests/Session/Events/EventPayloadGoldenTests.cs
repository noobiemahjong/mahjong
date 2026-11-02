using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Rules;
using Xunit;

namespace Mahjong.Tests.Session.Events;

/// <summary>
/// 每种事件 payload 的逐字节金标：手算小端字节、按契约字段顺序紧排、无对齐。
/// 期望字节不调用实现生成；同时断言 Seq 从 1 起、Tick 保留。
/// </summary>
public sealed class EventPayloadGoldenTests
{
    [Fact]
    public void RoundStarted_EncodesRoundNoBankerWallCountAndDealerStreak()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitRoundStarted(tick: 10, roundNo: 3, bankerSeat: 2, wallCount: 84, dealerStreak: 1);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(1L, sessionEvent.Seq);
        Assert.Equal(10L, sessionEvent.Tick);
        Assert.Equal(SessionEventKind.RoundStarted, sessionEvent.Kind);
        Assert.Equal(new byte[] { 0x03, 0x02, 0x54, 0x00, 0x01 }, sessionEvent.Payload.ToArray());
        Assert.Equal(5, sessionEvent.Payload.Length);
    }

    [Fact]
    public void RoundStarted_EncodesDealerStreakAsTrailingByte()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitRoundStarted(tick: 20, roundNo: 8, bankerSeat: 3, wallCount: 83, dealerStreak: 4);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(new byte[] { 0x08, 0x03, 0x53, 0x00, 0x04 }, sessionEvent.Payload.ToArray());
        Assert.Equal((byte)4, SessionEventPayloadReader.ReadRoundStarted(sessionEvent).DealerStreak);
    }

    [Fact]
    public void TurnStarted_EncodesSeatAndDeadlineAsLittleEndianInt64()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitTurnStarted(tick: 11, seat: 2, deadlineTick: 700);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(
            new byte[] { 0x02, 0xBC, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            sessionEvent.Payload.ToArray());
    }

    [Fact]
    public void Deal_EncodesSeatCountAndLittleEndianTileIds()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitDeal(tick: 10, seat: 1, tiles: Tiles(1, 2, 3));

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(1));

        Assert.Equal(
            new byte[] { 0x01, 0x03, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00 },
            sessionEvent.Payload.ToArray());
    }

    [Fact]
    public void HandSync_EncodesSameLayoutAsDeal()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitHandSync(tick: 10, seat: 1, tiles: Tiles(1, 2, 3));

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(SessionEventKind.HandSync, sessionEvent.Kind);
        Assert.Equal(
            new byte[] { 0x01, 0x03, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00 },
            sessionEvent.Payload.ToArray());
    }

    [Fact]
    public void Drew_EncodesSeatInt32TileIdAndWallCount()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitDrew(tick: 12, seat: 2, tileId: 21, wallCount: 83);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(
            new byte[] { 0x02, 0x15, 0x00, 0x00, 0x00, 0x53, 0x00 },
            sessionEvent.Payload.ToArray());
    }

    [Fact]
    public void Discarded_EncodesSeatSlotAndTileId()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitDiscarded(tick: 13, seat: 1, slot: 4, tileId: 21);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(new byte[] { 0x01, 0x04, 0x00, 0x15, 0x00 }, sessionEvent.Payload.ToArray());
    }

    [Fact]
    public void Meld_EncodesSeatKindCountTileIdsAndTargetSeat()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitMeld(
            tick: 14,
            seat: 3,
            kind: MeldKind.KongSupplement,
            tiles: Tiles(17, 18, 19, 20),
            targetSeat: 255);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(
            new byte[]
            {
                0x03, 0x04, 0x04,
                0x11, 0x00, 0x12, 0x00, 0x13, 0x00, 0x14, 0x00,
                0xFF,
            },
            sessionEvent.Payload.ToArray());
    }

    [Fact]
    public void RequestOpened_EncodesRequestIdSeatKindAllowedAndDeadline()
    {
        var buffer = new SessionEventBuffer();
        var request = new PendingRequest(
            RequestId: 7,
            Seat: 2,
            Kind: RequestKind.Discard,
            Allowed: AllowedVerbs.Discard | AllowedVerbs.Kong,
            DeadlineTick: 615);
        buffer.EmitRequestOpened(tick: 15, request);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(
            new byte[]
            {
                0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x02,
                0x00,
                0x05, 0x00,
                0x67, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            },
            sessionEvent.Payload.ToArray());
    }

    [Fact]
    public void RequestClosed_Answered_CarriesFiveByteAnswerCommand()
    {
        var buffer = new SessionEventBuffer();
        var request = new PendingRequest(9, 1, RequestKind.Pong, AllowedVerbs.Pong | AllowedVerbs.Kong | AllowedVerbs.Pass, 300);
        var answer = new GameCommand(Seq: 4, ReceivedTick: 15, Seat: 1, Verb: CommandVerb.Kong, Slot: 2, CardId: 21, RequestId: 9);
        buffer.EmitRequestClosed(tick: 16, request, RequestOutcomeKind.Answered, answer);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(
            new byte[]
            {
                0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00,
                0x05, 0x00,
                0x03, 0x02, 0x00, 0x15, 0x00,
            },
            sessionEvent.Payload.ToArray());
    }

    [Fact]
    public void RequestClosed_TimedOut_HasZeroLengthAnswer()
    {
        var buffer = new SessionEventBuffer();
        var request = new PendingRequest(10, 1, RequestKind.Pong, AllowedVerbs.Pong | AllowedVerbs.Kong | AllowedVerbs.Pass, 300);
        buffer.EmitRequestClosed(tick: 300, request, RequestOutcomeKind.TimedOut, answer: null);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(
            new byte[]
            {
                0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x01,
                0x00, 0x00,
            },
            sessionEvent.Payload.ToArray());
    }

    [Fact]
    public void RoundEnded_EncodesWinnerSignedByteScoresAndActionType()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitRoundEnded(
            tick: 100,
            winnerSeat: 2,
            scoreDelta: new short[] { 10, -5, -3, -2 },
            actionType: ScoreActionType.SelfDrawn | ScoreActionType.SevenPairs);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(
            new byte[]
            {
                0x02,
                0x0A, 0x00, 0x00, 0x00,
                0xFB, 0xFF, 0xFF, 0xFF,
                0xFD, 0xFF, 0xFF, 0xFF,
                0xFE, 0xFF, 0xFF, 0xFF,
                0x03, 0x00,
            },
            sessionEvent.Payload.ToArray());
    }

    [Fact]
    public void RoundEnded_Draw_UsesWinnerMinusOne()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitRoundEnded(
            tick: 100,
            winnerSeat: -1,
            scoreDelta: new short[] { 0, 0, 0, 0 },
            actionType: ScoreActionType.None);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(0xFF, sessionEvent.Payload.Span[0]);
        Assert.Equal(19, sessionEvent.Payload.Length);
    }

    [Fact]
    public void GameEnded_EncodesScoresRankingAndEmptyReplayId()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitGameEnded(
            tick: 101,
            totalScores: new short[] { 100, -20, -30, -50 },
            ranking: new byte[] { 0, 1, 2, 3 },
            replayId: string.Empty);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(
            new byte[]
            {
                0x64, 0x00, 0x00, 0x00,
                0xEC, 0xFF, 0xFF, 0xFF,
                0xE2, 0xFF, 0xFF, 0xFF,
                0xCE, 0xFF, 0xFF, 0xFF,
                0x00, 0x01, 0x02, 0x03,
                0x00,
            },
            sessionEvent.Payload.ToArray());
    }

    [Fact]
    public void GameEnded_EncodesUtf8ReplayIdWithLengthPrefix()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitGameEnded(
            tick: 101,
            totalScores: new short[] { 0, 0, 0, 0 },
            ranking: new byte[] { 0, 1, 2, 3 },
            replayId: "ab");

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        byte[] payload = sessionEvent.Payload.ToArray();
        Assert.Equal(23, payload.Length);
        Assert.Equal(0x02, payload[20]);
        Assert.Equal(new byte[] { 0x61, 0x62 }, payload[21..]);
    }

    [Fact]
    public void GameEnded_MultiByteUtf8ReplayId_RoundTripsThroughReader()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitGameEnded(
            tick: 101,
            totalScores: new short[] { 0, 0, 0, 0 },
            ranking: new byte[] { 0, 1, 2, 3 },
            replayId: "回放");

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));
        byte[] payload = sessionEvent.Payload.ToArray();

        // "回放" 的 UTF-8 为 E5 9B 9E E6 94 BE（6 字节），长度前缀 0x06。
        Assert.Equal(27, payload.Length);
        Assert.Equal(0x06, payload[20]);
        Assert.Equal(new byte[] { 0xE5, 0x9B, 0x9E, 0xE6, 0x94, 0xBE }, payload[21..]);

        GameEndedPayload decoded = SessionEventPayloadReader.ReadGameEnded(sessionEvent);
        Assert.Equal("回放", decoded.ReplayId);
        Assert.Equal(new int[] { 0, 0, 0, 0 }, decoded.TotalScores);
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, decoded.Ranking);
    }

    [Fact]
    public void SeatStateChanged_EncodesSeatAndState()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitSeatStateChanged(tick: 1, seat: 0, state: 2);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));

        Assert.Equal(new byte[] { 0x00, 0x02 }, sessionEvent.Payload.ToArray());
    }

    private static Tile[] Tiles(params int[] physicalIds) =>
        physicalIds.Select(RulesTestData.FromId).ToArray();
}
