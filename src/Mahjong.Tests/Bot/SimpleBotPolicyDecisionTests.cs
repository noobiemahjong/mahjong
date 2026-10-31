using Mahjong.Core.Bot;
using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Bot;

/// <summary>
/// <see cref="SimpleBotPolicy"/> 决策矩阵：自己回合 七对自摸 → 普通自摸 → 出牌；
/// 弃牌响应窗口 杠 → 碰 → 过；技能必须由可用列表把关且每个非 pass 动作先过真实校验器；
/// 不属于任何决策语境时抛异常。能胡判定用可编程 stub 以钉住分支顺序，真实查表见
/// <see cref="SimpleBotPolicyTableTests"/>。
/// </summary>
public sealed class SimpleBotPolicyDecisionTests
{
    private static readonly IRandomSource Random = new Pcg32(7);

    private static (SimpleBotPolicy Policy, ProgrammableWinChecker Wins, CardAndSkillVerifier Verifier)
        NewPolicy(bool win = false, bool sevenPairs = false)
    {
        var wins = new ProgrammableWinChecker { Win = win, SevenPairs = sevenPairs };
        var verifier = new CardAndSkillVerifier(wins, BotTestSupport.Joker);
        return (new SimpleBotPolicy(wins, verifier), wins, verifier);
    }

    /// <summary>标准胡牌型（4 组面子 + 1 将）；与真实查表用例使用同一手牌。</summary>
    private static GameState StandardWinBoard()
    {
        GameState state = BotTestSupport.NewState();
        BotTestSupport.FillHand(
            state,
            0,
            [.. BotTestSupport.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 17, 17)]);
        return state;
    }

    /// <summary>七对牌型；与真实查表用例使用同一手牌。</summary>
    private static GameState SevenPairsBoard()
    {
        GameState state = BotTestSupport.NewState();
        BotTestSupport.FillHand(
            state,
            0,
            [.. BotTestSupport.Hand(0, 0, 1, 1, 2, 2, 9, 9, 10, 10, 18, 18, 27, 27)]);
        return state;
    }

    /// <summary>
    /// 弃牌响应局面：座位 0 手牌 13 张含 2 张指定牌种，座位 1 最后一张弃牌是同牌种第 3 张，当前座位 1。
    /// 只在 <paramref name="typeId"/> 为 0 或 31 时使用，避免与填充牌牌种重叠。
    /// </summary>
    private static GameState ResponseBoardWithPairOfType(int typeId, out Tile target)
    {
        GameState state = BotTestSupport.NewState();
        BotTestSupport.FillHand(
            state,
            0,
            [BotTestSupport.T(typeId, 0), BotTestSupport.T(typeId, 1), .. BotTestSupport.Fillers(11)]);
        BotTestSupport.FillDiscards(state, 1, BotTestSupport.T(typeId, 2));
        state.CurrentSeat = 1;
        target = state.Players[1].Discards[0];
        return state;
    }

    [Fact]
    public void Constructor_NullWins_Throws()
    {
        var verifier = new CardAndSkillVerifier(new ProgrammableWinChecker(), BotTestSupport.Joker);

        var exception = Assert.Throws<ArgumentNullException>(() => new SimpleBotPolicy(null!, verifier));

        Assert.Equal("wins", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullVerifier_Throws()
    {
        var wins = new ProgrammableWinChecker();

        var exception = Assert.Throws<ArgumentNullException>(() => new SimpleBotPolicy(wins, null!));

        Assert.Equal("verifier", exception.ParamName);
    }

    [Fact]
    public void Constructor_WinsDependency_IsOnlyNullChecked()
    {
        var verifierWins = new ProgrammableWinChecker { Win = true };
        var verifier = new CardAndSkillVerifier(verifierWins, BotTestSupport.Joker);
        var policy = new SimpleBotPolicy(new ThrowingWinChecker(), verifier);
        GameState state = BotTestSupport.DiscardBoard14();
        var context = new BotContext(state, state.Players[0], [SkillId.SelfDrawn], Random);

        BotDecision decision = policy.Decide(context);

        Assert.Equal(SkillId.SelfDrawn, decision.Skill);
    }

    [Fact]
    public void Decide_NullContext_Throws()
    {
        var (policy, _, _) = NewPolicy();

        var exception = Assert.Throws<ArgumentNullException>(() => policy.Decide(null!));

        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public void SelfTurn_SevenPairsHand_ReturnsSevenPairsSkill()
    {
        var (policy, wins, _) = NewPolicy(sevenPairs: true);
        GameState state = SevenPairsBoard();
        var context = new BotContext(
            state,
            state.Players[0],
            [SkillId.SelfDrawnWith7Pairs, SkillId.SelfDrawn],
            Random);

        BotDecision decision = policy.Decide(context);

        Assert.Equal(SkillId.SelfDrawnWith7Pairs, decision.Skill);
        Assert.Null(decision.Card);
        Assert.Equal(["IsSevenPairs"], wins.Calls);
    }

    [Fact]
    public void SelfTurn_StandardWinningHand_ReturnsSelfDrawnSkill()
    {
        var (policy, wins, _) = NewPolicy(win: true);
        GameState state = StandardWinBoard();
        var context = new BotContext(
            state,
            state.Players[0],
            [SkillId.SelfDrawnWith7Pairs, SkillId.SelfDrawn],
            Random);

        BotDecision decision = policy.Decide(context);

        Assert.Equal(SkillId.SelfDrawn, decision.Skill);
        Assert.Null(decision.Card);
        Assert.Equal(["IsSevenPairs", "IsWin", "IsSevenPairs"], wins.Calls);
    }

    [Fact]
    public void SelfTurn_SevenPairsWinsOverStandardWin()
    {
        var (policy, wins, _) = NewPolicy(win: true, sevenPairs: true);
        GameState state = SevenPairsBoard();
        var context = new BotContext(
            state,
            state.Players[0],
            [SkillId.SelfDrawnWith7Pairs, SkillId.SelfDrawn],
            Random);

        BotDecision decision = policy.Decide(context);

        Assert.Equal(SkillId.SelfDrawnWith7Pairs, decision.Skill);
        Assert.DoesNotContain("IsWin", wins.Calls);
    }

    [Fact]
    public void SelfTurn_WinningHandButSkillNotListed_Discards()
    {
        var (policy, wins, _) = NewPolicy(win: true);
        GameState state = StandardWinBoard();
        var context = new BotContext(state, state.Players[0], [], Random);
        Tile expected = state.Players[0].Hand[0];

        BotDecision decision = policy.Decide(context);

        Assert.Null(decision.Skill);
        Assert.Equal(expected, decision.Card);
        Assert.Empty(wins.Calls);
    }

    [Fact]
    public void SelfTurn_SevenPairsHandWithOnlySelfDrawnListed_Discards()
    {
        var (policy, _, _) = NewPolicy(win: true, sevenPairs: true);
        GameState state = SevenPairsBoard();
        var context = new BotContext(state, state.Players[0], [SkillId.SelfDrawn], Random);
        Tile expected = state.Players[0].Hand[0];

        BotDecision decision = policy.Decide(context);

        Assert.Null(decision.Skill);
        Assert.Equal(expected, decision.Card);
    }

    [Fact]
    public void SelfTurn_LastDrawnLegal_IsPreferredOverEarlierHandSlot()
    {
        var (policy, _, verifier) = NewPolicy();
        GameState state = BotTestSupport.DiscardBoard14();
        Tile lastDrawn = state.Players[0].Hand[7];
        var context = new BotContext(state, state.Players[0], [], Random, lastDrawn: lastDrawn);

        BotDecision decision = policy.Decide(context);

        Assert.Null(decision.Skill);
        Assert.Equal(lastDrawn, decision.Card);
        Assert.True(verifier.Verify(state, state.Players[0], null, lastDrawn, null));
    }

    [Fact]
    public void SelfTurn_LastDrawnIsJoker_FallsBackToFirstHandSlot()
    {
        var (policy, _, verifier) = NewPolicy();
        GameState state = BotTestSupport.NewState();
        Tile joker = BotTestSupport.T(BotTestSupport.JokerTypeId, 0);
        BotTestSupport.FillHand(state, 0, [BotTestSupport.T(0, 0), joker, .. BotTestSupport.Hand(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)]);
        var context = new BotContext(state, state.Players[0], [], Random, lastDrawn: joker);

        BotDecision decision = policy.Decide(context);

        Assert.Equal(BotTestSupport.T(0, 0), decision.Card);
        Assert.False(verifier.Verify(state, state.Players[0], null, joker, null));
    }

    [Fact]
    public void SelfTurn_LastDrawnType31_IsDiscardedOnlyWhenNotJoker()
    {
        var wins = new ProgrammableWinChecker();
        var noJokerPolicy = new SimpleBotPolicy(wins, new CardAndSkillVerifier(wins));
        var joker31Policy = new SimpleBotPolicy(wins, new CardAndSkillVerifier(wins, BotTestSupport.Joker));

        GameState state = BotTestSupport.NewState();
        Tile lastDrawn = BotTestSupport.T(BotTestSupport.JokerTypeId, 0);
        BotTestSupport.FillHand(
            state,
            0,
            [.. BotTestSupport.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12), lastDrawn]);
        Assert.Equal(lastDrawn, state.Players[0].Hand[13]);
        var context = new BotContext(state, state.Players[0], [], Random, lastDrawn: lastDrawn);

        // 无鬼：T31 是普通牌，LastDrawn 合法，直接打出。
        BotDecision noJokerDecision = noJokerPolicy.Decide(context);
        Assert.Null(noJokerDecision.Skill);
        Assert.Equal(lastDrawn, noJokerDecision.Card);

        // joker=31：同一 context 下 T31 是鬼、不可打出，回退到槽位 0。
        BotDecision joker31Decision = joker31Policy.Decide(context);
        Assert.Null(joker31Decision.Skill);
        Assert.Equal(BotTestSupport.T(0, 0), joker31Decision.Card);
        Assert.NotEqual(lastDrawn, joker31Decision.Card);
    }

    [Fact]
    public void SelfTurn_LastDrawnNotInHand_FallsBackToFirstHandSlot()
    {
        var (policy, _, _) = NewPolicy();
        GameState state = BotTestSupport.DiscardBoard14();
        var context = new BotContext(
            state,
            state.Players[0],
            [],
            Random,
            lastDrawn: BotTestSupport.T(20, 0));

        BotDecision decision = policy.Decide(context);

        Assert.Equal(BotTestSupport.T(0, 0), decision.Card);
    }

    [Fact]
    public void SelfTurn_FirstHandSlotIsJoker_FallsBackToNextLegalSlot()
    {
        var (policy, _, _) = NewPolicy();
        GameState state = BotTestSupport.NewState();
        BotTestSupport.FillHand(
            state,
            0,
            [BotTestSupport.T(BotTestSupport.JokerTypeId, 0), BotTestSupport.T(0, 0),
                .. BotTestSupport.Hand(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)]);
        var context = new BotContext(state, state.Players[0], [], Random);

        BotDecision decision = policy.Decide(context);

        Assert.Equal(BotTestSupport.T(0, 0), decision.Card);
    }

    [Fact]
    public void SelfTurn_NoLegalDiscard_ThrowsAndKeepsState()
    {
        var (policy, _, _) = NewPolicy();
        GameState state = BotTestSupport.ThrowBoard13();
        var context = new BotContext(state, state.Players[0], [], Random);
        string snapshotBefore = BotTestSupport.Snapshot(state);
        string hashBefore = BotTestSupport.Hash(state);

        Assert.Throws<InvalidOperationException>(() => policy.Decide(context));

        Assert.Equal(snapshotBefore, BotTestSupport.Snapshot(state));
        Assert.Equal(hashBefore, BotTestSupport.Hash(state));
    }

    [Fact]
    public void SelfTurn_DiscardIsNotConstrainedByAvailableSkills()
    {
        var (policy, wins, _) = NewPolicy();
        GameState state = BotTestSupport.DiscardBoard14();
        var context = new BotContext(state, state.Players[0], [SkillId.Pong, SkillId.Kong], Random);

        BotDecision decision = policy.Decide(context);

        Assert.Null(decision.Skill);
        Assert.Equal(BotTestSupport.T(0, 0), decision.Card);
        Assert.Empty(wins.Calls);
    }

    [Fact]
    public void SelfTurn_ConcealedKongIsLegalButPolicyStillDiscards()
    {
        var wins = new ProgrammableWinChecker();
        var verifier = new CardAndSkillVerifier(wins, BotTestSupport.Joker);
        var policy = new SimpleBotPolicy(wins, verifier);
        GameState state = BotTestSupport.NewState();
        BotTestSupport.FillHand(
            state,
            0,
            [BotTestSupport.T(5, 0), BotTestSupport.T(5, 1), BotTestSupport.T(5, 2), BotTestSupport.T(5, 3),
                .. BotTestSupport.Fillers(10)]);
        BotTestSupport.FillWall(state, BotTestSupport.T(33, 0));
        PlayerState self = state.Players[0];
        Assert.True(verifier.Verify(state, self, SkillId.Kong, null, null));

        var context = new BotContext(state, self, [SkillId.Kong], Random);

        BotDecision decision = policy.Decide(context);

        Assert.Null(decision.Skill);
        Tile card = Assert.IsType<Tile>(decision.Card);
        Assert.Equal(BotTestSupport.T(5, 0), card);
        Assert.True(verifier.Verify(state, self, null, card, null));
    }

    [Fact]
    public void Response_KongAndPongBothLegal_ReturnsKong()
    {
        var (policy, _, verifier) = NewPolicy();
        GameState state = BotTestSupport.PongKongBoard(out Tile target);
        var context = new BotContext(state, state.Players[0], [SkillId.Pong, SkillId.Kong], Random, target);

        BotDecision decision = policy.Decide(context);

        Assert.Equal(SkillId.Kong, decision.Skill);
        Assert.Null(decision.Card);
        Assert.True(verifier.Verify(state, state.Players[0], SkillId.Kong, null, target));
    }

    [Fact]
    public void Response_OnlyKongListed_ReturnsKong()
    {
        var (policy, _, _) = NewPolicy();
        GameState state = BotTestSupport.PongKongBoard(out Tile target);
        var context = new BotContext(state, state.Players[0], [SkillId.Kong], Random, target);

        BotDecision decision = policy.Decide(context);

        Assert.Equal(SkillId.Kong, decision.Skill);
        Assert.Null(decision.Card);
    }

    [Fact]
    public void Response_OnlyPongListed_ReturnsPong()
    {
        var (policy, _, verifier) = NewPolicy();
        GameState state = BotTestSupport.PongKongBoard(out Tile target);
        var context = new BotContext(state, state.Players[0], [SkillId.Pong], Random, target);

        BotDecision decision = policy.Decide(context);

        Assert.Equal(SkillId.Pong, decision.Skill);
        Assert.Null(decision.Card);
        Assert.True(verifier.Verify(state, state.Players[0], SkillId.Pong, null, target));
    }

    [Fact]
    public void Response_NoLegalSkill_ReturnsPass()
    {
        var (policy, _, _) = NewPolicy();
        GameState state = BotTestSupport.PassBoard();
        Tile target = state.Players[1].Discards[0];
        var context = new BotContext(state, state.Players[0], [SkillId.Pong, SkillId.Kong], Random, target);

        BotDecision decision = policy.Decide(context);

        Assert.Null(decision.Skill);
        Assert.Null(decision.Card);
        Assert.Equal(default, decision);
    }

    [Fact]
    public void Response_EmptyAvailableSkills_ReturnsPass()
    {
        var (policy, _, _) = NewPolicy();
        GameState state = BotTestSupport.PongKongBoard(out Tile target);
        var context = new BotContext(state, state.Players[0], [], Random, target);

        BotDecision decision = policy.Decide(context);

        Assert.Null(decision.Skill);
        Assert.Null(decision.Card);
    }

    [Fact]
    public void Response_TargetCardIsJoker_ReturnsPass()
    {
        var (policy, _, _) = NewPolicy();
        GameState state = BotTestSupport.JokerTargetBoard();
        Tile target = state.Players[1].Discards[0];
        var context = new BotContext(state, state.Players[0], [SkillId.Pong, SkillId.Kong], Random, target);

        BotDecision decision = policy.Decide(context);

        Assert.Null(decision.Skill);
        Assert.Null(decision.Card);
    }

    [Fact]
    public void Response_Type31TargetPong_IsIllegalOnlyForJoker31Verifier()
    {
        GameState state = ResponseBoardWithPairOfType(31, out Tile target);

        BotDecision DecideWithJoker(TileType joker)
        {
            var wins = new ProgrammableWinChecker();
            var verifier = new CardAndSkillVerifier(wins, joker);
            var policy = new SimpleBotPolicy(wins, verifier);
            return policy.Decide(new BotContext(state, state.Players[0], [SkillId.Pong], Random, target));
        }

        BotDecision noJoker = DecideWithJoker(default(TileType));
        BotDecision joker31 = DecideWithJoker(BotTestSupport.Joker);
        BotDecision joker0 = DecideWithJoker(BotTestSupport.T(0, 0).Type);

        Assert.Equal(SkillId.Pong, noJoker.Skill);
        Assert.Null(noJoker.Card);
        Assert.Null(joker31.Skill);
        Assert.Null(joker31.Card);
        Assert.Equal(SkillId.Pong, joker0.Skill);
        Assert.Null(joker0.Card);
    }

    [Fact]
    public void Response_Type0TargetPong_IsIllegalOnlyForJokerType0Verifier()
    {
        GameState state = ResponseBoardWithPairOfType(0, out Tile target);

        BotDecision DecideWithJoker(TileType joker)
        {
            var wins = new ProgrammableWinChecker();
            var verifier = new CardAndSkillVerifier(wins, joker);
            var policy = new SimpleBotPolicy(wins, verifier);
            return policy.Decide(new BotContext(state, state.Players[0], [SkillId.Pong], Random, target));
        }

        BotDecision noJoker = DecideWithJoker(default(TileType));
        BotDecision joker31 = DecideWithJoker(BotTestSupport.Joker);
        BotDecision joker0 = DecideWithJoker(BotTestSupport.T(0, 0).Type);

        Assert.Equal(SkillId.Pong, noJoker.Skill);
        Assert.Null(noJoker.Card);
        Assert.Equal(SkillId.Pong, joker31.Skill);
        Assert.Null(joker31.Card);
        Assert.Null(joker0.Skill);
        Assert.Null(joker0.Card);
    }

    [Fact]
    public void Response_WallEmpty_RejectsKongAndReturnsPong()
    {
        var (policy, _, _) = NewPolicy();
        GameState state = BotTestSupport.PongKongBoard(out Tile target, wallTile: false);
        var context = new BotContext(state, state.Players[0], [SkillId.Pong, SkillId.Kong], Random, target);

        BotDecision decision = policy.Decide(context);

        Assert.Equal(SkillId.Pong, decision.Skill);
        Assert.Null(decision.Card);
    }

    [Fact]
    public void Response_NeverReturnsDiscard()
    {
        var (policy, _, _) = NewPolicy();
        GameState kongBoard = BotTestSupport.PongKongBoard(out Tile kongTarget);
        GameState passBoard = BotTestSupport.PassBoard();
        Tile passTarget = passBoard.Players[1].Discards[0];

        BotDecision kongDecision = policy.Decide(
            new BotContext(kongBoard, kongBoard.Players[0], [SkillId.Kong, SkillId.Pong], Random, kongTarget));
        BotDecision passDecision = policy.Decide(
            new BotContext(passBoard, passBoard.Players[0], [SkillId.Kong, SkillId.Pong], Random, passTarget));

        Assert.Null(kongDecision.Card);
        Assert.Null(passDecision.Card);
    }

    [Fact]
    public void NotSelfTurn_WithoutTargetCard_ThrowsAndKeepsState()
    {
        var (policy, _, _) = NewPolicy();
        GameState state = BotTestSupport.DiscardBoard14();
        state.CurrentSeat = 1;
        var context = new BotContext(state, state.Players[0], [], Random);
        string snapshotBefore = BotTestSupport.Snapshot(state);
        string hashBefore = BotTestSupport.Hash(state);

        Assert.Throws<InvalidOperationException>(() => policy.Decide(context));

        Assert.Equal(snapshotBefore, BotTestSupport.Snapshot(state));
        Assert.Equal(hashBefore, BotTestSupport.Hash(state));
    }

    [Fact]
    public void AvailableSkillsOrder_DoesNotChangeDecision()
    {
        var (policy, _, _) = NewPolicy();
        GameState first = BotTestSupport.PongKongBoard(out Tile firstTarget);
        GameState second = BotTestSupport.PongKongBoard(out Tile secondTarget);

        BotDecision firstDecision = policy.Decide(
            new BotContext(first, first.Players[0], [SkillId.Pong, SkillId.Kong], Random, firstTarget));
        BotDecision secondDecision = policy.Decide(
            new BotContext(second, second.Players[0], [SkillId.Kong, SkillId.Pong], Random, secondTarget));

        Assert.Equal(firstDecision, secondDecision);
        Assert.Equal(SkillId.Kong, firstDecision.Skill);
    }

    [Fact]
    public void SelfTurn_WithTargetCard_StillTakesSelfTurnBranch()
    {
        var (policy, _, _) = NewPolicy(win: true);
        GameState state = StandardWinBoard();
        BotTestSupport.FillDiscards(state, 1, BotTestSupport.T(20, 0));
        Tile target = state.Players[1].Discards[0];
        var context = new BotContext(
            state,
            state.Players[0],
            [SkillId.SelfDrawn, SkillId.Pong, SkillId.Kong],
            Random,
            target);

        BotDecision decision = policy.Decide(context);

        Assert.Equal(SkillId.SelfDrawn, decision.Skill);
        Assert.Null(decision.Card);
    }

    /// <summary>一旦被调用就抛异常的胡牌判定器：证明策略只做 null 校验、不在决策时使用构造注入的 wins。</summary>
    private sealed class ThrowingWinChecker : IWinChecker
    {
        /// <inheritdoc />
        public bool IsWin(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds, TileType joker) =>
            throw new InvalidOperationException("The policy must not call its wins dependency.");

        /// <inheritdoc />
        public IReadOnlyList<TileType> GetWinningTypes(
            IReadOnlyList<Tile> hand,
            IReadOnlyList<Meld> melds,
            TileType joker) =>
            throw new InvalidOperationException("The policy must not call its wins dependency.");

        /// <inheritdoc />
        public bool IsSevenPairs(IReadOnlyList<Tile> hand, TileType joker) =>
            throw new InvalidOperationException("The policy must not call its wins dependency.");
    }
}
