using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// <see cref="PongSkill"/> 契约：目标牌必须是响应座位的最后一张弃牌、手牌同 Type 非鬼 ≥2、目标座位 0–3 且不等于自己；
/// Commit 取手牌前两个匹配槽位 + 目标最后弃牌槽位组成 <see cref="MeldKind.Pong"/>，不摸牌，CurrentSeat 归碰家（强制出牌）；
/// Validate 只读，Commit 先复校验、失败抛 <see cref="InvalidOperationException"/> 且不改状态。
/// </summary>
public sealed class PongSkillTests
{
    [Fact]
    public void Id_IsPong()
    {
        Assert.Equal(SkillId.Pong, new PongSkill().Id);
    }

    [Fact]
    public void Validate_True_WhenTwoMatchingHandTilesAndTargetIsLastDiscard()
    {
        GameState state = PongBoard(out Tile target);

        Assert.True(new PongSkill().Validate(SkillTestData.Context(state, 0, target, targetSeat: 1)));
    }

    [Fact]
    public void Validate_False_WhenOnlyOneMatchingHandTile()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 0), .. SkillTestData.Fillers(12)]);
        SkillTestData.FillDiscards(state, 1, SkillTestData.T(5, 2), SkillTestData.T(5, 3));
        Tile target = state.Players[1].Discards[1];

        Assert.False(new PongSkill().Validate(SkillTestData.Context(state, 0, target, targetSeat: 1)));
    }

    [Fact]
    public void Validate_False_WhenTargetCardIsNull()
    {
        GameState state = PongBoard(out _);

        Assert.False(new PongSkill().Validate(SkillTestData.Context(state, 0, targetCard: null, targetSeat: 1)));
    }

    [Fact]
    public void Validate_False_WhenTargetCardIsAnEarlierDiscard()
    {
        GameState state = PongBoard(out _);
        Tile earlier = state.Players[1].Discards[1];

        // 座位 1 弃牌 [6万, 5万c2, 5万c3]：c2 是 5 万但不在最后一张，不能作为碰的目标。
        Assert.Equal((ushort)23, earlier.Id);
        Assert.False(new PongSkill().Validate(SkillTestData.Context(state, 0, earlier, targetSeat: 1)));
    }

    [Fact]
    public void Validate_False_WhenTargetCardIsNotInThatSeatsDiscards()
    {
        GameState state = PongBoard(out Tile target);
        SkillTestData.FillDiscards(state, 1, SkillTestData.T(7, 0));

        // 追加 7 万后，5万c3 不再是最后一张弃牌。
        Assert.Equal((ushort)29, state.Players[1].Discards[^1].Id);
        Assert.False(new PongSkill().Validate(SkillTestData.Context(state, 0, target, targetSeat: 1)));
    }

    [Fact]
    public void Validate_False_WhenTargetSeatEqualsSourceSeat()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 0), SkillTestData.T(5, 1), .. SkillTestData.Fillers(11)]);
        SkillTestData.FillDiscards(state, 0, SkillTestData.T(5, 2), SkillTestData.T(5, 3));
        Tile target = state.Players[0].Discards[1];

        Assert.False(new PongSkill().Validate(SkillTestData.Context(state, 0, target, targetSeat: 0)));
    }

    [Fact]
    public void Validate_False_WhenTargetSeatIsNoTargetSeat()
    {
        GameState state = PongBoard(out Tile target);

        Assert.False(new PongSkill().Validate(SkillTestData.Context(state, 0, target)));
    }

    [Fact]
    public void Validate_False_WhenMatchingHandTilesAreJokers()
    {
        GameState state = JokerPongBoard(out Tile target);

        // 同一副牌：无鬼时 2 张红中只是普通牌，可碰；红中为鬼时不可作为碰材料。
        Assert.True(new PongSkill().Validate(SkillTestData.Context(state, 0, target, targetSeat: 1)));
        Assert.False(new PongSkill(SkillTestData.Joker).Validate(SkillTestData.Context(state, 0, target, targetSeat: 1)));
    }

    [Fact]
    public void Validate_DoesNotMutateState()
    {
        GameState state = PongBoard(out Tile target);
        string snapshotBefore = SkillTestData.Snapshot(state);
        string hashBefore = SkillTestData.Hash(state);
        SkillContext context = SkillTestData.Context(state, 0, target, targetSeat: 1);
        var skill = new PongSkill();

        Assert.True(skill.Validate(context));
        Assert.True(skill.Validate(context));

        Assert.Equal(snapshotBefore, SkillTestData.Snapshot(state));
        Assert.Equal(hashBefore, SkillTestData.Hash(state));
    }

    [Fact]
    public void Commit_FormsPongFromMatchingHandSlotsAndLastDiscard_WithoutDrawing()
    {
        GameState state = PongBoard(out Tile target);
        string snapshotBefore = SkillTestData.Snapshot(state);
        int totalBefore = RulesTestData.TotalTileCount(state);

        new PongSkill().Commit(SkillTestData.Context(state, 0, target, targetSeat: 1));

        Meld meld = Assert.Single(state.Players[0].Melds);
        Assert.Equal(MeldKind.Pong, meld.Kind);
        Assert.True(meld.Opened);
        Assert.Equal((byte)1, meld.TargetSeat);
        Assert.Equal(new ushort[] { 21, 22, 24 }, SkillTestData.MeldIds(meld));
        Assert.Equal(
            new ushort[] { 73, 77, 81, 85, 89, 93, 97, 101, 105, 109, 113 },
            SkillTestData.HandIds(state.Players[0]));
        Assert.Equal(11, state.Players[0].Hand.Count);
        Assert.Equal(14, state.Players[0].Hand.Count + (3 * state.Players[0].Melds.Count));
        Assert.Empty(state.Players[0].Discards.Items);
        Assert.Equal(new ushort[] { 25, 23 }, SkillTestData.DiscardIds(state.Players[1]));
        Assert.Equal(new ushort[] { 133, 134, 135 }, SkillTestData.WallIds(state));
        Assert.Equal(3, state.Wall.Count);
        Assert.Equal((byte)0, state.CurrentSeat);
        Assert.Empty(state.Players[2].Hand.Items);
        Assert.Empty(state.Players[3].Hand.Items);
        Assert.Equal(totalBefore, RulesTestData.TotalTileCount(state));
        Assert.NotEqual(snapshotBefore, SkillTestData.Snapshot(state));
    }

    [Fact]
    public void Commit_SelectsTheFirstTwoMatchingHandSlots_AndLeavesTheThirdCopyInHand()
    {
        GameState state = SkillTestData.NewState();
        Tile[] fillers = SkillTestData.Fillers(10);
        SkillTestData.FillHand(
            state,
            0,
            [SkillTestData.T(5, 0), fillers[0], SkillTestData.T(5, 1), fillers[1], SkillTestData.T(5, 2),
                .. fillers[2..]]);
        SkillTestData.FillDiscards(state, 1, SkillTestData.T(5, 3));
        Tile target = state.Players[1].Discards[0];

        new PongSkill().Commit(SkillTestData.Context(state, 0, target, targetSeat: 1));

        Meld meld = Assert.Single(state.Players[0].Melds);
        Assert.Equal(new ushort[] { 21, 22, 24 }, SkillTestData.MeldIds(meld));

        // 手牌按槽位顺序移除第 0、2 槽：剩余 5万c2 仍在原相对位置（前两张填充牌之后）。
        Assert.Equal(
            new ushort[] { 73, 77, 23, 81, 85, 89, 93, 97, 101, 105, 109 },
            SkillTestData.HandIds(state.Players[0]));
    }

    [Fact]
    public void Commit_DoesNotDrawFromWall_AndLeavesWallUnchanged()
    {
        GameState state = PongBoard(out Tile target);

        new PongSkill().Commit(SkillTestData.Context(state, 0, target, targetSeat: 1));

        Assert.Equal(3, state.Wall.Count);
        Assert.Equal(new ushort[] { 133, 134, 135 }, SkillTestData.WallIds(state));
        Assert.Equal(11, state.Players[0].Hand.Count);
    }

    [Fact]
    public void Commit_SetsCurrentSeatToPonger()
    {
        GameState state = SkillTestData.NewState();
        state.CurrentSeat = 2;
        SkillTestData.FillHand(state, 1, [SkillTestData.T(5, 0), SkillTestData.T(5, 1), .. SkillTestData.Fillers(11)]);
        SkillTestData.FillDiscards(state, 0, SkillTestData.T(5, 2), SkillTestData.T(5, 3));
        Tile target = state.Players[0].Discards[1];

        new PongSkill().Commit(SkillTestData.Context(state, 1, target, targetSeat: 0));

        Assert.Equal((byte)1, state.CurrentSeat);
        Assert.Equal(11, state.Players[1].Hand.Count);
        Assert.Equal((byte)0, Assert.Single(state.Players[1].Melds).TargetSeat);
    }

    [Fact]
    public void Commit_WhenValidateFails_ThrowsAndLeavesStateUnchanged()
    {
        GameState state = PongBoard(out _);
        string snapshotBefore = SkillTestData.Snapshot(state);
        string hashBefore = SkillTestData.Hash(state);
        SkillContext invalid = SkillTestData.Context(state, 0, targetCard: null, targetSeat: 1);

        Assert.Throws<InvalidOperationException>(() => new PongSkill().Commit(invalid));

        Assert.Equal(snapshotBefore, SkillTestData.Snapshot(state));
        Assert.Equal(hashBefore, SkillTestData.Hash(state));
    }

    [Fact]
    public void Commit_CalledTwice_SecondCallThrowsAndLeavesStateUnchanged()
    {
        GameState state = PongBoard(out Tile target);
        SkillContext context = SkillTestData.Context(state, 0, target, targetSeat: 1);
        var skill = new PongSkill();
        skill.Commit(context);
        string snapshotAfterFirst = SkillTestData.Snapshot(state);
        string hashAfterFirst = SkillTestData.Hash(state);

        Assert.Throws<InvalidOperationException>(() => skill.Commit(context));

        Assert.Equal(snapshotAfterFirst, SkillTestData.Snapshot(state));
        Assert.Equal(hashAfterFirst, SkillTestData.Hash(state));
    }

    [Fact]
    public void Validate_False_WhenTargetCardIsUnknownId()
    {
        GameState state = PongBoard(out _);

        Assert.False(new PongSkill().Validate(SkillTestData.Context(state, 0, default(Tile), targetSeat: 1)));
    }

    [Fact]
    public void Validate_False_WhenTargetSeatHasNoDiscards()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 0), SkillTestData.T(5, 1), .. SkillTestData.Fillers(11)]);
        Tile target = SkillTestData.T(5, 2);

        Assert.Empty(state.Players[1].Discards.Items);
        Assert.False(new PongSkill().Validate(SkillTestData.Context(state, 0, target, targetSeat: 1)));
    }

    /// <summary>
    /// 碰的标准局面：座位 0 手牌 13 张（5万c0、5万c1 位于第 0/3 槽，其余为填充牌）；
    /// 座位 1 弃牌 [6万c0, 5万c2, 5万c3]（最后一张才是目标牌）；牌墙 3 张。
    /// </summary>
    private static GameState PongBoard(out Tile target)
    {
        GameState state = SkillTestData.NewState();
        Tile[] fillers = SkillTestData.Fillers(11);
        SkillTestData.FillHand(
            state,
            0,
            [SkillTestData.T(5, 0), fillers[0], fillers[1], SkillTestData.T(5, 1), .. fillers[2..]]);
        SkillTestData.FillDiscards(
            state,
            1,
            SkillTestData.T(6, 0),
            SkillTestData.T(5, 2),
            SkillTestData.T(5, 3));
        SkillTestData.FillWall(state, SkillTestData.T(33, 0), SkillTestData.T(33, 1), SkillTestData.T(33, 2));
        target = state.Players[1].Discards[2];
        return state;
    }

    /// <summary>红中（鬼牌牌种）碰的局面：座位 0 手牌含 2 张红中，座位 1 最后弃牌是第 3 张红中。</summary>
    private static GameState JokerPongBoard(out Tile target)
    {
        GameState state = SkillTestData.NewState();
        Tile[] fillers = SkillTestData.Fillers(11);
        SkillTestData.FillHand(
            state,
            0,
            [SkillTestData.T(31, 0), fillers[0], fillers[1], SkillTestData.T(31, 1), .. fillers[2..]]);
        SkillTestData.FillDiscards(state, 1, SkillTestData.T(31, 2));
        target = state.Players[1].Discards[0];
        return state;
    }
}
