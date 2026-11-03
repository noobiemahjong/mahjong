using System.Collections.ObjectModel;

namespace Mahjong.Core.Rules;

/// <summary>
/// 计分账本：分「本局暂存」与「累计总分」两层。
/// 单次计分动作先完成全部校验，再一次性写暂存，失败不改变任何状态；局终用 <see cref="Release"/>
/// 把暂存并入总分，流局先 <see cref="RollbackRound"/> 再 <see cref="Release"/>，净变化为 0。
///
/// 计分口径：自摸（无鬼 4 / 有鬼 2，七对 ×2，杠上开花 ×2）、明杠、暗杠、补杠、连庄奖励、跟庄、
/// 中马、抢杠、杠上开花承包、12 张落地承包。单次计分动作四座 delta 之和均为 0。
///
/// 金额上界：单次自摸单座最多 +24（七对与杠上开花互斥，各自只能再乘一次 ×2），单次杠单座最多 +6；
/// 按规则表取值时连庄奖励单座最多 ±9（每家最多 3）、跟庄单座最多 ±1、中马单座最多 ±3（每家最多 1）；
/// 三条承包入口按调用方给的基分记账，抢杠单座最多 ±24、杠上开花承包最多 ±48、12 张最多 ±12
/// （12 张要求 4 副露，与七对互斥，基分不含七对的 ×2）；
/// 每局单座净额是这些动作的累加，8 局累计仍远小于 short 的 ±32767，故用 short 记账且不做溢出保护。
///
/// 实例非线程安全；座位固定 4 个。
/// </summary>
public sealed class ScoreManager
{
    /// <summary>座位数，固定 4。</summary>
    private const int SeatCount = 4;

    /// <summary>已定义的番种、杠位与规则位位集；超出该掩码的位是非法输入。</summary>
    private const ScoreActionType AllActionTypes =
        ScoreActionType.SelfDrawn
        | ScoreActionType.SevenPairs
        | ScoreActionType.ConcealedKong
        | ScoreActionType.ExposedKong
        | ScoreActionType.KongSupplement
        | ScoreActionType.KongUpFlower
        | ScoreActionType.NoJoker
        | ScoreActionType.DealerStreak
        | ScoreActionType.FollowingTheBanker
        | ScoreActionType.HorseWinner
        | ScoreActionType.RobbingTheKong
        | ScoreActionType.RobbingTheKongPayForEveryone
        | ScoreActionType.KongUpFlowerPayForEveryone
        | ScoreActionType.TwelveCardsConceded;

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
    /// 七对自摸结算：每家付一份，赢家收三份；每份金额为 <c>(hasJoker ? 2 : 4) * 2</c>。
    /// 同时置位 <see cref="ScoreActionType.SelfDrawn"/>、<see cref="ScoreActionType.SevenPairs"/>，
    /// 无鬼时再置 <see cref="ScoreActionType.NoJoker"/>。
    /// 只约束赢家自己的手牌与副露（七对须 0 副露由调用方的判定与手牌张数保证），不检查本局其他家的杠位：
    /// 其他家先杠不妨碍赢家按七对结算，因此不能复用 <see cref="ApplySelfDrawn"/> 的局内杠位检查。
    /// </summary>
    /// <param name="winnerSeat">七对自摸赢家座位 0–3。</param>
    /// <param name="hasJoker">本局有鬼牌且赢家手牌含该牌种；<c>false</c> 表示无鬼自摸。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="winnerSeat"/> 不在 0–3；失败时不改状态。</exception>
    public void ApplySevenPairsSelfDrawn(byte winnerSeat, bool hasJoker)
    {
        if (winnerSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(winnerSeat),
                winnerSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        int per = (hasJoker ? 2 : 4) * 2;
        for (byte seat = 0; seat < SeatCount; seat++)
        {
            _roundDelta[seat] = seat == winnerSeat
                ? (short)(_roundDelta[seat] + (per * 3))
                : (short)(_roundDelta[seat] - per);
        }

        _roundActionType |= ScoreActionType.SelfDrawn | ScoreActionType.SevenPairs;
        if (!hasJoker)
        {
            _roundActionType |= ScoreActionType.NoJoker;
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

    /// <summary>
    /// 连庄奖励结算：三家各付一份、庄家收三份，即庄家 <c>+3*bonus</c>、其余三家各 <c>-bonus</c>，四座 delta 之和为 0。
    /// 置位 <see cref="ScoreActionType.DealerStreak"/>（一位规则，庄家收的一笔与三家付的三笔同置）。
    /// <paramref name="bonus"/> 为 0 时既不改暂存也不置位：本局无连庄奖励。
    /// 只写本局暂存，<see cref="Release"/> 才并入总分，<see cref="RollbackRound"/> 撤回。
    /// 本方法只做记账，不判定是否庄家胡、不查连庄次数、不设金额上界：金额由调用方按连庄奖励表取值。
    /// </summary>
    /// <param name="bankerSeat">本局庄家座位 0–3。</param>
    /// <param name="bonus">三家各付的连庄奖励，非负；0 表示本局无奖励。</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bankerSeat"/> 不在 0–3，或 <paramref name="bonus"/> 为负；失败时不改变任何状态。
    /// </exception>
    public void ApplyDealerStreak(byte bankerSeat, int bonus)
    {
        if (bankerSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bankerSeat),
                bankerSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        if (bonus < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bonus),
                bonus,
                "Dealer streak bonus must not be negative.");
        }

        if (bonus == 0)
        {
            return;
        }

        // 校验全部完成后再一次性写暂存，保证失败路径不改状态。
        for (byte seat = 0; seat < SeatCount; seat++)
        {
            _roundDelta[seat] = seat == bankerSeat
                ? (short)(_roundDelta[seat] + (bonus * 3))
                : (short)(_roundDelta[seat] - bonus);
        }

        _roundActionType |= ScoreActionType.DealerStreak;
    }

    /// <summary>
    /// 跟庄结算：赢家 <c>+amount</c>、庄家 <c>-amount</c>，四座 delta 之和为 0（两座相同时两笔相抵）。
    /// 置位 <see cref="ScoreActionType.FollowingTheBanker"/>（一位规则，赢家收的一笔与庄家付的一笔同置），每局最多一次由调用方保证。
    /// <paramref name="amount"/> 为 0 时既不改暂存也不置位：变体关闭或本局不付跟庄。
    /// 只写本局暂存，<see cref="Release"/> 才并入总分，<see cref="RollbackRound"/> 撤回。
    /// 本方法只做金额转移，不判定赢家是否庄家、不设金额上界；赢家与庄家不同座由调用方保证，入口不做该检查。
    /// </summary>
    /// <param name="winnerSeat">本局赢家座位 0–3。</param>
    /// <param name="bankerSeat">本局庄家座位 0–3，即付款方。</param>
    /// <param name="amount">跟庄金额，非负；0 表示本局不付。</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="winnerSeat"/> 或 <paramref name="bankerSeat"/> 不在 0–3，或 <paramref name="amount"/> 为负；
    /// 失败时不改变任何状态。
    /// </exception>
    public void ApplyFollowingTheBanker(byte winnerSeat, byte bankerSeat, int amount)
    {
        if (winnerSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(winnerSeat),
                winnerSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        if (bankerSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bankerSeat),
                bankerSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Following-the-banker amount must not be negative.");
        }

        if (amount == 0)
        {
            return;
        }

        _roundDelta[winnerSeat] = (short)(_roundDelta[winnerSeat] + amount);
        _roundDelta[bankerSeat] = (short)(_roundDelta[bankerSeat] - amount);
        _roundActionType |= ScoreActionType.FollowingTheBanker;
    }

    /// <summary>
    /// 中马结算：赢家 <c>+3*horseBonus</c>、其余三家各 <c>-horseBonus</c>，四座 delta 之和为 0。
    /// 置位 <see cref="ScoreActionType.HorseWinner"/>（一位规则：赢家收的一笔与三家各付的一笔同置）。
    /// <paramref name="horseBonus"/> 为 0 时既不改暂存也不置位：变体关闭或本局不翻马。
    /// 只写本局暂存，<see cref="Release"/> 才并入总分，<see cref="RollbackRound"/> 撤回。
    /// 本方法只做记账：马是否命中（<c>horseSeat == winnerSeat</c>）由调用方判定；payer 恒为其余三家，
    /// 与承包者无关，因此不接收 payer 参数，也不设金额上界。
    /// </summary>
    /// <param name="winnerSeat">本局赢家座位 0–3。</param>
    /// <param name="horseBonus">三家各付的中马金额，非负；0 表示本局不付。</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="winnerSeat"/> 不在 0–3，或 <paramref name="horseBonus"/> 为负；失败时不改变任何状态。
    /// </exception>
    public void ApplyHorseBonus(byte winnerSeat, int horseBonus)
    {
        if (winnerSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(winnerSeat),
                winnerSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        if (horseBonus < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(horseBonus),
                horseBonus,
                "Horse bonus must not be negative.");
        }

        if (horseBonus == 0)
        {
            return;
        }

        // 校验全部完成后再一次性写暂存，保证失败路径不改状态。
        for (byte seat = 0; seat < SeatCount; seat++)
        {
            _roundDelta[seat] = seat == winnerSeat
                ? (short)(_roundDelta[seat] + (horseBonus * 3))
                : (short)(_roundDelta[seat] - horseBonus);
        }

        _roundActionType |= ScoreActionType.HorseWinner;
    }

    /// <summary>
    /// 抢杠结算：赢家 <c>+3*basePer</c>、被抢杠者 <c>-3*basePer</c>，其余两座 0，四座 delta 之和为 0。
    /// 置位：赢家笔 <see cref="ScoreActionType.RobbingTheKong"/>，七对抢杠并置 <see cref="ScoreActionType.SevenPairs"/>；
    /// 被抢杠者笔 <see cref="ScoreActionType.RobbingTheKongPayForEveryone"/>；不置 <see cref="ScoreActionType.SelfDrawn"/>
    /// 与 <see cref="ScoreActionType.NoJoker"/>：抢杠胡不算自摸，有无鬼已折进基分。
    /// <paramref name="basePer"/> 为 0 时既不改暂存也不置位。
    /// 只写本局暂存，<see cref="Release"/> 才并入总分，<see cref="RollbackRound"/> 撤回。
    /// 本方法只做记账：不判定抢杠是否成立、不查手牌、不校验两家不同座（同座时两笔相抵为 0，仍按位规则置位），
    /// 也不设金额上界。
    /// </summary>
    /// <param name="winnerSeat">抢杠胡的赢家座位 0–3。</param>
    /// <param name="robbedSeat">被抢杠者座位 0–3，即本笔的 payer。</param>
    /// <param name="basePer">每份基分，非负，由调用方按有无鬼与七对取值；0 表示本笔不计账。</param>
    /// <param name="sevenPairs">本手牌按七对抢杠，置 <see cref="ScoreActionType.SevenPairs"/>。</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="winnerSeat"/> 或 <paramref name="robbedSeat"/> 不在 0–3，或 <paramref name="basePer"/> 为负；
    /// 失败时不改变任何状态。
    /// </exception>
    public void ApplyRobbingKong(byte winnerSeat, byte robbedSeat, int basePer, bool sevenPairs)
    {
        if (winnerSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(winnerSeat),
                winnerSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        if (robbedSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(robbedSeat),
                robbedSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        if (basePer < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(basePer),
                basePer,
                "Robbing-the-kong base score must not be negative.");
        }

        int per = basePer;
        if (per == 0)
        {
            return;
        }

        // 校验全部完成后再一次性写暂存，保证失败路径不改状态。
        int share = per * 3;
        _roundDelta[winnerSeat] = (short)(_roundDelta[winnerSeat] + share);
        _roundDelta[robbedSeat] = (short)(_roundDelta[robbedSeat] - share);
        _roundActionType |= ScoreActionType.RobbingTheKong;
        if (sevenPairs)
        {
            _roundActionType |= ScoreActionType.SevenPairs;
        }

        _roundActionType |= ScoreActionType.RobbingTheKongPayForEveryone;
    }

    /// <summary>
    /// 杠上开花承包结算：赢家 <c>+3*perK</c>、放明杠者 <c>-3*perK</c>，其余两座 0，四座 delta 之和为 0；
    /// <c>perK = 2*basePer</c>（杠分 = 基分 × 2），基分与原自摸的每份金额同口径。
    /// 置位：赢家笔 <see cref="ScoreActionType.SelfDrawn"/> 与 <see cref="ScoreActionType.KongUpFlower"/>，
    /// 无鬼并置 <see cref="ScoreActionType.NoJoker"/>，七对并置 <see cref="ScoreActionType.SevenPairs"/>；
    /// 放明杠者笔 <see cref="ScoreActionType.KongUpFlowerPayForEveryone"/>。
    /// <paramref name="basePer"/> 为 0 时既不改暂存也不置位。
    /// 只写本局暂存，<see cref="Release"/> 才并入总分，<see cref="RollbackRound"/> 撤回。
    /// 本方法只做记账：承包是否成立（最近摸牌来自明杠补牌且该明杠有目标座位）由调用方判定，
    /// 不查本局杠位、不校验两家不同座（同座时两笔相抵为 0，仍按位规则置位），也不设金额上界。
    /// </summary>
    /// <param name="winnerSeat">自摸赢家座位 0–3。</param>
    /// <param name="payerSeat">放明杠者座位 0–3，即本笔的 payer。</param>
    /// <param name="basePer">每份基分，非负；本方法内部乘 2 得杠分。</param>
    /// <param name="hasJoker">本局有鬼牌且赢家手牌含该牌种；<c>false</c> 时置 <see cref="ScoreActionType.NoJoker"/>。</param>
    /// <param name="sevenPairs">本手牌按七对胡；与杠上开花在实战互斥，按入参置位。</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="winnerSeat"/> 或 <paramref name="payerSeat"/> 不在 0–3，或 <paramref name="basePer"/> 为负；
    /// 失败时不改变任何状态。
    /// </exception>
    public void ApplyKongUpFlowerContract(byte winnerSeat, byte payerSeat, int basePer, bool hasJoker, bool sevenPairs)
    {
        if (winnerSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(winnerSeat),
                winnerSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        if (payerSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payerSeat),
                payerSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        if (basePer < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(basePer),
                basePer,
                "Kong-up-flower contract base score must not be negative.");
        }

        int per = basePer * 2;
        if (per == 0)
        {
            return;
        }

        // 校验全部完成后再一次性写暂存，保证失败路径不改状态。
        int share = per * 3;
        _roundDelta[winnerSeat] = (short)(_roundDelta[winnerSeat] + share);
        _roundDelta[payerSeat] = (short)(_roundDelta[payerSeat] - share);
        _roundActionType |= ScoreActionType.SelfDrawn | ScoreActionType.KongUpFlower;
        if (!hasJoker)
        {
            _roundActionType |= ScoreActionType.NoJoker;
        }

        if (sevenPairs)
        {
            _roundActionType |= ScoreActionType.SevenPairs;
        }

        _roundActionType |= ScoreActionType.KongUpFlowerPayForEveryone;
    }

    /// <summary>
    /// 12 张落地承包结算：赢家 <c>+3*basePer</c>、第 4 副露的供牌者 <c>-3*basePer</c>，其余两座 0，四座 delta 之和为 0。
    /// 置位：赢家笔 <see cref="ScoreActionType.SelfDrawn"/> 与 <see cref="ScoreActionType.TwelveCardsConceded"/>，
    /// 无鬼并置 <see cref="ScoreActionType.NoJoker"/>；供牌者笔为同一个 <see cref="ScoreActionType.TwelveCardsConceded"/>
    /// （一位规则，两侧同置）。
    /// <paramref name="basePer"/> 为 0 时既不改暂存也不置位。
    /// 只写本局暂存，<see cref="Release"/> 才并入总分，<see cref="RollbackRound"/> 撤回。
    /// 本方法只做记账：承包是否成立（4 副露全明且第 4 副露有供牌者）由调用方判定，
    /// 不校验两家不同座（同座时两笔相抵为 0，仍按位规则置位），也不设金额上界。
    /// </summary>
    /// <param name="winnerSeat">自摸赢家座位 0–3。</param>
    /// <param name="payerSeat">第 4 副露的供牌者座位 0–3，即本笔的 payer。</param>
    /// <param name="basePer">每份基分，非负（七对与 4 副露互斥，基分不含七对的 ×2）。</param>
    /// <param name="hasJoker">本局有鬼牌且赢家手牌含该牌种；<c>false</c> 时置 <see cref="ScoreActionType.NoJoker"/>。</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="winnerSeat"/> 或 <paramref name="payerSeat"/> 不在 0–3，或 <paramref name="basePer"/> 为负；
    /// 失败时不改变任何状态。
    /// </exception>
    public void ApplyTwelveCardsContract(byte winnerSeat, byte payerSeat, int basePer, bool hasJoker)
    {
        if (winnerSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(winnerSeat),
                winnerSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        if (payerSeat >= SeatCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payerSeat),
                payerSeat,
                $"Seat must be within 0-{SeatCount - 1}.");
        }

        if (basePer < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(basePer),
                basePer,
                "Twelve-cards contract base score must not be negative.");
        }

        int per = basePer;
        if (per == 0)
        {
            return;
        }

        // 校验全部完成后再一次性写暂存，保证失败路径不改状态。
        int share = per * 3;
        _roundDelta[winnerSeat] = (short)(_roundDelta[winnerSeat] + share);
        _roundDelta[payerSeat] = (short)(_roundDelta[payerSeat] - share);
        _roundActionType |= ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded;
        if (!hasJoker)
        {
            _roundActionType |= ScoreActionType.NoJoker;
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
