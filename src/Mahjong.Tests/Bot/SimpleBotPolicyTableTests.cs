using Mahjong.Core.Bot;
using Mahjong.Core.Rules;
using Mahjong.Tests.Content.Tables;
using Xunit;

namespace Mahjong.Tests.Bot;

/// <summary>
/// 真实查表下的能胡必胡：标准胡手牌 → <see cref="SkillId.SelfDrawn"/>；七对牌型且两技能都列 →
/// <see cref="SkillId.SelfDrawnWith7Pairs"/>；非胡手牌 → 出牌。真实查表由
/// <see cref="TableCatalogFixture"/> 共享，整个进程只加载一次 15 MB 数据。
/// </summary>
[Collection("table-catalog")]
public sealed class SimpleBotPolicyTableTests
{
    private static readonly IRandomSource Random = new Pcg32(7);

    private readonly WinChecker _wins;
    private readonly CardAndSkillVerifier _verifier;
    private readonly SimpleBotPolicy _policy;

    public SimpleBotPolicyTableTests(TableCatalogFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _wins = new WinChecker(fixture.Catalog);
        _verifier = new CardAndSkillVerifier(_wins, BotTestSupport.Joker);
        _policy = new SimpleBotPolicy(_wins, _verifier);
    }

    [Fact]
    public void StandardWinningHand_ReturnsSelfDrawn()
    {
        GameState state = BotTestSupport.NewState();
        BotTestSupport.FillHand(
            state,
            0,
            [.. BotTestSupport.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 17, 17)]);
        PlayerState self = state.Players[0];
        var context = new BotContext(state, self, [SkillId.SelfDrawnWith7Pairs, SkillId.SelfDrawn], Random);

        BotDecision decision = _policy.Decide(context);

        Assert.Equal(SkillId.SelfDrawn, decision.Skill);
        Assert.Null(decision.Card);
        Assert.True(_verifier.Verify(state, self, SkillId.SelfDrawn, null, null));
        Assert.False(_verifier.Verify(state, self, SkillId.SelfDrawnWith7Pairs, null, null));
    }

    [Fact]
    public void SevenPairsHandWithBothSkillsListed_ReturnsSevenPairs()
    {
        GameState state = BotTestSupport.NewState();
        BotTestSupport.FillHand(
            state,
            0,
            [.. BotTestSupport.Hand(0, 0, 1, 1, 2, 2, 9, 9, 10, 10, 18, 18, 27, 27)]);
        PlayerState self = state.Players[0];
        var context = new BotContext(state, self, [SkillId.SelfDrawnWith7Pairs, SkillId.SelfDrawn], Random);

        BotDecision decision = _policy.Decide(context);

        Assert.Equal(SkillId.SelfDrawnWith7Pairs, decision.Skill);
        Assert.Null(decision.Card);
        Assert.True(_verifier.Verify(state, self, SkillId.SelfDrawnWith7Pairs, null, null));
        Assert.False(_verifier.Verify(state, self, SkillId.SelfDrawn, null, null));
    }

    [Fact]
    public void NonWinningHand_ReturnsDiscard()
    {
        GameState state = BotTestSupport.DiscardBoard14();
        PlayerState self = state.Players[0];
        var context = new BotContext(state, self, [SkillId.SelfDrawnWith7Pairs, SkillId.SelfDrawn], Random);

        BotDecision decision = _policy.Decide(context);

        Assert.Null(decision.Skill);
        Assert.Equal(BotTestSupport.T(0, 0), decision.Card);
        Assert.False(_verifier.Verify(state, self, SkillId.SelfDrawn, null, null));
        Assert.False(_verifier.Verify(state, self, SkillId.SelfDrawnWith7Pairs, null, null));
    }

    [Fact]
    public void SevenPairsHandWithOnlySelfDrawnListed_ReturnsDiscard()
    {
        GameState state = BotTestSupport.NewState();
        BotTestSupport.FillHand(
            state,
            0,
            [.. BotTestSupport.Hand(0, 0, 1, 1, 2, 2, 9, 9, 10, 10, 18, 18, 27, 27)]);
        PlayerState self = state.Players[0];
        var context = new BotContext(state, self, [SkillId.SelfDrawn], Random);

        BotDecision decision = _policy.Decide(context);

        Assert.Null(decision.Skill);
        Assert.Equal(BotTestSupport.T(0, 0), decision.Card);
        Assert.False(_verifier.Verify(state, self, SkillId.SelfDrawn, null, null));
    }
}
