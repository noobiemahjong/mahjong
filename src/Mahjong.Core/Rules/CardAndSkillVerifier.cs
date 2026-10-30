namespace Mahjong.Core.Rules;

/// <summary>
/// 动作二次校验器：把座位、牌在手中、张数、鬼牌、目标牌与技能前置条件合成一次只读判定。
/// 检查顺序固定：玩家同引用 → 出牌分支 → 未定义技能 → 目标牌解析 → 折算张数 → 技能 <see cref="Skill.Validate"/>。
/// 不改变局面；除 null 参数异常外，非法输入一律返回 false。
/// </summary>
public sealed class CardAndSkillVerifier : ICardAndSkillVerifier
{
    /// <summary>折算张数：手牌张数 + 3 × 副露组数。</summary>
    private const int TilesPerMeld = 3;

    /// <summary>响应弃牌时的折算张数（13）。</summary>
    private const int ResponseHandSize = 13;

    /// <summary>自身回合的折算张数（14）。</summary>
    private const int WinningHandSize = 14;

    private readonly TileType _joker;
    private readonly PongSkill _pong;
    private readonly KongSkill _kong;
    private readonly SelfDrawnSkill _selfDrawn;
    private readonly SelfDrawnWith7PairsSkill _selfDrawnWith7Pairs;

    /// <summary>创建校验器；内部按 <see cref="SkillId"/> 分派到四个技能实例。</summary>
    /// <param name="wins">胡牌判定器；判定期只读。</param>
    /// <param name="joker">当前局鬼牌牌种；<c>default(TileType)</c> 表示本局无鬼。</param>
    /// <exception cref="ArgumentNullException"><paramref name="wins"/> 为 null。</exception>
    public CardAndSkillVerifier(IWinChecker wins, TileType joker = default)
    {
        ArgumentNullException.ThrowIfNull(wins);
        _joker = joker;
        _pong = new PongSkill(joker);
        _kong = new KongSkill(joker);
        _selfDrawn = new SelfDrawnSkill(wins, joker);
        _selfDrawnWith7Pairs = new SelfDrawnWith7PairsSkill(wins, joker);
    }

    /// <inheritdoc />
    public bool Verify(GameState state, PlayerState player, SkillId? skill, Tile? card, Tile? targetCard)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(player);

        if (!ReferenceEquals(state.Players[player.Seat], player))
        {
            return false;
        }

        // 未知 id 的牌不是真实牌，按“没有提供”处理。
        Tile? drawnTile = card is Tile cardValue && cardValue.Id != Tile.UnknownId ? cardValue : null;

        if (skill is null)
        {
            return VerifyDiscard(state, player, drawnTile);
        }

        if (!IsDefinedSkill(skill.Value))
        {
            return false;
        }

        byte targetSeat = Meld.NoTargetSeat;
        if (targetCard is Tile target && !TryResolveTargetSeat(state, target, out targetSeat))
        {
            return false;
        }

        int adjustedHandSize = player.Hand.Count + (TilesPerMeld * player.Melds.Count);
        if (!IsHandSizeValid(skill.Value, adjustedHandSize))
        {
            return false;
        }

        var context = new SkillContext(state, player, targetCard, drawnTile, targetSeat);
        return ResolveSkill(skill.Value).Validate(context);
    }

    /// <summary>出牌分支：牌非空、在手中、不是鬼牌、轮到自己、折算张数 14。</summary>
    private bool VerifyDiscard(GameState state, PlayerState player, Tile? card)
    {
        if (card is not Tile discard)
        {
            return false;
        }

        if (IsJoker(discard))
        {
            return false;
        }

        if (state.CurrentSeat != player.Seat)
        {
            return false;
        }

        if (player.Hand.IndexOf(discard) < 0)
        {
            return false;
        }

        return player.Hand.Count + (TilesPerMeld * player.Melds.Count) == WinningHandSize;
    }

    /// <summary>目标牌必须是某一座位弃牌区的最后一张，且只能命中一处；无命中或多处命中都返回 false。</summary>
    private static bool TryResolveTargetSeat(GameState state, Tile target, out byte targetSeat)
    {
        targetSeat = Meld.NoTargetSeat;
        int matches = 0;
        byte found = Meld.NoTargetSeat;

        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            TileZone discards = state.Players[seat].Discards;
            if (discards.Count == 0)
            {
                continue;
            }

            if (!discards[discards.Count - 1].Equals(target))
            {
                continue;
            }

            matches++;
            found = seat;
        }

        if (matches != 1)
        {
            return false;
        }

        targetSeat = found;
        return true;
    }

    /// <summary>技能是否存在：只有 1–4 是已定义技能。</summary>
    private static bool IsDefinedSkill(SkillId skill) => skill switch
    {
        SkillId.Pong or SkillId.Kong or SkillId.SelfDrawn or SkillId.SelfDrawnWith7Pairs => true,
        _ => false,
    };

    /// <summary>折算张数：碰 13；杠 13 或 14；自摸/七对 14。</summary>
    private static bool IsHandSizeValid(SkillId skill, int adjustedHandSize) => skill switch
    {
        SkillId.Pong => adjustedHandSize == ResponseHandSize,
        SkillId.Kong => adjustedHandSize is ResponseHandSize or WinningHandSize,
        _ => adjustedHandSize == WinningHandSize,
    };

    /// <summary>按技能编号分派到已构造的技能实例。</summary>
    private Skill ResolveSkill(SkillId skill) => skill switch
    {
        SkillId.Pong => _pong,
        SkillId.Kong => _kong,
        SkillId.SelfDrawn => _selfDrawn,
        _ => _selfDrawnWith7Pairs,
    };

    /// <summary>鬼牌是否生效：与 <see cref="Skill"/> 同口径。</summary>
    private bool IsJokerActive =>
        (byte)_joker.Suit >= (byte)SuitType.Character && (byte)_joker.Suit <= (byte)SuitType.Dragon;

    /// <summary>该物理牌是否是当前鬼牌。</summary>
    private bool IsJoker(Tile tile) => IsJokerActive && tile.Type.Id == _joker.Id;
}
