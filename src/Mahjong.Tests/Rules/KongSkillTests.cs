using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// <see cref="KongSkill"/> 三态契约（按上下文自行判定）：
/// 明杠（有目标弃牌 + 手牌 3 张同 Type）、暗杠（无目标、无同 Type 碰、手牌 4 张）、
/// 补杠（无目标、已有同 Type 碰、手牌第 4 张）；三种都从牌墙末尾补 1 张并把 CurrentSeat 归杠家；
/// 牌墙空时 Validate=false、Commit 抛 <see cref="InvalidOperationException"/> 且状态不变（流局由流程层判）；
/// 鬼牌不可作为杠材料。
/// </summary>
public sealed class KongSkillTests
{
    [Fact]
    public void Id_IsKong()
    {
        Assert.Equal(SkillId.Kong, new KongSkill().Id);
    }

    [Fact]
    public void Exposed_Commit_FormsKongFromThreeHandTilesPlusLastDiscard_AndDrawsFromTail()
    {
        GameState state = ExposedKongBoard(out Tile target);
        state.CurrentSeat = 2;
        string snapshotBefore = SkillTestData.Snapshot(state);
        int totalBefore = RulesTestData.TotalTileCount(state);

        new KongSkill().Commit(SkillTestData.Context(state, 0, target, targetSeat: 1));

        Meld meld = Assert.Single(state.Players[0].Melds);
        Assert.Equal(MeldKind.KongExposed, meld.Kind);
        Assert.True(meld.Opened);
        Assert.Equal((byte)1, meld.TargetSeat);
        Assert.Equal(new ushort[] { 21, 22, 23, 24 }, SkillTestData.MeldIds(meld));
        Assert.Equal(
            new ushort[] { 73, 77, 81, 85, 89, 93, 97, 101, 105, 109, 121 },
            SkillTestData.HandIds(state.Players[0]));
        Assert.Equal(11, state.Players[0].Hand.Count);
        Assert.Empty(state.Players[1].Discards.Items);
        Assert.Equal(new ushort[] { 133, 134, 135, 136 }, SkillTestData.WallIds(state));
        Assert.Equal(4, state.Wall.Count);
        Assert.Equal((byte)0, state.CurrentSeat);
        Assert.Equal(totalBefore, RulesTestData.TotalTileCount(state));
        Assert.NotEqual(snapshotBefore, SkillTestData.Snapshot(state));
    }

    [Fact]
    public void Concealed_Commit_FormsClosedKongFromFourHandTiles_AndDrawsFromTail()
    {
        GameState state = ConcealedKongBoard();
        state.CurrentSeat = 3;
        int totalBefore = RulesTestData.TotalTileCount(state);

        new KongSkill().Commit(SkillTestData.Context(state, 0));

        Meld meld = Assert.Single(state.Players[0].Melds);
        Assert.Equal(MeldKind.KongConcealed, meld.Kind);
        Assert.False(meld.Opened);
        Assert.Equal(Meld.NoTargetSeat, meld.TargetSeat);
        Assert.Equal((byte)255, meld.TargetSeat);
        Assert.Equal(new ushort[] { 21, 22, 23, 24 }, SkillTestData.MeldIds(meld));
        Assert.Equal(
            new ushort[] { 73, 77, 81, 85, 89, 93, 97, 101, 105, 121 },
            SkillTestData.HandIds(state.Players[0]));
        Assert.Equal(10, state.Players[0].Hand.Count);
        Assert.Equal(new ushort[] { 133, 134, 135, 136 }, SkillTestData.WallIds(state));
        Assert.Equal((byte)0, state.CurrentSeat);
        Assert.Equal(totalBefore, RulesTestData.TotalTileCount(state));
    }

    [Fact]
    public void Supplement_Commit_UpgradesTheSamePongObjectInPlace_AndDrawsFromTail()
    {
        GameState state = SupplementKongBoard(out Meld pong);
        state.CurrentSeat = 1;
        ushort[] pongTilesBefore = SkillTestData.MeldIds(pong);
        int totalBefore = RulesTestData.TotalTileCount(state);

        new KongSkill().Commit(SkillTestData.Context(state, 0));

        Assert.Single(state.Players[0].Melds);
        Assert.Same(pong, state.Players[0].Melds[0]);
        Assert.Equal(MeldKind.KongSupplement, pong.Kind);
        Assert.Equal(4, pong.Tiles.Count);
        Assert.Equal(pongTilesBefore, SkillTestData.MeldIds(pong).Take(3));
        Assert.Equal((ushort)24, pong.Tiles[3].Id);
        Assert.True(pong.Opened);
        Assert.Equal((byte)1, pong.TargetSeat);
        Assert.Equal(
            new ushort[] { 73, 77, 81, 85, 89, 93, 97, 101, 105, 109, 121 },
            SkillTestData.HandIds(state.Players[0]));
        Assert.Equal(11, state.Players[0].Hand.Count);
        Assert.Equal(new ushort[] { 133, 134, 135, 136 }, SkillTestData.WallIds(state));
        Assert.Equal((byte)0, state.CurrentSeat);
        Assert.Equal(totalBefore, RulesTestData.TotalTileCount(state));
    }

    [Fact]
    public void Supplement_TakesPriorityOverConcealed_WhenBothFormsAreAvailable()
    {
        // 已有 5万碰 + 手牌第 4 张 5万，同时手牌还有 4 张 6万（可暗杠）；固定判定顺序必须先补杠。
        GameState state = SkillTestData.NewState();
        var pong = new Meld(
            MeldKind.Pong,
            [SkillTestData.T(5, 0), SkillTestData.T(5, 1), SkillTestData.T(5, 2)],
            opened: true,
            targetSeat: 1);
        state.Players[0].Melds.Add(pong);
        SkillTestData.FillHand(
            state,
            0,
            [SkillTestData.T(5, 3), SkillTestData.T(6, 0), SkillTestData.T(6, 1), SkillTestData.T(6, 2),
                SkillTestData.T(6, 3), .. SkillTestData.Fillers(2)]);
        SkillTestData.FillWall(state, SkillTestData.T(33, 0), SkillTestData.T(30, 0));

        new KongSkill().Commit(SkillTestData.Context(state, 0));

        Assert.Single(state.Players[0].Melds);
        Assert.Same(pong, state.Players[0].Melds[0]);
        Assert.Equal(MeldKind.KongSupplement, pong.Kind);
        Assert.Equal(new ushort[] { 21, 22, 23, 24 }, SkillTestData.MeldIds(pong));
        Assert.Equal(new ushort[] { 25, 26, 27, 28, 73, 77, 121 }, SkillTestData.HandIds(state.Players[0]));
    }

    [Fact]
    public void Supplement_DrawnTileHintIsTriedBeforeScanningHandSlots()
    {
        // 已有 5万碰与 6万碰；手牌槽 0 是 5万c3、槽 1 是 6万c3；DrawnTile=6万c3 必须优先命中 6万碰。
        GameState state = SkillTestData.NewState();
        var pong5 = new Meld(
            MeldKind.Pong,
            [SkillTestData.T(5, 0), SkillTestData.T(5, 1), SkillTestData.T(5, 2)],
            opened: true,
            targetSeat: 1);
        var pong6 = new Meld(
            MeldKind.Pong,
            [SkillTestData.T(6, 0), SkillTestData.T(6, 1), SkillTestData.T(6, 2)],
            opened: true,
            targetSeat: 2);
        state.Players[0].Melds.Add(pong5);
        state.Players[0].Melds.Add(pong6);
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 3), SkillTestData.T(6, 3), .. SkillTestData.Fillers(2)]);
        SkillTestData.FillWall(state, SkillTestData.T(33, 0), SkillTestData.T(30, 0));

        new KongSkill().Commit(
            SkillTestData.Context(state, 0, drawnTile: SkillTestData.T(6, 3)));

        Assert.Equal(MeldKind.Pong, pong5.Kind);
        Assert.Equal(3, pong5.Tiles.Count);
        Assert.Equal(MeldKind.KongSupplement, pong6.Kind);
        Assert.Equal(new ushort[] { 25, 26, 27, 28 }, SkillTestData.MeldIds(pong6));
        Assert.Equal(new ushort[] { 24, 73, 77, 121 }, SkillTestData.HandIds(state.Players[0]));
    }

    [Fact]
    public void Exposed_Validate_False_WhenTargetCardIsAnEarlierDiscard()
    {
        GameState state = ExposedKongBoard(out Tile target);
        SkillTestData.FillDiscards(state, 1, SkillTestData.T(6, 0));

        Assert.False(new KongSkill().Validate(SkillTestData.Context(state, 0, target, targetSeat: 1)));
    }

    [Fact]
    public void Exposed_Validate_False_WhenOnlyTwoMatchingHandTiles()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 0), SkillTestData.T(5, 1), .. SkillTestData.Fillers(11)]);
        SkillTestData.FillDiscards(state, 1, SkillTestData.T(5, 3));

        Assert.False(
            new KongSkill().Validate(SkillTestData.Context(state, 0, state.Players[1].Discards[0], targetSeat: 1)));
    }

    [Fact]
    public void Exposed_Validate_False_WhenTargetSeatEqualsSourceSeat()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(
            state,
            0,
            [SkillTestData.T(5, 0), SkillTestData.T(5, 1), SkillTestData.T(5, 2), .. SkillTestData.Fillers(10)]);
        SkillTestData.FillDiscards(state, 0, SkillTestData.T(5, 3));

        Assert.False(
            new KongSkill().Validate(SkillTestData.Context(state, 0, state.Players[0].Discards[0], targetSeat: 0)));
    }

    [Fact]
    public void Concealed_Validate_False_WithOnlyThreeMatchingTilesAndNoTarget()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(
            state,
            0,
            [SkillTestData.T(5, 0), SkillTestData.T(5, 1), SkillTestData.T(5, 2), .. SkillTestData.Fillers(10)]);

        Assert.False(new KongSkill().Validate(SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void Supplement_Validate_False_WhenPongExistsButHandHasNoFourthTile()
    {
        GameState state = SkillTestData.NewState();
        state.Players[0].Melds.Add(
            new Meld(
                MeldKind.Pong,
                [SkillTestData.T(5, 0), SkillTestData.T(5, 1), SkillTestData.T(5, 2)],
                opened: true,
                targetSeat: 1));
        SkillTestData.FillHand(state, 0, SkillTestData.Fillers(11));

        Assert.False(new KongSkill().Validate(SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void JokerTiles_AreNotKongMaterial()
    {
        // 明杠：手牌 3 张红中 + 目标最后弃牌红中；无鬼可杠，红中为鬼时不可。
        GameState exposed = SkillTestData.NewState();
        SkillTestData.FillHand(
            exposed,
            0,
            [SkillTestData.T(31, 0), SkillTestData.T(31, 1), SkillTestData.T(31, 2), .. SkillTestData.Fillers(10)]);
        SkillTestData.FillDiscards(exposed, 1, SkillTestData.T(31, 3));
        SkillTestData.FillWall(exposed, SkillTestData.T(30, 0));
        Tile exposedTarget = exposed.Players[1].Discards[0];
        Assert.True(new KongSkill().Validate(SkillTestData.Context(exposed, 0, exposedTarget, targetSeat: 1)));
        Assert.False(
            new KongSkill(SkillTestData.Joker).Validate(SkillTestData.Context(exposed, 0, exposedTarget, targetSeat: 1)));

        // 暗杠：手牌 4 张红中；无鬼可杠，红中为鬼时不可。
        GameState concealed = SkillTestData.NewState();
        SkillTestData.FillHand(
            concealed,
            0,
            [SkillTestData.T(31, 0), SkillTestData.T(31, 1), SkillTestData.T(31, 2), SkillTestData.T(31, 3),
                .. SkillTestData.Fillers(9)]);
        SkillTestData.FillWall(concealed, SkillTestData.T(30, 0));
        Assert.True(new KongSkill().Validate(SkillTestData.Context(concealed, 0)));
        Assert.False(new KongSkill(SkillTestData.Joker).Validate(SkillTestData.Context(concealed, 0)));

        // 补杠：红中碰 + 手牌第 4 张；无鬼可补，红中为鬼时不可。
        GameState supplement = SkillTestData.NewState();
        supplement.Players[0].Melds.Add(
            new Meld(
                MeldKind.Pong,
                [SkillTestData.T(31, 0), SkillTestData.T(31, 1), SkillTestData.T(31, 2)],
                opened: true,
                targetSeat: 1));
        SkillTestData.FillHand(supplement, 0, [SkillTestData.T(31, 3), .. SkillTestData.Fillers(10)]);
        SkillTestData.FillWall(supplement, SkillTestData.T(30, 0));
        Assert.True(new KongSkill().Validate(SkillTestData.Context(supplement, 0)));
        Assert.False(new KongSkill(SkillTestData.Joker).Validate(SkillTestData.Context(supplement, 0)));
    }

    [Fact]
    public void Validate_DoesNotMutateState_ForAllThreeKongKinds()
    {
        AssertValidateLeavesStateUnchanged(
            ExposedKongBoard(out Tile exposedTarget),
            state => SkillTestData.Context(state, 0, exposedTarget, targetSeat: 1));
        AssertValidateLeavesStateUnchanged(ConcealedKongBoard(), state => SkillTestData.Context(state, 0));
        AssertValidateLeavesStateUnchanged(SupplementKongBoard(out _), state => SkillTestData.Context(state, 0));
    }

    [Fact]
    public void AllKongKinds_RejectEmptyWall_AndCommitLeavesStateUnchanged()
    {
        GameState exposed = ExposedKongBoard(out Tile exposedTarget);
        exposed.Wall.Clear();
        AssertEmptyWallRejected(exposed, state => SkillTestData.Context(state, 0, exposedTarget, targetSeat: 1));

        GameState concealed = ConcealedKongBoard();
        concealed.Wall.Clear();
        AssertEmptyWallRejected(concealed, state => SkillTestData.Context(state, 0));

        GameState supplement = SupplementKongBoard(out _);
        supplement.Wall.Clear();
        AssertEmptyWallRejected(supplement, state => SkillTestData.Context(state, 0));
    }

    [Fact]
    public void Commit_WhenValidateFails_ThrowsAndLeavesStateUnchanged()
    {
        GameState state = ExposedKongBoard(out _);
        SkillContext invalid = SkillTestData.Context(state, 0, targetCard: null, targetSeat: 1);
        string snapshotBefore = SkillTestData.Snapshot(state);
        string hashBefore = SkillTestData.Hash(state);

        Assert.Throws<InvalidOperationException>(() => new KongSkill().Commit(invalid));

        Assert.Equal(snapshotBefore, SkillTestData.Snapshot(state));
        Assert.Equal(hashBefore, SkillTestData.Hash(state));
    }

    private static void AssertValidateLeavesStateUnchanged(GameState state, Func<GameState, SkillContext> factory)
    {
        string snapshotBefore = SkillTestData.Snapshot(state);
        string hashBefore = SkillTestData.Hash(state);
        var skill = new KongSkill();

        Assert.True(skill.Validate(factory(state)));
        Assert.True(skill.Validate(factory(state)));

        Assert.Equal(snapshotBefore, SkillTestData.Snapshot(state));
        Assert.Equal(hashBefore, SkillTestData.Hash(state));
    }

    private static void AssertEmptyWallRejected(GameState state, Func<GameState, SkillContext> factory)
    {
        string snapshotBefore = SkillTestData.Snapshot(state);
        string hashBefore = SkillTestData.Hash(state);
        var skill = new KongSkill();

        Assert.True(state.IsWallEmpty);
        Assert.False(skill.Validate(factory(state)));
        Assert.Throws<InvalidOperationException>(() => skill.Commit(factory(state)));

        Assert.Equal(snapshotBefore, SkillTestData.Snapshot(state));
        Assert.Equal(hashBefore, SkillTestData.Hash(state));
    }

    /// <summary>
    /// 明杠局面：座位 0 手牌 13 张（5万c0/c1/c2 位于第 0/2/4 槽，其余为填充牌）；
    /// 座位 1 最后弃牌 5万c3；牌墙 5 张（末尾 133 作杠补牌）。
    /// </summary>
    private static GameState ExposedKongBoard(out Tile target)
    {
        GameState state = SkillTestData.NewState();
        Tile[] fillers = SkillTestData.Fillers(10);
        SkillTestData.FillHand(
            state,
            0,
            [SkillTestData.T(5, 0), fillers[0], SkillTestData.T(5, 1), fillers[1], SkillTestData.T(5, 2),
                .. fillers[2..]]);
        SkillTestData.FillDiscards(state, 1, SkillTestData.T(5, 3));
        SkillTestData.FillWall(
            state,
            SkillTestData.T(33, 0),
            SkillTestData.T(33, 1),
            SkillTestData.T(33, 2),
            SkillTestData.T(33, 3),
            SkillTestData.T(30, 0));
        target = state.Players[1].Discards[0];
        return state;
    }

    /// <summary>暗杠局面：座位 0 手牌 13 张（5万c0–c3 位于第 0/2/4/6 槽，其余为填充牌）；牌墙 5 张。</summary>
    private static GameState ConcealedKongBoard()
    {
        GameState state = SkillTestData.NewState();
        Tile[] fillers = SkillTestData.Fillers(9);
        SkillTestData.FillHand(
            state,
            0,
            [SkillTestData.T(5, 0), fillers[0], SkillTestData.T(5, 1), fillers[1], SkillTestData.T(5, 2), fillers[2],
                SkillTestData.T(5, 3), .. fillers[3..]]);
        SkillTestData.FillWall(
            state,
            SkillTestData.T(33, 0),
            SkillTestData.T(33, 1),
            SkillTestData.T(33, 2),
            SkillTestData.T(33, 3),
            SkillTestData.T(30, 0));
        return state;
    }

    /// <summary>补杠局面：座位 0 已有 5万碰（目标座位 1），手牌 11 张含第 4 张 5万；牌墙 5 张。</summary>
    private static GameState SupplementKongBoard(out Meld pong)
    {
        GameState state = SkillTestData.NewState();
        pong = new Meld(
            MeldKind.Pong,
            [SkillTestData.T(5, 0), SkillTestData.T(5, 1), SkillTestData.T(5, 2)],
            opened: true,
            targetSeat: 1);
        state.Players[0].Melds.Add(pong);
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 3), .. SkillTestData.Fillers(10)]);
        SkillTestData.FillWall(
            state,
            SkillTestData.T(33, 0),
            SkillTestData.T(33, 1),
            SkillTestData.T(33, 2),
            SkillTestData.T(33, 3),
            SkillTestData.T(30, 0));
        return state;
    }
}
