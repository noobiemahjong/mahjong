namespace Mahjong.Core.Rules;

/// <summary>
/// 一次技能动作的只读上下文：局面、发起座位、被响应的目标弃牌与刚摸到的牌。
/// 构造即校验，实例创建后不可变；<see cref="Skill.Validate"/> 只读它，<see cref="Skill.Commit"/> 只改 <see cref="State"/>。
/// </summary>
public sealed class SkillContext
{
    /// <summary>创建技能上下文。</summary>
    /// <param name="state">权威局面；不得为 null。</param>
    /// <param name="source">发起技能的玩家；必须是 <paramref name="state"/> 中同座位的同一引用。</param>
    /// <param name="targetCard">被响应的弃牌；无目标时为 null。</param>
    /// <param name="drawnTile">自摸或补杠刚摸到的牌；对碰/暗杠/明杠无意义，可为 null。</param>
    /// <param name="targetSeat">被响应的弃牌座位 0–3；无目标时为 <see cref="Meld.NoTargetSeat"/>。</param>
    /// <param name="requestId">关联请求 id；无请求时为 0。</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> 或 <paramref name="source"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> 不是 <paramref name="state"/> 中同座位玩家的同一引用。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="targetSeat"/> 既不在 0–3 也不等于 <see cref="Meld.NoTargetSeat"/>。</exception>
    public SkillContext(
        GameState state,
        PlayerState source,
        Tile? targetCard = null,
        Tile? drawnTile = null,
        byte targetSeat = Meld.NoTargetSeat,
        long requestId = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(source);

        if (!ReferenceEquals(state.Players[source.Seat], source))
        {
            throw new ArgumentException(
                "The source player must be the same instance as state.Players[source.Seat].",
                nameof(source));
        }

        if (targetSeat >= GameState.PlayerCount && targetSeat != Meld.NoTargetSeat)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetSeat),
                targetSeat,
                $"Target seat must be within 0-{GameState.PlayerCount - 1} or {Meld.NoTargetSeat}.");
        }

        State = state;
        Source = source;
        TargetCard = targetCard;
        DrawnTile = drawnTile;
        TargetSeat = targetSeat;
        RequestId = requestId;
    }

    /// <summary>权威局面；技能执行时就地推进。</summary>
    public GameState State { get; }

    /// <summary>发起技能的玩家。</summary>
    public PlayerState Source { get; }

    /// <summary>被响应的弃牌；碰/明杠必须非 null。</summary>
    public Tile? TargetCard { get; }

    /// <summary>自摸或补杠刚摸到的牌；可为 null。</summary>
    public Tile? DrawnTile { get; }

    /// <summary>被响应的弃牌座位 0–3；无目标时为 <see cref="Meld.NoTargetSeat"/>。</summary>
    public byte TargetSeat { get; }

    /// <summary>关联请求 id；无请求时为 0。</summary>
    public long RequestId { get; }
}
