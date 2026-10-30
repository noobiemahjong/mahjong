namespace Mahjong.Core.Rules;

/// <summary>
/// 七对自摸：手牌恰好 14 张且按牌种两两成对（鬼牌可补位），副露不参与判定。
/// 七对优先于普通自摸：同一手牌只允许 <see cref="SelfDrawnWith7PairsSkill"/> 命中。
/// </summary>
public sealed class SelfDrawnWith7PairsSkill : Skill
{
    private readonly IWinChecker _wins;

    /// <summary>创建七对自摸技能。</summary>
    /// <param name="wins">胡牌判定器；判定期只读。</param>
    /// <param name="joker">当前局鬼牌牌种；<c>default(TileType)</c> 表示本局无鬼。</param>
    /// <exception cref="ArgumentNullException"><paramref name="wins"/> 为 null。</exception>
    public SelfDrawnWith7PairsSkill(IWinChecker wins, TileType joker = default)
        : base(joker)
    {
        ArgumentNullException.ThrowIfNull(wins);
        _wins = wins;
    }

    /// <inheritdoc />
    public override SkillId Id => SkillId.SelfDrawnWith7Pairs;

    /// <summary>
    /// 前置条件：<see cref="IWinChecker.IsSevenPairs"/> 为 true。本方法不改局面。
    /// </summary>
    /// <inheritdoc />
    public override bool Validate(SkillContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _wins.IsSevenPairs(context.Source.Hand.Items, Joker);
    }

    /// <summary>
    /// 只复校验，不改局面：分数与局结束的结算归流程层。
    /// </summary>
    /// <inheritdoc />
    public override void Commit(SkillContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Validate(context))
        {
            throw new InvalidOperationException(
                "SelfDrawnWith7Pairs cannot be committed: the hand is not a seven-pairs hand.");
        }
    }
}
