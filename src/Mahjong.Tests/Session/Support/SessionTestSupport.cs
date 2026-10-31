using Mahjong.Core.Content;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Support;

namespace Mahjong.Tests.Session.Support;

/// <summary>
/// T-5 会话流程测试支撑：真实内容/查表只读单例、会话构造快捷方式、局面快照与带脚本的 tick 驱动器。
/// 不持有跨测试可变状态；驱动器一个实例对应一个会话，脚本必须是纯函数（同输入同输出）。
/// </summary>
public static class SessionTestSupport
{
    /// <summary>全景观察座位。</summary>
    public const byte FullViewSeat = 255;

    private static readonly Lazy<ContentCatalog> ContentHolder =
        new(() => ContentCatalog.Load(RepoPaths.ContentDirectory));

    /// <summary>仓库 <c>content/</c> 的只读内容视图；整个测试进程复用。</summary>
    public static ContentCatalog Content => ContentHolder.Value;

    /// <summary>当前内容的鬼牌牌种；无鬼为 <c>default(TileType)</c>。</summary>
    public static TileType Joker =>
        Content.Joker.Mode == JokerMode.FixedType && Content.Joker.Type.HasValue
            ? Content.Joker.Type.GetValueOrDefault()
            : default;

    /// <summary>用真实内容/查表创建四 Bot 会话（不启动）。</summary>
    public static GameSession CreateFourBots(TableCatalog tables, ulong seed, int tickRate = 10) =>
        LocalSessionFactory.CreateFourBots(Content, tables, seed, tickRate);

    /// <summary>用真实内容/查表创建单人会话（0 号真人、1–3 Bot；不启动）。</summary>
    public static GameSession CreateSinglePlayer(TableCatalog tables, ulong seed, int tickRate = 10) =>
        LocalSessionFactory.CreateSinglePlayer(Content, tables, seed, tickRate);

    /// <summary>按配置创建会话（不启动）。</summary>
    public static GameSession Create(SessionConfig config, TableCatalog tables, ulong seed) =>
        LocalSessionFactory.Create(config, Content, tables, seed);

    /// <summary>真实内容下的动作校验器，供测试脚本判断某一动作是否合法。</summary>
    public static CardAndSkillVerifier CreateVerifier(TableCatalog tables) =>
        new(new WinChecker(tables), Joker);

    /// <summary>局面状态哈希（64 字符小写 hex）。</summary>
    public static string Hash(GameSession session) => StateHash.Compute(session.State);

    /// <summary>牌数守恒检查：牌墙 + 四家手牌 + 弃牌 + 副露牌 = 136（发牌后）。</summary>
    public static int CountTiles(GameState state)
    {
        int total = state.Wall.Count;
        foreach (PlayerState player in state.Players)
        {
            total += player.Hand.Count + player.Discards.Count;
            foreach (Meld meld in player.Melds)
            {
                total += meld.Tiles.Count;
            }
        }

        return total;
    }

    /// <summary>创建带脚本的驱动器；<paramref name="humanSeat"/> 的请求由 <paramref name="policy"/> 应答。</summary>
    public static SessionDriver CreateDriver(
        GameSession session,
        TableCatalog tables,
        byte? humanSeat = null,
        Func<SessionDriver, RequestOpenedPayload, SubmitResult?>? policy = null) =>
        new(session, tables, humanSeat, policy);

    /// <summary>构造应答命令：Seq/ReceivedTick 不参与校验，统一填 0。</summary>
    public static GameCommand Command(byte seat, CommandVerb verb, long requestId, int slot = 0, ushort cardId = 0) =>
        new(0, 0, seat, verb, slot, cardId, requestId);

    /// <summary>手牌中第一张通过出牌校验的槽位；没有返回 -1。用于脚本化出牌。</summary>
    public static int FirstLegalDiscardSlot(GameSession session, CardAndSkillVerifier verifier, byte seat)
    {
        PlayerState player = session.State.Players[seat];
        IReadOnlyList<Tile> hand = player.Hand.Items;
        for (int slot = 0; slot < hand.Count; slot++)
        {
            if (verifier.Verify(session.State, player, skill: null, card: hand[slot], targetCard: null))
            {
                return slot;
            }
        }

        return -1;
    }
}

/// <summary>
/// 会话驱动器：按固定 tick 步进、破坏性排空事件、在 RequestOpened 事件上执行脚本策略。
/// 事件按产出顺序累积在 <see cref="Events"/>；脚本策略结果按调用顺序累积在 <see cref="SubmitResults"/>。
/// </summary>
public sealed class SessionDriver
{
    private readonly GameSession _session;
    private readonly CardAndSkillVerifier _verifier;
    private readonly byte? _humanSeat;
    private readonly Func<SessionDriver, RequestOpenedPayload, SubmitResult?>? _policy;
    private readonly List<SessionEvent> _events = [];
    private readonly List<SubmitResult> _submitResults = [];

    private byte _lastDiscardSeat = byte.MaxValue;

    internal SessionDriver(
        GameSession session,
        TableCatalog tables,
        byte? humanSeat,
        Func<SessionDriver, RequestOpenedPayload, SubmitResult?>? policy)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tables);
        _session = session;
        _verifier = SessionTestSupport.CreateVerifier(tables);
        _humanSeat = humanSeat;
        _policy = policy;
    }

    /// <summary>被驱动的会话。</summary>
    public GameSession Session => _session;

    /// <summary>按产出顺序累积的全部事件（自动 Drain 全景视图）。</summary>
    public IReadOnlyList<SessionEvent> Events => _events;

    /// <summary>脚本策略产生的提交结果，按调用顺序。</summary>
    public IReadOnlyList<SubmitResult> SubmitResults => _submitResults;

    /// <summary>最近一次弃牌的座位；尚未出牌为 255。</summary>
    public byte LastDiscardSeat => _lastDiscardSeat;

    /// <summary>会话当前 tick。</summary>
    public long Tick => _session.Tick;

    /// <summary>已步进次数。</summary>
    public int StepCount { get; private set; }

    /// <summary>步进一个 tick（tick 严格递增）并排空/分发事件。</summary>
    public void StepOnce()
    {
        _session.Step(_session.Tick + 1);
        StepCount++;
        Drain();
    }

    /// <summary>步进到满足条件或达到最大步数；返回条件是否满足。</summary>
    public bool RunUntil(Func<bool> condition, int maxSteps = 200_000)
    {
        ArgumentNullException.ThrowIfNull(condition);
        while (!condition())
        {
            if (StepCount >= maxSteps)
            {
                return false;
            }

            StepOnce();
        }

        return true;
    }

    /// <summary>一直步进到整场结束；返回是否结束。</summary>
    public bool RunToGameOver(int maxSteps = 200_000) => RunUntil(() => _session.IsGameOver, maxSteps);

    /// <summary>排空事件并按脚本应答本座位新打开的请求。</summary>
    public void Drain()
    {
        IReadOnlyList<SessionEvent> drained = _session.DrainEvents(SessionTestSupport.FullViewSeat);
        foreach (SessionEvent sessionEvent in drained)
        {
            _events.Add(sessionEvent);
            switch (sessionEvent.Kind)
            {
                case SessionEventKind.RequestOpened:
                    RequestOpenedPayload opened = SessionEventPayloadReader.ReadRequestOpened(sessionEvent);
                    if (_policy is not null && _humanSeat == opened.Seat)
                    {
                        SubmitResult? result = _policy(this, opened);
                        if (result is SubmitResult accepted)
                        {
                            _submitResults.Add(accepted);
                        }
                    }

                    break;

                case SessionEventKind.Discarded:
                    _lastDiscardSeat = SessionEventPayloadReader.ReadDiscarded(sessionEvent).Seat;
                    break;
            }
        }
    }

    /// <summary>最近一次弃牌的牌（来自出牌者弃牌区最后一张）；尚未出牌返回 null。</summary>
    public Tile? LastDiscardTile()
    {
        if (_lastDiscardSeat == byte.MaxValue)
        {
            return null;
        }

        PlayerState player = _session.State.Players[_lastDiscardSeat];
        return player.Discards.Count == 0 ? null : player.Discards[player.Discards.Count - 1];
    }

    /// <summary>当前局面的真实校验器（只读）。</summary>
    public CardAndSkillVerifier Verifier => _verifier;
}
