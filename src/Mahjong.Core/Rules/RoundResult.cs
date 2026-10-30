using System.Collections.ObjectModel;

namespace Mahjong.Core.Rules;

/// <summary>
/// 单局结果：局号、本局庄家、赢家、番种聚合与本局/总分快照。
/// 本类型是 T-5 <c>RoundEnded</c> 与 <c>GameEnded</c> 事件的 payload 来源；T-3 不定义事件类型、不做事件编码。
/// 实例不可变；两个分数列表是构造时复制的只读快照，不随 <see cref="ScoreManager"/> 的后续变化。
/// </summary>
public sealed class RoundResult
{
    /// <summary>流局时占位的赢家座位值。</summary>
    public const byte NoWinnerSeat = 255;

    private readonly ReadOnlyCollection<short> _scoreDelta;
    private readonly ReadOnlyCollection<short> _totalScores;

    /// <summary>创建单局结果；两个分数序列会被复制，之后修改调用方集合不影响本对象。</summary>
    /// <param name="roundNo">本局局号 1–8。</param>
    /// <param name="bankerSeat">本局坐庄的座位 0–3（轮换前的庄家）。</param>
    /// <param name="winnerSeat">赢家座位 0–3；流局为 <see cref="NoWinnerSeat"/>。</param>
    /// <param name="actionType">本局计分动作聚合；流局为 <see cref="ScoreActionType.None"/>。</param>
    /// <param name="scoreDelta">4 个座位的本局净变化，下标即座位号；流局全 0。</param>
    /// <param name="totalScores">4 个座位在本局结算后的累计总分，下标即座位号。</param>
    /// <exception cref="ArgumentNullException"><paramref name="scoreDelta"/> 或 <paramref name="totalScores"/> 为 null。</exception>
    /// <exception cref="ArgumentException">任一序列长度不是 4。</exception>
    internal RoundResult(
        byte roundNo,
        byte bankerSeat,
        byte winnerSeat,
        ScoreActionType actionType,
        IReadOnlyList<short> scoreDelta,
        IReadOnlyList<short> totalScores)
    {
        ArgumentNullException.ThrowIfNull(scoreDelta);
        ArgumentNullException.ThrowIfNull(totalScores);
        if (scoreDelta.Count != GameState.PlayerCount)
        {
            throw new ArgumentException(
                $"Score delta must contain {GameState.PlayerCount} seats.",
                nameof(scoreDelta));
        }

        if (totalScores.Count != GameState.PlayerCount)
        {
            throw new ArgumentException(
                $"Total scores must contain {GameState.PlayerCount} seats.",
                nameof(totalScores));
        }

        RoundNo = roundNo;
        BankerSeat = bankerSeat;
        WinnerSeat = winnerSeat;
        ActionType = actionType;

        short[] delta = new short[GameState.PlayerCount];
        short[] totals = new short[GameState.PlayerCount];
        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            delta[seat] = scoreDelta[seat];
            totals[seat] = totalScores[seat];
        }

        _scoreDelta = Array.AsReadOnly(delta);
        _totalScores = Array.AsReadOnly(totals);
    }

    /// <summary>本局局号 1–8。</summary>
    public byte RoundNo { get; }

    /// <summary>本局坐庄的座位 0–3（轮换前的庄家）。</summary>
    public byte BankerSeat { get; }

    /// <summary>赢家座位 0–3；流局为 <see cref="NoWinnerSeat"/>。</summary>
    public byte WinnerSeat { get; }

    /// <summary>是否流局：<see cref="WinnerSeat"/> 为 <see cref="NoWinnerSeat"/>。</summary>
    public bool IsDraw => WinnerSeat == NoWinnerSeat;

    /// <summary>本局计分动作聚合（自摸/七对/杠位/无鬼等）；流局为 <see cref="ScoreActionType.None"/>。</summary>
    public ScoreActionType ActionType { get; }

    /// <summary>
    /// 4 个座位的本局净变化，下标即座位号；流局全 0。
    /// 这是 <see cref="ScoreManager.Release"/> 之前取样的拷贝，不随账本清零而变化。
    /// </summary>
    public IReadOnlyList<short> ScoreDelta => _scoreDelta;

    /// <summary>4 个座位在本局结算后的累计总分，下标即座位号；是构造时的拷贝，不随后续对局变化。</summary>
    public IReadOnlyList<short> TotalScores => _totalScores;
}
