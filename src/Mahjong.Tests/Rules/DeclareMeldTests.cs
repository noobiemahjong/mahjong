using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// DeclareMeld 契约：把指定手牌槽位（Hand.Items 下标，手牌槽）与目标座位弃牌槽位（Discards.Items 下标，弃牌槽）
/// 移入副露；碰/明杠/暗杠/补杠四态的槽数、明暗与目标座位规则；补杠原地升级既有 Pong 且不新增副露；
/// 修改状态前完成全部校验，失败不改任何状态；操作前后全部区域物理牌总数守恒。
/// </summary>
public sealed class DeclareMeldTests
{
    [Fact]
    public void DeclarePong_AddsThreeTileOpenedMeldAndConsumesSources()
    {
        GameState state = PongReadyBoard();
        int before = RulesTestData.TotalTileCount(state);

        Meld meld = state.DeclareMeld(0, MeldKind.Pong, [1, 3], 1, 2);

        Assert.Equal(MeldKind.Pong, meld.Kind);
        Assert.True(meld.Opened);
        Assert.Equal(1, (int)meld.TargetSeat);
        Assert.Equal(new ushort[] { 21, 22, 23 }, SortedIds(meld.Tiles));
        Assert.Single(state.Players[0].Melds);
        Assert.Same(meld, state.Players[0].Melds[0]);
        Assert.Equal(new ushort[] { 1, 25, 29 }, RulesTestData.Ids(state.Players[0].Hand.Items));
        Assert.Equal(new ushort[] { 33, 37 }, RulesTestData.Ids(state.Players[1].Discards.Items));
        Assert.Equal(before, RulesTestData.TotalTileCount(state));
        Assert.Equal(0, state.Wall.Count);
        Assert.Equal(0, state.WallTail.Count);
        Assert.Empty(state.Players[2].Hand.Items);
        Assert.Empty(state.Players[3].Hand.Items);
    }

    [Fact]
    public void DeclarePong_RequiresTargetDiscardSlot()
    {
        GameState state = PongReadyBoard();

        Assert.Throws<ArgumentException>(() => state.DeclareMeld(0, MeldKind.Pong, [1, 3], 1));
    }

    [Fact]
    public void DeclarePong_RejectsTargetSeatEqualToOwnSeat()
    {
        GameState state = PongReadyBoard();

        Assert.Throws<ArgumentException>(() => state.DeclareMeld(0, MeldKind.Pong, [1, 3], 0, 2));
    }

    [Fact]
    public void DeclarePong_RejectsPublicTargetSeat()
    {
        GameState state = PongReadyBoard();

        Assert.Throws<ArgumentException>(
            () => state.DeclareMeld(0, MeldKind.Pong, [1, 3], Meld.NoTargetSeat, 2));
    }

    [Fact]
    public void DeclarePong_RejectsTargetSeatOutOfRange()
    {
        GameState state = PongReadyBoard();

        foreach (byte seat in new byte[] { 4, 200, 254 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => state.DeclareMeld(0, MeldKind.Pong, [1, 3], seat, 2));
        }
    }

    [Fact]
    public void DeclarePong_RejectsDiscardSlotOutOfRange()
    {
        GameState state = PongReadyBoard();

        Assert.Throws<InvalidOperationException>(() => state.DeclareMeld(0, MeldKind.Pong, [1, 3], 1, 3));
        Assert.Throws<InvalidOperationException>(() => state.DeclareMeld(0, MeldKind.Pong, [1, 3], 1, -1));
        Assert.Throws<InvalidOperationException>(() => state.DeclareMeld(0, MeldKind.Pong, [1, 3], 1, 99));
    }

    [Fact]
    public void DeclarePong_RejectsDiscardTypeMismatch()
    {
        GameState state = PongReadyBoard();

        // 目标座位弃牌槽 1 是 9 万（id 37），与手牌的 5 万（id 21/22）牌型不同。
        Assert.Throws<InvalidOperationException>(() => state.DeclareMeld(0, MeldKind.Pong, [1, 3], 1, 1));
    }

    [Fact]
    public void DeclarePong_RejectsHandTypeMismatch()
    {
        GameState state = PongReadyBoard();

        // 槽 1 是 5 万（id 21），槽 2 是 6 万（id 25）。
        Assert.Throws<InvalidOperationException>(() => state.DeclareMeld(0, MeldKind.Pong, [1, 2], 1, 2));
    }

    [Fact]
    public void DeclarePong_RejectsDuplicateHandSlots()
    {
        GameState state = PongReadyBoard();

        Assert.Throws<ArgumentException>(() => state.DeclareMeld(0, MeldKind.Pong, [1, 1], 1, 2));
    }

    [Fact]
    public void DeclarePong_RejectsWrongHandSlotCount()
    {
        GameState state = PongReadyBoard();

        int[][] wrongSlotCounts = [[], [0], [0, 1, 2], [0, 1, 2, 3]];
        foreach (int[] slots in wrongSlotCounts)
        {
            Assert.Throws<ArgumentException>(() => state.DeclareMeld(0, MeldKind.Pong, slots, 1, 2));
        }
    }

    [Fact]
    public void DeclarePong_RejectsHandSlotOutOfRange()
    {
        GameState state = PongReadyBoard();

        Assert.Throws<InvalidOperationException>(() => state.DeclareMeld(0, MeldKind.Pong, [1, 9], 1, 2));
    }

    [Fact]
    public void DeclareKongExposed_AddsFourTileOpenedMeld()
    {
        GameState state = KongExposedReadyBoard();
        int before = RulesTestData.TotalTileCount(state);

        Meld meld = state.DeclareMeld(0, MeldKind.KongExposed, [1, 2, 3], 1, 0);

        Assert.Equal(MeldKind.KongExposed, meld.Kind);
        Assert.True(meld.Opened);
        Assert.Equal(1, (int)meld.TargetSeat);
        Assert.Equal(new ushort[] { 21, 22, 23, 24 }, SortedIds(meld.Tiles));
        Assert.Equal(new ushort[] { 1 }, RulesTestData.Ids(state.Players[0].Hand.Items));
        Assert.Empty(state.Players[1].Discards.Items);
        Assert.Equal(before, RulesTestData.TotalTileCount(state));
    }

    [Fact]
    public void DeclareKongExposed_RejectsWrongHandSlotCount()
    {
        GameState state = KongExposedReadyBoard();

        int[][] wrongSlotCounts = [[], [1, 2], [0, 1, 2, 3]];
        foreach (int[] slots in wrongSlotCounts)
        {
            Assert.Throws<ArgumentException>(() => state.DeclareMeld(0, MeldKind.KongExposed, slots, 1, 0));
        }
    }

    [Fact]
    public void DeclareKongExposed_RequiresTargetDiscardSlotFromOtherSeat()
    {
        GameState state = KongExposedReadyBoard();

        Assert.Throws<ArgumentException>(() => state.DeclareMeld(0, MeldKind.KongExposed, [1, 2, 3], 1));
        Assert.Throws<ArgumentException>(
            () => state.DeclareMeld(0, MeldKind.KongExposed, [1, 2, 3], 0, 0));
        Assert.Throws<ArgumentException>(
            () => state.DeclareMeld(0, MeldKind.KongExposed, [1, 2, 3], Meld.NoTargetSeat, 0));
    }

    [Fact]
    public void DeclareKongConcealed_AddsClosedMeldWithoutTarget()
    {
        GameState state = KongConcealedReadyBoard();
        int before = RulesTestData.TotalTileCount(state);

        Meld meld = state.DeclareMeld(0, MeldKind.KongConcealed, [0, 1, 2, 3], Meld.NoTargetSeat);

        Assert.Equal(MeldKind.KongConcealed, meld.Kind);
        Assert.False(meld.Opened);
        Assert.Equal(255, (int)meld.TargetSeat);
        Assert.Equal(new ushort[] { 21, 22, 23, 24 }, SortedIds(meld.Tiles));
        Assert.Equal(new ushort[] { 1 }, RulesTestData.Ids(state.Players[0].Hand.Items));
        Assert.Equal(before, RulesTestData.TotalTileCount(state));
    }

    [Fact]
    public void DeclareKongConcealed_RejectsTargetSeatOrDiscardSlot()
    {
        GameState state = KongConcealedReadyBoard();

        Assert.Throws<ArgumentException>(
            () => state.DeclareMeld(0, MeldKind.KongConcealed, [0, 1, 2, 3], 1));
        Assert.Throws<ArgumentException>(
            () => state.DeclareMeld(0, MeldKind.KongConcealed, [0, 1, 2, 3], Meld.NoTargetSeat, 0));
    }

    [Fact]
    public void DeclareKongConcealed_RejectsWrongHandSlotCount()
    {
        GameState state = KongConcealedReadyBoard();

        int[][] wrongSlotCounts = [[], [0, 1, 2], [0, 1, 2, 3, 4]];
        foreach (int[] slots in wrongSlotCounts)
        {
            Assert.Throws<ArgumentException>(
                () => state.DeclareMeld(0, MeldKind.KongConcealed, slots, Meld.NoTargetSeat));
        }
    }

    [Fact]
    public void DeclareKongSupplement_UpgradesExistingPongInPlace()
    {
        GameState state = SupplementReadyBoard(out Meld pong);
        ushort[] tilesBefore = RulesTestData.Ids(pong.Tiles);
        int before = RulesTestData.TotalTileCount(state);

        Meld upgraded = state.DeclareMeld(0, MeldKind.KongSupplement, [1], Meld.NoTargetSeat);

        Assert.Same(pong, upgraded);
        Assert.Single(state.Players[0].Melds);
        Assert.Same(pong, state.Players[0].Melds[0]);
        Assert.Equal(MeldKind.KongSupplement, upgraded.Kind);
        Assert.Equal(4, upgraded.Tiles.Count);
        Assert.Equal(tilesBefore, RulesTestData.Ids(upgraded.Tiles).Take(3));
        Assert.Equal(24, (int)upgraded.Tiles[3].Id);
        Assert.True(upgraded.Opened);
        Assert.Equal(1, (int)upgraded.TargetSeat);
        Assert.Equal(new ushort[] { 1 }, RulesTestData.Ids(state.Players[0].Hand.Items));
        Assert.Equal(before, RulesTestData.TotalTileCount(state));
    }

    [Fact]
    public void DeclareKongSupplement_RequiresNoTargetSeatOrDiscardSlot()
    {
        GameState state = SupplementReadyBoard(out _);

        Assert.Throws<ArgumentException>(() => state.DeclareMeld(0, MeldKind.KongSupplement, [1], 1));
        Assert.Throws<ArgumentException>(
            () => state.DeclareMeld(0, MeldKind.KongSupplement, [1], Meld.NoTargetSeat, 0));
    }

    [Fact]
    public void DeclareKongSupplement_RejectsWrongHandSlotCount()
    {
        GameState state = SupplementReadyBoard(out _);

        int[][] wrongSlotCounts = [[], [0, 1]];
        foreach (int[] slots in wrongSlotCounts)
        {
            Assert.Throws<ArgumentException>(
                () => state.DeclareMeld(0, MeldKind.KongSupplement, slots, Meld.NoTargetSeat));
        }
    }

    [Fact]
    public void DeclareKongSupplement_RejectsHandSlotOutOfRange()
    {
        GameState state = SupplementReadyBoard(out _);
        string before = RulesTestData.Snapshot(state);

        Assert.Throws<InvalidOperationException>(
            () => state.DeclareMeld(0, MeldKind.KongSupplement, [9], Meld.NoTargetSeat));

        Assert.Equal(before, RulesTestData.Snapshot(state));
    }

    [Fact]
    public void DeclareKongSupplement_WithoutMatchingPongThrows()
    {
        GameState state = SupplementReadyBoard(out _);

        // 手牌槽 0 是 1 万（id 1），没有同牌型的 Pong 可升级。
        Assert.Throws<InvalidOperationException>(
            () => state.DeclareMeld(0, MeldKind.KongSupplement, [0], Meld.NoTargetSeat));
    }

    [Fact]
    public void DeclareKongSupplement_IgnoresMeldsOfOtherTypes()
    {
        var state = new GameState();
        state.Players[0].Melds.Add(
            new Meld(MeldKind.Pong, RulesTestData.Copies(5).Take(3).ToArray(), true, 1));
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(6, 0));
        string before = RulesTestData.Snapshot(state);

        Assert.Throws<InvalidOperationException>(
            () => state.DeclareMeld(0, MeldKind.KongSupplement, [0], Meld.NoTargetSeat));

        Assert.Equal(before, RulesTestData.Snapshot(state));
    }

    [Fact]
    public void DeclareKongSupplement_UpgradesPongNotKong()
    {
        var state = new GameState();
        var exposedKong = new Meld(MeldKind.KongExposed, RulesTestData.Copies(5), true, 1);
        state.Players[0].Melds.Add(exposedKong);
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(6, 0), RulesTestData.Physical(6, 1));
        state.Players[1].Discards.Append(RulesTestData.Physical(6, 2));
        Meld pong = state.DeclareMeld(0, MeldKind.Pong, [0, 1], 1, 0);
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(6, 3));

        Meld upgraded = state.DeclareMeld(0, MeldKind.KongSupplement, [0], Meld.NoTargetSeat);

        Assert.Same(pong, upgraded);
        Assert.Equal(MeldKind.KongSupplement, upgraded.Kind);
        Assert.Equal(MeldKind.KongExposed, exposedKong.Kind);
        Assert.Equal(4, exposedKong.Tiles.Count);
        Assert.Equal(2, state.Players[0].Melds.Count);
    }

    [Fact]
    public void DeclareKongSupplement_SelectsPongByTypeIdNotFirstPong()
    {
        var state = new GameState();
        var type5Pong = new Meld(MeldKind.Pong, RulesTestData.Copies(5).Take(3).ToArray(), true, 1);
        var type6Pong = new Meld(MeldKind.Pong, RulesTestData.Copies(6).Take(3).ToArray(), true, 2);
        state.Players[0].Melds.Add(type5Pong);
        state.Players[0].Melds.Add(type6Pong);
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(0, 0), RulesTestData.Physical(6, 3));
        int before = RulesTestData.TotalTileCount(state);

        Meld upgraded = state.DeclareMeld(0, MeldKind.KongSupplement, [1], Meld.NoTargetSeat);

        Assert.Same(type6Pong, upgraded);
        Assert.Equal(MeldKind.KongSupplement, type6Pong.Kind);
        Assert.Equal(4, type6Pong.Tiles.Count);
        Assert.Equal(new ushort[] { 25, 26, 27 }, RulesTestData.Ids(type6Pong.Tiles).Take(3));
        Assert.Equal(28, (int)type6Pong.Tiles[3].Id);
        Assert.True(type6Pong.Opened);
        Assert.Equal(2, (int)type6Pong.TargetSeat);

        // 第一副（type5）Pong 不受影响：杀掉“取第一个 Pong、忽略 Type.Id”的变异。
        Assert.Equal(MeldKind.Pong, type5Pong.Kind);
        Assert.Equal(3, type5Pong.Tiles.Count);
        Assert.Equal(new ushort[] { 21, 22, 23 }, RulesTestData.Ids(type5Pong.Tiles));

        Assert.Equal(2, state.Players[0].Melds.Count);
        Assert.Equal(new ushort[] { 1 }, RulesTestData.Ids(state.Players[0].Hand.Items));
        Assert.Equal(before, RulesTestData.TotalTileCount(state));
    }

    [Fact]
    public void DeclareMeld_RejectsSeatOutOfRange()
    {
        GameState state = PongReadyBoard();
        string before = RulesTestData.Snapshot(state);

        Assert.Throws<ArgumentOutOfRangeException>(() => state.DeclareMeld(4, MeldKind.Pong, [1, 3], 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.DeclareMeld(255, MeldKind.Pong, [1, 3], 1, 2));

        Assert.Equal(before, RulesTestData.Snapshot(state));
    }

    [Fact]
    public void DeclareMeld_RejectsUndefinedKind()
    {
        GameState state = PongReadyBoard();

        foreach (MeldKind kind in new[] { (MeldKind)0, (MeldKind)5, (MeldKind)255 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => state.DeclareMeld(0, kind, [1, 3], 1, 2));
        }
    }

    [Fact]
    public void DeclareMeld_HandSlotsUseHandDomainNotDiscardsDomain()
    {
        var state = new Board()
            .Hand(0, RulesTestData.Physical(0, 0), RulesTestData.Physical(5, 0), RulesTestData.Physical(5, 1))
            .Discards(1, RulesTestData.Physical(1, 0), RulesTestData.Physical(2, 0), RulesTestData.Physical(3, 0),
                RulesTestData.Physical(4, 0), RulesTestData.Physical(5, 2))
            .State;
        string before = RulesTestData.Snapshot(state);

        // 目标座位弃牌区有 5 槽，但 handSlots 只按手牌解释：手牌只有 3 槽。
        Assert.Throws<InvalidOperationException>(() => state.DeclareMeld(0, MeldKind.Pong, [3, 4], 1, 4));
        Assert.Equal(before, RulesTestData.Snapshot(state));

        Meld meld = state.DeclareMeld(0, MeldKind.Pong, [1, 2], 1, 4);
        Assert.Equal(new ushort[] { 21, 22, 23 }, SortedIds(meld.Tiles));
    }

    [Fact]
    public void DeclareMeld_PreservesTotalTileCountForAllKinds()
    {
        GameState pong = PongReadyBoard();
        int pongBefore = RulesTestData.TotalTileCount(pong);
        _ = pong.DeclareMeld(0, MeldKind.Pong, [1, 3], 1, 2);
        Assert.Equal(pongBefore, RulesTestData.TotalTileCount(pong));

        GameState kongExposed = KongExposedReadyBoard();
        int kongExposedBefore = RulesTestData.TotalTileCount(kongExposed);
        _ = kongExposed.DeclareMeld(0, MeldKind.KongExposed, [1, 2, 3], 1, 0);
        Assert.Equal(kongExposedBefore, RulesTestData.TotalTileCount(kongExposed));

        GameState kongConcealed = KongConcealedReadyBoard();
        int kongConcealedBefore = RulesTestData.TotalTileCount(kongConcealed);
        _ = kongConcealed.DeclareMeld(0, MeldKind.KongConcealed, [0, 1, 2, 3], Meld.NoTargetSeat);
        Assert.Equal(kongConcealedBefore, RulesTestData.TotalTileCount(kongConcealed));

        GameState supplement = SupplementReadyBoard(out _);
        int supplementBefore = RulesTestData.TotalTileCount(supplement);
        _ = supplement.DeclareMeld(0, MeldKind.KongSupplement, [1], Meld.NoTargetSeat);
        Assert.Equal(supplementBefore, RulesTestData.TotalTileCount(supplement));
    }

    [Fact]
    public void DeclareMeld_FailuresLeaveStateUnchanged()
    {
        var failures = new (string Name, Action<GameState> Call)[]
        {
            ("duplicate hand slots", s => _ = s.DeclareMeld(0, MeldKind.Pong, [1, 1], 1, 2)),
            ("missing target discard slot", s => _ = s.DeclareMeld(0, MeldKind.Pong, [1, 3], 1)),
            ("target seat equals own seat", s => _ = s.DeclareMeld(0, MeldKind.Pong, [1, 3], 0, 2)),
            ("discard slot out of range", s => _ = s.DeclareMeld(0, MeldKind.Pong, [1, 3], 1, 99)),
            ("hand slot out of range", s => _ = s.DeclareMeld(0, MeldKind.Pong, [1, 9], 1, 2)),
            ("hand type mismatch", s => _ = s.DeclareMeld(0, MeldKind.Pong, [1, 2], 1, 2)),
            ("discard type mismatch", s => _ = s.DeclareMeld(0, MeldKind.Pong, [1, 3], 1, 1)),
            ("seat out of range", s => _ = s.DeclareMeld(4, MeldKind.Pong, [1, 3], 1, 2)),
            ("concealed kong with target seat", s => _ = s.DeclareMeld(0, MeldKind.KongConcealed, [0, 1, 2, 3], 1)),
            ("supplement without pong", s => _ = s.DeclareMeld(0, MeldKind.KongSupplement, [0], Meld.NoTargetSeat)),
        };

        foreach ((string name, Action<GameState> call) in failures)
        {
            GameState state = PongReadyBoard();
            string before = RulesTestData.Snapshot(state);

            Assert.ThrowsAny<Exception>(() => call(state));

            Assert.True(before == RulesTestData.Snapshot(state), name + " 改变了局面状态");
        }
    }

    [Fact]
    public void DeclarePong_DoesNotDrawFromWallOrTail()
    {
        var state = new Board()
            .Hand(0, RulesTestData.Physical(5, 0), RulesTestData.Physical(5, 1))
            .Discards(1, RulesTestData.Physical(5, 2))
            .State;
        state.Wall.Append(RulesTestData.Physical(0, 0));
        state.WallTail.Append(RulesTestData.Physical(0, 1));

        _ = state.DeclareMeld(0, MeldKind.Pong, [0, 1], 1, 0);

        Assert.Equal(1, state.Wall.Count);
        Assert.Equal(1, state.WallTail.Count);
        Assert.Empty(state.Players[0].Hand.Items);
        Assert.Empty(state.Players[0].Discards.Items);
    }

    private static GameState PongReadyBoard() =>
        new Board()
            .Hand(0, RulesTestData.Physical(0, 0), RulesTestData.Physical(5, 0), RulesTestData.Physical(6, 0),
                RulesTestData.Physical(5, 1), RulesTestData.Physical(7, 0))
            .Discards(1, RulesTestData.Physical(8, 0), RulesTestData.Physical(9, 0), RulesTestData.Physical(5, 2))
            .State;

    private static GameState KongExposedReadyBoard() =>
        new Board()
            .Hand(0, RulesTestData.Physical(0, 0), RulesTestData.Physical(5, 0), RulesTestData.Physical(5, 1),
                RulesTestData.Physical(5, 2))
            .Discards(1, RulesTestData.Physical(5, 3))
            .State;

    private static GameState KongConcealedReadyBoard() =>
        new Board()
            .Hand(0, RulesTestData.Physical(5, 0), RulesTestData.Physical(5, 1), RulesTestData.Physical(5, 2),
                RulesTestData.Physical(5, 3), RulesTestData.Physical(0, 0))
            .State;

    private static GameState SupplementReadyBoard(out Meld pong)
    {
        var state = new GameState();
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(5, 0), RulesTestData.Physical(5, 1),
            RulesTestData.Physical(0, 0));
        state.Players[1].Discards.Append(RulesTestData.Physical(5, 2));
        pong = state.DeclareMeld(0, MeldKind.Pong, [0, 1], 1, 0);
        RulesTestData.Fill(state.Players[0], RulesTestData.Physical(5, 3));
        return state;
    }

    private static ushort[] SortedIds(IEnumerable<Tile> tiles) => RulesTestData.Ids(tiles).Order().ToArray();

    private sealed class Board
    {
        public GameState State { get; } = new();

        public Board Hand(byte seat, params Tile[] tiles)
        {
            RulesTestData.Fill(State.Players[seat], tiles);
            return this;
        }

        public Board Discards(byte seat, params Tile[] tiles)
        {
            RulesTestData.Discard(State.Players[seat], tiles);
            return this;
        }
    }
}
