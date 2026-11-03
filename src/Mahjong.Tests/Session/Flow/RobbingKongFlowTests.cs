using Mahjong.Core.Content;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Flow;

/// <summary>
/// T-3 §5 手算例 K-01…K-15 的会话侧断言（账本侧在 <c>ContractScoreTests</c>）：
/// 抢杠窗口的候选顺序/窗口参数/Cancelled/TimedOut/一次性消费、抢杠成立后的结算与事件序、
/// 补杠取消路径（无 Meld/杠分/Drew）、杠上开花承包、12 张落地承包、单链优先级与连庄叠加。
/// 全部用真实内容/查表 + 受控 tick 驱动；局面在请求打开时按用例合成（手牌/副露/牌墙头尾），
/// 因此不依赖随机发牌的取样运气。K-08 已扩到中马段（马座位、HorseRevealed 事件序、位集与马金额），
/// 另有一条非 K 编号补充用例钉住「12 张承包 × 马中赢家」的精确四座 delta（D-33/D-35）。
/// </summary>
[Collection("table-catalog")]
public sealed class RobbingKongFlowTests
{
    /// <summary>抢杠组两支（赢家位 + payer 位）。</summary>
    private const ScoreActionType RobbingBits =
        ScoreActionType.RobbingTheKong | ScoreActionType.RobbingTheKongPayForEveryone;

    private readonly TableCatalogFixture _fixture;

    public RobbingKongFlowTests(TableCatalogFixture fixture) => _fixture = fixture;

    // ------------------------------------------------------------------ K-01

    /// <summary>
    /// K-01 抢杠成功（无鬼 base=4，赢家 = 庄家）：候选升序开窗 → 应答 Win → 关窗 → 结算。
    /// 同时钉住 registry 契约：空候选不开窗、连续 id、Win|Pass、deadline = 开窗 + 150（用例内取实际超时）、
    /// 落败 Win 记 Cancelled、未应答记 TimedOut、结果一次性消费、与弃牌响应窗口隔离。
    /// </summary>
    [Fact]
    public void K01_RobbingKong_SucceedsAndKeepsTheKongCancelled()
    {
        AssertRobbingKongRegistryContract();

        FlowHarness harness = Start(seed: 7);
        harness.Step();
        byte banker = harness.Banker;
        byte kongSeat = Next(banker, 2);
        byte winner = banker;
        ushort x = 5;
        ushort robbedType = 0;
        int robbedPongTiles = 0;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(kongSeat))
            {
                PrepareSupplementKong(harness, kongSeat, x, new[] { winner }, winnerMeldCount: 0, winnerHasJoker: false, actingSeat: request.Seat);
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                if (!TryDeclareKong(harness, kongSeat, x))
                {
                    return AnswerDefault(harness, request);
                }

                return harness.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.RobbingKong)
            {
                // 关窗前快照：第 4 张在被抢者手牌末尾（补杠预判槽位），副露仍是碰（未升级）。
                PlayerState robbed = harness.Session.State.Players[kongSeat];
                robbedType = robbed.Hand[robbed.Hand.Count - 1].Type.Id;
                robbedPongTiles = Assert.Single(robbed.Melds).Tiles.Count;
                Assert.Equal(MeldKind.Pong, Assert.Single(robbed.Melds).Kind);
                return harness.Submit(request.Seat, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        // 窗口：一条，候选 = 赢家，Kind=5，Allowed=Win|Pass，deadline = 开窗 tick + 150。
        (int WindowIndex, RequestOpenedPayload Window) = Assert.Single(RobbingWindows(harness));
        Assert.Equal(winner, Window.Seat);
        Assert.Equal(AllowedVerbs.Win | AllowedVerbs.Pass, Window.Allowed);
        Assert.Equal(harness.Events[WindowIndex].Tick + 150, Window.DeadlineTick);

        // 结算：Answered(Win) 之后立刻 RoundEnded；期间不产 Meld/Drew（补杠取消、不补牌）。
        int closedIndex = IndexOfClosed(harness, Window.RequestId);
        Assert.True(closedIndex > WindowIndex);
        RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(harness.Events[closedIndex]);
        Assert.Equal(RequestOutcomeKind.Answered, closed.Outcome);
        Assert.Equal(CommandVerb.Win, closed.Answer!.Value.Verb);
        int endedIndex = IndexOfRoundEnded(harness);
        Assert.True(closedIndex < endedIndex);
        AssertNoMeldOrDrew(harness, closedIndex, endedIndex);

        // 第 4 张已由结算移入赢家手牌（D-12）：结算与下一局发牌同 tick 完成，事后读不到局面，
        // 故「关窗前第 4 张在被抢者手牌末尾 + 副露仍是碰」与「关窗后无 Meld/Drew」两处一起钉住移牌语义。
        Assert.Equal(x, robbedType);
        Assert.Equal(3, robbedPongTiles);

        // 账与位：无鬼 base=4 → 赢家 +12、被抢者 −12；赢家 = 庄、streak=1 → 无跟庄、无连庄位。
        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(winner, (byte)round.WinnerSeat);
        Assert.Equal(RobbingBits, round.ActionType);
        Assert.Equal(new[] { 12, 0, -12, 0 }, Offset(round.ScoreDelta, banker, winner, kongSeat));
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ K-02

    /// <summary>K-02 抢杠成功（有鬼 base=2）：赢家手牌含鬼牌，每份 2 → 赢家 +6、被抢者 −6；不置 NoJoker。</summary>
    [Fact]
    public void K02_RobbingKong_WithJoker_UsesBaseTwo()
    {
        FlowHarness harness = Start(seed: 11);
        harness.Step();
        byte banker = harness.Banker;
        byte kongSeat = Next(banker, 2);
        byte winner = banker;
        ushort x = 14;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(kongSeat))
            {
                PrepareSupplementKong(harness, kongSeat, x, new[] { winner }, winnerMeldCount: 0, winnerHasJoker: true, actingSeat: request.Seat);
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                if (!TryDeclareKong(harness, kongSeat, x))
                {
                    return AnswerDefault(harness, request);
                }

                return harness.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.RobbingKong)
            {
                return harness.Submit(request.Seat, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(winner, (byte)round.WinnerSeat);
        Assert.Equal(RobbingBits, round.ActionType);
        Assert.Equal(0, (int)(round.ActionType & ScoreActionType.NoJoker));
        Assert.Equal(new[] { 6, 0, -6, 0 }, Offset(round.ScoreDelta, banker, winner, kongSeat));
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ K-03

    /// <summary>
    /// K-03 七对抢杠：流程上不可构造——补杠第 4 张与被升级的碰占满同牌种 4 张，七对要求第 5 张，
    /// 而 <see cref="IWinChecker.IsSevenPairs"/> 对同牌种超过 4 张直接判否。
    /// 因此本用例钉住「候选为空则不开抢杠窗、补杠照常成立」，七对抢杠的账本分支由
    /// <c>ContractScoreTests.K03_SevenPairsRobbingKong_AddsSevenPairsBit</c> 覆盖。
    /// </summary>
    [Fact]
    public void K03_SevenPairsRobbingKong_IsRejectedByTheFourCopyLimit_AndTheKongProceeds()
    {
        FlowHarness harness = Start(seed: 3);
        harness.Step();
        byte banker = harness.Banker;
        byte kongSeat = Next(banker, 2);
        byte fakeCandidate = Next(banker, 1);
        ushort x = 5;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(kongSeat))
            {
                PrepareSupplementKong(
                    harness,
                    kongSeat,
                    x,
                    Array.Empty<byte>(),
                    winnerMeldCount: 0,
                    winnerHasJoker: false,
                    actingSeat: request.Seat);
                // 出牌座位就是伪候选：先补一张可打出的散牌凑满 14 张，打完这一张手牌正好 13 张，
                // 仍是「同牌种 4 张 + 4 对 + 1 散张」的七对型手牌（散牌必须不是鬼牌，鬼牌不可打出）。
                PlayerState seat = harness.Session.State.Players[fakeCandidate];
                ReplaceHand(seat, SevenPairsLikeHand(x).Append(MakeTile(30, 0)).ToArray());
                return harness.Submit(fakeCandidate, CommandVerb.Discard, request.RequestId, slot: seat.Hand.Count - 1);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                // 伪候选此时恰好 13 张：再加第 4 张即同牌种 5 张，七对与常规胡都必须判否（不开窗）。
                PlayerState fakeSeat = harness.Session.State.Players[fakeCandidate];
                Assert.Equal(13, fakeSeat.Hand.Count);
                Assert.False(
                    IsWinningHand(fakeSeat, MakeTile(x, 0)),
                    "同牌种第 5 张的七对型手牌必须被判否。");

                if (!TryDeclareKong(harness, kongSeat, x))
                {
                    return AnswerDefault(harness, request);
                }

                return harness.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent =>
            sessionEvent.Kind == SessionEventKind.Meld
            && SessionEventPayloadReader.ReadMeld(sessionEvent).Kind == MeldKind.KongSupplement)));

        Assert.Empty(RobbingWindows(harness));
        int meldIndex = harness.Events.FindIndex(sessionEvent =>
            sessionEvent.Kind == SessionEventKind.Meld
            && SessionEventPayloadReader.ReadMeld(sessionEvent).Kind == MeldKind.KongSupplement);
        Assert.True(meldIndex >= 0);
        Assert.Contains(
            harness.Events.Skip(meldIndex).TakeWhile(sessionEvent => sessionEvent.Tick == harness.Events[meldIndex].Tick),
            sessionEvent => sessionEvent.Kind == SessionEventKind.Drew
                && SessionEventPayloadReader.ReadDrew(sessionEvent).Seat == kongSeat);
    }

    // ------------------------------------------------------------------ K-04

    /// <summary>
    /// K-04 抢杠成立不重复收补杠杠分，且同局明杠照收：赢家先响应庄家弃牌明杠（+3/−3），
    /// 随后抢隔壁座位的补杠胡；账 = 明杠 + 抢杠 3×4 + 跟庄，位 = ExposedKong|RobbingTheKong 系列|FollowingTheBanker。
    /// </summary>
    [Fact]
    public void K04_RobbingKong_KeepsTheEarlierExposedKongAndStacksFollowingTheBanker()
    {
        FlowHarness harness = Start(seed: 5);
        harness.Step();
        byte banker = harness.Banker;
        byte winner = Next(banker, 1);
        byte kongSeat = Next(banker, 2);
        ushort exposedType = 0;
        ushort x = 20;
        bool exposedKong = false;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == banker && !exposedKong)
            {
                // 庄家第一打：把明杠材料放在手牌末位，让赢家能响应明杠。
                PlayerState seat = harness.Session.State.Players[banker];
                ReplaceHand(seat, JunkHand(seat.Hand.Count - 1));
                seat.Hand.Append(MakeTile(exposedType, 3));
                ReplaceHand(harness.Session.State.Players[winner], ExposedKongHand(exposedType, x, withJoker: false));
                return harness.Submit(banker, CommandVerb.Discard, request.RequestId, slot: seat.Hand.Count - 1);
            }

            if (request.Kind == RequestKind.Pong && request.Seat == winner && !exposedKong)
            {
                exposedKong = true;

                // 明杠补牌取墙尾：换成风牌，保证补牌本身不成胡（本用例要的是随后的抢杠胡）。
                MoveTileToWallTail(harness.Session.State, 27);
                return harness.Submit(winner, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == winner)
            {
                // 赢家这次出牌正好是抢杠家的上家：先就位抢杠局面（牌墙头换成抢的那张），再打出刚补的牌。
                PrepareSupplementKong(
                    harness,
                    kongSeat,
                    x,
                    new[] { winner },
                    winnerMeldCount: 1,
                    winnerHasJoker: false,
                    actingSeat: winner);
                return harness.Submit(
                    winner,
                    CommandVerb.Discard,
                    request.RequestId,
                    slot: harness.Session.State.Players[winner].Hand.Count - 1);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                if (!TryDeclareKong(harness, kongSeat, x))
                {
                    return AnswerDefault(harness, request);
                }

                return harness.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.RobbingKong)
            {
                return harness.Submit(request.Seat, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(winner, (byte)round.WinnerSeat);
        Assert.Equal(
            RobbingBits | ScoreActionType.ExposedKong | ScoreActionType.FollowingTheBanker,
            round.ActionType);

        int[] expected = new int[GameState.PlayerCount];
        expected[winner] += 12;
        expected[kongSeat] -= 12;
        expected[winner] += 3;
        expected[banker] -= 3;
        expected[winner] += 1;
        expected[banker] -= 1;
        Assert.Equal(expected, round.ScoreDelta);
        Assert.Equal(0, round.ScoreDelta.Sum());
        Assert.Equal(1, harness.Events.Count(sessionEvent =>
            sessionEvent.Kind == SessionEventKind.Meld
            && SessionEventPayloadReader.ReadMeld(sessionEvent).Kind == MeldKind.KongExposed));
        Assert.DoesNotContain(harness.Events, sessionEvent =>
            sessionEvent.Kind == SessionEventKind.Meld
            && SessionEventPayloadReader.ReadMeld(sessionEvent).Kind == MeldKind.KongSupplement);
    }

    // ------------------------------------------------------------------ K-05

    /// <summary>K-05 全员放弃 → 补杠成立 + 杠上开花自摸（无鬼，赢家 = 庄家）：[+27, −9, −9, −9]，补杠补牌不承包。</summary>
    [Fact]
    public void K05_RobbingKongDeclined_SupplementKongAndKongUpFlowerSelfDrawn()
    {
        FlowHarness harness = Start(seed: 9);
        harness.Step();
        byte banker = harness.Banker;
        byte kongSeat = banker;
        byte candidate = Next(banker, 1);
        ushort x = 5;
        ushort z = 14;
        bool passSubmitted = false;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(kongSeat))
            {
                PrepareSupplementKong(
                    harness,
                    kongSeat,
                    x,
                    new[] { candidate },
                    winnerMeldCount: 0,
                    winnerHasJoker: false,
                    actingSeat: request.Seat,
                    kongSupplementWinType: z);
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                if (!TryDeclareKong(harness, kongSeat, x))
                {
                    return AnswerDefault(harness, request);
                }

                return harness.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.RobbingKong)
            {
                passSubmitted = true;
                return harness.Submit(request.Seat, CommandVerb.Pass, request.RequestId);
            }

            if (request.Kind == RequestKind.Win && request.Seat == kongSeat)
            {
                return harness.Submit(kongSeat, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));
        Assert.True(passSubmitted);

        // 事件序：RequestClosed(Answered) < Meld(补杠) < Drew(补牌)。
        int closedIndex = IndexOfRobbingClosed(harness);
        int meldIndex = harness.Events.FindIndex(sessionEvent =>
            sessionEvent.Kind == SessionEventKind.Meld
            && SessionEventPayloadReader.ReadMeld(sessionEvent) is { Kind: MeldKind.KongSupplement, Seat: var meldSeat }
            && meldSeat == kongSeat);
        int drewIndex = harness.Events.FindIndex(meldIndex + 1, sessionEvent =>
            sessionEvent.Kind == SessionEventKind.Drew
            && SessionEventPayloadReader.ReadDrew(sessionEvent).Seat == kongSeat);
        Assert.True(closedIndex >= 0 && meldIndex > closedIndex && drewIndex > meldIndex);

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(
            ScoreActionType.KongSupplement | ScoreActionType.SelfDrawn
            | ScoreActionType.KongUpFlower | ScoreActionType.NoJoker,
            round.ActionType);
        int[] expected = new int[GameState.PlayerCount];
        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            expected[seat] = seat == kongSeat ? 3 : -1;
        }

        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            expected[seat] += seat == kongSeat ? 24 : -8;
        }

        Assert.Equal(expected, round.ScoreDelta);
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ K-06

    /// <summary>K-06 候选超时（TimedOut）按放弃 → 补杠成立 + 杠上开花自摸（有鬼）：[+15, −5, −5, −5]，不置 NoJoker。</summary>
    [Fact]
    public void K06_RobbingKongTimedOut_TreatedAsDecline_AndKongUpFlowerWithJoker()
    {
        // 候选座位取真人座位：Bot 座位会在固定延迟后自行应答，构造不出 TimedOut。
        FlowHarness probe = Start(seed: 13);
        probe.Step();
        byte probeBanker = probe.Banker;
        FlowHarness harness = Start(seed: 13, humanSeat: Next(probeBanker, 2));
        harness.Step();
        byte banker = harness.Banker;
        byte kongSeat = banker;
        byte candidate = Next(banker, 2);
        ushort x = 5;
        ushort z = 14;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(kongSeat))
            {
                PrepareSupplementKong(
                    harness,
                    kongSeat,
                    x,
                    new[] { candidate },
                    winnerMeldCount: 0,
                    winnerHasJoker: false,
                    actingSeat: request.Seat,
                    kongSupplementWinType: z,
                    kongHasJoker: true);
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                if (!TryDeclareKong(harness, kongSeat, x))
                {
                    return AnswerDefault(harness, request);
                }

                return harness.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.RobbingKong)
            {
                // 不应答：到 deadline 由 BeginTick 记 TimedOut。
                return null;
            }

            if (request.Kind == RequestKind.Win && request.Seat == kongSeat)
            {
                return harness.Submit(kongSeat, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        (int WindowIndex, RequestOpenedPayload Window) = Assert.Single(RobbingWindows(harness));
        int closedIndex = IndexOfClosed(harness, Window.RequestId);
        RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(harness.Events[closedIndex]);
        Assert.Equal(RequestOutcomeKind.TimedOut, closed.Outcome);
        Assert.Equal(harness.Events[WindowIndex].Tick + 150, harness.Events[closedIndex].Tick);

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(
            ScoreActionType.KongSupplement | ScoreActionType.SelfDrawn | ScoreActionType.KongUpFlower,
            round.ActionType);
        int[] expected = new int[GameState.PlayerCount];
        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            expected[seat] = (seat == kongSeat ? 3 : -1) + (seat == kongSeat ? 12 : -4);
        }

        Assert.Equal(expected, round.ScoreDelta);
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ K-07

    /// <summary>K-07 两候选可胡：关窗后取座位升序首个 Win，另一候选记 Cancelled 且不记账。</summary>
    [Fact]
    public void K07_TwoCandidates_LowestSeatWinsAndTheOtherIsCancelled()
    {
        FlowHarness harness = Start(seed: 17);
        harness.Step();
        byte banker = harness.Banker;
        byte kongSeat = banker;
        byte first = Next(banker, 1);
        byte second = Next(banker, 2);
        ushort x = 5;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(kongSeat))
            {
                PrepareSupplementKong(
                    harness,
                    kongSeat,
                    x,
                    new[] { first, second },
                    winnerMeldCount: 0,
                    winnerHasJoker: false,
                    actingSeat: request.Seat);
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                if (!TryDeclareKong(harness, kongSeat, x))
                {
                    return AnswerDefault(harness, request);
                }

                return harness.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.RobbingKong)
            {
                return harness.Submit(request.Seat, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        RequestOpenedPayload[] windows = RobbingWindows(harness).Select(pair => pair.Window).ToArray();
        Assert.Equal(2, windows.Length);
        Assert.True(windows[0].Seat < windows[1].Seat, "候选必须按座位升序开窗。");
        RequestClosedPayload loser = SessionEventPayloadReader.ReadRequestClosed(
            harness.Events[IndexOfClosed(harness, windows[1].RequestId)]);
        Assert.Equal(RequestOutcomeKind.Cancelled, loser.Outcome);
        Assert.Null(loser.Answer);

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(windows[0].Seat, (byte)round.WinnerSeat);

        // 中马段（T-4）：抢杠胡照常翻马，命中赢家时位集与 delta 各多一段（h ∈ {0,1}）；本用例只钉住抢杠两笔 + 跟庄。
        Assert.Equal(RobbingBits | ScoreActionType.FollowingTheBanker, round.ActionType & ~ScoreActionType.HorseWinner);
        int horseHit = (round.ActionType & ScoreActionType.HorseWinner) != 0 ? 1 : 0;
        int[] expected = new int[GameState.PlayerCount];
        expected[windows[0].Seat] += 12;
        expected[kongSeat] -= 12;
        expected[windows[0].Seat] += 1;
        expected[banker] -= 1;
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            expected[seat] += seat == round.WinnerSeat ? 3 * horseHit : -horseHit;
        }

        Assert.Equal(expected, round.ScoreDelta);
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ K-08

    /// <summary>
    /// K-08 抢杠 + 跟庄 + 中马（马中赢家）：三段账精确叠加——抢杠 3×4（无鬼 per0 = 4）、跟庄 1、
    /// 马中（赢家 +3×horseBonus、其余三家各 −horseBonus），位集含 <see cref="ScoreActionType.HorseWinner"/>，
    /// 事件序 <c>RequestClosed &lt; HorseRevealed &lt; RoundEnded</c> 且后两者同 tick。
    /// 座位按本用例种子：杠家 = 庄、赢家 = 庄 + 2，故被抢者与跟庄 payer 同座（−12−1−1 = −14），
    /// 赢家 +12+1+3 = +16，另两座各 −1；T-3 §5 的 K-08 行（被抢 = 座 3 → −2/−1/+16/−13）是同账的另一座位分配。
    /// 本局 streak = 1；表中 streak = 2 因赢家非庄，连庄两步既不计也不置位，两口径金额相同。
    /// </summary>
    [Fact]
    public void K08_RobbingKongWithFollowingTheBanker_StacksTheHorseHit()
    {
        FlowHarness harness = Start(seed: 19);
        harness.Step();
        byte banker = harness.Banker;
        byte kongSeat = banker;
        byte winner = Next(banker, 2);
        ushort x = 14;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(kongSeat))
            {
                PrepareSupplementKong(
                    harness,
                    kongSeat,
                    x,
                    new[] { winner },
                    winnerMeldCount: 0,
                    winnerHasJoker: false,
                    actingSeat: request.Seat);
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                if (!TryDeclareKong(harness, kongSeat, x))
                {
                    return AnswerDefault(harness, request);
                }

                return harness.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.RobbingKong)
            {
                // 结算第 4 步从墙头翻马：换成一张物理 id % 4 == 赢家的牌，把「马中」钉成确定事件。
                MoveHorseTileToWallHead(harness.Session.State, winner);
                return harness.Submit(request.Seat, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        int endedIndex = IndexOfRoundEnded(harness);
        int closedIndex = IndexOfRobbingClosed(harness);
        Assert.True(closedIndex >= 0 && closedIndex < endedIndex, "抢杠窗口必须先关闭再结算。");

        // 中马事件：紧邻 RoundEnded 之前、与 RoundEnded 同 tick；马座位 = 赢家（物理 id % 4）。
        int horseIndex = harness.Events.FindLastIndex(sessionEvent => sessionEvent.Kind == SessionEventKind.HorseRevealed);
        Assert.True(horseIndex >= 0, "中马局必须产 HorseRevealed。");
        Assert.Equal(endedIndex - 1, horseIndex);
        HorseRevealedPayload horse = SessionEventPayloadReader.ReadHorseRevealed(harness.Events[horseIndex]);
        Assert.Equal(winner, horse.HorseSeat);
        Assert.Equal(winner, horse.TileId % GameState.PlayerCount);
        Assert.Equal(harness.Events[endedIndex].Tick, harness.Events[horseIndex].Tick);
        Assert.True(harness.Events[closedIndex].Tick <= harness.Events[horseIndex].Tick);

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(winner, (byte)round.WinnerSeat);
        Assert.Equal(
            RobbingBits | ScoreActionType.FollowingTheBanker | ScoreActionType.HorseWinner,
            round.ActionType);
        Assert.Equal(0, (int)(round.ActionType & ScoreActionType.DealerStreak));

        int horseBonus = SessionTestSupport.Content.Variants.HorseBonus;
        Assert.Equal(1, horseBonus);

        // 账：抢杠 3×4 + 跟庄 1 + 马中（赢家 +3、其余各 −1）。
        int[] expected = new int[GameState.PlayerCount];
        expected[winner] += 12;
        expected[kongSeat] -= 12;
        expected[winner] += 1;
        expected[banker] -= 1;
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            expected[seat] += seat == winner ? 3 * horseBonus : -horseBonus;
        }

        Assert.Equal(expected, round.ScoreDelta);
        Assert.Equal(16, round.ScoreDelta[winner]);
        Assert.Equal(-14, round.ScoreDelta[kongSeat]);
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ K-09

    /// <summary>K-09 杠上开花承包（明杠、无鬼）：明杠 +3/−3、承包 perK = 2×4 → 赢家 +24、放杠者 −24、跟庄 → 只重定向 payer。</summary>
    [Fact]
    public void K09_KongUpFlowerContract_ExposedKongPayerCoversTheThreeShares()
    {
        FlowHarness harness = Start(seed: 23);
        harness.Step();
        byte banker = harness.Banker;
        byte winner = Next(banker, 1);
        ushort exposedType = 0;
        ushort z = 14;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == banker)
            {
                PlayerState seat = harness.Session.State.Players[banker];
                ReplaceHand(seat, JunkHand(seat.Hand.Count - 1));
                seat.Hand.Append(MakeTile(exposedType, 3));
                ReplaceHand(harness.Session.State.Players[winner], ExposedKongHand(exposedType, z, withJoker: false));
                return harness.Submit(banker, CommandVerb.Discard, request.RequestId, slot: seat.Hand.Count - 1);
            }

            if (request.Kind == RequestKind.Pong && request.Seat == winner)
            {
                // 明杠补牌取墙尾；排除赢家手里的物理 id，否则补牌进手会撞重复 id（R-3）。
                MoveTileToWallTail(
                    harness.Session.State,
                    z,
                    harness.Session.State.Players[winner].Hand.Items.Select(tile => tile.Id).ToArray());
                return harness.Submit(winner, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.Win && request.Seat == winner)
            {
                return harness.Submit(winner, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(winner, (byte)round.WinnerSeat);
        Assert.Equal(
            ScoreActionType.ExposedKong | ScoreActionType.SelfDrawn | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone | ScoreActionType.NoJoker
            | ScoreActionType.FollowingTheBanker,
            round.ActionType);

        int[] expected = new int[GameState.PlayerCount];
        expected[winner] += 3;
        expected[banker] -= 3;
        expected[winner] += 24;
        expected[banker] -= 24;
        expected[winner] += 1;
        expected[banker] -= 1;
        Assert.Equal(expected, round.ScoreDelta);
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ K-10

    /// <summary>K-10 补杠补牌自摸（有番种无承包）：补杠 [3,−1,−1,−1] + 开花 per=8 + 跟庄，不置 KongUpFlowerPayForEveryone。</summary>
    [Fact]
    public void K10_SupplementKongReplacementSelfDrawn_IsNotAContract()
    {
        FlowHarness harness = Start(seed: 29);
        harness.Step();
        byte banker = harness.Banker;
        byte kongSeat = Next(banker, 3);
        byte candidate = Next(banker, 1);
        ushort x = 5;
        ushort z = 14;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(kongSeat))
            {
                PrepareSupplementKong(
                    harness,
                    kongSeat,
                    x,
                    new[] { candidate },
                    winnerMeldCount: 0,
                    winnerHasJoker: false,
                    actingSeat: request.Seat,
                    kongSupplementWinType: z);
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                if (!TryDeclareKong(harness, kongSeat, x))
                {
                    return AnswerDefault(harness, request);
                }

                return harness.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.RobbingKong)
            {
                return harness.Submit(request.Seat, CommandVerb.Pass, request.RequestId);
            }

            if (request.Kind == RequestKind.Win && request.Seat == kongSeat)
            {
                return harness.Submit(kongSeat, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(kongSeat, (byte)round.WinnerSeat);
        Assert.Equal(
            ScoreActionType.KongSupplement | ScoreActionType.SelfDrawn | ScoreActionType.KongUpFlower
            | ScoreActionType.NoJoker | ScoreActionType.FollowingTheBanker,
            round.ActionType);

        int[] expected = new int[GameState.PlayerCount];
        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            expected[seat] = seat == kongSeat ? 3 : -1;
            expected[seat] += seat == kongSeat ? 24 : -8;
        }

        expected[kongSeat] += 1;
        expected[banker] -= 1;
        Assert.Equal(expected, round.ScoreDelta);
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ K-11

    /// <summary>K-11 12 张落地承包：4 副露全明、第 4 副露供牌者包三家（base=4 → +12/−12），跟庄叠加。</summary>
    [Fact]
    public void K11_TwelveCardsContract_OpenedMeldsPayFromTheFourthMeldTarget()
    {
        FlowHarness harness = Start(seed: 31);
        harness.Step();
        byte banker = harness.Banker;
        byte winner = Next(banker, 3);
        byte payer = Next(banker, 1);
        ushort pairType = 6;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(winner))
            {
                ArrangeTwelveCardsWinner(harness, winner, payer, pairType);
                MoveTileToWallHead(
                    harness.Session.State,
                    pairType,
                    harness.Session.State.Players[winner].Hand.Items.Select(tile => tile.Id).ToArray());
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Win && request.Seat == winner)
            {
                return harness.Submit(winner, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(winner, (byte)round.WinnerSeat);
        Assert.Equal(
            ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded
            | ScoreActionType.NoJoker | ScoreActionType.FollowingTheBanker,
            round.ActionType);

        int[] expected = new int[GameState.PlayerCount];
        expected[winner] += 12;
        expected[payer] -= 12;
        expected[winner] += 1;
        expected[banker] -= 1;
        Assert.Equal(expected, round.ScoreDelta);
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ K-12

    /// <summary>K-12 12 张承包、第 4 副露是补杠：<see cref="Meld.TargetSeat"/> 保留原碰放牌者，供牌者仍是它。</summary>
    [Fact]
    public void K12_TwelveCardsContract_SupplementFourthMeldKeepsTheOriginalPongTarget()
    {
        FlowHarness harness = Start(seed: 37);
        harness.Step();
        byte banker = harness.Banker;
        byte winner = Next(banker, 3);
        byte payer = Next(banker, 2);
        ushort pairType = 15;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(winner))
            {
                ArrangeTwelveCardsWinner(harness, winner, payer, pairType, fourthIsSupplement: true);
                MoveTileToWallHead(
                    harness.Session.State,
                    pairType,
                    harness.Session.State.Players[winner].Hand.Items.Select(tile => tile.Id).ToArray());
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Win && request.Seat == winner)
            {
                // 副露在结算时刻之后就会被下一局发牌清空，故在 Win 应答前取快照断言。
                Meld fourth = harness.Session.State.Players[winner].Melds[3];
                Assert.Equal(MeldKind.KongSupplement, fourth.Kind);
                Assert.Equal(payer, fourth.TargetSeat);
                return harness.Submit(winner, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(
            ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded
            | ScoreActionType.NoJoker | ScoreActionType.FollowingTheBanker,
            round.ActionType);
        int[] expected = new int[GameState.PlayerCount];
        expected[winner] += 12;
        expected[payer] -= 12;
        expected[winner] += 1;
        expected[banker] -= 1;
        Assert.Equal(expected, round.ScoreDelta);
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // -------------------------------------- 承包 × 中马补充用例（非 K 编号，D-33/D-35）

    /// <summary>
    /// 12 张落地承包 × 马中赢家（无杠位）：D-33/D-35 要的「承包 + 马命中」精确样本。
    /// 局面同 K-11（4 副露 + 摸到同牌种成对即胡），在 Win 应答前把墙头换成物理 id % 4 == 赢家的牌，
    /// 让结算第 4 步翻出的马必然命中赢家。账 = 承包 3×4 + 跟庄 1 + 马 +3/−1：
    /// 赢家 +16、供牌者 −13、庄家 −2、余座 −1；位集 = 自摸 + 12 张 + 无鬼 + 跟庄 + 中马。
    /// 再用 <see cref="IntegrationSupport.AssertSelfDrawnSettlement"/> 复核承包精确路径（KongFlags == 0）：
    /// 该局必须被接受，而「抹掉 HorseWinner 位、保留马支付」的篡改必须被判死——证明马段是承载断言的。
    /// K-01…K-15 编号集合不变（本用例不带 K 号）。
    /// </summary>
    [Fact]
    public void TwelveCardsContract_WithHorseHit_ChargesTheSupplierAndSpreadsTheHorsePayment()
    {
        FlowHarness harness = Start(seed: 31);
        harness.Step();
        byte banker = harness.Banker;
        byte winner = Next(banker, 3);
        byte payer = Next(banker, 1);
        ushort pairType = 6;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(winner))
            {
                ArrangeTwelveCardsWinner(harness, winner, payer, pairType);
                MoveTileToWallHead(
                    harness.Session.State,
                    pairType,
                    harness.Session.State.Players[winner].Hand.Items.Select(tile => tile.Id).ToArray());
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Win && request.Seat == winner)
            {
                MoveHorseTileToWallHead(harness.Session.State, winner);
                return harness.Submit(winner, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(winner, (byte)round.WinnerSeat);
        Assert.Equal(
            ScoreActionType.SelfDrawn | ScoreActionType.TwelveCardsConceded
            | ScoreActionType.NoJoker | ScoreActionType.FollowingTheBanker | ScoreActionType.HorseWinner,
            round.ActionType);

        int horseBonus = SessionTestSupport.Content.Variants.HorseBonus;
        Assert.Equal(1, horseBonus);

        int[] expected = new int[GameState.PlayerCount];
        expected[winner] += 12;
        expected[payer] -= 12;
        expected[winner] += 1;
        expected[banker] -= 1;
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            expected[seat] += seat == winner ? 3 * horseBonus : -horseBonus;
        }

        Assert.Equal(expected, round.ScoreDelta);
        Assert.Equal(16, round.ScoreDelta[winner]);
        Assert.Equal(-13, round.ScoreDelta[payer]);
        Assert.Equal(-2, round.ScoreDelta[banker]);
        Assert.Equal(0, round.ScoreDelta.Sum());

        // 支撑侧复核：承包精确路径必须把马段纳入期望，否则「承包 + 马命中」会算出 0 个 payer 匹配。
        RoundStartedPayload started = harness.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted)
            .Select(SessionEventPayloadReader.ReadRoundStarted)
            .First();
        ContentVariants variants = SessionTestSupport.Content.Variants;
        IntegrationSupport.AssertSelfDrawnSettlement(round, started, variants, winnerHasJoker: false);

        // 负向：位被抹掉而马支付仍在 → 期望不含马段，必须与实测不等（马段不是可跳过的空断言）。
        RoundEndedPayload bitDropped = new(
            round.WinnerSeat,
            round.ScoreDelta,
            round.ActionType & ~ScoreActionType.HorseWinner);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => IntegrationSupport.AssertSelfDrawnSettlement(bitDropped, started, variants, winnerHasJoker: false));
    }

    // ------------------------------------------------------------------ K-13

    /// <summary>
    /// K-13 优先级一（抢杠 ＞ 12 张）：流程上二者不可同局成立——4 副露的手牌只剩 1 张，成胡张必须与它成对，
    /// 即需要同牌种第 5 张，而抢杠第 4 张与被升级的碰已占满该牌种 4 张（前段钉住这个不可达性：候选为空、不开窗、补杠照常。
    /// 七对抢杠的账本分支见 <c>ContractScoreTests.K03</c>）。后段钉住「一次胡牌只取一个承包者」：
    /// 12 张承包局里没有抢杠两支，且只有供牌者一人付 3 份。
    /// </summary>
    [Fact]
    public void K13_RobbingKongAndTwelveCards_AreMutuallyExclusive_AndOnlyOneContractPays()
    {
        FlowHarness probe = Start(seed: 41);
        probe.Step();
        byte probeBanker = probe.Banker;
        byte kongSeat = Next(probeBanker, 1);
        byte fourMeldSeat = Next(probeBanker, 2);
        ushort x = 5;

        probe.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(kongSeat))
            {
                PrepareSupplementKong(
                    probe,
                    kongSeat,
                    x,
                    Array.Empty<byte>(),
                    winnerMeldCount: 0,
                    winnerHasJoker: false,
                    actingSeat: request.Seat);
                PlayerState fourMeldSeatState = probe.Session.State.Players[fourMeldSeat];
                fourMeldSeatState.Melds.Clear();
                for (int index = 0; index < 4; index++)
                {
                    fourMeldSeatState.Melds.Add(new Meld(
                        MeldKind.Pong,
                        new[]
                        {
                            MakeTile((ushort)(10 + index), 0),
                            MakeTile((ushort)(10 + index), 1),
                            MakeTile((ushort)(10 + index), 2),
                        },
                        opened: true,
                        targetSeat: Next(probeBanker, 3)));
                }

                // 4 副露只剩 1 张手牌：配不上抢杠第 4 张（需同牌种第 5 张），因此不是候选。
                ReplaceHand(fourMeldSeatState, new[] { MakeTile(6, 0) });
                return AnswerDefault(probe, request);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                if (!TryDeclareKong(probe, kongSeat, x))
                {
                    return AnswerDefault(probe, request);
                }

                return probe.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            return AnswerDefault(probe, request);
        };

        Assert.True(probe.RunUntil(() => probe.Events.Any(sessionEvent =>
            sessionEvent.Kind == SessionEventKind.Meld
            && SessionEventPayloadReader.ReadMeld(sessionEvent).Kind == MeldKind.KongSupplement)));
        Assert.Empty(RobbingWindows(probe));

        // 同牌种恰好 4 张物理牌：抢杠第 4 张 + 被升级的碰占满，候选手牌无从再取一张。
        Assert.Equal(4, Enumerable.Range(0, 4).Select(copy => MakeTile(x, copy).Id).Distinct().Count());

        // 12 张承包局：只有供牌者付，且不含抢杠两支。
        FlowHarness harness = Start(seed: 43);
        harness.Step();
        byte banker = harness.Banker;
        byte winner = Next(banker, 1);
        byte payer = Next(banker, 2);
        ushort pairType = 6;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(winner))
            {
                ArrangeTwelveCardsWinner(harness, winner, payer, pairType);
                MoveTileToWallHead(
                    harness.Session.State,
                    pairType,
                    harness.Session.State.Players[winner].Hand.Items.Select(tile => tile.Id).ToArray());
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Win && request.Seat == winner)
            {
                return harness.Submit(winner, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));
        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(0, (int)(round.ActionType & RobbingBits));
        Assert.NotEqual(0, (int)(round.ActionType & ScoreActionType.TwelveCardsConceded));
        Assert.Single(round.ScoreDelta, delta => delta == -12);
        Assert.Equal(-12, round.ScoreDelta[payer]);
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ K-14

    /// <summary>
    /// K-14 优先级二（杠上开花承包 ＞ 12 张）：赢家已有 3 副露，明杠后成为 4 副露；
    /// 明杠补牌自摸时两点同时成立，只取杠上开花承包（KP 位在、TC 位不在），payer = 放明杠者。
    /// </summary>
    [Fact]
    public void K14_KongUpFlowerContract_OutranksTwelveCards()
    {
        FlowHarness harness = Start(seed: 47);
        harness.Step();
        byte banker = harness.Banker;
        byte winner = Next(banker, 1);
        ushort exposedType = 0;
        ushort pairType = 9;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == banker)
            {
                PlayerState seat = harness.Session.State.Players[banker];
                ReplaceHand(seat, JunkHand(seat.Hand.Count - 1));
                seat.Hand.Append(MakeTile(exposedType, 3));

                // 赢家已 3 副露：手牌 = 明杠材料 3 张 + 唯一单张；明杠后手牌只剩这一张，补牌成对即胡。
                PlayerState winnerSeat = harness.Session.State.Players[winner];
                winnerSeat.Melds.Clear();
                for (int index = 0; index < 3; index++)
                {
                    winnerSeat.Melds.Add(new Meld(
                        MeldKind.Pong,
                        new[]
                        {
                            MakeTile((ushort)(11 + index), 0),
                            MakeTile((ushort)(11 + index), 1),
                            MakeTile((ushort)(11 + index), 2),
                        },
                        opened: true,
                        targetSeat: Next(banker, 2)));
                }

                ReplaceHand(winnerSeat, new[]
                {
                    MakeTile(exposedType, 0),
                    MakeTile(exposedType, 1),
                    MakeTile(exposedType, 2),
                    MakeTile(pairType, 0),
                });
                return harness.Submit(banker, CommandVerb.Discard, request.RequestId, slot: seat.Hand.Count - 1);
            }

            if (request.Kind == RequestKind.Pong && request.Seat == winner)
            {
                MoveTileToWallTail(
                    harness.Session.State,
                    pairType,
                    harness.Session.State.Players[winner].Hand.Items.Select(tile => tile.Id).ToArray());
                return harness.Submit(winner, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.Win && request.Seat == winner)
            {
                // 副露在结算后会被下一局发牌清空，故在 Win 应答前断言 4 副露。
                Assert.Equal(4, harness.Session.State.Players[winner].Melds.Count);
                return harness.Submit(winner, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(
            ScoreActionType.ExposedKong | ScoreActionType.SelfDrawn | ScoreActionType.KongUpFlower
            | ScoreActionType.KongUpFlowerPayForEveryone | ScoreActionType.NoJoker
            | ScoreActionType.FollowingTheBanker,
            round.ActionType);
        Assert.Equal(0, (int)(round.ActionType & ScoreActionType.TwelveCardsConceded));

        int[] expected = new int[GameState.PlayerCount];
        expected[winner] += 3;
        expected[banker] -= 3;
        expected[winner] += 24;
        expected[banker] -= 24;
        expected[winner] += 1;
        expected[banker] -= 1;
        Assert.Equal(expected, round.ScoreDelta);
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ K-15

    /// <summary>
    /// K-15 连庄叠加：庄家抢杠胡算庄家胡——本局 streak=1 时连庄奖励为 0（不置 DealerStreak 位），
    /// 但连庄状态机推进：下一局 RoundStarted 的庄家不变、dealerStreak 变 2。
    /// 金额段（streak=3 → b=2 的 +6/−2）由 <c>ContractScoreTests</c> 账本侧覆盖。
    /// </summary>
    [Fact]
    public void K15_RobbingKongByBanker_AdvancesTheDealerStreak()
    {
        FlowHarness harness = Start(seed: 53);
        harness.Step();
        byte banker = harness.Banker;
        byte kongSeat = Next(banker, 2);
        ushort x = 5;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(kongSeat))
            {
                PrepareSupplementKong(
                    harness,
                    kongSeat,
                    x,
                    new[] { banker },
                    winnerMeldCount: 0,
                    winnerHasJoker: false,
                    actingSeat: request.Seat);
                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                if (!TryDeclareKong(harness, kongSeat, x))
                {
                    return AnswerDefault(harness, request);
                }

                return harness.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.RobbingKong)
            {
                return harness.Submit(request.Seat, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Count(sessionEvent =>
            sessionEvent.Kind == SessionEventKind.RoundStarted) >= 2));

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(banker, (byte)round.WinnerSeat);
        Assert.Equal(RobbingBits, round.ActionType);
        Assert.Equal(0, (int)(round.ActionType & ScoreActionType.DealerStreak));

        RoundStartedPayload next = harness.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted)
            .Select(SessionEventPayloadReader.ReadRoundStarted)
            .Last();
        Assert.Equal((byte)2, next.RoundNo);
        Assert.Equal(banker, next.BankerSeat);
        Assert.Equal((byte)2, next.DealerStreak);
    }

    // ------------------------------------------------------- D-22 宽口径补充用例

    /// <summary>
    /// D-22（非 K 编号）：第 4 张来自**手牌**而非摸牌时，补杠仍开抢杠窗且可被抢。
    /// 局面：杠家已有同牌种碰、手里持有第 4 张，本回合摸到的是另一牌种（摸牌提示与碰不匹配）。
    /// 断言：窗口仍按 Win|Pass 打开、结算按抢杠两支记账，且被抢的就是手牌那张（关窗前快照：手牌里恰一张该牌种、
    /// 摸到的牌不是该牌种、副露仍是碰）；关窗后无 Meld/Drew。K-01…K-15 的编号集合不变（本用例不带 K 号）。
    /// </summary>
    [Fact]
    public void RobbingKong_FromHandHeldFourthTile_StillOpensTheWindow()
    {
        FlowHarness harness = Start(seed: 59);
        harness.Step();
        byte banker = harness.Banker;
        byte kongSeat = Next(banker, 2);
        byte winner = banker;
        ushort fourthType = 5;
        ushort drawnType = 27;
        bool snapshotted = false;

        harness.Policy = request =>
        {
            if (request.Kind == RequestKind.Discard && request.Seat == Previous(kongSeat))
            {
                GameState state = harness.Session.State;
                MoveTileToWallHead(state, drawnType);
                PlayerState kongPlayer = state.Players[kongSeat];
                kongPlayer.Melds.Clear();
                kongPlayer.Melds.Add(MakePong(fourthType, MakeTile(fourthType, 0).Id, targetSeat: Next(kongSeat, 1)));
                Tile[] hand = JunkHand(9).Append(MakeTile(fourthType, 0)).ToArray();
                ReplaceHand(kongPlayer, hand);
                for (byte seat = 0; seat < GameState.PlayerCount; seat++)
                {
                    if (seat == kongSeat || seat == request.Seat)
                    {
                        continue;
                    }

                    PlayerState player = state.Players[seat];
                    if (seat == winner)
                    {
                        ReplaceHand(player, WinningHand(fourthType, meldCount: 0, withJoker: false));
                        continue;
                    }

                    player.Melds.Clear();
                    ReplaceHand(player, JunkHand(13));
                }

                return AnswerDefault(harness, request);
            }

            if (request.Kind == RequestKind.Discard && request.Seat == kongSeat)
            {
                if (!TryDeclareKongFromHand(harness, kongSeat, fourthType))
                {
                    return AnswerDefault(harness, request);
                }

                return harness.Submit(kongSeat, CommandVerb.Kong, request.RequestId);
            }

            if (request.Kind == RequestKind.RobbingKong)
            {
                PlayerState robbed = harness.Session.State.Players[kongSeat];
                // 第 4 张是手牌那张：摸到的牌不是该牌种，手牌里恰一张该牌种且排在第 10 槽（不是末尾）。
                Assert.NotEqual(fourthType, robbed.Hand[robbed.Hand.Count - 1].Type.Id);
                Assert.Equal(drawnType, robbed.Hand[robbed.Hand.Count - 1].Type.Id);
                Assert.Equal(1, robbed.Hand.Items.Count(tile => tile.Type.Id == fourthType));
                Assert.Equal(fourthType, robbed.Hand[robbed.Hand.Count - 2].Type.Id);
                Meld pong = Assert.Single(robbed.Melds);
                Assert.Equal(MeldKind.Pong, pong.Kind);
                Assert.Equal(3, pong.Tiles.Count);
                snapshotted = true;
                return harness.Submit(request.Seat, CommandVerb.Win, request.RequestId);
            }

            return AnswerDefault(harness, request);
        };

        Assert.True(harness.RunUntil(() => harness.Events.Any(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)));
        Assert.True(snapshotted, "手牌持第 4 张的补杠必须开抢杠窗。");

        (int WindowIndex, RequestOpenedPayload Window) = Assert.Single(RobbingWindows(harness));
        Assert.Equal(winner, Window.Seat);
        Assert.Equal(AllowedVerbs.Win | AllowedVerbs.Pass, Window.Allowed);
        Assert.Equal(harness.Events[WindowIndex].Tick + 150, Window.DeadlineTick);

        int closedIndex = IndexOfClosed(harness, Window.RequestId);
        RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(harness.Events[closedIndex]);
        Assert.Equal(RequestOutcomeKind.Answered, closed.Outcome);
        int endedIndex = IndexOfRoundEnded(harness);
        Assert.True(closedIndex < endedIndex);
        AssertNoMeldOrDrew(harness, closedIndex, endedIndex);

        RoundEndedPayload round = LastRoundEnded(harness);
        Assert.Equal(winner, (byte)round.WinnerSeat);
        Assert.Equal(RobbingBits, round.ActionType);
        Assert.Equal(new[] { 12, 0, -12, 0 }, Offset(round.ScoreDelta, banker, winner, kongSeat));
        Assert.Equal(0, round.ScoreDelta.Sum());
    }

    // ------------------------------------------------------------------ 支撑

    private FlowHarness Start(ulong seed)
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed);
        FlowHarness harness = new(session, _fixture.Catalog);
        session.Start();
        harness.Drain();
        return harness;
    }

    /// <summary>
    /// 指定一个真人座位起局：该座位不走 Bot 队列，因此用例可以让它的窗口「无人应答」而真正超时
    /// （Bot 座位会在固定延迟后自行应答，超时路径无从构造）。
    /// </summary>
    private FlowHarness Start(ulong seed, byte humanSeat)
    {
        GameSession session = SessionTestSupport.Create(
            new SessionConfig(humanSeat, GameState.PlayerCount - 1),
            _fixture.Catalog,
            seed);
        FlowHarness harness = new(session, _fixture.Catalog);
        session.Start();
        harness.Drain();
        return harness;
    }

    /// <summary>
    /// 合成补杠局面，必须在杠家上家（<paramref name="actingSeat"/>）出牌时调用：自身回合的杠位在请求打开时
    /// 已按「牌墙非空 + 摸到的牌可杠」算好，故碰与手牌都得先于杠家摸牌就位。
    /// 做四件事：① 牌墙头换成 <paramref name="fourthType"/>（杠家下一手摸到的第 4 张）；
    /// ② 杠家补一副同牌种碰（排除牌墙头那张的物理 id），手牌按 13 = 10 + 3 摆好；
    /// ③ <paramref name="kongSupplementWinType"/> 非空时把杠家 10 张手牌设计成「补牌即胡」并把牌墙尾换成该牌种；
    /// ④ 候选座位换成能胡该牌种的手牌，其余座位换成散牌。返回牌墙头那张（杠家将摸到的第 4 张）。
    /// <paramref name="actingSeat"/> 正在出牌，手牌与副露保持原样（其弃牌槽位由用例自己决定）。
    /// </summary>
    private Tile PrepareSupplementKong(
        FlowHarness harness,
        byte kongSeat,
        ushort fourthType,
        IReadOnlyList<byte> candidateSeats,
        int winnerMeldCount,
        bool winnerHasJoker,
        byte actingSeat,
        ushort? kongSupplementWinType = null,
        bool kongHasJoker = false)
    {
        GameState state = harness.Session.State;
        Tile head = MoveTileToWallHead(state, fourthType);

        PlayerState kongPlayer = state.Players[kongSeat];
        kongPlayer.Melds.Clear();
        kongPlayer.Melds.Add(MakePong(fourthType, head.Id, targetSeat: Next(kongSeat, 1)));
        ReplaceHand(
            kongPlayer,
            kongSupplementWinType is ushort winType
                ? SupplementWinDesign(winType, kongHasJoker)
                : JunkHand(10));

        if (kongSupplementWinType is ushort tailType)
        {
            MoveTileToWallTail(
                state,
                tailType,
                kongPlayer.Hand.Items.Select(tile => tile.Id).ToArray());
        }

        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            if (seat == kongSeat || seat == actingSeat)
            {
                continue;
            }

            PlayerState player = state.Players[seat];
            if (!candidateSeats.Contains(seat))
            {
                player.Melds.Clear();
                ReplaceHand(player, JunkHand(13));
                continue;
            }

            ReplaceHand(player, WinningHand(fourthType, winnerMeldCount, winnerHasJoker));
            Assert.True(
                IsWinningHand(player, MakeTile(fourthType, 3)),
                $"候选座位 {seat} 的合成手牌必须能与第 4 张成胡。");
        }

        return head;
    }

    /// <summary>12 张承包的赢家：4 副露 + 1 张手牌，牌墙头换成同牌种（摸到即对子成胡）。</summary>
    private static void ArrangeTwelveCardsWinner(
        FlowHarness harness,
        byte winner,
        byte payer,
        ushort pairType,
        bool fourthIsSupplement = false)
    {
        PlayerState player = harness.Session.State.Players[winner];
        player.Melds.Clear();
        for (int index = 0; index < 3; index++)
        {
            player.Melds.Add(new Meld(
                MeldKind.Pong,
                new[]
                {
                    MakeTile((ushort)(10 + index), 0),
                    MakeTile((ushort)(10 + index), 1),
                    MakeTile((ushort)(10 + index), 2),
                },
                opened: true,
                targetSeat: Next(payer, 1)));
        }

        Meld fourth = fourthIsSupplement
            ? new Meld(
                MeldKind.KongSupplement,
                new[]
                {
                    MakeTile((ushort)14, 0),
                    MakeTile((ushort)14, 1),
                    MakeTile((ushort)14, 2),
                    MakeTile((ushort)14, 3),
                },
                opened: true,
                targetSeat: payer)
            : new Meld(
                MeldKind.Pong,
                new[] { MakeTile((ushort)14, 0), MakeTile((ushort)14, 1), MakeTile((ushort)14, 2) },
                opened: true,
                targetSeat: payer);
        player.Melds.Add(fourth);

        ReplaceHand(player, new[] { MakeTile(pairType, 0) });
    }

    /// <summary>
    /// 明杠响应前的赢家手牌：3 张明杠材料 + 10 张设计牌（13 = 3 + 10，1 副露折算）；
    /// 明杠补牌 <paramref name="winType"/> 进手后即 11 张 = 3 面子 + 将，补牌即胡。
    /// </summary>
    private static Tile[] ExposedKongHand(ushort exposedType, ushort winType, bool withJoker)
    {
        List<Tile> tiles =
        [
            MakeTile(exposedType, 0),
            MakeTile(exposedType, 1),
            MakeTile(exposedType, 2),
        ];
        tiles.AddRange(WinningHand(winType, meldCount: 1, withJoker, exposedType));
        return tiles.ToArray();
    }

    /// <summary>
    /// 差一张 <paramref name="completingType"/> 即胡的 13−3×melds 张手牌：
    /// 两张边张构成顺子 <c>[completingType−2, completingType−1, completingType]</c>，
    /// 其余部分 = (3−melds) 组刻子 + 1 将（有鬼时用「填充牌 + 鬼牌」当将）。
    /// 因此「手牌 + completingType」按 <see cref="IWinChecker.IsWin"/> 的 14 张口径必然成胡。
    /// </summary>
    private static Tile[] WinningHand(ushort completingType, int meldCount, bool withJoker, params ushort[] avoidTypes)
    {
        int handCount = 13 - (3 * meldCount);
        int setCount = 3 - meldCount;
        List<Tile> tiles =
        [
            MakeTile((ushort)(completingType - 2), 0),
            MakeTile((ushort)(completingType - 1), 0),
        ];
        ushort[] fillers = FillerTypes(completingType, 4, avoidTypes);
        int cursor = 0;
        for (int set = 0; set < setCount; set++)
        {
            ushort type = fillers[cursor++];
            tiles.Add(MakeTile(type, 0));
            tiles.Add(MakeTile(type, 1));
            tiles.Add(MakeTile(type, 2));
        }

        if (withJoker)
        {
            tiles.Add(MakeTile(fillers[cursor], 0));
            tiles.Add(MakeTile(SessionTestSupport.Joker.Id, 0));
        }
        else
        {
            tiles.Add(MakeTile(fillers[cursor], 0));
            tiles.Add(MakeTile(fillers[cursor], 1));
        }

        Assert.Equal(handCount, tiles.Count);
        return tiles.ToArray();
    }

    /// <summary>
    /// 补杠「补牌即胡」的杠家手牌设计：杠家已有 1 副碰，手牌口径是 13−3 = 10 张，
    /// 补牌进手后 11 张 = 3 面子 + 将；语义与 <see cref="WinningHand"/> 的 1 副露口径同一份实现。
    /// </summary>
    private static Tile[] SupplementWinDesign(ushort winType, bool withJoker) =>
        WinningHand(winType, meldCount: 1, withJoker);

    /// <summary>
    /// 「七对型」手牌：手里已有被抢牌种的 4 张（补杠者占满该牌种时不可能出现的局面）+ 4 对 + 1 张散牌，
    /// 再加第 4 张即同牌种 5 张，<see cref="IWinChecker.IsSevenPairs"/> 必须判否。
    /// </summary>
    private static Tile[] SevenPairsLikeHand(ushort typeId)
    {
        List<Tile> tiles = [];
        for (int copy = 0; copy < 4; copy++)
        {
            tiles.Add(MakeTile(typeId, copy));
        }

        for (int pair = 0; pair < 4; pair++)
        {
            ushort type = (ushort)(pair + 20);
            tiles.Add(MakeTile(type, 0));
            tiles.Add(MakeTile(type, 1));
        }

        tiles.Add(MakeTile(24, 0));
        return tiles.ToArray();
    }

    /// <summary>不可能成胡的散牌：相邻牌种间隔 ≥3 且全部单张（七对与常规胡都不成立）。</summary>
    private static Tile[] JunkHand(int count)
    {
        ushort[] pattern = [0, 3, 6, 9, 12, 15, 18, 21, 24, 27, 28, 29, 30];
        Tile[] tiles = new Tile[count];
        for (int index = 0; index < count; index++)
        {
            tiles[index] = MakeTile(pattern[index % pattern.Length], index / pattern.Length);
        }

        return tiles;
    }

    /// <summary>与完成牌不相邻的填充牌种，最多四种，互不相同；<paramref name="avoidTypes"/> 里的牌种跳过。</summary>
    private static ushort[] FillerTypes(ushort completingType, int count, params ushort[] avoidTypes)
    {
        List<ushort> types = [];
        for (ushort type = 0; type < 31 && types.Count < count; type++)
        {
            if (type >= completingType - 2 && type <= completingType + 2)
            {
                continue;
            }

            if (type == SessionTestSupport.Joker.Id || avoidTypes.Contains(type))
            {
                continue;
            }

            types.Add(type);
        }

        Assert.Equal(count, types.Count);
        return types.ToArray();
    }

    private static Meld MakePong(ushort typeId, ushort excludedId, byte targetSeat)
    {
        List<Tile> tiles = [];
        for (int copy = 0; copy < 4 && tiles.Count < 3; copy++)
        {
            Tile tile = MakeTile(typeId, copy);
            if (tile.Id != excludedId)
            {
                tiles.Add(tile);
            }
        }

        Assert.Equal(3, tiles.Count);
        return new Meld(MeldKind.Pong, tiles, opened: true, targetSeat);
    }

    private bool IsWinningHand(PlayerState player, Tile completingTile)
    {
        WinChecker checker = new(_fixture.Catalog);
        Tile[] hand = player.Hand.Items.Append(completingTile).ToArray();
        return checker.IsSevenPairs(hand, SessionTestSupport.Joker)
            || checker.IsWin(hand, player.Melds, SessionTestSupport.Joker);
    }

    private static void ReplaceHand(PlayerState player, IReadOnlyList<Tile> tiles)
    {
        player.Hand.Clear();
        foreach (Tile tile in tiles)
        {
            player.Hand.Append(tile);
        }
    }

    /// <summary>
    /// 把牌墙里指定牌种的牌挪到墙头，让下一位摸到它。<paramref name="excludeIds"/> 里的物理 id 不会被选中
    /// （摸到的牌会追加进某个手牌区，同 id 撞车会触发 <see cref="TileZone"/> 的重复 id 断言）。
    /// 墙里没有可用牌时合成一张该牌种的空闲副本；四种副本都被占用且都在排除集里时退回 copy 3。
    /// </summary>
    private static Tile MoveTileToWallHead(GameState state, ushort typeId, params ushort[] excludeIds)
    {
        TileZone wall = state.Wall;
        for (int index = 0; index < wall.Count; index++)
        {
            if (wall[index].Type.Id != typeId || excludeIds.Contains(wall[index].Id))
            {
                continue;
            }

            Tile head = wall.RemoveAt(index);
            Tile[] rest = wall.Items.ToArray();
            wall.Clear();
            wall.Append(head);
            foreach (Tile tile in rest)
            {
                wall.Append(tile);
            }

            return head;
        }

        Tile synthetic = FindFreeTile(state, typeId, excludeIds);
        Tile[] tailTiles = wall.Items.ToArray();
        wall.Clear();
        wall.Append(synthetic);
        foreach (Tile tile in tailTiles)
        {
            wall.Append(tile);
        }

        return synthetic;
    }

    /// <summary>把牌墙里指定牌种的牌挪到墙尾（补杠/明杠的补牌来源）；排除口径同 <see cref="MoveTileToWallHead"/>。</summary>
    private static Tile MoveTileToWallTail(GameState state, ushort typeId, params ushort[] excludeIds)
    {
        TileZone wall = state.Wall;
        for (int index = 0; index < wall.Count; index++)
        {
            if (wall[index].Type.Id != typeId || excludeIds.Contains(wall[index].Id))
            {
                continue;
            }

            Tile tile = wall.RemoveAt(index);
            wall.Append(tile);
            return tile;
        }

        Tile synthetic = FindFreeTile(state, typeId, excludeIds);
        wall.Append(synthetic);
        return synthetic;
    }

    /// <summary>该牌种尚未出现在任何区域、且不在排除集里的物理牌；都占用时返回 copy 3 的物理牌。</summary>
    private static Tile FindFreeTile(GameState state, ushort typeId, ushort[] excludeIds)
    {
        HashSet<ushort> present = [];
        foreach (Tile tile in state.Wall.Items)
        {
            present.Add(tile.Id);
        }

        foreach (PlayerState player in state.Players)
        {
            foreach (Tile tile in player.Hand.Items)
            {
                present.Add(tile.Id);
            }

            foreach (Tile tile in player.Discards.Items)
            {
                present.Add(tile.Id);
            }

            foreach (Meld meld in player.Melds)
            {
                foreach (Tile tile in meld.Tiles)
                {
                    present.Add(tile.Id);
                }
            }
        }

        for (int copy = 3; copy >= 0; copy--)
        {
            Tile candidate = MakeTile(typeId, copy);
            if (!present.Contains(candidate.Id) && !excludeIds.Contains(candidate.Id))
            {
                return candidate;
            }
        }

        return MakeTile(typeId, 3);
    }

    /// <summary>
    /// 把一张物理 id 取模 4 命中 <paramref name="horseSeat"/> 的牌挪到墙头，让结算第 4 步翻出的马命中该座。
    /// <paramref name="excludeIds"/> 口径同 <see cref="MoveTileToWallHead"/>；墙里没有这种签名的牌时，
    /// 用任意牌种的一张空闲副本合成并插到墙头（结算时同样会被取出进揭示区）。
    /// </summary>
    private static Tile MoveHorseTileToWallHead(GameState state, byte horseSeat, params ushort[] excludeIds)
    {
        TileZone wall = state.Wall;
        for (int index = 0; index < wall.Count; index++)
        {
            Tile tile = wall[index];
            if (tile.Id % GameState.PlayerCount != horseSeat || excludeIds.Contains(tile.Id))
            {
                continue;
            }

            Tile head = wall.RemoveAt(index);
            Tile[] rest = wall.Items.ToArray();
            wall.Clear();
            wall.Append(head);
            foreach (Tile other in rest)
            {
                wall.Append(other);
            }

            return head;
        }

        Tile synthetic = FindFreeHorseTile(state, horseSeat, excludeIds);
        Tile[] tailTiles = wall.Items.ToArray();
        wall.Clear();
        wall.Append(synthetic);
        foreach (Tile other in tailTiles)
        {
            wall.Append(other);
        }

        return synthetic;
    }

    /// <summary>任意牌种里物理 id 取模 4 命中该座、且不在本局牌墙/手牌/弃牌/副露里的副本；同牌种四种副本都被占时退回牌种 0 的对应副本。</summary>
    private static Tile FindFreeHorseTile(GameState state, byte horseSeat, ushort[] excludeIds)
    {
        for (ushort typeId = 0; typeId < 34; typeId++)
        {
            Tile candidate = FindFreeTile(state, typeId, excludeIds);
            if (candidate.Id % GameState.PlayerCount == horseSeat)
            {
                return candidate;
            }
        }

        return MakeTile(0, (horseSeat + GameState.PlayerCount - 1) % GameState.PlayerCount);
    }

    private static Tile MakeTile(ushort typeId, int copy) =>
        new((ushort)((typeId * 4) + copy + 1), TypeOf(typeId));

    private static TileType TypeOf(ushort typeId) => typeId switch
    {
        <= 8 => new TileType(typeId, SuitType.Character, (byte)(typeId + 1)),
        <= 17 => new TileType(typeId, SuitType.Dot, (byte)(typeId - 8)),
        <= 26 => new TileType(typeId, SuitType.Bamboo, (byte)(typeId - 17)),
        <= 30 => new TileType(typeId, SuitType.Wind, (byte)(typeId - 26)),
        _ => new TileType(typeId, SuitType.Dragon, (byte)(typeId - 30)),
    };

    private static byte Next(byte seat, int offset) => (byte)((seat + offset) % GameState.PlayerCount);

    private static byte Previous(byte seat) => (byte)((seat + GameState.PlayerCount - 1) % GameState.PlayerCount);

    private static SubmitResult? AnswerDefault(FlowHarness harness, RequestOpenedPayload request)
    {
        if (request.Kind == RequestKind.Discard)
        {
            int slot = SessionTestSupport.FirstLegalDiscardSlot(harness.Session, harness.Verifier, request.Seat);
            Assert.True(slot >= 0, $"座位 {request.Seat} 没有可打的牌。");
            return harness.Submit(request.Seat, CommandVerb.Discard, request.RequestId, slot);
        }

        return harness.Submit(request.Seat, CommandVerb.Pass, request.RequestId);
    }

    private static List<(int Index, RequestOpenedPayload Window)> RobbingWindows(FlowHarness harness) =>
        harness.Events
            .Select((sessionEvent, index) => (sessionEvent, index))
            .Where(pair => pair.sessionEvent.Kind == SessionEventKind.RequestOpened
                && SessionEventPayloadReader.ReadRequestOpened(pair.sessionEvent).Kind == RequestKind.RobbingKong)
            .Select(pair => (pair.index, SessionEventPayloadReader.ReadRequestOpened(pair.sessionEvent)))
            .ToList();

    private static int IndexOfClosed(FlowHarness harness, long requestId) =>
        harness.Events.FindIndex(sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed
            && SessionEventPayloadReader.ReadRequestClosed(sessionEvent).RequestId == requestId);

    private static int IndexOfRobbingClosed(FlowHarness harness)
    {
        HashSet<long> robbingIds = RobbingWindows(harness).Select(pair => pair.Window.RequestId).ToHashSet();
        return harness.Events.FindIndex(sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed
            && robbingIds.Contains(SessionEventPayloadReader.ReadRequestClosed(sessionEvent).RequestId));
    }

    private static int IndexOfRoundEnded(FlowHarness harness) =>
        harness.Events.FindIndex(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded);

    private static RoundEndedPayload LastRoundEnded(FlowHarness harness) =>
        SessionEventPayloadReader.ReadRoundEnded(harness.Events.Last(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded));

    private static void AssertNoMeldOrDrew(FlowHarness harness, int startIndex, int endIndex)
    {
        for (int index = startIndex; index < endIndex; index++)
        {
            Assert.NotEqual(SessionEventKind.Meld, harness.Events[index].Kind);
            Assert.NotEqual(SessionEventKind.Drew, harness.Events[index].Kind);
        }
    }

    /// <summary>直接对 <see cref="SessionRequestRegistry"/> 的抢杠组契约做断言（窗口参数、仲裁、一次性消费、隔离）。</summary>
    private static void AssertRobbingKongRegistryContract()
    {
        GameState state = new();
        RecordingSink sink = new();
        SessionRequestRegistry registry = new(state, sink);
        IRequestRegistry registryPort = registry;
        Tile fourth = MakeTile(5, 0);

        Assert.Null(registry.OpenRobbingKongGroup(fourth, Array.Empty<PlayerState>(), 10, 160));

        long groupId = registry.OpenRobbingKongGroup(
            fourth,
            new[] { state.Players[1], state.Players[2], state.Players[3] },
            10,
            160)!.Value;

        PendingRequest[] requests = registry.OpenRequests
            .Where(request => request.Kind == RequestKind.RobbingKong)
            .OrderBy(request => request.RequestId)
            .ToArray();
        Assert.Equal(3, requests.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, requests.Select(request => request.Seat).ToArray());
        Assert.All(requests, request => Assert.Equal(AllowedVerbs.Win | AllowedVerbs.Pass, request.Allowed));
        Assert.All(requests, request => Assert.Equal(160, request.DeadlineTick));
        for (int index = 1; index < requests.Length; index++)
        {
            Assert.Equal(requests[index - 1].RequestId + 1, requests[index].RequestId);
        }

        Assert.False(registry.TryGetResponseTarget(requests[0].RequestId, out _));

        Assert.True(registry.TryRecordAnswer(requests[0], new GameCommand(0, 0, 1, CommandVerb.Pass, 0, 0, requests[0].RequestId), 20));
        Assert.True(registry.TryRecordAnswer(requests[1], new GameCommand(0, 0, 2, CommandVerb.Win, 0, 0, requests[1].RequestId), 21));
        Assert.False(registry.IsRobbingKongGroupClosed(groupId));
        registryPort.BeginTick(22);
        Assert.False(registry.IsRobbingKongGroupClosed(groupId));
        registryPort.BeginTick(160);
        Assert.True(registry.IsRobbingKongGroupClosed(groupId));

        Assert.Equal(RequestOutcomeKind.Answered, registry.TakeRobbingKongResult(groupId).Outcomes[1].Kind);
        Assert.Throws<InvalidOperationException>(() => registry.TakeRobbingKongResult(groupId));

        long second = registry.OpenRobbingKongGroup(
            fourth,
            new[] { state.Players[0], state.Players[1] },
            30,
            180)!.Value;
        PendingRequest[] secondRequests = registry.OpenRequests
            .Where(request => request.Kind == RequestKind.RobbingKong && request.RequestId > requests[2].RequestId)
            .OrderBy(request => request.RequestId)
            .ToArray();
        Assert.True(registry.TryRecordAnswer(secondRequests[0], new GameCommand(0, 0, 0, CommandVerb.Win, 0, 0, secondRequests[0].RequestId), 31));
        Assert.True(registry.TryRecordAnswer(secondRequests[1], new GameCommand(0, 0, 1, CommandVerb.Win, 0, 0, secondRequests[1].RequestId), 32));
        registryPort.BeginTick(33);
        RobbingKongResult secondResult = registry.TakeRobbingKongResult(second);
        Assert.Equal((byte)0, secondResult.WinnerSeat);
        Assert.Equal(RequestOutcomeKind.Answered, secondResult.Outcomes[0].Kind);
        Assert.Equal(RequestOutcomeKind.Cancelled, secondResult.Outcomes[1].Kind);

        long responseGroup = registry.OpenGroup(
            MakeTile(7, 0),
            new[] { state.Players[1], state.Players[2] },
            40,
            190)!.Value;
        PendingRequest response = registry.OpenRequests.First(request => request.Seat == 1 && request.Kind == RequestKind.Pong);
        Assert.Equal(AllowedVerbs.Pong | AllowedVerbs.Kong | AllowedVerbs.Pass, response.Allowed);
        Assert.False(registry.IsGroupClosed(responseGroup));
    }

    /// <summary>记录请求开闭的假接缝，只用于 registry 契约断言。</summary>
    private sealed class RecordingSink : ISessionRequestSink
    {
        public void RequestOpened(PendingRequest request)
        {
        }

        public void RequestClosed(PendingRequest request, RequestOutcomeKind outcome, GameCommand? answer)
        {
        }
    }

    /// <summary>受控 tick 驱动器：每步排空事件，并把 RequestOpened 交给当前策略立即应答。</summary>
    private sealed class FlowHarness
    {
        private readonly GameSession _session;
        private readonly List<RequestOpenedPayload> _deferred = [];

        private Func<RequestOpenedPayload, SubmitResult?>? _policy;

        internal FlowHarness(GameSession session, TableCatalog tables)
        {
            _session = session;
            Verifier = SessionTestSupport.CreateVerifier(tables);
        }

        public GameSession Session => _session;

        public CardAndSkillVerifier Verifier { get; }

        public List<SessionEvent> Events { get; } = [];

        /// <summary>
        /// 请求应答策略。赋值前已打开的请求会被暂存，赋值瞬间按原顺序补跑一遍：
        /// 用例总是先 <see cref="Start"/> 再按庄家座位写策略，这样做可避免 Bot 抢先代答首轮请求。
        /// </summary>
        public Func<RequestOpenedPayload, SubmitResult?>? Policy
        {
            get => _policy;
            set
            {
                _policy = value;
                if (value is null || _deferred.Count == 0)
                {
                    return;
                }

                RequestOpenedPayload[] pending = _deferred.ToArray();
                _deferred.Clear();
                foreach (RequestOpenedPayload request in pending)
                {
                    value(request);
                }
            }
        }

        public byte Banker { get; private set; } = byte.MaxValue;

        public bool RoundEndedSeen { get; private set; }

        public int Steps { get; private set; }

        public void Step()
        {
            _session.Step(_session.Tick + 1);
            Steps++;
            Drain();
        }

        public void Drain()
        {
            foreach (SessionEvent sessionEvent in _session.DrainEvents(SessionTestSupport.FullViewSeat))
            {
                Events.Add(sessionEvent);
                switch (sessionEvent.Kind)
                {
                    case SessionEventKind.RoundStarted:
                        Banker = SessionEventPayloadReader.ReadRoundStarted(sessionEvent).BankerSeat;
                        break;
                    case SessionEventKind.RoundEnded:
                        RoundEndedSeen = true;
                        break;
                    case SessionEventKind.RequestOpened:
                        if (!RoundEndedSeen)
                        {
                            RequestOpenedPayload opened = SessionEventPayloadReader.ReadRequestOpened(sessionEvent);
                            if (_policy is null)
                            {
                                _deferred.Add(opened);
                            }
                            else
                            {
                                _policy(opened);
                            }
                        }

                        break;
                }
            }
        }

        public SubmitResult? Submit(byte seat, CommandVerb verb, long requestId, int slot = 0, ushort cardId = 0) =>
            _session.Submit(SessionTestSupport.Command(seat, verb, requestId, slot, cardId));

        public bool RunUntil(Func<bool> condition, int maxSteps = 4000)
        {
            while (!condition())
            {
                if (Steps >= maxSteps)
                {
                    return false;
                }

                Step();
            }

            return true;
        }
    }

    /// <summary>
    /// 手牌持第 4 张的杠位是否就位：手牌里有该牌种（不在末尾，因为末尾是本回合摸到的另一牌种），
    /// 且副露里有同牌种的碰。用于 D-22 的手牌扫描补杠用例。
    /// </summary>
    private static bool TryDeclareKongFromHand(FlowHarness harness, byte kongSeat, ushort fourthType)
    {
        PlayerState player = harness.Session.State.Players[kongSeat];
        if (!player.Hand.Items.Any(tile => tile.Type.Id == fourthType))
        {
            return false;
        }

        foreach (Meld meld in player.Melds)
        {
            if (meld.Kind == MeldKind.Pong && meld.Tiles.Count > 0 && meld.Tiles[0].Type.Id == fourthType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 杠位是否已就位：手牌最后一张正是牌墙头换上去的那张（补杠预判的槽位），且副露里有同牌种的碰。
    /// 未就位时（庄家首巡、已被抢/已补杠后的出牌）用例按普通出牌处理，避免把「第二次出牌」误当杠位。
    /// </summary>
    private static bool TryDeclareKong(FlowHarness harness, byte kongSeat, ushort fourthType)
    {
        PlayerState player = harness.Session.State.Players[kongSeat];
        if (player.Hand.Count == 0 || player.Hand[player.Hand.Count - 1].Type.Id != fourthType)
        {
            return false;
        }

        foreach (Meld meld in player.Melds)
        {
            if (meld.Kind == MeldKind.Pong && meld.Tiles.Count > 0 && meld.Tiles[0].Type.Id == fourthType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>把四座 delta 旋转到「庄赢 / 赢家 / 被抢」的固定视角，供座位随机的用例断言金额。</summary>
    private static int[] Offset(int[] delta, byte banker, byte winner, byte robbed)
    {
        int[] result = new int[GameState.PlayerCount];
        result[0] = delta[banker];
        result[1] = winner == banker ? 0 : delta[winner];
        result[2] = robbed == banker || robbed == winner ? 0 : delta[robbed];
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            if (seat != banker && seat != winner && seat != robbed)
            {
                result[3] = delta[seat];
            }
        }

        return result;
    }
}
