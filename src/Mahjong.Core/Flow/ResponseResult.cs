using Mahjong.Core.Rules;

namespace Mahjong.Core.Flow;

/// <summary>
/// 响应窗口结算快照：谁赢了、赢的是什么、各请求的 <see cref="RequestOutcome"/>。
/// 由 <see cref="ResponseWindow.Close"/> 一次性产出，实例只读；无胜者时 <see cref="WinnerSeat"/> 为 <see cref="NoWinnerSeat"/>。
/// </summary>
public sealed class ResponseResult
{
    /// <summary>无胜者时的座位值。</summary>
    public const byte NoWinnerSeat = 255;

    internal ResponseResult(
        Tile targetCard,
        byte winnerSeat,
        SkillId? winnerSkill,
        IReadOnlyList<PendingRequest> requests,
        IReadOnlyList<RequestOutcome> outcomes)
    {
        TargetCard = targetCard;
        WinnerSeat = winnerSeat;
        WinnerSkill = winnerSkill;
        Requests = requests;
        Outcomes = outcomes;
    }

    /// <summary>是否有胜者；弃牌无人响应或只有过时为 false。</summary>
    public bool HasWinner => WinnerSeat != NoWinnerSeat;

    /// <summary>胜者座位 0–3；无胜者为 <see cref="NoWinnerSeat"/>。</summary>
    public byte WinnerSeat { get; }

    /// <summary>胜出的技能；无胜者为 null。</summary>
    public SkillId? WinnerSkill { get; }

    /// <summary>被响应的弃牌。</summary>
    public Tile TargetCard { get; }

    /// <summary>本窗口的请求快照，顺序等于开窗时的候选顺序。</summary>
    public IReadOnlyList<PendingRequest> Requests { get; }

    /// <summary>
    /// 逐请求结算结果，顺序等于 <see cref="Requests"/>：胜者 Answered 且 Payload 为 boxed <see cref="SkillId"/>；
    /// 提交了竞争技能但落败者 Cancelled 且 Payload 为 null；显式过者 Answered 且 Payload 为 null；未应答者 TimedOut 且 Payload 为 null。
    /// </summary>
    public IReadOnlyList<RequestOutcome> Outcomes { get; }
}
