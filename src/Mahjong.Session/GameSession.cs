using Mahjong.Core.Bot;
using Mahjong.Core.Content;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;

namespace Mahjong.Session;

/// <summary>
/// 本地权威会话：持有唯一权威局面与计分账本，用单根动作协程按 tick 推进 8 局流程，
/// 接收命令、开闭请求、产出版本化事件流；不读时钟、不做 IO、无静态可变状态，实例之间零共享。
/// </summary>
/// <remarks>
/// 生命周期：构造后未开始；<see cref="Start"/> 只能调用一次并创建根动作；
/// <see cref="Step"/> 必须严格递增 tick；<see cref="Submit"/> 只记录应答，不推进局面；
/// <see cref="DrainEvents"/> 是破坏性单消费者读取。
/// </remarks>
public sealed partial class GameSession : IRequestRegistry, ISessionRequestSink
{
    /// <summary>全景观察座位：事件编码不过滤可见性。</summary>
    private const byte FullViewSeat = 255;

    /// <summary>座位状态编码：真人在线。</summary>
    private const byte SeatStateOnline = 0;

    /// <summary>座位状态编码：Bot 托管。</summary>
    private const byte SeatStateBot = 2;

    private readonly SessionConfig _config;
    private readonly IWinChecker _wins;
    private readonly IRandomSource _rng;
    private readonly TileType _joker;
    private readonly bool _jokerActive;

    /// <summary>连庄奖励表，按连庄次数升序；空表表示变体关闭，任何连庄都不付。</summary>
    private readonly IReadOnlyList<int> _dealerStreakBonus;

    /// <summary>跟庄金额；0 表示变体关闭，非庄胡不付跟庄。</summary>
    private readonly int _followingBanker;
    private readonly byte? _humanSeat;
    private readonly int _botDelayTicks;
    private readonly GameState _state = new();
    private readonly ScoreManager _scores = new();
    private readonly CardAndSkillVerifier _verifier;
    private readonly SimpleBotPolicy _botPolicy;
    private readonly PongSkill _pongSkill;
    private readonly KongSkill _kongSkill;
    private readonly SessionEventBuffer _events = new();
    private readonly SessionRequestRegistry _requests;
    private readonly ActionScheduler _scheduler;
    private readonly List<BotSubmission> _botQueue = [];
    private readonly HashSet<long> _answeredRequestIds = [];

    private long _tick;
    private bool _started;
    private bool _gameOver;
    private Tile? _lastDrawn;
    private bool _lastDrawWasKongSupplement;

    /// <summary>
    /// 创建会话：校验配置并装配局面、计分、校验器、请求登记与调度器；不启动、不产事件。
    /// 鬼牌只取 <see cref="ContentCatalog.Joker"/> 的固定牌种，无鬼时用 <c>default(TileType)</c>；
    /// 计分变体只取 <see cref="ContentCatalog.Variants"/> 的连庄奖励表与跟庄金额，缺省即关闭（不付、不置位）。
    /// </summary>
    /// <param name="config">会话配置；真人座位/Bot 数组合与 tick/超时数值在此校验。</param>
    /// <param name="content">已加载内容目录；只读，用于取鬼牌规则与计分变体。</param>
    /// <param name="wins">胡牌判定器；判定期只读。</param>
    /// <param name="rng">随机源；只用于首局起庄与每局洗牌，Bot 不消费。</param>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    /// <exception cref="ArgumentException">真人座位/Bot 数组合非法。</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 真人座位不在 0–3，或 TickRate/TurnTimeoutTicks/ResponseTimeoutTicks 小于 1；<c>ParamName</c> 为 <c>config</c>。
    /// </exception>
    public GameSession(SessionConfig config, ContentCatalog content, IWinChecker wins, IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(wins);
        ArgumentNullException.ThrowIfNull(rng);

        ValidateConfig(config);

        _config = config;
        _humanSeat = config.HumanSeat;
        _wins = wins;
        _rng = rng;
        _joker = content.Joker.Mode == JokerMode.FixedType && content.Joker.Type.HasValue
            ? content.Joker.Type.GetValueOrDefault()
            : default;
        _jokerActive = IsJokerActive(_joker);
        _dealerStreakBonus = content.Variants.DealerStreakBonus;
        _followingBanker = content.Variants.FollowingBanker;
        _botDelayTicks = ComputeBotDelayTicks(config.TickRate);
        _verifier = new CardAndSkillVerifier(wins, _joker);
        _botPolicy = new SimpleBotPolicy(wins, _verifier);
        _pongSkill = new PongSkill(_joker);
        _kongSkill = new KongSkill(_joker);
        _requests = new SessionRequestRegistry(_state, this);
        _scheduler = new ActionScheduler(this);
    }

    /// <summary>唯一权威局面；会话存活期间是同一实例。</summary>
    public GameState State => _state;

    /// <summary>最近一次成功 <see cref="Step"/> 的 tick；首次 Step 前为 0。</summary>
    public long Tick => _tick;

    /// <summary>是否已调用 <see cref="Start"/>。</summary>
    public bool IsStarted => _started;

    /// <summary>8 局是否已结束（已产出 GameEnded）。</summary>
    public bool IsGameOver => _gameOver;

    /// <summary>当前未结算请求，requestId 升序；无请求时为空只读列表。</summary>
    public IReadOnlyList<PendingRequest> ActiveRequests => _requests.OpenRequests;

    /// <summary>
    /// 启动会话：按座位 0–3 产出 SeatStateChanged（真人 Online、其余 Bot），创建单根动作协程并交给调度器。
    /// 只推进流程到「等待第一次 Step」，不发牌、不产其它事件；重复调用抛异常。
    /// </summary>
    /// <exception cref="InvalidOperationException">已经启动过。</exception>
    public void Start()
    {
        if (_started)
        {
            throw new InvalidOperationException("会话已启动，Start 只能调用一次。");
        }

        _started = true;
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            byte state = _humanSeat == seat ? SeatStateOnline : SeatStateBot;
            _events.EmitSeatStateChanged(_tick, seat, state);
        }

        GameAction loop = _scheduler.CreateAction("GameLoop", RunGameLoop(), priority: 10, interruptible: false);
        _scheduler.Start(loop);
    }

    /// <summary>
    /// 推进一个逻辑 tick：先处理到期 Bot 应答，再让调度器结算请求并推进动作协程，最后记录 tick。
    /// Bot 应答与人类应答共用 <see cref="Submit"/> 的到达批次口径（D13）：本方法前两步仍在旧 <see cref="Tick"/> 下提交，
    /// 因此 Step(t) 内的 Bot 记 t-1，与上一 Step 之后的人类提交同批；最后一步才把 <see cref="Tick"/> 推进到 t。
    /// 未启动抛异常；tick 必须严格大于 <see cref="Tick"/>。
    /// </summary>
    /// <param name="tick">本次逻辑 tick。</param>
    /// <exception cref="InvalidOperationException">尚未 <see cref="Start"/>。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tick"/> 不大于 <see cref="Tick"/>。</exception>
    public void Step(long tick)
    {
        if (!_started)
        {
            throw new InvalidOperationException("会话尚未启动，不能 Step。");
        }

        if (tick <= _tick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "Step 的 tick 必须严格递增。");
        }

        if (_gameOver)
        {
            // 终局后只推进逻辑时钟：不处理请求、不产事件、不改局面。
            _tick = tick;
            return;
        }

        ProcessBotQueue(tick);
        _scheduler.Step(tick);
        _tick = tick;
    }

    /// <summary>
    /// 提交一条命令：校验座位、开放请求、动词位与动作合法性，全部通过后只把命令记录为该请求的应答；
    /// 不推进局面、不产事件、不推进 tick。任何非法输入/状态都返回 false 与非空原因，不抛异常。
    /// 到达批次 arrivalSeq 取调用时的 <see cref="Tick"/>，即最近一次已完成 Step 的 tick（D13）：
    /// Step(t) 内 Bot 在 Tick 更新前提交记 t-1，与上一 Step 之后的人类提交同批。
    /// </summary>
    /// <param name="command">玩家命令；座位、动词、槽位与请求 id 由本方法校验。</param>
    /// <returns>接受时 <see cref="SubmitResult.RequestId"/> 为被应答请求 id；拒绝时为 0。</returns>
    public SubmitResult Submit(GameCommand command) => SubmitCore(command);

    /// <summary>
    /// 破坏性读取事件：按 viewer 编码 payload 后清空缓冲；同一事件不会重复返回。
    /// 未启动或已排空时返回空列表，不返回 null。
    /// </summary>
    /// <param name="viewerSeat">观察座位 0–3；255 表示全景。</param>
    /// <returns>按产出顺序排列的事件快照。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="viewerSeat"/> 不在 0–3 且不等于 255。</exception>
    public IReadOnlyList<SessionEvent> DrainEvents(byte viewerSeat)
    {
        if (viewerSeat != FullViewSeat && viewerSeat >= GameState.PlayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(viewerSeat), viewerSeat, "观察座位必须是 0–3 或 255。");
        }

        if (!_started)
        {
            return Array.Empty<SessionEvent>();
        }

        return _events.Drain(viewerSeat);
    }

    /// <summary>调度器每 tick 的请求结算入口：转发给请求登记表（超时/取消/响应窗口关闭在此产出）。</summary>
    void IRequestRegistry.BeginTick(long tick) => ((IRequestRegistry)_requests).BeginTick(tick);

    /// <summary>调度器解除 WaitRequest 时的消费入口：与 <see cref="SessionRequestRegistry.TakeOutcome"/> 共享一次性消费状态。</summary>
    bool IRequestRegistry.TryConsume(long requestId, out RequestOutcome outcome) =>
        ((IRequestRegistry)_requests).TryConsume(requestId, out outcome);

    /// <summary>请求已打开：写 RequestOpened 事件，Bot 座位按固定延迟挂起到期应答。</summary>
    void ISessionRequestSink.RequestOpened(PendingRequest request)
    {
        _events.EmitRequestOpened(_scheduler.Tick, request);
        if (!IsHumanSeat(request.Seat))
        {
            _botQueue.Add(new BotSubmission(request.RequestId, _scheduler.Tick + _botDelayTicks, request.Seat));
        }
    }

    /// <summary>请求已结算：写 RequestClosed 事件；Answered 且非 null 才携带应答命令。</summary>
    void ISessionRequestSink.RequestClosed(PendingRequest request, RequestOutcomeKind outcome, GameCommand? answer)
    {
        _answeredRequestIds.Remove(request.RequestId);
        _events.EmitRequestClosed(_scheduler.Tick, request, outcome, answer);
    }

    /// <summary>
    /// 提交核心路径：人类与 Bot 共用；只记录应答，不推进局面。
    /// arrivalSeq 使用调用时的 <see cref="Tick"/>（最近一次已完成 Step 的 tick，D13）；Bot 在 <see cref="Step"/>
    /// 更新 Tick 之前提交，因此记上一 tick 批次。
    /// </summary>
    private SubmitResult SubmitCore(GameCommand command)
    {
        if (!_started)
        {
            return Reject("会话尚未启动。");
        }

        if (_gameOver)
        {
            return Reject("整场已结束，不能再提交命令。");
        }

        if (command.Seat >= GameState.PlayerCount)
        {
            return Reject("命令座位必须在 0–3。");
        }

        if (!_requests.TryFindOpen(command.Seat, command.RequestId, out PendingRequest request))
        {
            return Reject("该座位没有匹配的开放请求。");
        }

        if (!TryMapVerb(command.Verb, out AllowedVerbs verbFlag) || (request.Allowed & verbFlag) == 0)
        {
            return Reject("请求不允许该动词。");
        }

        string? invalid = ValidateAnswer(command, request);
        if (invalid is not null)
        {
            return new SubmitResult(false, invalid, 0);
        }

        if (!_requests.TryRecordAnswer(request, command, _tick))
        {
            return Reject("请求已应答、已结算或到达过晚。");
        }

        _answeredRequestIds.Add(request.RequestId);
        return new SubmitResult(true, null, request.RequestId);
    }

    /// <summary>按动作类型复核应答合法性；合法返回 null，否则返回拒绝原因。</summary>
    private string? ValidateAnswer(GameCommand command, PendingRequest request)
    {
        PlayerState player = _state.Players[command.Seat];
        switch (command.Verb)
        {
            case CommandVerb.Pass:
                return null;

            case CommandVerb.Discard:
                if ((uint)command.Slot >= (uint)player.Hand.Count)
                {
                    return "出牌槽位越界。";
                }

                Tile tile = player.Hand[command.Slot];
                if (command.CardId != 0 && command.CardId != tile.Id)
                {
                    return "出牌牌 id 与槽位不一致。";
                }

                return _verifier.Verify(_state, player, skill: null, card: tile, targetCard: null)
                    ? null
                    : "当前局面不允许出这张牌。";

            case CommandVerb.Pong:
                if (!_requests.TryGetResponseTarget(request.RequestId, out Tile pongTarget))
                {
                    return "当前请求没有可响应的弃牌。";
                }

                return _verifier.Verify(_state, player, SkillId.Pong, card: null, targetCard: pongTarget)
                    ? null
                    : "当前局面不允许碰。";

            case CommandVerb.Kong:
                if (_requests.TryGetResponseTarget(request.RequestId, out Tile kongTarget))
                {
                    return _verifier.Verify(_state, player, SkillId.Kong, card: null, targetCard: kongTarget)
                        ? null
                        : "当前局面不允许明杠。";
                }

                return _verifier.Verify(_state, player, SkillId.Kong, card: _lastDrawn, targetCard: null)
                    ? null
                    : "当前局面不允许暗杠或补杠。";

            case CommandVerb.Win:
                if (_verifier.Verify(_state, player, SkillId.SelfDrawnWith7Pairs, card: null, targetCard: null))
                {
                    return null;
                }

                return _verifier.Verify(_state, player, SkillId.SelfDrawn, card: null, targetCard: null)
                    ? null
                    : "当前手牌不能自摸。";

            default:
                return "未定义的命令动词。";
        }
    }

    /// <summary>把动词映射为允许位；未定义取值返回 false。</summary>
    private static bool TryMapVerb(CommandVerb verb, out AllowedVerbs allowed)
    {
        switch (verb)
        {
            case CommandVerb.Discard:
                allowed = AllowedVerbs.Discard;
                return true;
            case CommandVerb.Pong:
                allowed = AllowedVerbs.Pong;
                return true;
            case CommandVerb.Kong:
                allowed = AllowedVerbs.Kong;
                return true;
            case CommandVerb.Win:
                allowed = AllowedVerbs.Win;
                return true;
            case CommandVerb.Pass:
                allowed = AllowedVerbs.Pass;
                return true;
            default:
                allowed = AllowedVerbs.None;
                return false;
        }
    }

    /// <summary>处理已到期的 Bot 应答：按 requestId 升序逐条决策并走与人类相同的提交路径。</summary>
    private void ProcessBotQueue(long tick)
    {
        if (_botQueue.Count == 0)
        {
            return;
        }

        List<BotSubmission> due = [];
        for (int index = 0; index < _botQueue.Count;)
        {
            BotSubmission submission = _botQueue[index];
            if (submission.DueTick <= tick)
            {
                due.Add(submission);
                _botQueue.RemoveAt(index);
            }
            else
            {
                index++;
            }
        }

        if (due.Count == 0)
        {
            return;
        }

        due.Sort(static (left, right) => left.RequestId.CompareTo(right.RequestId));
        foreach (BotSubmission submission in due)
        {
            ExecuteBot(submission);
        }
    }

    /// <summary>执行一条 Bot 应答；请求已被结算时跳过，决策被拒视为 bug 抛异常。</summary>
    private void ExecuteBot(BotSubmission submission)
    {
        if (!_requests.TryFindOpen(submission.Seat, submission.RequestId, out PendingRequest request))
        {
            return;
        }

        // 该请求已被别的提交应答（例如宿主越权代答 Bot 座位）：Bot 不再重复应答，避免制造一次「被拒」的 bug 路径。
        if (_answeredRequestIds.Contains(request.RequestId))
        {
            return;
        }

        BotContext context = CreateBotContext(request);
        BotDecision decision = _botPolicy.Decide(context);
        GameCommand command = ConvertBotDecision(submission.Seat, request.RequestId, decision);
        SubmitResult result = SubmitCore(command);
        if (!result.Accepted)
        {
            throw new InvalidOperationException($"Bot 对请求 {request.RequestId} 的决策被拒：{result.Reason}");
        }
    }

    /// <summary>按请求种类构造 Bot 上下文：响应窗口给目标弃牌与碰/杠；自己回合给摸到的牌。</summary>
    private BotContext CreateBotContext(PendingRequest request)
    {
        PlayerState player = _state.Players[request.Seat];
        if (_requests.TryGetResponseTarget(request.RequestId, out Tile target))
        {
            return new BotContext(
                _state,
                player,
                new[] { SkillId.Pong, SkillId.Kong },
                _rng,
                targetCard: target,
                lastDrawn: null);
        }

        return request.Kind == RequestKind.Win
            ? new BotContext(
                _state,
                player,
                new[] { SkillId.SelfDrawn, SkillId.SelfDrawnWith7Pairs },
                _rng,
                lastDrawn: _lastDrawn)
            : new BotContext(
                _state,
                player,
                Array.Empty<SkillId>(),
                _rng,
                lastDrawn: _lastDrawn);
    }

    /// <summary>把 Bot 决策转换为命令：技能→动词，出牌→手牌槽位；两者皆空→过，两者皆非空视为 bug。</summary>
    private GameCommand ConvertBotDecision(byte seat, long requestId, BotDecision decision)
    {
        if (decision.Skill is SkillId skill)
        {
            if (decision.Card is not null)
            {
                throw new InvalidOperationException("Bot 决策同时给出技能与出牌，属于 bug。");
            }

            CommandVerb verb = skill switch
            {
                SkillId.Pong => CommandVerb.Pong,
                SkillId.Kong => CommandVerb.Kong,
                SkillId.SelfDrawn or SkillId.SelfDrawnWith7Pairs => CommandVerb.Win,
                _ => throw new InvalidOperationException($"Bot 选择了不支持的技能 {skill}。"),
            };
            return new GameCommand(0, 0, seat, verb, 0, 0, requestId);
        }

        if (decision.Card is Tile card)
        {
            int slot = _state.Players[seat].Hand.IndexOf(card);
            if (slot < 0)
            {
                throw new InvalidOperationException("Bot 选择了不在手中的牌，属于 bug。");
            }

            return new GameCommand(0, 0, seat, CommandVerb.Discard, slot, card.Id, requestId);
        }

        return new GameCommand(0, 0, seat, CommandVerb.Pass, 0, 0, requestId);
    }

    /// <summary>该座位是否由真人操作；<see cref="SessionConfig.HumanSeat"/> 为 null 时四座都算 Bot。</summary>
    private bool IsHumanSeat(byte seat) => _humanSeat == seat;

    /// <summary>配置校验：真人座位/Bot 数组合与 tick/超时下界；ParamName 统一为 <c>config</c>。</summary>
    private static void ValidateConfig(SessionConfig config)
    {
        if (config.HumanSeat is byte humanSeat && humanSeat >= GameState.PlayerCount)
        {
            throw new ArgumentOutOfRangeException("config", humanSeat, "真人座位必须在 0–3。");
        }

        int expectedBotCount = config.HumanSeat is null ? GameState.PlayerCount : GameState.PlayerCount - 1;
        if (config.BotCount != expectedBotCount)
        {
            throw new ArgumentException($"Bot 数必须为 {expectedBotCount}。", "config");
        }

        if (config.TickRate < 1)
        {
            throw new ArgumentOutOfRangeException("config", config.TickRate, "TickRate 至少为 1。");
        }

        if (config.TurnTimeoutTicks < 1)
        {
            throw new ArgumentOutOfRangeException("config", config.TurnTimeoutTicks, "TurnTimeoutTicks 至少为 1。");
        }

        if (config.ResponseTimeoutTicks < 1)
        {
            throw new ArgumentOutOfRangeException("config", config.ResponseTimeoutTicks, "ResponseTimeoutTicks 至少为 1。");
        }
    }

    /// <summary>Bot 应答延迟：max(1, ceil(TickRate × 300 / 1000)) tick。</summary>
    private static int ComputeBotDelayTicks(int tickRate)
    {
        long delay = (((long)tickRate * 300) + 999) / 1000;
        if (delay < 1)
        {
            delay = 1;
        }

        return delay > int.MaxValue ? int.MaxValue : (int)delay;
    }

    /// <summary>鬼牌是否生效：花色落在 1–5 表示有鬼，<c>default(TileType)</c> 的花色 0 表示无鬼。</summary>
    private static bool IsJokerActive(TileType joker) =>
        (byte)joker.Suit >= (byte)SuitType.Character && (byte)joker.Suit <= (byte)SuitType.Dragon;

    /// <summary>拒绝结果：Accepted=false、非空原因、RequestId=0。</summary>
    private static SubmitResult Reject(string reason) => new(false, reason, 0);

    /// <summary>待处理的 Bot 应答：请求 id、到期 tick 与座位。</summary>
    private readonly record struct BotSubmission(long RequestId, long DueTick, byte Seat);
}
