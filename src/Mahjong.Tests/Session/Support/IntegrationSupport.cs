using System.Text;
using Mahjong.Core.Content;
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

    /// <summary>抢杠的两支位：赢家位与 payer 位。</summary>
    private const ScoreActionType RobbingBits =
        ScoreActionType.RobbingTheKong | ScoreActionType.RobbingTheKongPayForEveryone;

    /// <summary>三种杠位：本局有杠时不做精确金额核算。</summary>
    private const ScoreActionType KongFlags =
        ScoreActionType.ConcealedKong | ScoreActionType.ExposedKong | ScoreActionType.KongSupplement;

    /// <summary>
    /// 每份金额：<c>(有鬼 ? 2 : 4) × (七对 ? 2 : 1) × <paramref name="perMultiplier"/></c>；
    /// 杠上开花的乘数为 2、其余为 1。<paramref name="winnerHasJoker"/> 必须由调用方按赢家手牌显式给出，
    /// 不由账本位反推（抢杠胡不置 <see cref="ScoreActionType.NoJoker"/>，反推会把无鬼抢杠算成有鬼）。
    /// </summary>
    public static int PerShare(bool winnerHasJoker, bool sevenPairs, int perMultiplier = 1) =>
        (winnerHasJoker ? 2 : 4) * (sevenPairs ? 2 : 1) * perMultiplier;

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
    /// 断言一局胡牌结算：四座 delta 净 0、赢家金额为正、流局全 0 且动作聚合 None。按位分流四支：
    /// ①抢杠（<see cref="ScoreActionType.RobbingTheKong"/> 系列，赢家不置 SelfDrawn）；
    /// ②杠上开花承包（<see cref="ScoreActionType.KongUpFlowerPayForEveryone"/>）；
    /// ③12 张落地承包（<see cref="ScoreActionType.TwelveCardsConceded"/>）；
    /// ④常规自摸（历史口径，断言原样保留）。
    /// 无杠位的局按 (鬼牌?2:4) × (七对?2:1) × (杠上开花?2:1) 精确核算三家各付/赢家收三份，
    /// 再叠加结算第 2 步跟庄（赢家 ≠ 庄且变体开启）与第 3 步连庄奖励（赢家 = 庄且表值大于 0），
    /// 承包支只重定向 payer、赢家金额不变；有杠位的局只做零和/符号/位集断言（杠分改变四座分配，
    /// 精确金额由 Scoring 单测与 RobbingKongFlowTests 覆盖）。中马段按 HorseWinner 位加：命中时赢家
    /// +3×horseBonus、其余三家各 −horseBonus（一位规则，位与金额同置）。
    /// 变体金额与被测结算同源取自内容目录，关闭时各项都不加。
    /// </summary>
    /// <param name="round">本局结算 payload。</param>
    /// <param name="started">本局开场的 RoundStarted payload（庄家与连庄次数）。</param>
    /// <param name="variants">内容目录的规则变体。</param>
    /// <param name="winnerHasJoker">
    /// 赢家手牌有无鬼牌；显式给值时优先使用，不做反推。为 <c>null</c> 时：自摸/七对/12 张/杠上开花承包
    /// 四条路径按 <see cref="ScoreActionType.NoJoker"/> 位取值（这四个记账入口按契约置/清该位，位即事实）；
    /// 抢杠路径不置该位，故只要求结算金额落在两个合法基分（无鬼 4 / 有鬼 2，七对再 ×2）之一，
    /// 精确基分由显式传值（或 RobbingKongFlowTests 的 K-01/K-02 与 ContractHandCalcTests）固定。
    /// </param>
    public static void AssertSelfDrawnSettlement(
        RoundEndedPayload round,
        RoundStartedPayload started,
        ContentVariants variants,
        bool? winnerHasJoker = null)
    {
        ArgumentNullException.ThrowIfNull(variants);

        Assert.Equal(GameState.PlayerCount, round.ScoreDelta.Length);
        Assert.Equal(0, round.ScoreDelta.Sum());
        Assert.InRange(round.WinnerSeat, (sbyte)-1, (sbyte)3);

        if (round.WinnerSeat < 0)
        {
            Assert.Equal(ScoreActionType.None, round.ActionType);
            Assert.All(round.ScoreDelta, delta => Assert.Equal(0, delta));
            return;
        }

        int winner = round.WinnerSeat;

        // 抢杠：赢家笔 RobbingTheKong、payer 笔 RobbingTheKongPayForEveryone，均不置 SelfDrawn。
        if ((round.ActionType & RobbingBits) != 0)
        {
            Assert.Equal(RobbingBits, round.ActionType & RobbingBits);
            Assert.Equal(0, (int)(round.ActionType & ScoreActionType.SelfDrawn));
            Assert.True(
                round.ScoreDelta[winner] > 0,
                $"抢杠赢家 {winner} 的结算金额必须为正，实际 {round.ScoreDelta[winner]}。");
            AssertContractPayer(
                round,
                winner,
                started,
                variants,
                contractBit: RobbingBits,
                winnerHasJoker: winnerHasJoker);
            return;
        }

        // 杠上开花承包：赢家仍置 SelfDrawn|KongUpFlower，放明杠者为唯一 payer。
        if ((round.ActionType & ScoreActionType.KongUpFlowerPayForEveryone) != 0)
        {
            Assert.True((round.ActionType & ScoreActionType.SelfDrawn) != 0);
            Assert.True((round.ActionType & ScoreActionType.KongUpFlower) != 0);
            Assert.Equal(0, (int)(round.ActionType & ScoreActionType.TwelveCardsConceded));
            AssertContractPayer(
                round,
                winner,
                started,
                variants,
                contractBit: ScoreActionType.KongUpFlowerPayForEveryone,
                perMultiplier: 2,
                winnerHasJoker: winnerHasJoker);
            return;
        }

        // 12 张落地承包：一位规则，赢家与供牌者同置 TwelveCardsConceded。
        if ((round.ActionType & ScoreActionType.TwelveCardsConceded) != 0)
        {
            Assert.True((round.ActionType & ScoreActionType.SelfDrawn) != 0);
            AssertContractPayer(
                round,
                winner,
                started,
                variants,
                contractBit: ScoreActionType.TwelveCardsConceded,
                perMultiplier: 1,
                winnerHasJoker: winnerHasJoker);
            return;
        }

        Assert.True(
            (round.ActionType & ScoreActionType.SelfDrawn) != 0,
            $"赢家局 {round.WinnerSeat} 必须置位 SelfDrawn，实际 {round.ActionType}。");
        Assert.True(
            round.ScoreDelta[winner] > 0,
            $"赢家 {winner} 的结算金额必须为正，实际 {round.ScoreDelta[winner]}。");

        if ((round.ActionType & KongFlags) != 0)
        {
            return;
        }

        bool sevenPairs = (round.ActionType & ScoreActionType.SevenPairs) != 0;
        bool kongUpFlower = (round.ActionType & ScoreActionType.KongUpFlower) != 0;

        // 显式值优先；自摸路径的 NoJoker 位由 ApplySelfDrawn/ApplySevenPairsSelfDrawn 按契约置/清，可作回退。
        bool hasJoker = winnerHasJoker ?? ((round.ActionType & ScoreActionType.NoJoker) == 0);
        int perSeat = PerShare(hasJoker, sevenPairs, kongUpFlower ? 2 : 1);
        int[] expected = new int[GameState.PlayerCount];
        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            expected[seat] = seat == winner ? perSeat * (GameState.PlayerCount - 1) : -perSeat;
        }

        bool bankerWin = winner == started.BankerSeat;
        Assert.Equal(
            !bankerWin && variants.FollowingBanker > 0,
            (round.ActionType & ScoreActionType.FollowingTheBanker) != 0);
        if (!bankerWin && variants.FollowingBanker > 0)
        {
            expected[winner] += variants.FollowingBanker;
            expected[started.BankerSeat] -= variants.FollowingBanker;
        }

        int bonus = DealerStreakBonus(variants.DealerStreakBonus, started.DealerStreak);
        Assert.Equal(bankerWin && bonus > 0, (round.ActionType & ScoreActionType.DealerStreak) != 0);
        if (bankerWin && bonus > 0)
        {
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
            {
                expected[seat] = seat == started.BankerSeat ? expected[seat] + (3 * bonus) : expected[seat] - bonus;
            }
        }

        // 结算第 4 步中马：马中才置位，置位即赢家 +3×horseBonus、其余三家各 −horseBonus。
        if ((round.ActionType & ScoreActionType.HorseWinner) != 0)
        {
            Assert.True(
                variants.HorseBonus > 0,
                $"变体关闭（horseBonus={variants.HorseBonus}）时不得置 HorseWinner 位。");
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
            {
                expected[seat] = seat == winner
                    ? expected[seat] + (3 * variants.HorseBonus)
                    : expected[seat] - variants.HorseBonus;
            }
        }

        Assert.Equal(expected, round.ScoreDelta);
    }

    /// <summary>
    /// 承包局的 payer 核算：赢家收 3 份（每份 = 基分 × <paramref name="perMultiplier"/>），payer 付 3 份，
    /// 其余两座 0，再按结算第 2/3 步叠加跟庄或连庄、按第 4 步叠加中马
    /// （<see cref="ScoreActionType.HorseWinner"/> 置位时赢家 +3×horseBonus、其余三家各 −horseBonus）。
    /// 有杠位的局只做结构断言（杠分改变四座分配，精确金额由流程组用例覆盖），但位与变体开关仍校验。
    /// payer 未知，因此对三个候选座位各算一次期望，要求恰好一个与实测 delta 相等；
    /// 承包 + 马命中组合因此必须同时满足承包三段与马支付，不能靠提前 return 跳过。
    /// </summary>
    private static void AssertContractPayer(
        RoundEndedPayload round,
        int winner,
        RoundStartedPayload started,
        ContentVariants variants,
        ScoreActionType contractBit,
        int perMultiplier = 1,
        bool? winnerHasJoker = null)
    {
        Assert.NotEqual(0, (int)(round.ActionType & contractBit));

        // 位与开关一致：马中才置位，置位时 horseBonus 必须为正（关闭时置位即违约）。
        bool horseHit = (round.ActionType & ScoreActionType.HorseWinner) != 0;
        if (horseHit)
        {
            Assert.True(
                variants.HorseBonus > 0,
                $"变体关闭（horseBonus={variants.HorseBonus}）时不得置 HorseWinner 位。");
        }

        if ((round.ActionType & KongFlags) != 0)
        {
            return;
        }

        bool sevenPairs = (round.ActionType & ScoreActionType.SevenPairs) != 0;
        int[] actual = round.ScoreDelta;

        int matches = 0;
        foreach (int per in CandidateShares(round, sevenPairs, perMultiplier, winnerHasJoker))
        {
            for (byte payer = 0; payer < GameState.PlayerCount; payer++)
            {
                if (payer == winner)
                {
                    continue;
                }

                int[] expected = new int[GameState.PlayerCount];
                expected[winner] = per * 3;
                expected[payer] = -per * 3;
                if (winner != started.BankerSeat && variants.FollowingBanker > 0)
                {
                    expected[winner] += variants.FollowingBanker;
                    expected[started.BankerSeat] -= variants.FollowingBanker;
                }
                else if (winner == started.BankerSeat)
                {
                    int bonus = DealerStreakBonus(variants.DealerStreakBonus, started.DealerStreak);
                    for (byte seat = 0; bonus > 0 && seat < GameState.PlayerCount; seat++)
                    {
                        expected[seat] = seat == started.BankerSeat ? expected[seat] + (3 * bonus) : expected[seat] - bonus;
                    }
                }

                if (horseHit)
                {
                    for (byte seat = 0; seat < GameState.PlayerCount; seat++)
                    {
                        expected[seat] = seat == winner
                            ? expected[seat] + (3 * variants.HorseBonus)
                            : expected[seat] - variants.HorseBonus;
                    }
                }

                if (expected.SequenceEqual(actual))
                {
                    matches++;
                }
            }
        }

        Assert.Equal(1, matches);
    }

    /// <summary>
    /// 承包支允许的每份金额候选：显式有无鬼时只有一个；抢杠分支缺显式值时两种基分都合法
    /// （该路径不置 <see cref="ScoreActionType.NoJoker"/>，事件流无法区分有无鬼，不得反推）；
    /// 其余分支按 NoJoker 位取（记账入口按契约置/清该位）。
    /// </summary>
    private static IReadOnlyList<int> CandidateShares(
        RoundEndedPayload round,
        bool sevenPairs,
        int perMultiplier,
        bool? winnerHasJoker)
    {
        if (winnerHasJoker is bool explicitJoker)
        {
            return [PerShare(explicitJoker, sevenPairs, perMultiplier)];
        }

        if ((round.ActionType & RobbingBits) != 0)
        {
            return
            [
                PerShare(winnerHasJoker: true, sevenPairs, perMultiplier),
                PerShare(winnerHasJoker: false, sevenPairs, perMultiplier),
            ];
        }

        return [PerShare((round.ActionType & ScoreActionType.NoJoker) == 0, sevenPairs, perMultiplier)];
    }

    /// <summary>连庄奖励表取值：<c>table[min(streak, 表长) - 1]</c>；空表（变体关闭）为 0。</summary>
    private static int DealerStreakBonus(IReadOnlyList<int> table, byte dealerStreak)
    {
        if (table.Count == 0)
        {
            return 0;
        }

        int index = Math.Min(dealerStreak, table.Count) - 1;
        return index < 0 ? 0 : table[index];
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
