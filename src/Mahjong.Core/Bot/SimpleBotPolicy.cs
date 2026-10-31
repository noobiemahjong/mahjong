using Mahjong.Core.Rules;

namespace Mahjong.Core.Bot;

/// <summary>
/// M0.2 的「合法即可」Bot 策略：自己回合按 七对自摸 → 普通自摸 → 出牌 决策；
/// 弃牌响应窗口按 杠 → 碰 → 过 决策。每个非「过」选择都先经 <see cref="ICardAndSkillVerifier"/> 校验。
/// 策略只读局面，不读时钟、不 sleep、不做 IO、不消费 <see cref="BotContext.Random"/>。
/// </summary>
public sealed class SimpleBotPolicy : IBotPolicy
{
    private readonly ICardAndSkillVerifier _verifier;

    /// <summary>创建策略。</summary>
    /// <remarks>
    /// <paramref name="wins"/> 只做依赖完整性校验、不保存：策略不知道该局的鬼牌牌种，无法正确调用胡牌判定；
    /// 动作合法性唯一权威是 <paramref name="verifier"/>，它内部已注入胡牌判定与鬼牌。
    /// 保存一个不能正确使用的依赖只会误导后续维护者。
    /// </remarks>
    /// <param name="wins">胡牌判定器；只用于 null 校验。</param>
    /// <param name="verifier">动作二次校验器；每个非「过」动作在返回前都经它校验一次。</param>
    /// <exception cref="ArgumentNullException"><paramref name="wins"/> 或 <paramref name="verifier"/> 为 null。</exception>
    public SimpleBotPolicy(IWinChecker wins, ICardAndSkillVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(wins);
        ArgumentNullException.ThrowIfNull(verifier);
        _verifier = verifier;
    }

    /// <summary>
    /// 决策顺序固定：自己回合（七对自摸 → 普通自摸 → 出牌）；非自己回合且有目标弃牌（杠 → 碰 → 过）；
    /// 其余情况抛异常。返回的技能一定在 <see cref="BotContext.AvailableSkills"/> 内，出牌不受技能列表约束。
    /// </summary>
    /// <param name="context">决策上下文；只读。</param>
    /// <returns>技能动作（<see cref="BotDecision.Card"/> 为 null）、出牌（<see cref="BotDecision.Skill"/> 为 null）或「过」（两者皆 null）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">
    /// 非自己回合且没有目标弃牌（不属于任何合法决策语境）；或自己回合没有任何合法出牌。
    /// 异常抛出前不修改局面。
    /// </exception>
    public BotDecision Decide(BotContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        GameState state = context.State;
        PlayerState self = context.Self;

        if (state.CurrentSeat == self.Seat)
        {
            // 七对优先于普通自摸：同一手牌只会命中七对技能，顺序仍按契约固定。
            if (HasSkill(context, SkillId.SelfDrawnWith7Pairs)
                && _verifier.Verify(state, self, SkillId.SelfDrawnWith7Pairs, null, null))
            {
                return new BotDecision(SkillId.SelfDrawnWith7Pairs, null);
            }

            if (HasSkill(context, SkillId.SelfDrawn)
                && _verifier.Verify(state, self, SkillId.SelfDrawn, null, null))
            {
                return new BotDecision(SkillId.SelfDrawn, null);
            }

            return DecideDiscard(context);
        }

        if (context.TargetCard is Tile targetCard)
        {
            // 响应弃牌：杠优先于碰；两者都不可行时过。
            if (HasSkill(context, SkillId.Kong)
                && _verifier.Verify(state, self, SkillId.Kong, null, targetCard))
            {
                return new BotDecision(SkillId.Kong, null);
            }

            if (HasSkill(context, SkillId.Pong)
                && _verifier.Verify(state, self, SkillId.Pong, null, targetCard))
            {
                return new BotDecision(SkillId.Pong, null);
            }

            return default;
        }

        throw new InvalidOperationException(
            "SimpleBotPolicy cannot decide: it is not the self player's turn and there is no target card to respond to.");
    }

    /// <summary>出牌：LastDrawn 合法优先，否则按手牌槽位升序取第一张合法牌。</summary>
    private BotDecision DecideDiscard(BotContext context)
    {
        GameState state = context.State;
        PlayerState self = context.Self;

        if (context.LastDrawn is Tile lastDrawn
            && _verifier.Verify(state, self, null, lastDrawn, null))
        {
            return new BotDecision(null, lastDrawn);
        }

        IReadOnlyList<Tile> hand = context.Hand;
        for (int slot = 0; slot < hand.Count; slot++)
        {
            Tile tile = hand[slot];
            if (_verifier.Verify(state, self, null, tile, null))
            {
                return new BotDecision(null, tile);
            }
        }

        throw new InvalidOperationException(
            "SimpleBotPolicy cannot decide: the hand contains no legal discard tile.");
    }

    /// <summary>技能是否在本次可用列表内；策略按成员判定，不按顺序。</summary>
    private static bool HasSkill(BotContext context, SkillId skill)
    {
        IReadOnlyList<SkillId> available = context.AvailableSkills;
        for (int index = 0; index < available.Count; index++)
        {
            if (available[index] == skill)
            {
                return true;
            }
        }

        return false;
    }
}
