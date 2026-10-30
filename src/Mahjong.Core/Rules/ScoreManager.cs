using System.Collections.ObjectModel;

namespace Mahjong.Core.Rules;

/// <summary>
/// 计分账本：分「本局暂存」与「累计总分」两层。
/// 单次计分动作先完成全部校验，再一次性写暂存，失败不改变任何状态；局终用 <see cref="Release"/>
/// 把暂存并入总分，流局先 <see cref="RollbackRound"/> 再 <see cref="Release"/>，净变化为 0。
///
/// 计分口径是 M0.2 子集：自摸（无鬼 4 / 有鬼 2，七对 ×2，杠上开花 ×2）、明杠、暗杠、补杠；
/// 不含连庄奖励、承包、跟庄、中马。单次自摸四座 delta 之和为 0，单次杠四座 delta 之和为 0。
///
/// 金额上界：单次自摸单座最多 +24（七对与杠上开花互斥，各自只能再乘一次 ×2），单次杠单座最多 +6；
/// 每局单座净额是这些动作的累加，8 局累计仍远小于 short 的 ±32767，故用 short 记账且不做溢出保护。
///
/// 实例非线程安全；座位固定 4 个。
/// </summary>
public sealed class ScoreManager
{
    /// <summary>座位数，固定 4。</summary>
    private const int SeatCount = 4;

    /// <summary>已定义的番种/杠位位集；超出该掩码的位是非法输入。</summary>
    private const ScoreActionType AllActionTypes =
        ScoreActionType.SelfDrawn
        | ScoreActionType.SevenPairs
        | ScoreActionType.ConcealedKong
        | ScoreActionType.ExposedKong
        | ScoreActionType.KongSupplement
        | ScoreActionType.KongUpFlower
        | ScoreActionType.NoJoker;

    /// <summary>三种杠位的并集，用于「七对须 0 副露」与「杠上开花须有杠」的判定。</summary>
    private const ScoreActionType KongActions =
        ScoreActionType.ConcealedKong | ScoreActionType.ExposedKong | ScoreActionType.KongSupplement;

    private readonly short[] _roundDelta = new short[SeatCount];
    private readonly short[] _totalScores = new short[SeatCount];
    private readonly ReadOnlyCollection<short> _roundDeltaView;
    private readonly ReadOnlyCollection<short> _totalScoresView;

    private ScoreActionType _roundActionType;

    /// <summary>创建账本：本局暂存与累计总分全 0，番种聚合为 <see cref="ScoreActionType.None"/>。</summary>
    public ScoreManager()
    {
        _roundDeltaView = Array.AsReadOnly(_roundDelta);
        _totalScoresView = Array.AsReadOnly(_totalScores);
    }

    /// <summary>
    /// 本局暂存的 4 座金额，下标即座位号。返回长度恒为 4 的实时只读视图：
    /// 后续 <see cref="Apply"/> 等调用对其可见，但调用方不能通过该视图修改内部状态。
    /// </summary>
    public IReadOnlyList<short> RoundDelta => _roundDeltaView;

    /// <summary>
    /// 累计总分的 4 座金额，下标即座位号。返回长度恒为 4 的实时只读视图：
    /// 只有 <see cref="Release"/> 会改变它，调用方不能通过该视图修改内部状态。
    /// </summary>
    public IReadOnlyList<short> TotalScores => _totalScoresView;

    /// <summary>
    /// 本局已发生的番种与杠位聚合，供自摸/杠的组合校验与 <see cref="RoundResult.ActionType"/> 取样。
    /// <see cref="Release"/> 与 <see cref="RollbackRound"/> 后为 <see cref="ScoreActionType.None"/>。
    /// </summary>
    public ScoreActionType RoundActionType => _roundActionType;

    /// <summary>
    /// 记一笔金额与对应番种/杠位到本局暂存：<c>RoundDelta[seat] += delta</c>、<c>RoundActionType |= type</c>。
    /// 只做记账，不校验金额符号与业务组合；自摸/杠的组合校验由 <see cref="ApplySelfDrawn"/> 与
    /// <see cref="ApplyKong"/> 负责。<paramref name="type"/> 允许 <see cref="ScoreActionType.None"/>（纯金额调整）。
    /// </summary>
    /// <param name="seat">座位 0–3。</param>
    /// <param name="delta">本笔金额，可为负。</param>
    /// <param name="type">同时记入本局的番种/杠位。</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="seat"/> 不在 0–3，或 <paramref name="type"/> 含未定义位；失败时不改变任何状态。
    /// </exception>
    public void Apply(byte seat, short delta, ScoreActionType type)
    {
        if (seat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seat),
                seat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        // (value | defined) == defined 等价于 value 只使用已定义位，且不受 ushort 补码常量转换限制。
        if (((ushort)type | (ushort)AllActionTypes) != (ushort)AllActionTypes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Score action type must only contain defined flags.");
        }

        _roundDelta[seat] = (short)(_roundDelta[seat] + delta);
        _roundActionType |= type;
    }

    /// <summary>
    /// 自摸结算：每家付一份，赢家收三份。每份金额为
    /// <c>(hasJoker ? 2 : 4) * (sevenPairs ? 2 : 1) * (kongUpFlower ? 2 : 1)</c>，
    /// 即无鬼 4 / 有鬼 2，七对与杠上开花各自再 ×2。
    /// 同时置位：<see cref="ScoreActionType.SelfDrawn"/> 恒置；无鬼置 <see cref="ScoreActionType.NoJoker"/>；
    /// 七对置 <see cref="ScoreActionType.SevenPairs"/>；杠上开花置 <see cref="ScoreActionType.KongUpFlower"/>。
    ///
    /// 不可组合的三种情况（违反时抛异常且不改状态）：七对与杠上开花同局（七对要求 0 副露，杠上开花要求有杠）；
    /// 七对与本局已发生的任一杠位（七对须 0 副露）；杠上开花而本局尚无杠位。
    /// </summary>
    /// <param name="winnerSeat">自摸赢家座位 0–3。</param>
    /// <param name="hasJoker">本局有鬼牌且赢家手牌含该牌种；<c>false</c> 表示无鬼自摸。</param>
    /// <param name="sevenPairs">本手牌按七对胡。</param>
    /// <param name="kongUpFlower">本手牌是杠后补牌自摸（杠上开花）。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="winnerSeat"/> 不在 0–3；失败时不改状态。</exception>
    /// <exception cref="InvalidOperationException">
    /// 七对与杠上开花同时为真；七对与本局已发生杠位组合；杠上开花而本局无杠位。失败时不改状态。
    /// </exception>
    public void ApplySelfDrawn(byte winnerSeat, bool hasJoker, bool sevenPairs, bool kongUpFlower)
    {
        if (winnerSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(winnerSeat),
                winnerSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        if (sevenPairs && kongUpFlower)
        {
            throw new InvalidOperationException(
                "Seven pairs and kong-up-flower cannot be scored together: seven pairs requires no melds, "
                + "while kong-up-flower requires a kong.");
        }

        if (sevenPairs && (_roundActionType & KongActions) != 0)
        {
            throw new InvalidOperationException(
                "Seven pairs cannot be scored after a kong: seven pairs requires no melds.");
        }

        if (kongUpFlower && (_roundActionType & KongActions) == 0)
        {
            throw new InvalidOperationException(
                "Kong-up-flower requires a kong to have been scored in this round.");
        }

        int per = (hasJoker ? 2 : 4) * (sevenPairs ? 2 : 1) * (kongUpFlower ? 2 : 1);

        // 校验全部完成后再一次性写暂存，保证失败路径不改状态。
        for (byte seat = 0; seat < SeatCount; seat++)
        {
            _roundDelta[seat] = seat == winnerSeat
                ? (short)(_roundDelta[seat] + (per * 3))
                : (short)(_roundDelta[seat] - per);
        }

        _roundActionType |= ScoreActionType.SelfDrawn;
        if (!hasJoker)
        {
            _roundActionType |= ScoreActionType.NoJoker;
        }

        if (sevenPairs)
        {
            _roundActionType |= ScoreActionType.SevenPairs;
        }

        if (kongUpFlower)
        {
            _roundActionType |= ScoreActionType.KongUpFlower;
        }
    }

    /// <summary>
    /// 杠结算：明杠由放杠者付 3（杠家 +3、放杠者 -3）；暗杠其余三家各付 2（杠家 +6）；
    /// 补杠其余三家各付 1（杠家 +3）。同时置位对应的杠位标记。
    /// 明杠的放杠者必须是被取牌的那一家；暗杠与补杠没有目标座位，必须传 <see cref="Meld.NoTargetSeat"/>。
    /// </summary>
    /// <param name="seat">杠家座位 0–3。</param>
    /// <param name="kind">杠的种类：<see cref="MeldKind.KongExposed"/>、<see cref="MeldKind.KongConcealed"/> 或 <see cref="MeldKind.KongSupplement"/>。</param>
    /// <param name="sourceSeat">明杠的放杠者座位 0–3；暗杠与补杠必须为 <see cref="Meld.NoTargetSeat"/>。</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="seat"/> 不在 0–3，或 <paramref name="kind"/> 不是三种杠之一（含 <see cref="MeldKind.Pong"/> 与未定义值）；失败时不改状态。
    /// </exception>
    /// <exception cref="ArgumentException">
    /// 明杠的 <paramref name="sourceSeat"/> 不在 0–3 或等于杠家；暗杠/补杠的 <paramref name="sourceSeat"/> 不是
    /// <see cref="Meld.NoTargetSeat"/>。失败时不改状态。
    /// </exception>
    public void ApplyKong(byte seat, MeldKind kind, byte sourceSeat = Meld.NoTargetSeat)
    {
        if (seat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seat),
                seat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        switch (kind)
        {
            case MeldKind.KongExposed:
                if (sourceSeat >= SeatCount || sourceSeat == seat)
                {
                    throw new ArgumentException(
                        "An exposed kong requires a source seat in 0-3 that differs from the kong seat.",
                        nameof(sourceSeat));
                }

                _roundDelta[seat] = (short)(_roundDelta[seat] + 3);
                _roundDelta[sourceSeat] = (short)(_roundDelta[sourceSeat] - 3);
                _roundActionType |= ScoreActionType.ExposedKong;
                return;

            case MeldKind.KongConcealed:
                RequireNoTargetSeat(sourceSeat, kind);
                for (byte other = 0; other < SeatCount; other++)
                {
                    _roundDelta[other] = other == seat
                        ? (short)(_roundDelta[other] + 6)
                        : (short)(_roundDelta[other] - 2);
                }

                _roundActionType |= ScoreActionType.ConcealedKong;
                return;

            case MeldKind.KongSupplement:
                RequireNoTargetSeat(sourceSeat, kind);
                for (byte other = 0; other < SeatCount; other++)
                {
                    _roundDelta[other] = other == seat
                        ? (short)(_roundDelta[other] + 3)
                        : (short)(_roundDelta[other] - 1);
                }

                _roundActionType |= ScoreActionType.KongSupplement;
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "Kong kind must be KongExposed, KongConcealed or KongSupplement.");
        }
    }

    /// <summary>撤回本局全部暂存：<see cref="RoundDelta"/> 归 0、<see cref="RoundActionType"/> 归 None；累计总分不变。幂等。</summary>
    public void RollbackRound()
    {
        Array.Clear(_roundDelta);
        _roundActionType = ScoreActionType.None;
    }

    /// <summary>
    /// 把本局暂存并入累计总分并清零暂存与番种聚合：<c>TotalScores[i] += RoundDelta[i]</c>，随后
    /// <see cref="RoundDelta"/> 归 0、<see cref="RoundActionType"/> 归 None。幂等（第二次加 0）。
    /// 流局口径为先 <see cref="RollbackRound"/> 再调用本方法，净 0、总分不变。
    /// </summary>
    public void Release()
    {
        for (byte seat = 0; seat < SeatCount; seat++)
        {
            _totalScores[seat] = (short)(_totalScores[seat] + _roundDelta[seat]);
            _roundDelta[seat] = 0;
        }

        _roundActionType = ScoreActionType.None;
    }

    /// <summary>校验暗杠/补杠没有目标座位；失败抛 <see cref="ArgumentException"/> 且调用方尚未写状态。</summary>
    private static void RequireNoTargetSeat(byte sourceSeat, MeldKind kind)
    {
        if (sourceSeat != Meld.NoTargetSeat)
        {
            throw new ArgumentException(
                $"A {kind} kong must use {Meld.NoTargetSeat} as source seat.",
                nameof(sourceSeat));
        }
    }
}
