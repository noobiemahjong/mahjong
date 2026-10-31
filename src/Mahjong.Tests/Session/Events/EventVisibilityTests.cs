using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Rules;
using Xunit;

namespace Mahjong.Tests.Session.Events;

/// <summary>
/// 可见性只在 Deal/HandSync/Drew 编码时生效：viewer 为该事件座位或 255 才给牌 id；
/// 其他座位只得到真实 count / tileId=-1。其余事件对任何 viewer 都全量编码。
/// </summary>
public sealed class EventVisibilityTests
{
    [Fact]
    public void Deal_OwnerAndFullViewSeeTileIds_OthersSeeCountOnly()
    {
        DealPayload owner = ReadDeal(seat: 1, viewer: 1);
        Assert.Equal((byte)1, owner.Seat);
        Assert.Equal((byte)3, owner.Count);
        Assert.Equal(new ushort[] { 1, 2, 3 }, owner.TileIds);

        DealPayload full = ReadDeal(seat: 1, viewer: 255);
        Assert.Equal(new ushort[] { 1, 2, 3 }, full.TileIds);

        DealPayload other = ReadDeal(seat: 1, viewer: 0);
        Assert.Equal((byte)1, other.Seat);
        Assert.Equal((byte)3, other.Count);
        Assert.Empty(other.TileIds);
    }

    [Fact]
    public void Deal_HiddenEncoding_IsTwoBytes_AndKeepsRealCount()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitDeal(tick: 0, seat: 2, tiles: ThirteenTiles());

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(0));

        Assert.Equal(2, sessionEvent.Payload.Length);
        Assert.Equal(new byte[] { 0x02, 0x0D }, sessionEvent.Payload.ToArray());
        DealPayload decoded = SessionEventPayloadReader.ReadDeal(sessionEvent);
        Assert.Equal((byte)13, decoded.Count);
        Assert.Empty(decoded.TileIds);
    }

    [Fact]
    public void HandSync_OwnerAndFullViewSeeTileIds_OthersSeeCountOnly()
    {
        HandSyncPayload owner = ReadHandSync(seat: 3, viewer: 3);
        Assert.Equal((byte)3, owner.Seat);
        Assert.Equal((byte)3, owner.Count);
        Assert.Equal(new ushort[] { 1, 2, 3 }, owner.TileIds);

        HandSyncPayload full = ReadHandSync(seat: 3, viewer: 255);
        Assert.Equal(new ushort[] { 1, 2, 3 }, full.TileIds);

        HandSyncPayload other = ReadHandSync(seat: 3, viewer: 2);
        Assert.Equal((byte)3, other.Seat);
        Assert.Equal((byte)3, other.Count);
        Assert.Empty(other.TileIds);
    }

    [Fact]
    public void Drew_OwnerAndFullViewGetRealId_OthersGetMinusOne()
    {
        Assert.Equal(21, ReadDrew(seat: 2, viewer: 2).TileId);
        Assert.Equal(21, ReadDrew(seat: 2, viewer: 255).TileId);

        DrewPayload hidden = ReadDrew(seat: 2, viewer: 0);
        Assert.Equal((byte)2, hidden.Seat);
        Assert.Equal(-1, hidden.TileId);
        Assert.Equal((ushort)83, hidden.WallCount);
    }

    [Fact]
    public void Drew_PayloadLengthIsSevenBytesForEveryViewer()
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitDrew(tick: 0, seat: 2, tileId: 21, wallCount: 83);

        SessionEvent sessionEvent = Assert.Single(buffer.Drain(0));

        Assert.Equal(7, sessionEvent.Payload.Length);
    }

    [Fact]
    public void OtherEventKinds_AreNotFilteredByViewer()
    {
        byte[] asSeatZero = DrainOtherEvents(0);
        byte[] asFullView = DrainOtherEvents(255);

        Assert.Equal(asFullView, asSeatZero);
    }

    private static byte[] DrainOtherEvents(byte viewer)
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitDiscarded(tick: 1, seat: 1, slot: 0, tileId: 21);
        buffer.EmitMeld(tick: 2, seat: 1, kind: MeldKind.Pong, tiles: Tiles(1, 2, 3), targetSeat: 2);
        buffer.EmitRequestOpened(tick: 3, new PendingRequest(1, 1, RequestKind.Pong, AllowedVerbs.Pong | AllowedVerbs.Pass, 10));
        buffer.EmitSeatStateChanged(tick: 4, seat: 1, state: 2);

        IReadOnlyList<SessionEvent> events = buffer.Drain(viewer);
        return events.SelectMany(sessionEvent => sessionEvent.Payload.ToArray()).ToArray();
    }

    private static DealPayload ReadDeal(byte seat, byte viewer)
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitDeal(tick: 0, seat: seat, tiles: Tiles(1, 2, 3));
        SessionEvent sessionEvent = Assert.Single(buffer.Drain(viewer));
        return SessionEventPayloadReader.ReadDeal(sessionEvent);
    }

    private static HandSyncPayload ReadHandSync(byte seat, byte viewer)
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitHandSync(tick: 0, seat: seat, tiles: Tiles(1, 2, 3));
        SessionEvent sessionEvent = Assert.Single(buffer.Drain(viewer));
        return SessionEventPayloadReader.ReadHandSync(sessionEvent);
    }

    private static DrewPayload ReadDrew(byte seat, byte viewer)
    {
        var buffer = new SessionEventBuffer();
        buffer.EmitDrew(tick: 0, seat: seat, tileId: 21, wallCount: 83);
        SessionEvent sessionEvent = Assert.Single(buffer.Drain(viewer));
        return SessionEventPayloadReader.ReadDrew(sessionEvent);
    }

    private static Tile[] Tiles(params int[] physicalIds) =>
        physicalIds.Select(RulesTestData.FromId).ToArray();

    private static Tile[] ThirteenTiles() =>
        Enumerable.Range(1, 13).Select(RulesTestData.FromId).ToArray();
}
