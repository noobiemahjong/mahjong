using System.Collections.ObjectModel;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;

namespace Mahjong.Session;

/// <summary>
/// 请求登记表：从 1 单调分配请求 id、登记单座请求与弃牌响应窗口、收集应答、在 tick 边界结算并上报请求开闭事件。
/// 响应窗口按候选顺序占用连续 id，仲裁复用 <see cref="ResponseWindow"/>（杠 &gt; 碰 → arrivalTick 升序 → 座位升序）。
/// 每个请求只产出一次结果；单座结果与窗口逐请求结果都只可消费一次。实例非线程安全；无静态可变状态。
/// </summary>
internal sealed class SessionRequestRegistry : IRequestRegistry
{
    private readonly GameState _state;
    private readonly ISessionRequestSink _sink;
    private readonly List<SingleRequest> _singles = [];
    private readonly List<ResponseGroup> _groups = [];
    private long _nextRequestId = 1;
    private long _nextGroupId = 1;

    /// <summary>创建登记表。</summary>
    /// <param name="state">权威局面；用于按座位取 <see cref="PlayerState"/> 引用。</param>
    /// <param name="sink">请求开闭事件接收者。</param>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    public SessionRequestRegistry(GameState state, ISessionRequestSink sink)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sink);
        _state = state;
        _sink = sink;
    }

    /// <summary>未结算请求，requestId 升序；无请求时为空只读列表。</summary>
    public IReadOnlyList<PendingRequest> OpenRequests
    {
        get
        {
            List<PendingRequest> open = [];
            foreach (SingleRequest single in _singles)
            {
                if (!single.Settled)
                {
                    open.Add(single.Request);
                }
            }

            foreach (ResponseGroup group in _groups)
            {
                if (group.Window.IsOpen)
                {
                    foreach (PendingRequest request in group.Window.Requests)
                    {
                        open.Add(request);
                    }
                }
            }

            if (open.Count == 0)
            {
                return Array.Empty<PendingRequest>();
            }

            open.Sort((left, right) => left.RequestId.CompareTo(right.RequestId));
            return new ReadOnlyCollection<PendingRequest>(open);
        }
    }

    /// <summary>打开一个单座请求：分配下一个请求 id 并上报 RequestOpened。</summary>
    /// <param name="seat">目标座位 0–3。</param>
    /// <param name="kind">请求种类。</param>
    /// <param name="allowed">允许应答的动词位集。</param>
    /// <param name="openedTick">开请求 tick。</param>
    /// <param name="deadlineTick">截止 tick，不得早于 <paramref name="openedTick"/>。</param>
    /// <returns>新请求。</returns>
    /// <exception cref="ArgumentOutOfRangeException">座位越界，或截止 tick 早于开请求 tick（防御性校验）。</exception>
    public PendingRequest OpenSingle(byte seat, RequestKind kind, AllowedVerbs allowed, long openedTick, long deadlineTick)
    {
        ValidateSeat(seat, nameof(seat));
        ValidateDeadline(openedTick, deadlineTick);
        PendingRequest request = new(_nextRequestId++, seat, kind, allowed, deadlineTick);
        _singles.Add(new SingleRequest { Request = request });
        _sink.RequestOpened(request);
        return request;
    }

    /// <summary>
    /// 打开弃牌响应窗口：按候选顺序占用连续请求 id 并逐个上报 RequestOpened。
    /// 候选为空时返回 null 且不分配 groupId/requestId。
    /// </summary>
    /// <param name="discard">被响应的弃牌，物理 id 必须已知。</param>
    /// <param name="candidates">可响应的玩家，0–3 个，顺序即请求 id 顺序。</param>
    /// <param name="openedTick">开窗 tick。</param>
    /// <param name="deadlineTick">截止 tick，不得早于 <paramref name="openedTick"/>。</param>
    /// <returns>请求组 id；候选为空时为 null。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidates"/> 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">截止 tick 早于开窗 tick（防御性校验）。</exception>
    /// <exception cref="ArgumentException">候选超过 3 个、座位重复或弃牌 id 未知。</exception>
    public long? OpenGroup(Tile discard, IReadOnlyList<PlayerState> candidates, long openedTick, long deadlineTick)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            return null;
        }

        ValidateDeadline(openedTick, deadlineTick);

        ResponseWindow window = new(_nextRequestId);
        window.Open(discard, candidates, deadlineTick);

        PlayerState[] candidateCopy = new PlayerState[candidates.Count];
        for (int index = 0; index < candidateCopy.Length; index++)
        {
            candidateCopy[index] = candidates[index];
        }

        long groupId = _nextGroupId++;
        ResponseGroup group = new()
        {
            GroupId = groupId,
            Window = window,
            Candidates = candidateCopy,
            Commands = new GameCommand?[candidateCopy.Length],
            Consumed = new bool[candidateCopy.Length],
        };
        _nextRequestId += candidateCopy.Length;
        _groups.Add(group);
        foreach (PendingRequest request in window.Requests)
        {
            _sink.RequestOpened(request);
        }

        return groupId;
    }

    /// <summary>
    /// 按座位找开放请求：<paramref name="requestId"/> 为 0 时返回该座位唯一的开放请求；
    /// 非 0 时要求开放请求的 id 与之相等。
    /// </summary>
    /// <param name="seat">座位 0–3。</param>
    /// <param name="requestId">请求 id；0 表示按座位匹配。</param>
    /// <param name="request">找到的开放请求；返回 false 时为 default。</param>
    /// <returns>找到返回 true。</returns>
    public bool TryFindOpen(byte seat, long requestId, out PendingRequest request)
    {
        request = default;
        foreach (SingleRequest single in _singles)
        {
            if (single.Settled || single.Request.Seat != seat)
            {
                continue;
            }

            if (requestId != 0 && single.Request.RequestId != requestId)
            {
                continue;
            }

            request = single.Request;
            return true;
        }

        foreach (ResponseGroup group in _groups)
        {
            if (!group.Window.IsOpen)
            {
                continue;
            }

            foreach (PendingRequest candidate in group.Window.Requests)
            {
                if (candidate.Seat != seat)
                {
                    continue;
                }

                if (requestId != 0 && candidate.RequestId != requestId)
                {
                    continue;
                }

                request = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 响应窗口候选请求的响应目标：窗口弃牌。窗口已关闭后仍按候选身份返回 true，不表示窗口仍开放。
    /// </summary>
    /// <param name="requestId">请求 id。</param>
    /// <param name="target">窗口弃牌；返回 false 时为 default。</param>
    /// <returns>该请求是响应窗口候选返回 true；单座请求或未知 id 返回 false。</returns>
    public bool TryGetResponseTarget(long requestId, out Tile target)
    {
        target = default;
        foreach (ResponseGroup group in _groups)
        {
            foreach (PendingRequest request in group.Window.Requests)
            {
                if (request.RequestId == requestId)
                {
                    target = group.Window.Discard;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 记录一次应答。requestId/座位不匹配、请求已结算或已应答、动词不在允许位集内、
    /// 到达 tick 晚于截止 tick 时返回 false 且不改变状态。
    /// </summary>
    /// <param name="request">被应答的请求；必须是登记表中的同一请求。</param>
    /// <param name="command">应答命令。</param>
    /// <param name="arrivalTick">到达 tick；不得晚于请求截止 tick。</param>
    /// <returns>记录成功返回 true。</returns>
    public bool TryRecordAnswer(PendingRequest request, GameCommand command, long arrivalTick)
    {
        if (command.RequestId != 0 && command.RequestId != request.RequestId)
        {
            return false;
        }

        SingleRequest? single = FindSingle(request.RequestId);
        if (single is not null)
        {
            if (single.Settled || single.Answered)
            {
                return false;
            }

            if (single.Request.Seat != request.Seat || single.Request.Kind != request.Kind)
            {
                return false;
            }

            if (arrivalTick > single.Request.DeadlineTick || !Allows(single.Request.Allowed, command.Verb))
            {
                return false;
            }

            single.Answered = true;
            single.Answer = command;
            return true;
        }

        ResponseGroup? group = FindGroupByRequest(request.RequestId, out int candidateIndex);
        if (group is null || group.Closed)
        {
            return false;
        }

        PendingRequest stored = group.Window.Requests[candidateIndex];
        if (stored.Seat != request.Seat || stored.Kind != request.Kind)
        {
            return false;
        }

        bool recorded = command.Verb switch
        {
            CommandVerb.Pong => group.Window.TrySubmit(group.Candidates[candidateIndex], SkillId.Pong, request.RequestId, arrivalTick),
            CommandVerb.Kong => group.Window.TrySubmit(group.Candidates[candidateIndex], SkillId.Kong, request.RequestId, arrivalTick),
            CommandVerb.Pass => group.Window.TryPass(group.Candidates[candidateIndex], request.RequestId, arrivalTick),
            _ => false,
        };
        if (recorded)
        {
            group.Commands[candidateIndex] = command;
        }

        return recorded;
    }

    /// <summary>单座请求已产出结果，或该请求所属响应窗口已关闭。</summary>
    /// <param name="requestId">请求 id。</param>
    /// <returns>已结算返回 true；未知 id 返回 false。</returns>
    public bool IsSettled(long requestId)
    {
        SingleRequest? single = FindSingle(requestId);
        if (single is not null)
        {
            return single.Settled;
        }

        ResponseGroup? group = FindGroupByRequest(requestId, out _);
        return group is not null && group.Closed;
    }

    /// <summary>取走单座请求的结算结果；同一结果只能取一次。</summary>
    /// <param name="requestId">单座请求 id。</param>
    /// <returns>结算结果；Answered 的 Payload 是 boxed <see cref="GameCommand"/>。</returns>
    /// <exception cref="InvalidOperationException">id 不存在、不是单座请求、尚未结算或结果已被消费。</exception>
    public RequestOutcome TakeOutcome(long requestId)
    {
        SingleRequest? single = FindSingle(requestId);
        if (single is null)
        {
            throw new InvalidOperationException($"请求 {requestId} 不存在或不是单座请求。");
        }

        if (!single.Settled)
        {
            throw new InvalidOperationException($"请求 {requestId} 尚未结算。");
        }

        if (single.Consumed)
        {
            throw new InvalidOperationException($"请求 {requestId} 的结算结果已被消费。");
        }

        single.Consumed = true;
        return single.Outcome;
    }

    /// <summary>响应窗口是否已关闭。</summary>
    /// <param name="groupId">请求组 id。</param>
    /// <returns>已关闭返回 true；未知 groupId 返回 false。</returns>
    public bool IsGroupClosed(long groupId)
    {
        ResponseGroup? group = FindGroup(groupId);
        return group is not null && group.Closed;
    }

    /// <summary>取响应窗口结算快照；窗口未关闭时抛异常。幂等：关闭后可重复调用，返回同一快照实例。</summary>
    /// <param name="groupId">请求组 id。</param>
    /// <returns>窗口结算快照，含胜者、技能与逐请求结果。</returns>
    /// <exception cref="InvalidOperationException">group id 不存在或窗口尚未关闭。</exception>
    public ResponseResult TakeGroupResult(long groupId)
    {
        ResponseGroup? group = FindGroup(groupId);
        if (group is null)
        {
            throw new InvalidOperationException($"请求组 {groupId} 不存在。");
        }

        if (!group.Closed)
        {
            throw new InvalidOperationException($"请求组 {groupId} 尚未关闭。");
        }

        return group.Result!;
    }

    /// <summary>
    /// 推进到指定 tick：先按 requestId 升序结算单座请求（已应答 → Answered；到 deadline → TimedOut），
    /// 再关闭全体已应答或到 deadline 的响应窗口，按候选顺序逐座上报 RequestClosed。每个请求只产出一次结果。
    /// </summary>
    /// <param name="tick">当前逻辑 tick。</param>
    void IRequestRegistry.BeginTick(long tick)
    {
        foreach (SingleRequest single in _singles)
        {
            if (single.Settled)
            {
                continue;
            }

            if (single.Answered)
            {
                SettleSingle(single, RequestOutcomeKind.Answered, single.Answer);
            }
            else if (tick >= single.Request.DeadlineTick)
            {
                SettleSingle(single, RequestOutcomeKind.TimedOut, null);
            }
        }

        foreach (ResponseGroup group in _groups)
        {
            if (group.Closed)
            {
                continue;
            }

            if (!group.Window.AllAnswered && tick < group.Window.DeadlineTick)
            {
                continue;
            }

            group.Window.Close(tick);
            ResponseResult result = group.Window.Result!;
            group.Result = result;
            group.Outcomes = new RequestOutcome[result.Outcomes.Count];
            for (int index = 0; index < result.Outcomes.Count; index++)
            {
                group.Outcomes[index] = result.Outcomes[index];
            }

            group.Closed = true;
            for (int index = 0; index < result.Requests.Count; index++)
            {
                RequestOutcome outcome = result.Outcomes[index];
                GameCommand? answer = outcome.Kind == RequestOutcomeKind.Answered ? group.Commands[index] : null;
                _sink.RequestClosed(result.Requests[index], outcome.Kind, answer);
            }
        }
    }

    /// <summary>
    /// 尝试取走一次结算结果；同一结果只成功一次，与 <see cref="TakeOutcome"/> 共享消费状态。
    /// </summary>
    /// <param name="requestId">请求 id。</param>
    /// <param name="outcome">结算结果；返回 false 时无意义。</param>
    /// <returns>该请求已结算且结果尚未被消费返回 true。</returns>
    bool IRequestRegistry.TryConsume(long requestId, out RequestOutcome outcome)
    {
        outcome = default;
        SingleRequest? single = FindSingle(requestId);
        if (single is not null)
        {
            if (!single.Settled || single.Consumed)
            {
                return false;
            }

            single.Consumed = true;
            outcome = single.Outcome;
            return true;
        }

        ResponseGroup? group = FindGroupByRequest(requestId, out int candidateIndex);
        if (group is null || !group.Closed || group.Consumed[candidateIndex])
        {
            return false;
        }

        group.Consumed[candidateIndex] = true;
        outcome = group.Outcomes[candidateIndex];
        return true;
    }

    private void SettleSingle(SingleRequest single, RequestOutcomeKind kind, GameCommand? answer)
    {
        single.Settled = true;
        object? payload = kind == RequestOutcomeKind.Answered ? answer : null;
        single.Outcome = new RequestOutcome(single.Request.RequestId, kind, payload);
        _sink.RequestClosed(single.Request, kind, answer);
    }

    private SingleRequest? FindSingle(long requestId)
    {
        foreach (SingleRequest single in _singles)
        {
            if (single.Request.RequestId == requestId)
            {
                return single;
            }
        }

        return null;
    }

    private ResponseGroup? FindGroup(long groupId)
    {
        foreach (ResponseGroup group in _groups)
        {
            if (group.GroupId == groupId)
            {
                return group;
            }
        }

        return null;
    }

    private ResponseGroup? FindGroupByRequest(long requestId, out int candidateIndex)
    {
        foreach (ResponseGroup group in _groups)
        {
            IReadOnlyList<PendingRequest> requests = group.Window.Requests;
            for (int index = 0; index < requests.Count; index++)
            {
                if (requests[index].RequestId == requestId)
                {
                    candidateIndex = index;
                    return group;
                }
            }
        }

        candidateIndex = -1;
        return null;
    }

    private void ValidateSeat(byte seat, string paramName)
    {
        if (seat >= _state.Players.Length)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                seat,
                $"座位必须在 0–{_state.Players.Length - 1}。");
        }
    }

    private static void ValidateDeadline(long openedTick, long deadlineTick)
    {
        if (deadlineTick < openedTick)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadlineTick),
                deadlineTick,
                "截止 tick 不得早于开请求/开窗 tick。");
        }
    }

    private static bool Allows(AllowedVerbs allowed, CommandVerb verb) => verb switch
    {
        CommandVerb.Discard => (allowed & AllowedVerbs.Discard) != 0,
        CommandVerb.Pong => (allowed & AllowedVerbs.Pong) != 0,
        CommandVerb.Kong => (allowed & AllowedVerbs.Kong) != 0,
        CommandVerb.Win => (allowed & AllowedVerbs.Win) != 0,
        CommandVerb.Pass => (allowed & AllowedVerbs.Pass) != 0,
        _ => false,
    };

    /// <summary>单座请求的登记与结算状态。</summary>
    private sealed class SingleRequest
    {
        public PendingRequest Request;
        public bool Answered;
        public GameCommand Answer;
        public bool Settled;
        public RequestOutcome Outcome;
        public bool Consumed;
    }

    /// <summary>响应窗口的登记与结算状态。</summary>
    private sealed class ResponseGroup
    {
        public long GroupId;
        public ResponseWindow Window = null!;
        public PlayerState[] Candidates = [];
        public GameCommand?[] Commands = [];
        public bool[] Consumed = [];
        public bool Closed;
        public ResponseResult? Result;
        public RequestOutcome[] Outcomes = [];
    }
}
