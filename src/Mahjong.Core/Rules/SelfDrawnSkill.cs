namespace Mahjong.Core.Rules;

/// <summary>
/// 自摸（常规胡）：手牌 + 副露折算张数 14 且成常规胡，同时不是七对（七对由
/// <see cref="SelfDrawnWith7PairsSkill"/> 独占，防止同一手牌被两种技能双算）。
/// </summary>
public sealed class SelfDrawnSkill : Skill
{
    private readonly IWinChecker _wins;

    /// <summary>创建自摸技能。</summary>
    /// <param name="wins">胡牌判定器；判定期只读。</param>
    /// <param name="joker">当前局鬼牌牌种；<c>default(TileType)</c> 表示本局无鬼。</param>
    /// <exception cref="ArgumentNullException"><paramref name="wins"/> 为 null。</exception>
    public SelfDrawnSkill(IWinChecker wins, TileType joker = default)
        : base(joker)
    {
        ArgumentNullException.ThrowIfNull(wins);
        _wins = wins;
    }

    /// <inheritdoc />
    public override SkillId Id => SkillId.SelfDrawn;

    /// <summary>
    /// 前置条件：<see cref="IWinChecker.IsWin"/> 为 true 且 <see cref="IWinChecker.IsSevenPairs"/> 为 false。
    /// 张数非法时由判定器直接返回 false，本方法不改局面。
    /// </summary>
    /// <inheritdoc />
    public override bool Validate(SkillContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PlayerState source = context.Source;
        return _wins.IsWin(source.Hand.Items, source.Melds, Joker)
            && !_wins.IsSevenPairs(source.Hand.Items, Joker);
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
                "SelfDrawn cannot be committed: the hand is not a regular winning hand or is a seven-pairs hand.");
        }
    }
}
