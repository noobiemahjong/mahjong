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

        // 补杠优先，且与 internal 预判入口共用同一段判定（摸牌优先 → 手牌槽位升序）。
        if (TryResolveSupplementPlan(source, context.DrawnTile, out plan))
        {
            return true;
        }

        IReadOnlyList<Tile> hand = source.Hand.Items;

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

    /// <summary>
    /// 补杠判定的唯一实现，供 <see cref="TryResolveFromHand"/> 与 internal 预判入口共用：
    /// <paramref name="drawnTile"/> 是明确提示（有值、非未知牌、非鬼、其牌种已有同牌种的碰、手牌有该牌种）时先用它；
    /// 否则按手牌槽位升序取第一张「已有同牌种碰 + 非鬼」的牌；两者都无返回 false。
    /// </summary>
    private bool TryResolveSupplementPlan(PlayerState source, Tile? drawnTile, out KongPlan plan)
    {
        plan = default;

        if (drawnTile is Tile drawn
            && drawn.Id != Tile.UnknownId
            && !IsJoker(drawn)
            && TryCreateSupplementPlan(source, drawn.Type.Id, out plan))
        {
            return true;
        }

        IReadOnlyList<Tile> hand = source.Hand.Items;
        for (int slot = 0; slot < hand.Count; slot++)
        {
            if (IsJoker(hand[slot]))
            {
                continue;
            }

            if (TryCreateSupplementPlan(source, hand[slot].Type.Id, out plan))
            {
                return true;
            }
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

    /// <summary>
    /// 补杠预判的只读入口。开窗预判与 <see cref="Commit"/> 共用同一补杠判定方法（摸牌优先 → 手牌升序），
    /// 因此预判给出的牌就是结算时实际会被加进碰的那一张；只读，不改变局面。两个来源的行为：
    /// ① 摸到的第 4 张：<paramref name="drawnTile"/> 有值、非未知牌、非鬼，且其牌种已有同牌种的碰、手牌有该牌种，
    /// 取该手牌槽位；② 手牌持有的第 4 张：<paramref name="drawnTile"/> 为 null、未知牌、鬼牌或不匹配时，
    /// 按手牌槽位升序取第一张「已有同牌种碰 + 非鬼」的牌；③ 两者都无（无同牌种碰或无第 4 张）返回 false。
    /// </summary>
    /// <param name="source">补杠者。</param>
    /// <param name="drawnTile">刚摸到的牌，作为优先提示；null 表示只按手牌扫描。</param>
    /// <param name="plan">成功时为只读计划；失败时为 default。</param>
    /// <returns>可补杠返回 true。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> 为 null。</exception>
    internal bool TryCreateSupplementPlan(PlayerState source, Tile? drawnTile, out KongSupplementPlan plan)
    {
        ArgumentNullException.ThrowIfNull(source);
        plan = default;

        if (!TryResolveSupplementPlan(source, drawnTile, out KongPlan core))
        {
            return false;
        }

        plan = new KongSupplementPlan(core.Kind, core.HandSlots, core.TargetSeat, source.Hand[core.HandSlots[0]]);
        return true;
    }

    /// <summary>
    /// 补杠预判的只读计划：形态恒为 <see cref="MeldKind.KongSupplement"/>，目标座位恒为 <see cref="Meld.NoTargetSeat"/>。
    /// <see cref="HandSlots"/> 是第 4 张牌在手牌域的槽位（补杠恰一张），<see cref="Tile"/> 是同一张牌的物理牌；
    /// 两者同源，结算时按槽位移牌即可保持物理 id 不变。
    /// </summary>
    internal readonly struct KongSupplementPlan
    {
        internal KongSupplementPlan(MeldKind kind, int[] handSlots, byte targetSeat, Tile tile)
        {
            Kind = kind;
            HandSlots = Array.AsReadOnly(handSlots);
            TargetSeat = targetSeat;
            Tile = tile;
        }

        /// <summary>副露形态，恒为 <see cref="MeldKind.KongSupplement"/>。</summary>
        public MeldKind Kind { get; }

        /// <summary>第 4 张牌的手牌槽位，长度恒为 1；只读视图，写入抛 <see cref="NotSupportedException"/>。</summary>
        public IReadOnlyList<int> HandSlots { get; }

        /// <summary>目标座位，恒为 <see cref="Meld.NoTargetSeat"/>（补杠不从其他座位取牌）。</summary>
        public byte TargetSeat { get; }

        /// <summary>被升级为杠的第 4 张牌（物理牌 id 1–136）。</summary>
        public Tile Tile { get; }
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
