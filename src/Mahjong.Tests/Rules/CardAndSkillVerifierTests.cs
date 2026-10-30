using Mahjong.Core.Rules;
using Mahjong.Tests.Content.Tables;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// <see cref="CardAndSkillVerifier"/> 契约：null 只对 state/player 抛 <see cref="ArgumentNullException"/>；
/// 玩家必须是局内同座位同一引用；出牌分支检查「牌在手中、不是鬼牌、轮到自己、折合 14 张」；
/// 技能分支检查「已定义技能 → 目标牌必须是某座最后一张弃牌且只命中一处 → 折合张数 → 技能 Validate」；
/// 其余非法输入一律 false 且不改变局面。
/// 真实查表由 <see cref="TableCatalogFixture"/> 共享，整个进程只加载一次 15 MB 数据。
/// </summary>
[Collection("table-catalog")]
public sealed class CardAndSkillVerifierTests
{
    private readonly WinChecker _checker;
    private readonly CardAndSkillVerifier _verifier;

    public CardAndSkillVerifierTests(TableCatalogFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _checker = new WinChecker(fixture.Catalog);
        _verifier = new CardAndSkillVerifier(_checker, WinCheckerTestData.Joker);
    }

    [Fact]
    public void Constructor_RejectsNullChecker()
    {
        Assert.Throws<ArgumentNullException>(() => new CardAndSkillVerifier(null!));
    }

    [Fact]
    public void Verify_RejectsNullStateOrPlayer()
    {
        GameState state = SkillTestData.NewState();

        Assert.Throws<ArgumentNullException>(() => _verifier.Verify(null!, state.Players[0], null, null, null));
        Assert.Throws<ArgumentNullException>(() => _verifier.Verify(state, null!, null, null, null));
    }

    [Fact]
    public void Verify_PlayerNotTheSameReferenceInState_ReturnsFalse()
    {
        GameState state = SkillTestData.NewState();

        Assert.False(_verifier.Verify(state, new PlayerState(0), null, null, null));
    }

    [Fact]
    public void Verify_PlayerLookalikeWithFullLegalHand_ReturnsFalse()
    {
        // 反「同引用检查退化为同座位号检查」变异：lookalike 与 state.Players[0] 座位号相同、引用不同，
        // 且有 14 张合法手牌与可出的牌。退化实现会走出牌分支返回 true；原实现必须在同引用检查处返回 false。
        GameState state = DiscardBoard(out _);
        Tile card = state.Players[0].Hand[3];

        var lookalike = new PlayerState(0);
        RulesTestData.Fill(lookalike, [.. state.Players[0].Hand.Items]);

        Assert.NotSame(state.Players[0], lookalike);
        Assert.Equal(state.Players[0].Seat, lookalike.Seat);
        Assert.Equal(14, lookalike.Hand.Count);
        Assert.Equal(state.CurrentSeat, lookalike.Seat);
        Assert.Equal(card, lookalike.Hand[3]);

        // 同一张物理牌、同一局面：真实玩家 true；仅引用不同的 lookalike 必须 false。
        Assert.True(_verifier.Verify(state, state.Players[0], null, card, null));
        Assert.False(_verifier.Verify(state, lookalike, null, card, null));
    }

    [Fact]
    public void Discard_ValidCard_ReturnsTrue()
    {
        GameState state = DiscardBoard(out Tile card);

        Assert.True(_verifier.Verify(state, state.Players[0], null, card, null));
    }

    [Fact]
    public void Discard_NullOrUnknownCard_ReturnsFalse()
    {
        GameState state = DiscardBoard(out _);
        Tile unknown = default;

        Assert.Equal(Tile.UnknownId, unknown.Id);
        Assert.False(_verifier.Verify(state, state.Players[0], null, null, null));
        Assert.False(_verifier.Verify(state, state.Players[0], null, unknown, null));
    }

    [Fact]
    public void Discard_CardNotInHand_ReturnsFalse()
    {
        GameState state = DiscardBoard(out _);
        Tile sameTypeOtherCopy = SkillTestData.T(3, 1);

        Assert.False(_verifier.Verify(state, state.Players[0], null, sameTypeOtherCopy, null));
    }

    [Fact]
    public void Discard_NotCurrentSeat_ReturnsFalse()
    {
        GameState state = DiscardBoard(out Tile card);
        state.CurrentSeat = 1;

        Assert.False(_verifier.Verify(state, state.Players[0], null, card, null));
    }

    [Fact]
    public void Discard_ThirteenTileHand_ReturnsFalse()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [.. SkillTestData.Fillers(13)]);
        Tile card = state.Players[0].Hand[0];

        Assert.False(_verifier.Verify(state, state.Players[0], null, card, null));
    }

    [Fact]
    public void Discard_HandPlusOneMeldEqualsFourteen_ReturnsTrue()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [.. SkillTestData.Fillers(11)]);
        state.Players[0].Melds.Add(PongMeld(33, targetSeat: Meld.NoTargetSeat));
        Tile card = state.Players[0].Hand[0];

        Assert.Equal(14, state.Players[0].Hand.Count + (3 * state.Players[0].Melds.Count));
        Assert.True(_verifier.Verify(state, state.Players[0], null, card, null));
    }

    [Fact]
    public void Discard_JokerCard_IsRejected_OnlyWhenJokerIsActive()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [SkillTestData.T(31, 0), .. SkillTestData.Fillers(13)]);
        Tile card = state.Players[0].Hand[0];

        Assert.False(_verifier.Verify(state, state.Players[0], null, card, null));

        var noJokerVerifier = new CardAndSkillVerifier(_checker);
        Assert.True(noJokerVerifier.Verify(state, state.Players[0], null, card, null));
    }

    [Theory]
    [InlineData((uint)0)]
    [InlineData((uint)5)]
    [InlineData((uint)255)]
    public void Verify_UndefinedSkill_ReturnsFalse(uint rawSkill)
    {
        GameState state = DiscardBoard(out Tile card);

        Assert.False(_verifier.Verify(state, state.Players[0], (SkillId)rawSkill, card, null));
    }

    [Fact]
    public void Pong_TargetCardMustExistInSomeLastDiscard()
    {
        GameState state = PongBoard(out _);

        // 目标牌没有任何座位打到最后一张弃牌。
        Assert.False(_verifier.Verify(state, state.Players[0], SkillId.Pong, null, SkillTestData.T(7, 0)));
    }

    [Fact]
    public void Pong_TargetCardMustBeTheLastDiscardNotAnEarlierOne()
    {
        GameState state = PongBoard(out _);
        Tile earlier = state.Players[1].Discards[1];

        Assert.False(_verifier.Verify(state, state.Players[0], SkillId.Pong, null, earlier));
    }

    [Fact]
    public void Pong_TargetCardAppearingInTwoSeatsLastDiscards_ReturnsFalse()
    {
        GameState state = PongBoard(out Tile target);
        SkillTestData.FillDiscards(state, 2, target);

        // 同一物理牌出现在两个座位弃牌区的最后一张：目标无法唯一解析。
        Assert.False(_verifier.Verify(state, state.Players[0], SkillId.Pong, null, target));
    }

    [Fact]
    public void Pong_TargetCardInOwnLastDiscard_ReturnsFalse()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 0), SkillTestData.T(5, 1), .. SkillTestData.Fillers(11)]);
        SkillTestData.FillDiscards(state, 0, SkillTestData.T(5, 2));

        Assert.False(_verifier.Verify(state, state.Players[0], SkillId.Pong, null, state.Players[0].Discards[0]));
    }

    [Fact]
    public void Pong_ValidResponse_ReturnsTrue()
    {
        GameState state = PongBoard(out Tile target);

        Assert.True(_verifier.Verify(state, state.Players[0], SkillId.Pong, null, target));
    }

    [Fact]
    public void Pong_RequiresThirteenTiles()
    {
        GameState fourteen = PongBoard(out Tile target14);
        SkillTestData.FillHand(fourteen, 0, SkillTestData.T(29, 1));
        Assert.Equal(14, fourteen.Players[0].Hand.Count);
        Assert.False(_verifier.Verify(fourteen, fourteen.Players[0], SkillId.Pong, null, target14));

        GameState twelve = SkillTestData.NewState();
        SkillTestData.FillHand(twelve, 0, [SkillTestData.T(5, 0), SkillTestData.T(5, 1), .. SkillTestData.Fillers(10)]);
        SkillTestData.FillDiscards(twelve, 1, SkillTestData.T(5, 2));
        Assert.Equal(12, twelve.Players[0].Hand.Count);
        Assert.False(_verifier.Verify(twelve, twelve.Players[0], SkillId.Pong, null, twelve.Players[1].Discards[0]));
    }

    [Fact]
    public void Pong_HandPlusMeldEqualsThirteen_ReturnsTrue()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 0), SkillTestData.T(5, 1), .. SkillTestData.Fillers(8)]);
        state.Players[0].Melds.Add(PongMeld(33, targetSeat: Meld.NoTargetSeat));
        SkillTestData.FillDiscards(state, 1, SkillTestData.T(5, 2));

        Assert.Equal(13, state.Players[0].Hand.Count + (3 * state.Players[0].Melds.Count));
        Assert.True(_verifier.Verify(state, state.Players[0], SkillId.Pong, null, state.Players[1].Discards[0]));
    }

    [Fact]
    public void Pong_WithOnlyOneMatchingHandTile_FailsSkillValidate()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 0), .. SkillTestData.Fillers(12)]);
        SkillTestData.FillDiscards(state, 1, SkillTestData.T(5, 2));

        Assert.False(_verifier.Verify(state, state.Players[0], SkillId.Pong, null, state.Players[1].Discards[0]));
    }

    [Fact]
    public void Kong_Exposed_AcceptsThirteenOrFourteenTiles_RejectsTwelve()
    {
        Assert.True(VerifyExposedKong(handCount: 13));
        Assert.True(VerifyExposedKong(handCount: 14));
        Assert.False(VerifyExposedKong(handCount: 12));
    }

    [Fact]
    public void Kong_Concealed_RequiresNonEmptyWallAndLegalKong()
    {
        GameState ready = ConcealedKongBoard();
        Assert.True(_verifier.Verify(ready, ready.Players[0], SkillId.Kong, null, null));

        GameState emptyWall = ConcealedKongBoard();
        emptyWall.Wall.Clear();
        Assert.False(_verifier.Verify(emptyWall, emptyWall.Players[0], SkillId.Kong, null, null));

        GameState onlyThree = SkillTestData.NewState();
        SkillTestData.FillHand(
            onlyThree,
            0,
            [SkillTestData.T(5, 0), SkillTestData.T(5, 1), SkillTestData.T(5, 2), .. SkillTestData.Fillers(10)]);
        SkillTestData.FillWall(onlyThree, SkillTestData.T(33, 0));
        Assert.False(_verifier.Verify(onlyThree, onlyThree.Players[0], SkillId.Kong, null, null));
    }

    [Fact]
    public void SelfDrawn_RequiresFourteenTiles_AndUsesChecker()
    {
        GameState win = StateWithHand(WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 17, 17));
        Assert.True(_verifier.Verify(win, win.Players[0], SkillId.SelfDrawn, null, null));

        GameState nonWin = StateWithHand(WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13));
        Assert.False(_verifier.Verify(nonWin, nonWin.Players[0], SkillId.SelfDrawn, null, null));

        GameState thirteen = StateWithHand(WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 17));
        Assert.False(_verifier.Verify(thirteen, thirteen.Players[0], SkillId.SelfDrawn, null, null));
    }

    [Fact]
    public void SelfDrawnWith7Pairs_IsDispatchedSeparatelyFromSelfDrawn()
    {
        GameState sevenPairs = StateWithHand(WinHands.SevenPairs());
        Assert.True(_verifier.Verify(sevenPairs, sevenPairs.Players[0], SkillId.SelfDrawnWith7Pairs, null, null));
        Assert.False(_verifier.Verify(sevenPairs, sevenPairs.Players[0], SkillId.SelfDrawn, null, null));

        GameState win = StateWithHand(WinHands.StandardWin());
        Assert.True(_verifier.Verify(win, win.Players[0], SkillId.SelfDrawn, null, null));
        Assert.False(_verifier.Verify(win, win.Players[0], SkillId.SelfDrawnWith7Pairs, null, null));
    }

    [Fact]
    public void SelfDrawn_HandPlusMeldsEqualsFourteen_ReturnsTrue()
    {
        GameState state = StateWithHand(WinCheckerTestData.Hand(0, 0, 0, 1, 2, 3, 4, 5, 6, 8, 8));
        state.Players[0].Melds.Add(WinCheckerTestData.Pong(9));

        Assert.True(_verifier.Verify(state, state.Players[0], SkillId.SelfDrawn, null, null));
    }

    [Fact]
    public void Verify_DoesNotChangeState_OnTrueAndFalsePaths()
    {
        GameState state = PongBoard(out Tile target);
        string snapshotBefore = SkillTestData.Snapshot(state);
        string hashBefore = SkillTestData.Hash(state);
        CardAndSkillVerifier verifier = _verifier;
        PlayerState player = state.Players[0];

        Assert.True(verifier.Verify(state, player, SkillId.Pong, null, target));
        Assert.False(verifier.Verify(state, player, SkillId.Pong, null, SkillTestData.T(7, 0)));
        Assert.False(verifier.Verify(state, player, SkillId.SelfDrawn, null, null));

        // 该局面手牌 13 张，出牌分支要求折合 14 张，因此 false（只验证分支不写状态）。
        Assert.False(verifier.Verify(state, player, null, player.Hand[0], null));

        Assert.Equal(snapshotBefore, SkillTestData.Snapshot(state));
        Assert.Equal(hashBefore, SkillTestData.Hash(state));
    }

    private bool VerifyExposedKong(int handCount)
    {
        GameState state = SkillTestData.NewState();
        Tile[] fillers = SkillTestData.Fillers(handCount - 3);
        SkillTestData.FillHand(
            state,
            0,
            [SkillTestData.T(5, 0), SkillTestData.T(5, 1), SkillTestData.T(5, 2), .. fillers]);
        SkillTestData.FillDiscards(state, 1, SkillTestData.T(5, 3));
        SkillTestData.FillWall(state, SkillTestData.T(33, 0));
        return _verifier.Verify(state, state.Players[0], SkillId.Kong, null, state.Players[1].Discards[0]);
    }

    private static GameState DiscardBoard(out Tile card)
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(
            state,
            0,
            WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13).ToArray());
        card = state.Players[0].Hand[3];
        return state;
    }

    /// <summary>碰响应局面：座位 0 手牌 13 张（2 张 5万 + 填充牌），座位 1 弃牌 [6万, 5万c2, 5万c3]。</summary>
    private static GameState PongBoard(out Tile target)
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [SkillTestData.T(5, 0), SkillTestData.T(5, 1), .. SkillTestData.Fillers(11)]);
        SkillTestData.FillDiscards(state, 1, SkillTestData.T(6, 0), SkillTestData.T(5, 2), SkillTestData.T(5, 3));
        target = state.Players[1].Discards[2];
        return state;
    }

    private static GameState ConcealedKongBoard()
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(
            state,
            0,
            [SkillTestData.T(5, 0), SkillTestData.T(5, 1), SkillTestData.T(5, 2), SkillTestData.T(5, 3),
                .. SkillTestData.Fillers(9)]);
        SkillTestData.FillWall(state, SkillTestData.T(33, 0));
        return state;
    }

    private static GameState StateWithHand(List<Tile> hand)
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [.. hand]);
        return state;
    }

    private static Meld PongMeld(int typeId, byte targetSeat) =>
        new(MeldKind.Pong, RulesTestData.Copies(typeId).Take(3).ToArray(), opened: true, targetSeat: targetSeat);

    /// <summary>自摸用例的固定手牌，测试内独立列出。</summary>
    private static class WinHands
    {
        public static List<Tile> StandardWin() =>
            WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 17, 17);

        public static List<Tile> SevenPairs() =>
            WinCheckerTestData.Hand(0, 0, 1, 1, 2, 2, 9, 9, 10, 10, 18, 18, 27, 27);
    }
}
