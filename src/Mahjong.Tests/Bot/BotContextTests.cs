using Mahjong.Core.Bot;
using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Bot;

/// <summary>
/// <see cref="BotContext"/> 构造契约：null 四参数、同座位同一引用、技能列表已定义且不重复、
/// Hand 是手牌实时只读视图、属性暴露输入、构造不改局面、技能列表是构造时快照。
/// </summary>
public sealed class BotContextTests
{
    private static readonly IRandomSource Random = new Pcg32(7);

    [Fact]
    public void Constructor_NullState_Throws()
    {
        GameState state = BotTestSupport.DiscardBoard14();
        var exception = Assert.Throws<ArgumentNullException>(
            () => new BotContext(null!, state.Players[0], [], Random));

        Assert.Equal("state", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullSelf_Throws()
    {
        GameState state = BotTestSupport.DiscardBoard14();
        var exception = Assert.Throws<ArgumentNullException>(
            () => new BotContext(state, null!, [], Random));

        Assert.Equal("self", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullAvailableSkills_Throws()
    {
        GameState state = BotTestSupport.DiscardBoard14();
        var exception = Assert.Throws<ArgumentNullException>(
            () => new BotContext(state, state.Players[0], null!, Random));

        Assert.Equal("availableSkills", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullRandom_Throws()
    {
        GameState state = BotTestSupport.DiscardBoard14();
        var exception = Assert.Throws<ArgumentNullException>(
            () => new BotContext(state, state.Players[0], [], null!));

        Assert.Equal("random", exception.ParamName);
    }

    [Fact]
    public void Constructor_LookalikePlayerWithSameSeat_Throws()
    {
        GameState state = BotTestSupport.DiscardBoard14();
        var lookalike = new PlayerState(0);

        var exception = Assert.Throws<ArgumentException>(
            () => new BotContext(state, lookalike, [], Random));

        Assert.Equal("self", exception.ParamName);
        Assert.NotSame(state.Players[0], lookalike);
    }

    [Theory]
    [InlineData((uint)0)]
    [InlineData((uint)5)]
    [InlineData((uint)255)]
    public void Constructor_UndefinedSkill_Throws(uint rawSkill)
    {
        GameState state = BotTestSupport.DiscardBoard14();

        var exception = Assert.Throws<ArgumentException>(
            () => new BotContext(state, state.Players[0], [(SkillId)rawSkill], Random));

        Assert.Equal("availableSkills", exception.ParamName);
        Assert.Contains("undefined skill id", exception.Message);
        Assert.Contains(rawSkill.ToString(), exception.Message);
    }

    [Fact]
    public void Constructor_DuplicateSkill_Throws()
    {
        GameState state = BotTestSupport.DiscardBoard14();

        var duplicatePair = Assert.Throws<ArgumentException>(
            () => new BotContext(state, state.Players[0], [SkillId.Pong, SkillId.Pong], Random));
        Assert.Equal("availableSkills", duplicatePair.ParamName);

        var duplicateNonAdjacent = Assert.Throws<ArgumentException>(
            () => new BotContext(
                state,
                state.Players[0],
                [SkillId.SelfDrawn, SkillId.Kong, SkillId.SelfDrawn],
                Random));
        Assert.Equal("availableSkills", duplicateNonAdjacent.ParamName);
    }

    [Fact]
    public void Constructor_EmptySkillsAndNullOptionalArguments_Allows()
    {
        GameState state = BotTestSupport.DiscardBoard14();

        var context = new BotContext(state, state.Players[0], [], Random);

        Assert.Empty(context.AvailableSkills);
        Assert.Null(context.TargetCard);
        Assert.Null(context.LastDrawn);
    }

    [Fact]
    public void Constructor_AllFourDefinedSkills_ArePreserved()
    {
        GameState state = BotTestSupport.DiscardBoard14();
        SkillId[] skills = [SkillId.SelfDrawn, SkillId.Kong, SkillId.Pong, SkillId.SelfDrawnWith7Pairs];

        var context = new BotContext(state, state.Players[0], skills, Random);

        // 契约明确顺序无语义：只比较集合成员，不绑定遍历顺序。
        Assert.Equal(skills.Order(), context.AvailableSkills.Order());
    }

    [Fact]
    public void AvailableSkills_IsSnapshot_InputListMutationDoesNotReachContext()
    {
        GameState state = BotTestSupport.DiscardBoard14();
        var skills = new List<SkillId> { SkillId.Pong };

        // 契约要求构造后不可变；实现选择构造时复制快照，因此调用方后续改动输入列表不影响上下文。
        var context = new BotContext(state, state.Players[0], skills, Random);
        skills.Add(SkillId.Kong);

        Assert.Equal([SkillId.Pong], context.AvailableSkills);
    }

    [Fact]
    public void Hand_IsLiveReadOnlyViewOfSelfHand()
    {
        GameState state = BotTestSupport.NewState();
        PlayerState self = state.Players[0];
        var context = new BotContext(state, self, [], Random);

        Assert.Same(self.Hand.Items, context.Hand);
        Assert.Empty(context.Hand);

        self.Hand.Append(BotTestSupport.T(3, 0));
        self.Hand.Append(BotTestSupport.T(1, 0));

        Assert.Equal(2, context.Hand.Count);
        Assert.Equal(
            [BotTestSupport.T(3, 0), BotTestSupport.T(1, 0)],
            context.Hand);
    }

    [Fact]
    public void Hand_IsNotSorted_NorCopied()
    {
        GameState state = BotTestSupport.NewState();
        BotTestSupport.FillHand(state, 0, BotTestSupport.T(9, 0), BotTestSupport.T(0, 0));

        var context = new BotContext(state, state.Players[0], [], Random);

        Assert.Equal(BotTestSupport.T(9, 0), context.Hand[0]);
        Assert.Equal(BotTestSupport.T(0, 0), context.Hand[1]);
        Assert.Same(state.Players[0].Hand.Items, context.Hand);
    }

    [Fact]
    public void Properties_ExposeInputReferencesAndContents()
    {
        GameState state = BotTestSupport.PongKongBoard(out Tile target);
        PlayerState self = state.Players[0];
        var random = new Pcg32(11);
        SkillId[] skills = [SkillId.Pong, SkillId.Kong];
        Tile lastDrawn = BotTestSupport.T(20, 0);

        var context = new BotContext(state, self, skills, random, target, lastDrawn);

        Assert.Same(state, context.State);
        Assert.Same(self, context.Self);
        Assert.Same(random, context.Random);
        Assert.Equal(target, context.TargetCard);
        Assert.Equal(lastDrawn, context.LastDrawn);
        Assert.Equal(skills, context.AvailableSkills);
        Assert.Equal(self.Hand.Items, context.Hand);
    }

    [Fact]
    public void Constructor_DoesNotValidateTargetCardOrLastDrawn()
    {
        GameState state = BotTestSupport.DiscardBoard14();
        Tile notInState = BotTestSupport.T(20, 0);

        var context = new BotContext(state, state.Players[0], [], Random, notInState, notInState);

        Assert.Equal(notInState, context.TargetCard);
        Assert.Equal(notInState, context.LastDrawn);
        Assert.DoesNotContain(notInState, state.Players[0].Hand.Items);
        Assert.DoesNotContain(notInState, state.Players[0].Discards.Items);
    }

    [Fact]
    public void Constructor_DoesNotChangeState()
    {
        GameState state = BotTestSupport.PongKongBoard(out Tile target);
        string snapshotBefore = BotTestSupport.Snapshot(state);
        string hashBefore = BotTestSupport.Hash(state);
        int tilesBefore = BotTestSupport.TotalTiles(state);

        _ = new BotContext(
            state,
            state.Players[0],
            [SkillId.Pong, SkillId.Kong],
            Random,
            target,
            BotTestSupport.T(20, 0));

        Assert.Equal(snapshotBefore, BotTestSupport.Snapshot(state));
        Assert.Equal(hashBefore, BotTestSupport.Hash(state));
        Assert.Equal(tilesBefore, BotTestSupport.TotalTiles(state));
    }
}
