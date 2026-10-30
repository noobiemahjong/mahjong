namespace Mahjong.Core.Rules;

/// <summary>
/// 碰：用手牌同牌种的两张非鬼牌，加上被响应座位的最后一张弃牌组成碰。
/// 碰不摸牌，声明后立即把 <see cref="GameState.CurrentSeat"/> 设为碰家，由流程层强制其出牌。
/// </summary>
public sealed class PongSkill : Skill
{
    /// <summary>创建碰技能。</summary>
    /// <param name="joker">当前局鬼牌牌种；<c>default(TileType)</c> 表示本局无鬼。鬼牌不可作为碰材料。</param>
    public PongSkill(TileType joker = default)
        : base(joker)
    {
    }

    /// <inheritdoc />
    public override SkillId Id => SkillId.Pong;

    /// <summary>
    /// 前置条件：目标牌非 null、不是鬼牌、是被响应座位的最后一张弃牌，目标座位 0–3 且不等于碰家；
    /// 碰家手牌中同牌种非鬼牌至少 2 张。
    /// </summary>
    /// <inheritdoc />
    public override bool Validate(SkillContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryResolveTarget(context, out _))
        {
            return false;
        }

        return CountHandTilesOfType(context.Source, context.TargetCard!.Value.Type.Id) >= 2;
    }

    /// <summary>
    /// 先完整复校验，再取手牌前两个匹配槽位与被响应座位的最后弃牌槽位声明碰，最后把当前座位交给碰家。
    /// 失败抛 <see cref="InvalidOperationException"/> 且局面不变。
    /// </summary>
    /// <inheritdoc />
    public override void Commit(SkillContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryResolveTarget(context, out int discardSlot))
        {
            throw new InvalidOperationException(
                "Pong cannot be committed: the target discard or target seat does not satisfy the pong preconditions.");
        }

        PlayerState source = context.Source;
        Tile target = context.TargetCard!.Value;
        int[]? handSlots = FindHandSlots(source, target.Type.Id, 2);
        if (handSlots is null)
        {
            throw new InvalidOperationException(
                "Pong cannot be committed: fewer than two matching non-joker hand tiles remain.");
        }

        context.State.DeclareMeld(source.Seat, MeldKind.Pong, handSlots, context.TargetSeat, discardSlot);
        context.State.CurrentSeat = source.Seat;
    }

    /// <summary>校验目标牌是被响应座位的最后一张弃牌，并返回该弃牌槽位。</summary>
    private bool TryResolveTarget(SkillContext context, out int discardSlot)
    {
        discardSlot = -1;

        if (context.TargetCard is not Tile target || target.Id == Tile.UnknownId)
        {
            return false;
        }

        if (IsJoker(target))
        {
            return false;
        }

        if (context.TargetSeat >= GameState.PlayerCount || context.TargetSeat == context.Source.Seat)
        {
            return false;
        }

        TileZone discards = context.State.Players[context.TargetSeat].Discards;
        if (discards.Count == 0)
        {
            return false;
        }

        int last = discards.Count - 1;
        if (!discards[last].Equals(target))
        {
            return false;
        }

        discardSlot = last;
        return true;
    }
}
