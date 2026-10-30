namespace Mahjong.Core.Rules;

/// <summary>
/// 技能动作的抽象：一个技能只回答两件事——当前上下文是否可执行（<see cref="Validate"/>），以及执行并推进局面（<see cref="Commit"/>）。
/// 技能不读时钟、不做 IO、不持有静态可变状态；实例只持构造注入的鬼牌与胡牌判定器，可跨局面复用。
/// </summary>
public abstract class Skill
{
    /// <summary>用鬼牌牌种创建技能基类。</summary>
    /// <param name="joker">当前局鬼牌牌种；<c>default(TileType)</c> 表示本局无鬼。</param>
    protected Skill(TileType joker = default)
    {
        Joker = joker;
    }

    /// <summary>技能编号，与协议 <see cref="SkillId"/> 一致。</summary>
    public abstract SkillId Id { get; }

    /// <summary>
    /// 判断上下文是否满足本技能的全部前置条件；只读，不改变 <see cref="SkillContext.State"/>。
    /// </summary>
    /// <remarks>
    /// 技能只校验与自身动作有关的条件（目标牌、牌种张数、墙尾可补牌等）；折算张数（手牌 + 3 × 副露）、
    /// 轮次与手牌归属等跨动作不变量由调用方（<see cref="ICardAndSkillVerifier"/>）负责。
    /// </remarks>
    /// <param name="context">技能上下文。</param>
    /// <returns>满足前置条件返回 true；不满足返回 false。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> 为 null。</exception>
    public abstract bool Validate(SkillContext context);

    /// <summary>
    /// 执行技能：先完整复校验，再一次性推进局面。
    /// 复校验失败抛 <see cref="InvalidOperationException"/> 且局面不变；执行路径上不存在可失败的中间态。
    /// </summary>
    /// <param name="context">技能上下文；<see cref="SkillContext.State"/> 会被推进。</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">上下文不满足前置条件；失败时局面不变。</exception>
    public abstract void Commit(SkillContext context);

    /// <summary>当前局鬼牌牌种；<c>default(TileType)</c> 表示无鬼。</summary>
    protected TileType Joker { get; }

    /// <summary>鬼牌是否生效：牌种花色落在 1–5 表示有鬼，<c>default(TileType)</c> 的花色 0 表示无鬼。</summary>
    protected bool IsJokerActive =>
        (byte)Joker.Suit >= (byte)SuitType.Character && (byte)Joker.Suit <= (byte)SuitType.Dragon;

    /// <summary>该物理牌是否是当前鬼牌；无鬼时恒为 false。</summary>
    /// <param name="tile">要判断的牌。</param>
    /// <returns>鬼牌返回 true。</returns>
    protected bool IsJoker(Tile tile) => IsJokerActive && tile.Type.Id == Joker.Id;

    /// <summary>统计手牌中指定牌种的非鬼牌张数。</summary>
    /// <param name="player">玩家。</param>
    /// <param name="typeId">牌种 id。</param>
    /// <returns>手牌中该牌种的非鬼牌张数。</returns>
    protected int CountHandTilesOfType(PlayerState player, ushort typeId)
    {
        int count = 0;
        IReadOnlyList<Tile> hand = player.Hand.Items;
        for (int slot = 0; slot < hand.Count; slot++)
        {
            Tile tile = hand[slot];
            if (tile.Type.Id == typeId && !IsJoker(tile))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 按手牌槽位升序找前 <paramref name="count"/> 个指定牌种的非鬼牌槽位。
    /// </summary>
    /// <param name="player">玩家。</param>
    /// <param name="typeId">牌种 id。</param>
    /// <param name="count">需要的张数；必须为正。</param>
    /// <returns>槽位数组，长度等于 <paramref name="count"/>；不足时返回 null。</returns>
    protected int[]? FindHandSlots(PlayerState player, ushort typeId, int count)
    {
        if (count <= 0)
        {
            return Array.Empty<int>();
        }

        int[] slots = new int[count];
        int found = 0;
        IReadOnlyList<Tile> hand = player.Hand.Items;
        for (int slot = 0; slot < hand.Count; slot++)
        {
            Tile tile = hand[slot];
            if (tile.Type.Id != typeId || IsJoker(tile))
            {
                continue;
            }

            slots[found++] = slot;
            if (found == count)
            {
                return slots;
            }
        }

        return null;
    }

    /// <summary>按牌种查找该玩家已有的碰副露。</summary>
    /// <param name="player">玩家。</param>
    /// <param name="typeId">牌种 id。</param>
    /// <returns>第一组同牌种的碰；没有返回 null。</returns>
    protected static Meld? FindPong(PlayerState player, ushort typeId)
    {
        for (int index = 0; index < player.Melds.Count; index++)
        {
            Meld meld = player.Melds[index];
            if (meld.Kind == MeldKind.Pong && meld.Tiles.Count > 0 && meld.Tiles[0].Type.Id == typeId)
            {
                return meld;
            }
        }

        return null;
    }
}
