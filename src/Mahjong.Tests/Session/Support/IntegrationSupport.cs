using System.Text;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Xunit;

namespace Mahjong.Tests.Session.Support;

/// <summary>
/// T-5 会话集成测试支撑：可配置观察座位与脚本策略的整场驱动器、整场运行快照与事件流文本化。
/// 复用 <see cref="SessionTestSupport"/> 的内容/查表/哈希/牌数工具，不改动其实现；
/// 一个驱动器实例只驱动一个会话，脚本策略必须是纯函数（同输入同输出），驱动器本身不持有跨会话可变状态。
/// </summary>
public static class IntegrationSupport
{
    /// <summary>全景观察座位：事件编码不做可见性过滤。</summary>
    public const byte FullViewSeat = 255;

    /// <summary>完整 8 局流程的默认最大步数；超过即视为流程未收敛。</summary>
    public const int DefaultMaxSteps = 200_000;

    /// <summary>物理牌总数；牌数守恒的目标值。</summary>
    public const int TotalTileCount = 136;

    /// <summary>构造应答命令：Seq/ReceivedTick 不参与校验，统一填 0。</summary>
    public static GameCommand Command(byte seat, CommandVerb verb, long requestId, int slot = 0, ushort cardId = 0) =>
        new(0, 0, seat, verb, slot, cardId, requestId);

    /// <summary>
    /// 脚本化真人策略：Win 请求一律胡；Discard 请求打第一张通过校验的牌且从不杠；
    /// 响应窗口（Pong 请求）一律过。对局面是纯函数，可直接用于确定性比对。
    /// </summary>
    /// <exception cref="InvalidOperationException">Discard 请求时手牌没有任何合法出牌（规则层 bug）。</exception>
    public static SubmitResult? ScriptedHumanAnswer(IntegrationDriver driver, RequestOpenedPayload request)
    {
        ArgumentNullException.ThrowIfNull(driver);

        GameSession session = driver.Session;
        switch (request.Kind)
        {
            case RequestKind.Win:
                return session.Submit(Command(request.Seat, CommandVerb.Win, request.RequestId));

            case RequestKind.Discard:
            {
                int slot = SessionTestSupport.FirstLegalDiscardSlot(session, driver.Verifier, request.Seat);
                if (slot < 0)
                {
                    throw new InvalidOperationException($"座位 {request.Seat} 没有可自动打出的合法牌，脚本无法继续。");
                }

                ushort cardId = session.State.Players[request.Seat].Hand[slot].Id;
                return session.Submit(Command(request.Seat, CommandVerb.Discard, request.RequestId, slot, cardId));
            }

            default:
                return session.Submit(Command(request.Seat, CommandVerb.Pass, request.RequestId));
        }
    }

    /// <summary>用真实内容/查表跑完一整场四 Bot 对局；返回快照（未结束时 <see cref="IntegrationRun.GameOver"/> 为 false）。</summary>
    public static IntegrationRun RunFourBots(
        TableCatalog tables,
        ulong seed,
        byte viewerSeat = FullViewSeat,
        int tickRate = 10)
    {
        GameSession session = SessionTestSupport.CreateFourBots(tables, seed, tickRate);
        IntegrationDriver driver = new(session, SessionTestSupport.CreateVerifier(tables), viewerSeat);
        driver.Start();
        driver.RunToGameOver();
        return driver.ToRun();
    }

    /// <summary>用真实内容/查表跑完一整场脚本化单人（0 号真人、1–3 Bot）对局；返回快照。</summary>
    public static IntegrationRun RunScriptedSinglePlayer(
        TableCatalog tables,
        ulong seed,
        byte viewerSeat = FullViewSeat,
        int tickRate = 10)
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(tables, seed, tickRate);
        IntegrationDriver driver = new(
            session,
            SessionTestSupport.CreateVerifier(tables),
            viewerSeat,
            humanSeat: 0,
            policy: ScriptedHumanAnswer);
        driver.Start();
        driver.RunToGameOver();
        return driver.ToRun();
    }

    /// <summary>把事件流序列化为逐事件文本：<c>Seq|Tick|Kind|Payload hex</c>，每事件一行。</summary>
    public static string SerializeEventStream(IEnumerable<SessionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        StringBuilder builder = new();
        foreach (SessionEvent sessionEvent in events)
        {
            builder
                .Append(sessionEvent.Seq).Append('|')
                .Append(sessionEvent.Tick).Append('|')
                .Append((byte)sessionEvent.Kind).Append('|')
                .Append(Convert.ToHexString(sessionEvent.Payload.Span))
                .Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>从 <see cref="PlayerState.Score"/> 镜像读取四座总分。</summary>
    public static short[] MirrorScores(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        short[] scores = new short[GameState.PlayerCount];
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            scores[seat] = session.State.Players[seat].Score;
        }

        return scores;
    }

    /// <summary>累加事件流中每条 RoundEnded 的 ScoreDelta，得到逐局增量累计的总分。</summary>
    public static int[] AccumulateRoundDeltas(IEnumerable<SessionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        int[] totals = new int[GameState.PlayerCount];
        foreach (SessionEvent sessionEvent in events)
        {
            if (sessionEvent.Kind != SessionEventKind.RoundEnded)
            {
                continue;
            }

            RoundEndedPayload payload = SessionEventPayloadReader.ReadRoundEnded(sessionEvent);
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
            {
                totals[seat] += payload.ScoreDelta[seat];
            }
        }

        return totals;
    }

    /// <summary>按事件顺序读取每局 RoundStarted 的庄家座位（长度 = 局数）。</summary>
    public static byte[] RoundBankers(IEnumerable<SessionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted)
            .Select(sessionEvent => SessionEventPayloadReader.ReadRoundStarted(sessionEvent).BankerSeat)
            .ToArray();
    }

    /// <summary>按事件顺序读取每局 RoundEnded 的赢家座位（-1 表示流局；长度 = 局数）。</summary>
    public static sbyte[] RoundWinners(IEnumerable<SessionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)
            .Select(sessionEvent => SessionEventPayloadReader.ReadRoundEnded(sessionEvent).WinnerSeat)
            .ToArray();
    }

    /// <summary>
    /// 断言换庄口径：首局庄家任意；之后赢家坐庄（连庄即赢家为原庄），流局庄家不变。
    /// </summary>
    public static void AssertBankerRotation(IReadOnlyList<byte> bankers, IReadOnlyList<sbyte> winners)
    {
        ArgumentNullException.ThrowIfNull(bankers);
        ArgumentNullException.ThrowIfNull(winners);

        Assert.Equal(bankers.Count, winners.Count);
        Assert.NotEmpty(bankers);
        Assert.InRange(bankers[0], (byte)0, (byte)3);

        for (int round = 1; round < bankers.Count; round++)
        {
            sbyte previousWinner = winners[round - 1];
            byte expected = previousWinner >= 0 ? (byte)previousWinner : bankers[round - 1];
            Assert.Equal(expected, bankers[round]);
        }
    }

    /// <summary>
    /// 断言一局自摸结算：四座 delta 净 0；赢家局置位 SelfDrawn 且赢家金额为正；流局全 0 且动作聚合 None。
    /// 该局没有任何杠位时按 (鬼牌?2:4) × (七对?2:1) × (杠上开花?2:1) 精确核算：三家各付一份、赢家收三份；
    /// 有杠位的局只做零和/符号断言（杠分改变四座分配，精确金额由 Scoring 单测覆盖）。
    /// </summary>
    public static void AssertSelfDrawnSettlement(RoundEndedPayload round)
    {
        Assert.Equal(GameState.PlayerCount, round.ScoreDelta.Length);
        Assert.Equal(0, round.ScoreDelta.Sum());
        Assert.InRange(round.WinnerSeat, (sbyte)-1, (sbyte)3);

        if (round.WinnerSeat < 0)
        {
            Assert.Equal(ScoreActionType.None, round.ActionType);
            Assert.All(round.ScoreDelta, delta => Assert.Equal(0, delta));
            return;
        }

        Assert.True(
            (round.ActionType & ScoreActionType.SelfDrawn) != 0,
            $"赢家局 {round.WinnerSeat} 必须置位 SelfDrawn，实际 {round.ActionType}。");

        int winner = round.WinnerSeat;
        Assert.True(
            round.ScoreDelta[winner] > 0,
            $"赢家 {winner} 的结算金额必须为正，实际 {round.ScoreDelta[winner]}。");

        const ScoreActionType KongFlags =
            ScoreActionType.ConcealedKong | ScoreActionType.ExposedKong | ScoreActionType.KongSupplement;
        if ((round.ActionType & KongFlags) != 0)
        {
            return;
        }

        bool sevenPairs = (round.ActionType & ScoreActionType.SevenPairs) != 0;
        bool hasJoker = (round.ActionType & ScoreActionType.NoJoker) == 0;
        bool kongUpFlower = (round.ActionType & ScoreActionType.KongUpFlower) != 0;
        int perSeat = (hasJoker ? 2 : 4) * (sevenPairs ? 2 : 1) * (kongUpFlower ? 2 : 1);
        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            int expected = seat == winner ? perSeat * (GameState.PlayerCount - 1) : -perSeat;
            Assert.Equal(expected, round.ScoreDelta[seat]);
        }
    }

    /// <summary>断言事件流序号从 1 开始、逐事件严格 +1（Drain 不重复、不丢号）。</summary>
    public static void AssertContiguousSeq(IReadOnlyList<SessionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        Assert.NotEmpty(events);
        for (int index = 0; index < events.Count; index++)
        {
            Assert.Equal(index + 1L, events[index].Seq);
        }
    }
}

/// <summary>
/// 整场运行快照：终态会话、按产出顺序的全部事件、脚本提交结果、每步牌数。
/// 快照中的列表是只读副本，之后继续驱动原会话不会改变已取快照。
/// </summary>
public sealed class IntegrationRun
{
    internal IntegrationRun(
        GameSession session,
        IReadOnlyList<SessionEvent> events,
        IReadOnlyList<SubmitResult> submitResults,
        IReadOnlyList<int> tileCounts,
        int stepCount)
    {
        Session = session;
        Events = events;
        SubmitResults = submitResults;
        TileCounts = tileCounts;
        StepCount = stepCount;
    }

    /// <summary>整场结束时的会话（终态，可继续 Step 只推进时钟）。</summary>
    public GameSession Session { get; }

    /// <summary>按产出顺序累积的全部事件。</summary>
    public IReadOnlyList<SessionEvent> Events { get; }

    /// <summary>脚本策略产生的提交结果，按调用顺序。</summary>
    public IReadOnlyList<SubmitResult> SubmitResults { get; }

    /// <summary>每步 Step 后的牌数（墙 + 手牌 + 弃牌 + 副露）。</summary>
    public IReadOnlyList<int> TileCounts { get; }

    /// <summary>实际步进次数。</summary>
    public int StepCount { get; }

    /// <summary>会话是否已产出 GameEnded。</summary>
    public bool GameOver => Session.IsGameOver;

    /// <summary>终局局面哈希（64 字符小写 hex）。</summary>
    public string FinalStateHash => SessionTestSupport.Hash(Session);

    /// <summary>终局四座总分镜像。</summary>
    public short[] MirrorScores => IntegrationSupport.MirrorScores(Session);

    /// <summary>事件流文本（Seq|Tick|Kind|Payload hex，逐事件一行）。</summary>
    public string SerializedEvents => IntegrationSupport.SerializeEventStream(Events);
}

/// <summary>
/// 整场驱动器：按固定 tick 步进、以指定观众座位破坏性排空事件、在 RequestOpened 上执行脚本策略。
/// 事件按产出顺序累积在 <see cref="Events"/>，脚本提交结果按调用顺序累积在 <see cref="SubmitResults"/>；
/// 每步之后记录牌数，用于逐步牌数守恒断言。
/// </summary>
public sealed class IntegrationDriver
{
    private readonly GameSession _session;
    private readonly CardAndSkillVerifier _verifier;
    private readonly byte _viewerSeat;
    private readonly byte? _humanSeat;
    private readonly Func<IntegrationDriver, RequestOpenedPayload, SubmitResult?>? _policy;
    private readonly List<SessionEvent> _events = [];
    private readonly List<SubmitResult> _submitResults = [];
    private readonly List<int> _tileCounts = [];

    /// <summary>创建驱动器；<paramref name="policy"/> 只对 <paramref name="humanSeat"/> 的请求执行（为 null 时不限座位）。</summary>
    public IntegrationDriver(
        GameSession session,
        CardAndSkillVerifier verifier,
        byte viewerSeat = IntegrationSupport.FullViewSeat,
        byte? humanSeat = null,
        Func<IntegrationDriver, RequestOpenedPayload, SubmitResult?>? policy = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(verifier);

        _session = session;
        _verifier = verifier;
        _viewerSeat = viewerSeat;
        _humanSeat = humanSeat;
        _policy = policy;
    }

    /// <summary>被驱动的会话。</summary>
    public GameSession Session => _session;

    /// <summary>真实内容下的动作校验器（只读）。</summary>
    public CardAndSkillVerifier Verifier => _verifier;

    /// <summary>按产出顺序累积的全部事件。</summary>
    public IReadOnlyList<SessionEvent> Events => _events;

    /// <summary>脚本策略产生的提交结果，按调用顺序。</summary>
    public IReadOnlyList<SubmitResult> SubmitResults => _submitResults;

    /// <summary>每步 Step 后的牌数。</summary>
    public IReadOnlyList<int> TileCounts => _tileCounts;

    /// <summary>已步进次数。</summary>
    public int StepCount { get; private set; }

    /// <summary>会话当前 tick。</summary>
    public long Tick => _session.Tick;

    /// <summary>启动会话并排空启动事件（四个座位状态）。</summary>
    public void Start()
    {
        _session.Start();
        Drain();
    }

    /// <summary>步进一个 tick（tick 严格递增）、记录牌数并排空/分发事件。</summary>
    public void StepOnce()
    {
        _session.Step(_session.Tick + 1);
        StepCount++;
        _tileCounts.Add(SessionTestSupport.CountTiles(_session.State));
        Drain();
    }

    /// <summary>步进到整场结束或达到最大步数；返回是否结束。</summary>
    public bool RunToGameOver(int maxSteps = IntegrationSupport.DefaultMaxSteps)
    {
        while (!_session.IsGameOver)
        {
            if (StepCount >= maxSteps)
            {
                return false;
            }

            StepOnce();
        }

        return true;
    }

    /// <summary>排空当前缓冲区：记录事件，并对匹配脚本座位的 RequestOpened 执行策略。</summary>
    public void Drain()
    {
        IReadOnlyList<SessionEvent> drained = _session.DrainEvents(_viewerSeat);
        foreach (SessionEvent sessionEvent in drained)
        {
            _events.Add(sessionEvent);
            if (_policy is null || sessionEvent.Kind != SessionEventKind.RequestOpened)
            {
                continue;
            }

            RequestOpenedPayload opened = SessionEventPayloadReader.ReadRequestOpened(sessionEvent);
            if (_humanSeat is byte humanSeat && humanSeat != opened.Seat)
            {
                continue;
            }

            SubmitResult? result = _policy(this, opened);
            if (result is SubmitResult submitResult)
            {
                _submitResults.Add(submitResult);
            }
        }
    }

    /// <summary>把当前累积状态复制为不可变快照。</summary>
    public IntegrationRun ToRun() => new(
        _session,
        Array.AsReadOnly(_events.ToArray()),
        Array.AsReadOnly(_submitResults.ToArray()),
        Array.AsReadOnly(_tileCounts.ToArray()),
        StepCount);
}
