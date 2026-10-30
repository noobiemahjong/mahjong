namespace Mahjong.Core.Rules;

/// <summary>
/// 杠：按上下文自行判定明杠、补杠、暗杠三种形态。三种形态都必须从牌墙末尾补 1 张，
/// 声明后把 <see cref="GameState.CurrentSeat"/> 设为杠家；牌墙空时不可声明（流局判定归流程层）。
/// 判定顺序固定为 明杠（有目标弃牌）→ 补杠（已有同牌种碰）→ 暗杠（手牌四张），保证同输入同结果。
/// </summary>
public sealed class KongSkill : Skill
{
    /// <summary>创建杠技能。</summary>
    /// <param name="joker">当前局鬼牌牌种；<c>default(TileType)</c> 表示本局无鬼。鬼牌不可作为杠材料。</param>
    public KongSkill(TileType joker = default)
        : base(joker)
    {
    }

    /// <inheritdoc />
    public override SkillId Id => SkillId.Kong;

    /// <summary>
    /// 前置条件：牌墙非空；有目标弃牌时走明杠，否则按手牌判补杠或暗杠。
    /// 目标牌与目标座位的校验口径与碰一致。
    /// </summary>
    /// <inheritdoc />
    public override bool Validate(SkillContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return TryResolve(context, out _);
    }

    /// <summary>
    /// 先完整复校验，再声明对应形态的杠、从牌墙末尾补 1 张、把当前座位交给杠家。
    /// 失败抛 <see cref="InvalidOperationException"/> 且局面不变。
    /// </summary>
    /// <inheritdoc />
    public override void Commit(SkillContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryResolve(context, out KongPlan plan))
        {
            throw new InvalidOperationException(
                "Kong cannot be committed: the context does not satisfy any kong form preconditions.");
        }

        byte seat = context.Source.Seat;
        context.State.DeclareMeld(seat, plan.Kind, plan.HandSlots, plan.TargetSeat, plan.TargetDiscardSlot);
        context.State.DrawFromTail(seat);
        context.State.CurrentSeat = seat;
    }

    /// <summary>按固定顺序判定杠的形态并给出执行计划；墙空返回 false。</summary>
    private bool TryResolve(SkillContext context, out KongPlan plan)
    {
        plan = default;

        if (context.State.IsWallEmpty)
        {
            return false;
        }

        return context.TargetCard is not null
            ? TryResolveExposed(context, out plan)
            : TryResolveFromHand(context, out plan);
    }

    /// <summary>明杠：3 张手牌 + 被响应座位的最后一张弃牌。</summary>
    private bool TryResolveExposed(SkillContext context, out KongPlan plan)
    {
        plan = default;

        if (context.TargetCard is not Tile target || target.Id == Tile.UnknownId || IsJoker(target))
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

        int[]? handSlots = FindHandSlots(context.Source, target.Type.Id, 3);
        if (handSlots is null)
        {
            return false;
        }

        plan = new KongPlan(MeldKind.KongExposed, handSlots, context.TargetSeat, last);
        return true;
    }

    /// <summary>无目标弃牌时：先找补杠（已有碰 + 手牌第 4 张），再找暗杠（手牌四张）。</summary>
    private bool TryResolveFromHand(SkillContext context, out KongPlan plan)
    {
        plan = default;
        PlayerState source = context.Source;

        // 补杠优先：DrawnTile 的牌种是明确提示，先试它；否则按手牌槽位升序扫描。
        if (context.DrawnTile is Tile drawn
            && drawn.Id != Tile.UnknownId
            && !IsJoker(drawn)
            && TryCreateSupplementPlan(source, drawn.Type.Id, out plan))
        {
            return true;
        }

        IReadOnlyList<Tile> hand = source.Hand.Items;
        for (int slot = 0; slot < hand.Count; slot++)
        {
            Tile tile = hand[slot];
            if (IsJoker(tile))
            {
                continue;
            }

            if (TryCreateSupplementPlan(source, tile.Type.Id, out plan))
            {
                return true;
            }
        }

        // 暗杠：手牌同牌种四张，且没有同牌种的碰（有碰只能补杠）。
        for (int slot = 0; slot < hand.Count; slot++)
        {
            Tile tile = hand[slot];
            if (IsJoker(tile))
            {
                continue;
            }

            ushort typeId = tile.Type.Id;
            if (FindPong(source, typeId) is not null)
            {
                continue;
            }

            int[]? handSlots = FindHandSlots(source, typeId, 4);
            if (handSlots is null)
            {
                continue;
            }

            plan = new KongPlan(MeldKind.KongConcealed, handSlots, Meld.NoTargetSeat, null);
            return true;
        }

        return false;
    }

    /// <summary>为指定牌种构造补杠计划：必须已有同牌种的碰，且手牌至少一张该牌种的非鬼牌。</summary>
    private bool TryCreateSupplementPlan(PlayerState source, ushort typeId, out KongPlan plan)
    {
        plan = default;

        if (FindPong(source, typeId) is null)
        {
            return false;
        }

        int[]? handSlots = FindHandSlots(source, typeId, 1);
        if (handSlots is null)
        {
            return false;
        }

        plan = new KongPlan(MeldKind.KongSupplement, handSlots, Meld.NoTargetSeat, null);
        return true;
    }

    /// <summary>一次杠的执行计划：形态、手牌槽位与目标弃牌位置。</summary>
    private readonly struct KongPlan
    {
        public KongPlan(MeldKind kind, int[] handSlots, byte targetSeat, int? targetDiscardSlot)
        {
            Kind = kind;
            HandSlots = handSlots;
            TargetSeat = targetSeat;
            TargetDiscardSlot = targetDiscardSlot;
        }

        public MeldKind Kind { get; }

        public int[] HandSlots { get; }

        public byte TargetSeat { get; }

        public int? TargetDiscardSlot { get; }
    }
}
