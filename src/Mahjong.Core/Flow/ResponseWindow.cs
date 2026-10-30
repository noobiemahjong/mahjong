using System.Collections.ObjectModel;
using Mahjong.Core.Rules;

namespace Mahjong.Core.Flow;

/// <summary>
/// 弃牌响应窗口：按候选座位各开一个碰/杠请求，收集“碰/杠/过”应答，并在关闭时按固定仲裁键结算。
/// 只做窗口与仲裁，不做游戏合法性判断（调用方先过 <see cref="Rules.ICardAndSkillVerifier"/>）；
/// 不实现 <see cref="IRequestRegistry"/>，超时挂接与注册表由会话层适配。
/// 实例非线程安全，无静态可变状态；同一实例只开一次窗。
/// </summary>
public sealed class ResponseWindow
{
    /// <summary>一个弃牌最多三家可响应。</summary>
    private const int MaxCandidates = 3;

    private readonly long _firstRequestId;
    private readonly List<PlayerState> _candidates = [];
    private PendingRequest[] _requests = [];
    private ReadOnlyCollection<PendingRequest> _requestsView = Array.AsReadOnly(Array.Empty<PendingRequest>());
    private bool[] _answered = [];
    private SkillId?[] _skills = [];
    private long[] _arrival = [];
    private Tile _discard;
    private long _deadlineTick;
    private bool _opened;
    private bool _closed;
    private ResponseResult? _result;

    /// <summary>创建尚未开窗的响应窗口。</summary>
    /// <param name="firstRequestId">本窗口第一个请求的 id；第 i 个候选用 firstRequestId + i，必须是后续会话可区分的值。</param>
    public ResponseWindow(long firstRequestId = 1)
    {
        _firstRequestId = firstRequestId;
    }

    /// <summary>本窗口结算结果；<see cref="Close"/> 之前为 null。</summary>
    public ResponseResult? Result => _result;

    /// <summary>本窗口的请求快照，顺序等于候选顺序；开窗前为空只读列表。</summary>
    public IReadOnlyList<PendingRequest> Requests => _requestsView;

    /// <summary>被响应的弃牌；开窗前为 <c>default(Tile)</c>。</summary>
    public Tile Discard => _discard;

    /// <summary>截止 tick；开窗前为 0。</summary>
    public long DeadlineTick => _deadlineTick;

    /// <summary>窗口已开且未关闭。</summary>
    public bool IsOpen => _opened && !_closed;

    /// <summary>全体候选都已应答（碰/杠/过都算应答）；开窗前为 false，候选为 0 时开窗后为 true。</summary>
    public bool AllAnswered
    {
        get
        {
            if (!_opened)
            {
                return false;
            }

            for (int index = 0; index < _answered.Length; index++)
            {
                if (!_answered[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// 开窗：按候选顺序分配连续 requestId，为每座生成允许碰/杠/过的请求。单次使用。
    /// </summary>
    /// <param name="discard">被响应的弃牌；物理 id 必须已知（非 <see cref="Tile.UnknownId"/>）。</param>
    /// <param name="candidates">可响应的玩家，0–3 个，座位不得重复，元素不得为 null。</param>
    /// <param name="deadlineTick">截止 tick；不得为负。迟到判据为 <c>arrivalSeq &gt; deadlineTick</c>。</param>
    /// <exception cref="InvalidOperationException">已开过窗（含已关闭）。失败时窗口保持原状。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="candidates"/> 或其中元素为 null。</exception>
    /// <exception cref="ArgumentException">候选超过 3 个、座位重复、或弃牌 id 未知。失败时窗口保持未开窗。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deadlineTick"/> 为负。</exception>
    public void Open(Tile discard, IReadOnlyList<PlayerState> candidates, long deadlineTick)
    {
        if (_opened)
        {
            throw new InvalidOperationException("A response window can be opened only once.");
        }

        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count > MaxCandidates)
        {
            throw new ArgumentException(
                $"At most {MaxCandidates} candidates can respond to one discard.",
                nameof(candidates));
        }

        bool[] seats = new bool[GameState.PlayerCount];
        for (int index = 0; index < candidates.Count; index++)
        {
            PlayerState? candidate = candidates[index];
            if (candidate is null)
            {
                throw new ArgumentNullException(nameof(candidates), "Candidate players must not be null.");
            }

            if (seats[candidate.Seat])
            {
                throw new ArgumentException(
                    $"Candidate seat {candidate.Seat} appears more than once.",
                    nameof(candidates));
            }

            seats[candidate.Seat] = true;
        }

        if (discard.Id == Tile.UnknownId)
        {
            throw new ArgumentException(
                "The discarded tile must have a known physical id.",
                nameof(discard));
        }

        if (deadlineTick < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadlineTick),
                deadlineTick,
                "Deadline tick must be non-negative.");
        }

        _candidates.Clear();
        _candidates.AddRange(candidates);

        _requests = new PendingRequest[candidates.Count];
        for (int index = 0; index < candidates.Count; index++)
        {
            PlayerState candidate = _candidates[index];
            _requests[index] = new PendingRequest(
                _firstRequestId + index,
                candidate.Seat,
                RequestKind.Pong,
                AllowedVerbs.Pong | AllowedVerbs.Kong | AllowedVerbs.Pass,
                deadlineTick);
        }

        _requestsView = Array.AsReadOnly(_requests);
        _answered = new bool[candidates.Count];
        _skills = new SkillId?[candidates.Count];
        _arrival = new long[candidates.Count];
        _discard = discard;
        _deadlineTick = deadlineTick;
        _opened = true;
    }

    /// <summary>
    /// 记录一次碰/杠应答。窗口未开或已关闭、非候选、requestId 不匹配、技能不是碰/杠、
    /// 到达批次晚于截止 tick、该座已应答时返回 false 且不改变窗口状态。
    /// </summary>
    /// <param name="player">应答玩家；必须是开窗时传入的同一引用。</param>
    /// <param name="skill">碰或杠；其他技能编号一律拒绝。</param>
    /// <param name="requestId">该座的请求 id。</param>
    /// <param name="arrivalSeq">到达批次；同一逻辑 tick 的应答传同一个值，跨 tick 单调递增。</param>
    /// <returns>记录成功返回 true。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> 为 null。</exception>
    public bool TrySubmit(PlayerState player, SkillId skill, long requestId, long arrivalSeq)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!_opened || _closed)
        {
            return false;
        }

        int index = IndexOf(player);
        if (index < 0)
        {
            return false;
        }

        if (_requests[index].RequestId != requestId)
        {
            return false;
        }

        if (skill is not (SkillId.Pong or SkillId.Kong))
        {
            return false;
        }

        if (arrivalSeq > _deadlineTick)
        {
            return false;
        }

        if (_answered[index])
        {
            return false;
        }

        _answered[index] = true;
        _skills[index] = skill;
        _arrival[index] = arrivalSeq;
        return true;
    }

    /// <summary>
    /// 记录一次显式“过”。校验口径与 <see cref="TrySubmit"/> 相同；过是应答但不参与胜者仲裁。
    /// </summary>
    /// <param name="player">应答玩家；必须是开窗时传入的同一引用。</param>
    /// <param name="requestId">该座的请求 id。</param>
    /// <param name="arrivalSeq">到达批次；同一逻辑 tick 的应答传同一个值，跨 tick 单调递增。</param>
    /// <returns>记录成功返回 true。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> 为 null。</exception>
    public bool TryPass(PlayerState player, long requestId, long arrivalSeq)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!_opened || _closed)
        {
            return false;
        }

        int index = IndexOf(player);
        if (index < 0)
        {
            return false;
        }

        if (_requests[index].RequestId != requestId)
        {
            return false;
        }

        if (arrivalSeq > _deadlineTick)
        {
            return false;
        }

        if (_answered[index])
        {
            return false;
        }

        _answered[index] = true;
        _skills[index] = null;
        _arrival[index] = arrivalSeq;
        return true;
    }

    /// <summary>
    /// 关闭窗口并产出结算：胜者按“杠 &gt; 碰 → arrivalSeq 升序 → 座位升序”选出；
    /// 未在截止前全体应答且 <paramref name="tick"/> 未到截止 tick 时拒绝关闭。
    /// </summary>
    /// <param name="tick">当前逻辑 tick。</param>
    /// <exception cref="InvalidOperationException">窗口未开，或 tick 未到截止且并非全体已应答。失败时窗口不关闭。</exception>
    public void Close(long tick)
    {
        if (_closed)
        {
            return;
        }

        if (!_opened)
        {
            throw new InvalidOperationException("A response window must be opened before it can be closed.");
        }

        if (tick < _deadlineTick && !AllAnswered)
        {
            throw new InvalidOperationException(
                "The response window cannot be closed before its deadline while some candidates are still pending.");
        }

        int winnerIndex = FindWinner();
        RequestOutcome[] outcomes = new RequestOutcome[_candidates.Count];
        for (int index = 0; index < outcomes.Length; index++)
        {
            long requestId = _requests[index].RequestId;
            if (_skills[index] is SkillId submitted)
            {
                outcomes[index] = index == winnerIndex
                    ? new RequestOutcome(requestId, RequestOutcomeKind.Answered, submitted)
                    : new RequestOutcome(requestId, RequestOutcomeKind.Cancelled, null);
            }
            else if (_answered[index])
            {
                outcomes[index] = new RequestOutcome(requestId, RequestOutcomeKind.Answered, null);
            }
            else
            {
                outcomes[index] = new RequestOutcome(requestId, RequestOutcomeKind.TimedOut, null);
            }
        }

        byte winnerSeat = winnerIndex >= 0 ? _candidates[winnerIndex].Seat : ResponseResult.NoWinnerSeat;
        SkillId? winnerSkill = winnerIndex >= 0 ? _skills[winnerIndex] : null;
        _result = new ResponseResult(
            _discard,
            winnerSeat,
            winnerSkill,
            _requestsView,
            Array.AsReadOnly(outcomes));
        _closed = true;
    }

    /// <summary>按“杠 &gt; 碰、arrivalSeq 升序、座位升序”找出胜者下标；无有效提交返回 -1。</summary>
    private int FindWinner()
    {
        int winnerIndex = -1;
        for (int index = 0; index < _skills.Length; index++)
        {
            if (_skills[index] is null)
            {
                continue;
            }

            if (winnerIndex < 0 || Beats(index, winnerIndex))
            {
                winnerIndex = index;
            }
        }

        return winnerIndex;
    }

    /// <summary>判断 <paramref name="left"/> 是否按仲裁键胜过 <paramref name="right"/>；两者都必须已提交技能。</summary>
    private bool Beats(int left, int right)
    {
        int leftPriority = Priority(_skills[left]!.Value);
        int rightPriority = Priority(_skills[right]!.Value);
        if (leftPriority != rightPriority)
        {
            return leftPriority > rightPriority;
        }

        int byArrival = _arrival[left].CompareTo(_arrival[right]);
        if (byArrival != 0)
        {
            return byArrival < 0;
        }

        return _candidates[left].Seat < _candidates[right].Seat;
    }

    /// <summary>仲裁优先级：杠 &gt; 碰。</summary>
    private static int Priority(SkillId skill) => skill == SkillId.Kong ? 2 : 1;

    /// <summary>按引用查找候坐下标；不是候选返回 -1。</summary>
    private int IndexOf(PlayerState player)
    {
        for (int index = 0; index < _candidates.Count; index++)
        {
            if (ReferenceEquals(_candidates[index], player))
            {
                return index;
            }
        }

        return -1;
    }
}
