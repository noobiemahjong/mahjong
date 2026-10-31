using Mahjong.Core.Bot;
using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Bot;

/// <summary>
/// <see cref="SimpleBotPolicy"/> 安全契约：决策不改局面与牌数、返回的出牌/技能都经真实校验器、
/// 「过」只在弃牌响应窗口出现、不消费随机、同一实例可跨上下文复用、多种子整局每步合法。
/// </summary>
public sealed class SimpleBotPolicySafetyTests
{
    private static readonly IRandomSource Random = new Pcg32(7);

    private sealed record Fixture(string Name, GameState State, BotContext Context);

    [Fact]
    public void Decide_DoesNotChangeState_OnEveryDecisionPath()
    {
        var (policy, _) = NewPolicy();
        Fixture[] fixtures = DecisionFixtures();
        Assert.Equal(5, fixtures.Length);

        foreach (Fixture fixture in fixtures)
        {
            string snapshotBefore = BotTestSupport.Snapshot(fixture.State);
            string hashBefore = BotTestSupport.Hash(fixture.State);
            int tilesBefore = BotTestSupport.TotalTiles(fixture.State);

            _ = policy.Decide(fixture.Context);

            Assert.Equal(snapshotBefore, BotTestSupport.Snapshot(fixture.State));
            Assert.Equal(hashBefore, BotTestSupport.Hash(fixture.State));
            Assert.Equal(tilesBefore, BotTestSupport.TotalTiles(fixture.State));
        }
    }

    [Fact]
    public void DecidedDiscard_IsLegalAndInHand()
    {
        var (policy, verifier) = NewPolicy();
        GameState state = BotTestSupport.DiscardBoard14();
        PlayerState self = state.Players[0];
        var context = new BotContext(state, self, [], Random);

        BotDecision decision = policy.Decide(context);

        Assert.Null(decision.Skill);
        Tile card = Assert.IsType<Tile>(decision.Card);
        int slot = self.Hand.IndexOf(card);
        Assert.InRange(slot, 0, self.Hand.Count - 1);
        Assert.Equal(self.Hand[slot], card);
        Assert.True(verifier.Verify(state, self, null, card, null));
    }

    [Fact]
    public void DecidedSkills_AreVerifiedAndListed()
    {
        var (policy, verifier) = NewPolicy();
        GameState winState = BotTestSupport.DiscardBoard14();
        GameState pongState = BotTestSupport.PongKongBoard(out Tile pongTarget);
        GameState kongState = BotTestSupport.PongKongBoard(out Tile kongTarget);

        var cases = new (GameState State, BotContext Context, SkillId Expected, Tile? Target)[]
        {
            (winState, new BotContext(winState, winState.Players[0], [SkillId.SelfDrawn], Random),
                SkillId.SelfDrawn, null),
            (pongState, new BotContext(pongState, pongState.Players[0], [SkillId.Pong], Random, pongTarget),
                SkillId.Pong, pongTarget),
            (kongState, new BotContext(kongState, kongState.Players[0], [SkillId.Kong], Random, kongTarget),
                SkillId.Kong, kongTarget),
        };

        foreach (var item in cases)
        {
            BotDecision decision = policy.Decide(item.Context);

            Assert.Equal(item.Expected, decision.Skill);
            Assert.Null(decision.Card);
            Assert.Contains(item.Expected, item.Context.AvailableSkills);
            Assert.True(verifier.Verify(item.State, item.State.Players[0], item.Expected, null, item.Target));
        }
    }

    [Fact]
    public void Pass_IsReturnedOnlyInResponseWindow()
    {
        var (policy, _) = NewPolicy();
        GameState passState = BotTestSupport.PassBoard();
        Tile target = passState.Players[1].Discards[0];
        var passContext = new BotContext(
            passState,
            passState.Players[0],
            [SkillId.Pong, SkillId.Kong],
            Random,
            target);

        BotDecision passDecision = policy.Decide(passContext);
        Assert.Null(passDecision.Skill);
        Assert.Null(passDecision.Card);
        Assert.NotEqual(passState.CurrentSeat, passContext.Self.Seat);
        Assert.NotNull(passContext.TargetCard);

        GameState discardState = BotTestSupport.DiscardBoard14();
        BotDecision discardDecision = policy.Decide(
            new BotContext(discardState, discardState.Players[0], [], Random));
        Assert.True(discardDecision.Card.HasValue);

        GameState noTargetState = BotTestSupport.DiscardBoard14();
        noTargetState.CurrentSeat = 1;
        Assert.Throws<InvalidOperationException>(
            () => policy.Decide(new BotContext(noTargetState, noTargetState.Players[0], [], Random)));
    }

    [Fact]
    public void Decide_DoesNotConsumeRandom()
    {
        var (policy, _) = NewPolicy();
        var random = new CountingRandomSource();

        GameState discardState = BotTestSupport.DiscardBoard14();
        policy.Decide(new BotContext(discardState, discardState.Players[0], [], random));

        GameState winState = BotTestSupport.DiscardBoard14();
        policy.Decide(new BotContext(winState, winState.Players[0], [SkillId.SelfDrawn], random));

        GameState pongState = BotTestSupport.PongKongBoard(out Tile target);
        policy.Decide(new BotContext(pongState, pongState.Players[0], [SkillId.Pong], random, target));

        GameState passState = BotTestSupport.PassBoard();
        Tile passTarget = passState.Players[1].Discards[0];
        policy.Decide(new BotContext(passState, passState.Players[0], [SkillId.Pong], random, passTarget));

        Assert.Equal(0, random.Calls);
    }

    [Fact]
    public void Decide_ConservesAll136Tiles()
    {
        var (policy, _) = NewPolicy();
        GameState state = BotTestSupport.NewState();
        state.Deal(new Pcg32(20261029), bankerSeat: 0);
        state.DrawFromWall(0);
        Assert.Equal(136, BotTestSupport.TotalTiles(state));

        var context = new BotContext(state, state.Players[0], [], Random);
        _ = policy.Decide(context);

        Assert.Equal(136, BotTestSupport.TotalTiles(state));
    }

    [Fact]
    public void SamePolicyInstance_IsReusableAcrossContexts()
    {
        var (policy, _) = NewPolicy();
        GameState firstDiscard = BotTestSupport.DiscardBoard14();
        BotDecision first = policy.Decide(new BotContext(firstDiscard, firstDiscard.Players[0], [], Random));

        GameState pongState = BotTestSupport.PongKongBoard(out Tile target);
        BotDecision pong = policy.Decide(
            new BotContext(pongState, pongState.Players[0], [SkillId.Pong], Random, target));

        GameState passState = BotTestSupport.PassBoard();
        Tile passTarget = passState.Players[1].Discards[0];
        BotDecision pass = policy.Decide(
            new BotContext(passState, passState.Players[0], [SkillId.Pong], Random, passTarget));

        GameState secondDiscard = BotTestSupport.DiscardBoard14();
        BotDecision second = policy.Decide(
            new BotContext(secondDiscard, secondDiscard.Players[0], [], Random));

        Assert.Equal(first, second);
        Assert.Equal(SkillId.Pong, pong.Skill);
        Assert.Null(pass.Skill);
        Assert.Null(pass.Card);
    }

    [Fact]
    public void MultiSeedGames_EveryDecisionIsLegal()
    {
        var (policy, verifier) = NewPolicy();
        ulong[] seeds = [1, 42, 20261029];

        foreach (ulong seed in seeds)
        {
            BotGameRun run = BotGameDriver.RunDiscardOnlyGame(seed, policy, verifier);

            Assert.True(run.Completed, $"seed {seed} did not reach an empty wall");
            Assert.Null(run.IllegalTurn);
            Assert.Equal(84, run.Turns.Count);
            Assert.Equal(84, run.TileCountsAfterTurn.Count);
            Assert.All(run.Turns, turn => Assert.True(turn.Legal));
            Assert.All(run.Turns, turn => Assert.Null(turn.Decision.Skill));
            Assert.All(run.Turns, turn => Assert.True(turn.Decision.Card.HasValue));
            Assert.All(
                run.Turns,
                turn => Assert.False(turn.Decision.Skill.HasValue && turn.Decision.Card.HasValue));
            Assert.All(run.TileCountsAfterTurn, count => Assert.Equal(136, count));
        }
    }

    private static (SimpleBotPolicy Policy, CardAndSkillVerifier Verifier) NewPolicy()
    {
        var wins = new ProgrammableWinChecker { Win = true };
        var verifier = new CardAndSkillVerifier(wins, BotTestSupport.Joker);
        return (new SimpleBotPolicy(wins, verifier), verifier);
    }

    private static Fixture[] DecisionFixtures()
    {
        var fixtures = new List<Fixture>();

        GameState discardState = BotTestSupport.DiscardBoard14();
        fixtures.Add(new Fixture(
            "self-turn-discard",
            discardState,
            new BotContext(discardState, discardState.Players[0], [], Random)));

        GameState winState = BotTestSupport.DiscardBoard14();
        fixtures.Add(new Fixture(
            "self-turn-win",
            winState,
            new BotContext(winState, winState.Players[0], [SkillId.SelfDrawn], Random)));

        GameState pongState = BotTestSupport.PongKongBoard(out Tile pongTarget);
        fixtures.Add(new Fixture(
            "pong-response",
            pongState,
            new BotContext(pongState, pongState.Players[0], [SkillId.Pong], Random, pongTarget)));

        GameState kongState = BotTestSupport.PongKongBoard(out Tile kongTarget);
        fixtures.Add(new Fixture(
            "kong-response",
            kongState,
            new BotContext(kongState, kongState.Players[0], [SkillId.Kong], Random, kongTarget)));

        GameState passState = BotTestSupport.PassBoard();
        Tile passTarget = passState.Players[1].Discards[0];
        fixtures.Add(new Fixture(
            "pass",
            passState,
            new BotContext(passState, passState.Players[0], [SkillId.Pong, SkillId.Kong], Random, passTarget)));

        return [.. fixtures];
    }
}
