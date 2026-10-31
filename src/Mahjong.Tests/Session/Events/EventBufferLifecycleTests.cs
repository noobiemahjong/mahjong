using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Rules;
using Xunit;

namespace Mahjong.Tests.Session.Events;

/// <summary>
/// 事件缓冲生命周期：seq 在 Emit 时从 1 单调分配、Tick 保留、Drain 破坏性且不重复、
/// viewer 校验、已取出的 payload 不被后续写入改变、Emit 参数校验。
/// </summary>
public sealed class EventBufferLifecycleTests
{
    [Fact]
    public void Seq_StartsAtOne_IsMonotonic_AndContinuesAfterDrain()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitSeatStateChanged(tick: 1, seat: 0, state: 2);
        buffer.EmitSeatStateChanged(tick: 1, seat: 1, state: 2);

        IReadOnlyList<SessionEvent> first = buffer.Drain(255);
        Assert.Equal(new long[] { 1, 2 }, first.Select(sessionEvent => sessionEvent.Seq));
        Assert.Equal(new long[] { 1, 1 }, first.Select(sessionEvent => sessionEvent.Tick));

        buffer.EmitSeatStateChanged(tick: 2, seat: 2, state: 2);
        SessionEvent third = Assert.Single(buffer.Drain(255));
        Assert.Equal(3L, third.Seq);
        Assert.Equal(2L, third.Tick);
    }

    [Fact]
    public void Drain_IsDestructive_SecondCallReturnsEmpty()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitSeatStateChanged(tick: 1, seat: 0, state: 2);

        Assert.Single(buffer.Drain(255));
        Assert.Empty(buffer.Drain(255));
        Assert.Empty(buffer.Drain(0));
    }

    [Fact]
    public void Drain_EmptyBuffer_ReturnsEmptyListNotNull()
    {
        var buffer = new SessionEventBuffer();

        IReadOnlyList<SessionEvent> events = buffer.Drain(255);

        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(255)]
    public void Drain_AcceptsValidViewers(byte viewer)
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitSeatStateChanged(tick: 1, seat: 0, state: 2);

        Assert.Single(buffer.Drain(viewer));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(128)]
    [InlineData(254)]
    public void Drain_RejectsInvalidViewers(byte viewer)
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitSeatStateChanged(tick: 1, seat: 0, state: 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Drain(viewer));
        Assert.Single(buffer.Drain(255));
    }

    [Fact]
    public void DrainedPayload_IsNotChangedByLaterEmits()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitDiscarded(tick: 1, seat: 2, slot: 3, tileId: 21);
        SessionEvent first = Assert.Single(buffer.Drain(255));
        byte[] original = first.Payload.ToArray();

        buffer.EmitDiscarded(tick: 2, seat: 0, slot: 0, tileId: 5);
        buffer.EmitGameEnded(tick: 3, new short[] { 0, 0, 0, 0 }, new byte[] { 0, 1, 2, 3 }, string.Empty);

        Assert.Equal(original, first.Payload.ToArray());
        Assert.Equal(original, first.Payload.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(255)]
    public void Meld_TargetSeat_RoundTrips(byte targetSeat)
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitMeld(tick: 5, seat: 2, kind: MeldKind.Pong, tiles: Tiles(1, 2, 3), targetSeat: targetSeat);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(255));
        MeldPayload decoded = SessionEventPayloadReader.ReadMeld(sessionEvent);

        Assert.Equal((byte)2, decoded.Seat);
        Assert.Equal(MeldKind.Pong, decoded.Kind);
        Assert.Equal(new ushort[] { 1, 2, 3 }, decoded.TileIds);
        Assert.Equal(targetSeat, decoded.TargetSeat);
    }

    [Fact]
    public void EmitRoundEnded_RejectsScoreSequenceWithWrongLength()
    {
        var buffer = new SessionEventBuffer();

        Assert.Throws<ArgumentNullException>(
            () => buffer.EmitRoundEnded(0, 0, null!, ScoreActionType.None));
        Assert.Throws<ArgumentException>(
            () => buffer.EmitRoundEnded(0, 0, new short[] { 1, 2, 3 }, ScoreActionType.None));
    }

    [Fact]
    public void EmitGameEnded_RejectsInvalidSequencesAndReplayId()
    {
        var buffer = new SessionEventBuffer();

        Assert.Throws<ArgumentNullException>(
            () => buffer.EmitGameEnded(0, null!, new byte[] { 0, 1, 2, 3 }, string.Empty));
        Assert.Throws<ArgumentNullException>(
            () => buffer.EmitGameEnded(0, new short[] { 0, 0, 0, 0 }, null!, string.Empty));
        Assert.Throws<ArgumentNullException>(
            () => buffer.EmitGameEnded(0, new short[] { 0, 0, 0, 0 }, new byte[] { 0, 1, 2, 3 }, null!));
        Assert.Throws<ArgumentException>(
            () => buffer.EmitGameEnded(0, new short[] { 0, 0, 0 }, new byte[] { 0, 1, 2, 3 }, string.Empty));
        Assert.Throws<ArgumentException>(
            () => buffer.EmitGameEnded(0, new short[] { 0, 0, 0, 0 }, new byte[] { 0, 1, 2 }, string.Empty));
        Assert.Throws<ArgumentException>(
            () => buffer.EmitGameEnded(0, new short[] { 0, 0, 0, 0 }, new byte[] { 0, 1, 2, 4 }, string.Empty));
        Assert.Throws<ArgumentException>(
            () => buffer.EmitGameEnded(0, new short[] { 0, 0, 0, 0 }, new byte[] { 0, 1, 2, 3 }, new string('x', 256)));
    }

    [Fact]
    public void EmitDealAndMeld_RejectNullTileLists()
    {
        var buffer = new SessionEventBuffer();

        Assert.Throws<ArgumentNullException>(() => buffer.EmitDeal(0, 0, null!));
        Assert.Throws<ArgumentNullException>(() => buffer.EmitHandSync(0, 0, null!));
        Assert.Throws<ArgumentNullException>(
            () => buffer.EmitMeld(0, 0, MeldKind.Pong, null!, 255));
    }

    [Fact]
    public void EmitRequestClosed_AnsweredWithoutCommand_IsRejected()
    {
        var buffer = new SessionEventBuffer();
        var request = new PendingRequest(1, 1, RequestKind.Pong, AllowedVerbs.Pong | AllowedVerbs.Pass, 10);

        Assert.Throws<ArgumentException>(
            () => buffer.EmitRequestClosed(0, request, RequestOutcomeKind.Answered, answer: null));
        Assert.Empty(buffer.Drain(255));
    }

    private static Tile[] Tiles(params int[] physicalIds) =>
        physicalIds.Select(RulesTestData.FromId).ToArray();
}
