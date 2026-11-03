using Mahjong.Core.Content;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Rules;
using Mahjong.Tests.Session.Support;
using Mahjong.Tests.Support;
using Xunit;

namespace Mahjong.Tests.Session.Flow;

/// <summary>
/// 回合动作与结算：自己回合暗杠/补杠、响应碰后的强制出牌、响应明杠的杠分与补牌、
/// 自摸/七对/流局结算、杠上开花精确口径、庄家轮换与 PlayerState.Score 镜像。
/// 全部用真实内容/查表 + 固定 tick 驱动；脚本策略是纯函数（同 seed 同脚本可复现）。
/// </summary>
[Collection("table-catalog")]
public sealed class GameSessionRoundSettlementTests
{
    private readonly TableCatalogFixture _fixture;

    public GameSessionRoundSettlementTests(TableCatalogFixture fixture) => _fixture = fixture;

    /// <summary>进攻型脚本：能胡就胡、能碰就碰、能杠就杠，否则打第一张合法牌。</summary>
    private static SubmitResult? AggressivePolicy(SessionDriver driver, RequestOpenedPayload request)
    {
        GameSession session = driver.Session;
        PlayerState player = session.State.Players[request.Seat];
        CardAndSkillVerifier verifier = driver.Verifier;

        if (request.Kind == RequestKind.Win)
        {
            bool canWin = verifier.Verify(session.State, player, SkillId.SelfDrawnWith7Pairs, null, null)
                || verifier.Verify(session.State, player, SkillId.SelfDrawn, null, null);
            return session.Submit(SessionTestSupport.Command(
                request.Seat, canWin ? CommandVerb.Win : CommandVerb.Pass, request.RequestId));
        }

        if (request.Kind == RequestKind.Pong)
        {
            if (driver.LastDiscardTile() is Tile target)
            {
                if (verifier.Verify(session.State, player, SkillId.Pong, null, target))
                {
                    return session.Submit(SessionTestSupport.Command(request.Seat, CommandVerb.Pong, request.RequestId));
                }

                if (verifier.Verify(session.State, player, SkillId.Kong, null, target))
                {
                    return session.Submit(SessionTestSupport.Command(request.Seat, CommandVerb.Kong, request.RequestId));
                }
            }

            return session.Submit(SessionTestSupport.Command(request.Seat, CommandVerb.Pass, request.RequestId));
        }

        if ((request.Allowed & AllowedVerbs.Kong) != 0
            && player.Hand.Count > 0
            && verifier.Verify(session.State, player, SkillId.Kong, player.Hand[0], null))
        {
            return session.Submit(SessionTestSupport.Command(request.Seat, CommandVerb.Kong, request.RequestId));
        }

        int slot = SessionTestSupport.FirstLegalDiscardSlot(session, verifier, request.Seat);
        return session.Submit(SessionTestSupport.Command(request.Seat, CommandVerb.Discard, request.RequestId, slot));
    }

    private (GameSession Session, SessionDriver Driver) StartSinglePlayer(ulong seed)
    {
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog, humanSeat: 0, AggressivePolicy);
        return (session, driver);
    }

    private static int IndexOfFirstMeld(SessionDriver driver, MeldKind kind) =>
        driver.Events
            .Select((sessionEvent, index) => (sessionEvent, index))
            .Where(pair => pair.sessionEvent.Kind == SessionEventKind.Meld
                && SessionEventPayloadReader.ReadMeld(pair.sessionEvent).Kind == kind)
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();

    [Fact]
    public void ConcealedKong_EmitsMeldThenSupplementDrawThenContinuesOwnTurn()
    {
        (GameSession session, SessionDriver driver) = StartSinglePlayer(seed: 7);
        Assert.True(driver.RunUntil(() => IndexOfFirstMeld(driver, MeldKind.KongConcealed) >= 0));
        int meldIndex = IndexOfFirstMeld(driver, MeldKind.KongConcealed);
        SessionEvent meldEvent = driver.Events[meldIndex];
        MeldPayload meld = SessionEventPayloadReader.ReadMeld(meldEvent);

        Assert.Equal((byte)0, meld.Seat);
        Assert.Equal(Meld.NoTargetSeat, meld.TargetSeat);
        Assert.Equal(4, meld.TileIds.Length);
        Assert.Single(meld.TileIds.Select(id => (id - 1) / 4).Distinct());

        // 补牌：同 tick、紧后的 Drew 给杠家，牌墙张数已减 1。
        SessionEvent drewEvent = Assert.Single(
            driver.Events.Skip(meldIndex).TakeWhile(sessionEvent => sessionEvent.Tick == meldEvent.Tick),
            sessionEvent => sessionEvent.Kind == SessionEventKind.Drew
                && SessionEventPayloadReader.ReadDrew(sessionEvent).Seat == meld.Seat);
        DrewPayload drew = SessionEventPayloadReader.ReadDrew(drewEvent);
        Assert.Contains(drew.TileId, session.State.Players[0].Hand.Items.Select(tile => (int)tile.Id));

        // 杠后继续自己回合：同 tick 再开 Win 或 Discard 请求，随后强制出牌（不产 TurnStarted）。
        Assert.Contains(
            driver.Events.Skip(meldIndex).TakeWhile(sessionEvent => sessionEvent.Tick == meldEvent.Tick),
            sessionEvent => sessionEvent.Kind == SessionEventKind.RequestOpened
                && SessionEventPayloadReader.ReadRequestOpened(sessionEvent).Seat == meld.Seat);

        // 杠分金额不在此处断言：逐局 delta 由 EveryRoundEnded_DeltaMatchesKongAndWinAccounting_AndMirrorIsWrittenBack 用事件重算覆盖。
        AssertForcedDiscardFollowsKong(driver, meldIndex, meld.Seat);
    }

    [Fact]
    public void SupplementKong_UpgradesExistingPongAndDrawsReplacement()
    {
        (GameSession session, SessionDriver driver) = StartSinglePlayer(seed: 1);
        Assert.True(driver.RunUntil(() => IndexOfFirstMeld(driver, MeldKind.KongSupplement) >= 0));
        int meldIndex = IndexOfFirstMeld(driver, MeldKind.KongSupplement);
        SessionEvent meldEvent = driver.Events[meldIndex];
        MeldPayload meld = SessionEventPayloadReader.ReadMeld(meldEvent);

        Assert.Equal((byte)0, meld.Seat);
        Assert.Equal(Meld.NoTargetSeat, meld.TargetSeat);
        Assert.Equal(4, meld.TileIds.Length);

        // 补杠前同一牌种已有碰；升级后原碰对象仍在（对象身份保留）。
        bool hasEarlierPong = driver.Events.Take(meldIndex).Any(sessionEvent =>
            sessionEvent.Kind == SessionEventKind.Meld
            && SessionEventPayloadReader.ReadMeld(sessionEvent) is { Kind: MeldKind.Pong } earlier
            && earlier.Seat == meld.Seat
            && earlier.TileIds.Length == 3
            && earlier.TileIds[0] == meld.TileIds[0]);
        Assert.True(hasEarlierPong);
        Assert.Contains(session.State.Players[0].Melds, existing =>
            existing.Kind is MeldKind.KongSupplement or MeldKind.Pong
            && existing.Tiles.Count == 4
            && existing.Tiles[0].Type.Id == (meld.TileIds[0] - 1) / 4);

        Assert.Contains(
            driver.Events.Skip(meldIndex).TakeWhile(sessionEvent => sessionEvent.Tick == meldEvent.Tick),
            sessionEvent => sessionEvent.Kind == SessionEventKind.Drew
                && SessionEventPayloadReader.ReadDrew(sessionEvent).Seat == meld.Seat);

        // 杠分金额不在此处断言：逐局 delta 由 EveryRoundEnded_DeltaMatchesKongAndWinAccounting_AndMirrorIsWrittenBack 用事件重算覆盖。
        AssertForcedDiscardFollowsKong(driver, meldIndex, meld.Seat);
    }

    [Fact]
    public void ExposedKong_ConsumesDiscardScoresKongAndDrawsReplacement()
    {
        (GameSession session, SessionDriver driver) = StartSinglePlayer(seed: 2);
        Assert.True(driver.RunUntil(() => IndexOfFirstMeld(driver, MeldKind.KongExposed) >= 0));
        int meldIndex = IndexOfFirstMeld(driver, MeldKind.KongExposed);
        SessionEvent meldEvent = driver.Events[meldIndex];
        MeldPayload meld = SessionEventPayloadReader.ReadMeld(meldEvent);

        Assert.Equal(4, meld.TileIds.Length);
        Assert.NotEqual(Meld.NoTargetSeat, meld.TargetSeat);
        Assert.NotEqual(meld.Seat, meld.TargetSeat);

        // 明杠取的是目标座位刚打出的那张弃牌。
        DiscardedPayload discard = SessionEventPayloadReader.ReadDiscarded(
            driver.Events.Take(meldIndex).Reverse().First(sessionEvent => sessionEvent.Kind == SessionEventKind.Discarded));
        Assert.Equal(meld.TargetSeat, discard.Seat);
        Assert.Contains(discard.TileId, meld.TileIds);

        Assert.Contains(
            driver.Events.Skip(meldIndex).TakeWhile(sessionEvent => sessionEvent.Tick == meldEvent.Tick),
            sessionEvent => sessionEvent.Kind == SessionEventKind.Drew
                && SessionEventPayloadReader.ReadDrew(sessionEvent).Seat == meld.Seat);

        // 杠分金额不在此处断言：逐局 delta 由 EveryRoundEnded_DeltaMatchesKongAndWinAccounting_AndMirrorIsWrittenBack 用事件重算覆盖。
        AssertForcedDiscardFollowsKong(driver, meldIndex, meld.Seat);
    }

    /// <summary>断言杠后同一回合内由杠家完成强制出牌，且期间不产 TurnStarted。</summary>
    private static void AssertForcedDiscardFollowsKong(SessionDriver driver, int meldIndex, byte seat)
    {
        Assert.True(
            driver.RunUntil(() => driver.Events.Skip(meldIndex).Any(sessionEvent =>
                sessionEvent.Kind == SessionEventKind.Discarded
                && SessionEventPayloadReader.ReadDiscarded(sessionEvent).Seat == seat)),
            $"seat {seat} 杠后未完成出牌");

        int discardedIndex = driver.Events
            .Select((sessionEvent, index) => (sessionEvent, index))
            .First(pair => pair.index > meldIndex
                && pair.sessionEvent.Kind == SessionEventKind.Discarded
                && SessionEventPayloadReader.ReadDiscarded(pair.sessionEvent).Seat == seat)
            .index;

        for (int index = meldIndex + 1; index < discardedIndex; index++)
        {
            Assert.NotEqual(SessionEventKind.TurnStarted, driver.Events[index].Kind);
        }
    }

    [Fact]
    public void Pong_ForcesDiscardWithoutNewTurnStarted()
    {
        (GameSession session, SessionDriver driver) = StartSinglePlayer(seed: 1);
        Assert.True(driver.RunUntil(() => IndexOfFirstMeld(driver, MeldKind.Pong) >= 0));
        int meldIndex = IndexOfFirstMeld(driver, MeldKind.Pong);
        SessionEvent meldEvent = driver.Events[meldIndex];
        MeldPayload meld = SessionEventPayloadReader.ReadMeld(meldEvent);
        byte pongSeat = meld.Seat;
        Assert.NotEqual(Meld.NoTargetSeat, meld.TargetSeat);
        Assert.Equal(3, meld.TileIds.Length);

        // 碰后不摸牌、不产 TurnStarted：下一条该座位事件是同 tick 的 Discard 请求，且只允许出牌。
        SessionEvent nextSeatEvent = Assert.Single(
            driver.Events.Skip(meldIndex + 1).TakeWhile(sessionEvent => sessionEvent.Kind != SessionEventKind.TurnStarted)
                .Where(sessionEvent => sessionEvent.Kind is SessionEventKind.RequestOpened or SessionEventKind.Discarded or SessionEventKind.Drew)
                .Take(1));
        Assert.Equal(meldEvent.Tick, nextSeatEvent.Tick);
        Assert.Equal(SessionEventKind.RequestOpened, nextSeatEvent.Kind);
        RequestOpenedPayload forced = SessionEventPayloadReader.ReadRequestOpened(nextSeatEvent);
        Assert.Equal(pongSeat, forced.Seat);
        Assert.Equal(RequestKind.Discard, forced.Kind);
        Assert.Equal(AllowedVerbs.Discard, forced.Allowed);

        // 强制出牌完成后才轮到下家（出现 TurnStarted 或本局结束）。
        Assert.True(driver.RunUntil(() => driver.Events.Skip(meldIndex).Any(sessionEvent =>
            sessionEvent.Kind is SessionEventKind.TurnStarted or SessionEventKind.RoundEnded)));
    }

    [Fact]
    public void EveryRoundEnded_DeltaMatchesKongAndWinAccounting_AndMirrorIsWrittenBack()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 7);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        ContentVariants variants = SessionTestSupport.Content.Variants;
        int eventCursor = 0;
        var deltas = new int[GameState.PlayerCount];
        while (!session.IsGameOver)
        {
            driver.StepOnce();
            for (; eventCursor < driver.Events.Count; eventCursor++)
            {
                SessionEvent sessionEvent = driver.Events[eventCursor];
                if (sessionEvent.Kind != SessionEventKind.RoundEnded)
                {
                    continue;
                }

                RoundEndedPayload round = SessionEventPayloadReader.ReadRoundEnded(sessionEvent);
                RoundStartedPayload started = RoundStartBefore(driver.Events, eventCursor);
                List<MeldPayload> kongs = KongsInRound(driver.Events, eventCursor);
                IReadOnlyList<int[]> expected = ExpectedDelta(round, started, kongs, variants, driver.Events, eventCursor);
                AssertDeltaMatches(round.ScoreDelta, expected);
                Assert.Equal(0, round.ScoreDelta.Sum());

                for (byte seat = 0; seat < GameState.PlayerCount; seat++)
                {
                    deltas[seat] += round.ScoreDelta[seat];
                    Assert.Equal(deltas[seat], (int)session.State.Players[seat].Score);
                }
            }
        }

        Assert.True(driver.RunToGameOver());
        Assert.Equal(8, driver.Events.Count(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded));
    }

    [Fact]
    public void BankerRotation_WinnerTakesBanker_DrawKeepsBanker()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 20);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());

        var rounds = new List<(byte RoundNo, byte Banker)>();
        var winners = new List<sbyte>();
        foreach (SessionEvent sessionEvent in driver.Events)
        {
            switch (sessionEvent.Kind)
            {
                case SessionEventKind.RoundStarted:
                    RoundStartedPayload started = SessionEventPayloadReader.ReadRoundStarted(sessionEvent);
                    rounds.Add((started.RoundNo, started.BankerSeat));
                    break;
                case SessionEventKind.RoundEnded:
                    winners.Add(SessionEventPayloadReader.ReadRoundEnded(sessionEvent).WinnerSeat);
                    break;
            }
        }

        Assert.Equal(8, rounds.Count);
        Assert.Equal(8, winners.Count);
        for (int index = 1; index < rounds.Count; index++)
        {
            byte previousBanker = rounds[index - 1].Banker;
            sbyte winner = winners[index - 1];
            byte expected = winner >= 0 ? (byte)winner : previousBanker;
            Assert.Equal((byte)(index + 1), rounds[index].RoundNo);
            Assert.Equal(expected, rounds[index].Banker);
        }
    }

    [Fact]
    public void EveryRoundEnded_StartsNextRoundInSameTick()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 7);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());

        int roundEndedCount = 0;
        int nextRoundInSameTick = 0;
        for (int index = 0; index < driver.Events.Count; index++)
        {
            SessionEvent sessionEvent = driver.Events[index];
            if (sessionEvent.Kind != SessionEventKind.RoundEnded)
            {
                continue;
            }

            roundEndedCount++;
            Assert.True(index + 1 < driver.Events.Count);
            SessionEvent next = driver.Events[index + 1];
            Assert.Equal(sessionEvent.Tick, next.Tick);
            if (roundEndedCount < 8)
            {
                Assert.Equal(SessionEventKind.RoundStarted, next.Kind);
                nextRoundInSameTick++;
            }
            else
            {
                Assert.Equal(SessionEventKind.GameEnded, next.Kind);
            }
        }

        Assert.Equal(8, roundEndedCount);
        Assert.Equal(7, nextRoundInSameTick);
    }

    [Fact]
    public void DrawRound_ZeroDeltaAndKeepsBanker()
    {
        (GameSession session, SessionDriver driver) = StartSinglePlayer(seed: 1);
        Assert.True(driver.RunUntil(() => driver.Events.Any(sessionEvent =>
            sessionEvent.Kind == SessionEventKind.RoundEnded
            && SessionEventPayloadReader.ReadRoundEnded(sessionEvent).WinnerSeat == -1)));

        int drawIndex = driver.Events
            .Select((sessionEvent, index) => (sessionEvent, index))
            .First(pair => pair.sessionEvent.Kind == SessionEventKind.RoundEnded
                && SessionEventPayloadReader.ReadRoundEnded(pair.sessionEvent).WinnerSeat == -1)
            .index;
        RoundEndedPayload draw = SessionEventPayloadReader.ReadRoundEnded(driver.Events[drawIndex]);
        Assert.Equal(new int[GameState.PlayerCount], draw.ScoreDelta);
        Assert.Equal(ScoreActionType.None, draw.ActionType);

        byte drawBanker = SessionEventPayloadReader.ReadRoundStarted(
            driver.Events.Take(drawIndex).Last(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted)).BankerSeat;

        // 流局后同一 tick 开下一局，庄家不变。
        SessionEvent nextStarted = Assert.Single(
            driver.Events.Skip(drawIndex),
            sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted);
        Assert.Equal(driver.Events[drawIndex].Tick, nextStarted.Tick);
        Assert.Equal(drawBanker, SessionEventPayloadReader.ReadRoundStarted(nextStarted).BankerSeat);
    }

    [Fact]
    public void SevenPairsSelfDrawn_UsesSevenPairsScoringFlags()
    {
        (GameSession session, SessionDriver driver) = StartSinglePlayer(seed: 117);
        Assert.True(driver.RunUntil(() => driver.Events.Any(sessionEvent =>
            sessionEvent.Kind == SessionEventKind.RoundEnded
            && (SessionEventPayloadReader.ReadRoundEnded(sessionEvent).ActionType & ScoreActionType.SevenPairs) != 0)));

        int index = driver.Events
            .Select((sessionEvent, index) => (sessionEvent, index))
            .First(pair => pair.sessionEvent.Kind == SessionEventKind.RoundEnded
                && (SessionEventPayloadReader.ReadRoundEnded(pair.sessionEvent).ActionType & ScoreActionType.SevenPairs) != 0)
            .index;
        RoundEndedPayload round = SessionEventPayloadReader.ReadRoundEnded(driver.Events[index]);

        Assert.NotEqual(-1, round.WinnerSeat);
        Assert.True((round.ActionType & ScoreActionType.SelfDrawn) != 0);
        AssertDeltaMatches(
            round.ScoreDelta,
            ExpectedDelta(
                round,
                RoundStartBefore(driver.Events, index),
                KongsInRound(driver.Events, index),
                SessionTestSupport.Content.Variants,
                driver.Events,
                index));

        // 七对金额：per = (有鬼 ? 2 : 4) × 2；赢家 +3per、其余 -per，叠加本局已发生的杠分。
        // 七对自摸入口（ApplySevenPairsSelfDrawn）按契约置/清 NoJoker 位，故该位即手牌事实；
        // 每份金额统一走 IntegrationSupport.PerShare，不在这里另立一套公式。
        bool winnerHasJoker = (round.ActionType & ScoreActionType.NoJoker) == 0;
        int per = IntegrationSupport.PerShare(winnerHasJoker, sevenPairs: true);
        int kongDeltaWinner = round.ScoreDelta[round.WinnerSeat] - (3 * per);
        Assert.True(kongDeltaWinner >= 0);
    }

    [Fact]
    public void KongUpFlower_IsSetOnlyWhenWinnersLastDrawWasReplacement()
    {
        // seed 7 第 7 局：0 号明杠后补牌自摸 → 置 KongUpFlower，且杠分与开花倍数同时成立。
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 7);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());

        var roundEnded = driver.Events
            .Select((sessionEvent, index) => (sessionEvent, index))
            .Where(pair => pair.sessionEvent.Kind == SessionEventKind.RoundEnded)
            .Select(pair => (Payload: SessionEventPayloadReader.ReadRoundEnded(pair.sessionEvent), pair.index))
            .ToArray();

        (RoundEndedPayload Payload, int Index) up = roundEnded.First(pair =>
            (pair.Payload.ActionType & ScoreActionType.KongUpFlower) != 0);
        Assert.NotEqual(-1, up.Payload.WinnerSeat);
        AssertDeltaMatches(
            up.Payload.ScoreDelta,
            ExpectedDelta(
                up.Payload,
                RoundStartBefore(driver.Events, up.Index),
                KongsInRound(driver.Events, up.Index),
                SessionTestSupport.Content.Variants,
                driver.Events,
                up.Index));

        // 赢家本回合最后一摸是杠后补牌：该 Drew 紧跟在赢家的杠 Meld 之后且同 tick。
        int lastDrewIndex = -1;
        for (int index = 0; index <= up.Index; index++)
        {
            if (driver.Events[index].Kind == SessionEventKind.Drew
                && SessionEventPayloadReader.ReadDrew(driver.Events[index]).Seat == up.Payload.WinnerSeat)
            {
                lastDrewIndex = index;
            }
        }

        Assert.True(lastDrewIndex > 0);
        SessionEvent previous = driver.Events[lastDrewIndex - 1];
        Assert.Equal(SessionEventKind.Meld, previous.Kind);
        Assert.Equal(driver.Events[lastDrewIndex].Tick, previous.Tick);
        MeldPayload previousMeld = SessionEventPayloadReader.ReadMeld(previous);
        Assert.Equal((byte)up.Payload.WinnerSeat, previousMeld.Seat);
        Assert.NotEqual(MeldKind.Pong, previousMeld.Kind);

        // 反例：seed 20 同局有他家杠、赢家最后一摸是普通墙头牌 → 不得置 KongUpFlower。
        GameSession other = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 20);
        other.Start();
        SessionDriver otherDriver = SessionTestSupport.CreateDriver(other, _fixture.Catalog);
        Assert.True(otherDriver.RunToGameOver());
        var otherRounds = otherDriver.Events
            .Select((sessionEvent, index) => (sessionEvent, index))
            .Where(pair => pair.sessionEvent.Kind == SessionEventKind.RoundEnded)
            .Select(pair => (Payload: SessionEventPayloadReader.ReadRoundEnded(pair.sessionEvent), pair.index))
            .ToArray();
        Assert.Contains(otherRounds, pair =>
            (pair.Payload.ActionType & ScoreActionType.SelfDrawn) != 0
            && (pair.Payload.ActionType & ScoreActionType.KongUpFlower) == 0
            && KongsInRound(otherDriver.Events, pair.index).Count > 0);
    }

    [Fact]
    public void RoundResult_PublicConstructor_SnapshotsScoresForSettlementPayload()
    {
        short[] delta = [12, -4, -4, -4];
        short[] totals = [12, -4, -4, -4];

        var result = new RoundResult(roundNo: 3, bankerSeat: 1, winnerSeat: 0, ScoreActionType.SelfDrawn, delta, totals);

        Assert.Equal((byte)3, result.RoundNo);
        Assert.Equal((byte)1, result.BankerSeat);
        Assert.Equal((byte)0, result.WinnerSeat);
        Assert.False(result.IsDraw);
        Assert.Equal(ScoreActionType.SelfDrawn, result.ActionType);
        Assert.Equal(new short[] { 12, -4, -4, -4 }, result.ScoreDelta.ToArray());
        Assert.Equal(new short[] { 12, -4, -4, -4 }, result.TotalScores.ToArray());

        // 构造时复制：修改入参数组不影响结果快照。
        delta[0] = 99;
        totals[0] = 99;
        Assert.Equal(12, (int)result.ScoreDelta[0]);
        Assert.Equal(12, (int)result.TotalScores[0]);
    }

    [Fact]
    public void NoResponseWindow_AdvancesToNextTurnInSameTick()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 7);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());

        bool found = false;
        for (int index = 0; index + 1 < driver.Events.Count; index++)
        {
            if (driver.Events[index].Kind != SessionEventKind.Discarded)
            {
                continue;
            }

            SessionEvent next = driver.Events[index + 1];
            if (next.Kind != SessionEventKind.TurnStarted || next.Tick != driver.Events[index].Tick)
            {
                continue;
            }

            DiscardedPayload discarded = SessionEventPayloadReader.ReadDiscarded(driver.Events[index]);
            TurnStartedPayload turn = SessionEventPayloadReader.ReadTurnStarted(next);
            Assert.Equal((byte)((discarded.Seat + 1) % GameState.PlayerCount), turn.Seat);
            found = true;
            break;
        }

        Assert.True(found);
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(20UL)]
    [InlineData(1UL)]
    [InlineData(77UL)]
    public void EveryRoundStarted_DealerStreakFollowsBankerWinRules(ulong seed)
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());

        RoundStartedPayload[] rounds = driver.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted)
            .Select(SessionEventPayloadReader.ReadRoundStarted)
            .ToArray();
        sbyte[] winners = driver.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)
            .Select(sessionEvent => SessionEventPayloadReader.ReadRoundEnded(sessionEvent).WinnerSeat)
            .ToArray();

        Assert.Equal(8, rounds.Length);
        Assert.Equal(8, winners.Length);

        // 第 1 局恒 1；之后庄胡 +1（不封顶）、非庄胡归 1（按赢家 ≠ 本局庄家判定）、流局不变。
        Assert.Equal((byte)1, rounds[0].DealerStreak);
        for (int index = 1; index < rounds.Length; index++)
        {
            RoundStartedPayload previous = rounds[index - 1];
            sbyte winner = winners[index - 1];
            byte expectedBanker = winner >= 0 ? (byte)winner : previous.BankerSeat;
            byte expectedStreak = winner < 0
                ? previous.DealerStreak
                : winner == previous.BankerSeat
                    ? (byte)(previous.DealerStreak + 1)
                    : (byte)1;

            Assert.Equal((byte)(index + 1), rounds[index].RoundNo);
            Assert.Equal(expectedBanker, rounds[index].BankerSeat);
            Assert.Equal(expectedStreak, rounds[index].DealerStreak);
        }
    }

    [Fact]
    public void RoundEnded_SetsFollowingTheBankerAndDealerStreakOnlyForMatchingWinKind()
    {
        ContentVariants variants = SessionTestSupport.Content.Variants;
        int bankerWins = 0;
        int nonBankerWins = 0;
        int streaksAboveOne = 0;

        // seed 77 的第 5 局是连续第二次庄胡（streak 2），保证「连庄奖励 > 0 置位」分支有实际样本。
        foreach (ulong seed in new ulong[] { 7, 20, 77 })
        {
            GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed);
            session.Start();
            SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
            Assert.True(driver.RunToGameOver());

            RoundStartedPayload[] rounds = driver.Events
                .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted)
                .Select(SessionEventPayloadReader.ReadRoundStarted)
                .ToArray();
            RoundEndedPayload[] ended = driver.Events
                .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)
                .Select(SessionEventPayloadReader.ReadRoundEnded)
                .ToArray();
            Assert.Equal(rounds.Length, ended.Length);

            for (int index = 0; index < ended.Length; index++)
            {
                RoundEndedPayload round = ended[index];
                if (round.WinnerSeat < 0)
                {
                    Assert.Equal(ScoreActionType.None, round.ActionType);
                    Assert.All(round.ScoreDelta, delta => Assert.Equal(0, delta));
                    continue;
                }

                bool bankerWin = round.WinnerSeat == rounds[index].BankerSeat;
                int bonus = DealerStreakBonus(variants.DealerStreakBonus, rounds[index].DealerStreak);
                bool followingBit = (round.ActionType & ScoreActionType.FollowingTheBanker) != 0;
                bool streakBit = (round.ActionType & ScoreActionType.DealerStreak) != 0;

                // 跟庄只在非庄胡且变体开启时置位；连庄奖励只在庄胡且本局表值大于 0 时置位；两位互斥。
                Assert.Equal(!bankerWin && variants.FollowingBanker > 0, followingBit);
                Assert.Equal(bankerWin && bonus > 0, streakBit);
                Assert.False(followingBit && streakBit);

                if (bankerWin && rounds[index].DealerStreak > 1)
                {
                    streaksAboveOne++;
                }

                if (bankerWin)
                {
                    bankerWins++;
                }
                else
                {
                    nonBankerWins++;
                }
            }
        }

        // 三个种子合起来必须同时覆盖庄胡、非庄胡与连庄（streak > 1），否则上述分支断言没有实际样本。
        Assert.True(bankerWins > 0);
        Assert.True(nonBankerWins > 0);
        Assert.True(streaksAboveOne > 0);
    }

    [Fact]
    public void VariantsDisabled_StreakStillAdvancesButNothingIsPaidOrFlagged()
    {
        using var content = new TempContentDirectory();
        ContentCatalog catalog = ContentCatalog.Load(content.Root);
        GameSession session = LocalSessionFactory.Create(new SessionConfig(null, 4, 10), catalog, _fixture.Catalog, seed: 7);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());

        ContentVariants disabled = ContentVariants.Disabled;
        Assert.Empty(disabled.DealerStreakBonus);
        Assert.Equal(0, disabled.FollowingBanker);

        RoundStartedPayload[] rounds = driver.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted)
            .Select(SessionEventPayloadReader.ReadRoundStarted)
            .ToArray();
        sbyte[] winners = driver.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)
            .Select(sessionEvent => SessionEventPayloadReader.ReadRoundEnded(sessionEvent).WinnerSeat)
            .ToArray();
        Assert.Equal(8, rounds.Length);
        Assert.Equal(8, winners.Length);

        // 关闭只停付款与置位：连庄次数状态机照常推进（第 1 局 1、庄胡 +1、非庄胡归 1、流局不变）。
        Assert.Equal((byte)1, rounds[0].DealerStreak);
        for (int index = 1; index < rounds.Length; index++)
        {
            RoundStartedPayload previous = rounds[index - 1];
            sbyte winner = winners[index - 1];
            byte expectedStreak = winner < 0
                ? previous.DealerStreak
                : winner == previous.BankerSeat
                    ? (byte)(previous.DealerStreak + 1)
                    : (byte)1;
            Assert.Equal(expectedStreak, rounds[index].DealerStreak);
        }

        const ScoreActionType VariantBits = ScoreActionType.DealerStreak | ScoreActionType.FollowingTheBanker;
        for (int index = 0; index < driver.Events.Count; index++)
        {
            SessionEvent sessionEvent = driver.Events[index];
            if (sessionEvent.Kind != SessionEventKind.RoundEnded)
            {
                continue;
            }

            RoundEndedPayload round = SessionEventPayloadReader.ReadRoundEnded(sessionEvent);
            Assert.Equal(0, (int)(round.ActionType & VariantBits));
            AssertDeltaMatches(
                round.ScoreDelta,
                ExpectedDelta(
                    round,
                    RoundStartBefore(driver.Events, index),
                    KongsInRound(driver.Events, index),
                    disabled,
                    driver.Events,
                    index));
            Assert.Equal(0, round.ScoreDelta.Sum());
        }
    }

    [Fact]
    public void TryRevealHorse_EmptyWall_ReturnsFalseAndLeavesEveryZoneUntouched()
    {
        var state = new GameState();
        string before = StateHash.Compute(state);

        Assert.False(state.TryRevealHorse(out Tile none));
        Assert.Equal(default, none);
        Assert.Equal(0, state.Revealed.Count);
        Assert.Equal(0, state.Wall.Count);
        Assert.Equal(before, StateHash.Compute(state));

        // 有牌时从墙头取一张进揭示区：物理 id 不变、墙数 −1、区域 id 为公共揭示区。
        state.Wall.Append(RulesTestData.FromId(5));
        string withWall = StateHash.Compute(state);
        Assert.True(state.TryRevealHorse(out Tile revealed));
        Assert.Equal(5, revealed.Id);
        Assert.Equal(0, state.Wall.Count);
        Assert.Equal(1, state.Revealed.Count);
        Assert.Equal(5, state.Revealed[0].Id);
        Assert.Equal(new ZoneId(ZoneId.PublicSeat, ZoneKind.Revealed), state.Revealed.Id);
        Assert.NotEqual(withWall, StateHash.Compute(state));
    }

    [Fact]
    public void HorseRevealed_PrecedesRoundEndedInSameTick_AndMirrorsHorseWinnerBit()
    {
        ContentVariants variants = SessionTestSupport.Content.Variants;
        Assert.True(variants.HorseBonus > 0);

        int hits = 0;
        int misses = 0;
        int revealedRounds = 0;
        foreach (ulong seed in new ulong[] { 7, 20, 1, 77 })
        {
            GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed);
            session.Start();
            SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
            Assert.True(driver.RunToGameOver());

            for (int index = 0; index < driver.Events.Count; index++)
            {
                if (driver.Events[index].Kind != SessionEventKind.HorseRevealed)
                {
                    continue;
                }

                revealedRounds++;
                HorseRevealedPayload horse = SessionEventPayloadReader.ReadHorseRevealed(driver.Events[index]);

                // 顺序：HorseRevealed 紧邻 RoundEnded 之前、同 tick（RequestClosed < HorseRevealed < RoundEnded）。
                Assert.True(index + 1 < driver.Events.Count);
                SessionEvent next = driver.Events[index + 1];
                Assert.Equal(SessionEventKind.RoundEnded, next.Kind);
                Assert.Equal(driver.Events[index].Tick, next.Tick);

                // 载荷：物理牌 1–136，马座位 = 物理 id % 4；位与命中一致（一位规则，未命中不置位）。
                Assert.InRange(horse.TileId, (ushort)1, (ushort)136);
                Assert.Equal((byte)(horse.TileId % GameState.PlayerCount), horse.HorseSeat);
                RoundEndedPayload round = SessionEventPayloadReader.ReadRoundEnded(next);
                bool hit = horse.HorseSeat == round.WinnerSeat;
                Assert.Equal(hit, (round.ActionType & ScoreActionType.HorseWinner) != 0);
                if (hit)
                {
                    hits++;
                }
                else
                {
                    misses++;
                }
            }
        }

        // 样本必须同时覆盖命中与不命中，否则上面的位断言没有实际对照。
        Assert.True(revealedRounds > 0);
        Assert.True(hits > 0);
        Assert.True(misses > 0);
    }

    [Fact]
    public void SameSeed_Twice_ProducesSameHorseSequenceDeltasAndFinalStateHash()
    {
        (string firstHorse, string firstDeltas, string firstHash) = RunSeed(seed: 7);
        (string secondHorse, string secondDeltas, string secondHash) = RunSeed(seed: 7);

        Assert.NotEmpty(firstHorse);
        Assert.Equal(firstHorse, secondHorse);
        Assert.Equal(firstDeltas, secondDeltas);
        Assert.Equal(firstHash, secondHash);
    }

    /// <summary>跑完一整场并把翻马序列、逐局 delta 与终态哈希文本化，用于同 seed 两次比对。</summary>
    private (string Horse, string Deltas, string Hash) RunSeed(ulong seed)
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed);
        session.Start();
        SessionDriver driver = SessionTestSupport.CreateDriver(session, _fixture.Catalog);
        Assert.True(driver.RunToGameOver());

        string horse = string.Join(
            ';',
            driver.Events
                .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.HorseRevealed)
                .Select(sessionEvent => $"{sessionEvent.Tick}:{Convert.ToHexString(sessionEvent.Payload.Span)}"));
        string deltas = string.Join(
            ';',
            driver.Events
                .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)
                .Select(sessionEvent => string.Join(',', SessionEventPayloadReader.ReadRoundEnded(sessionEvent).ScoreDelta)));
        return (horse, deltas, SessionTestSupport.Hash(session));
    }

    /// <summary>取本局（上一个 RoundStarted 之后到 <paramref name="roundEndedIndex"/>）的杠事件。</summary>
    private static List<MeldPayload> KongsInRound(IReadOnlyList<SessionEvent> events, int roundEndedIndex)
    {
        int start = roundEndedIndex;
        while (start > 0 && events[start - 1].Kind != SessionEventKind.RoundStarted)
        {
            start--;
        }

        List<MeldPayload> kongs = [];
        for (int index = start; index < roundEndedIndex; index++)
        {
            if (events[index].Kind == SessionEventKind.Meld)
            {
                MeldPayload meld = SessionEventPayloadReader.ReadMeld(events[index]);
                if (meld.Kind != MeldKind.Pong)
                {
                    kongs.Add(meld);
                }
            }
        }

        return kongs;
    }

    /// <summary>取 <paramref name="index"/> 之前最近一条 RoundStarted 的 payload（本局开场快照）。</summary>
    private static RoundStartedPayload RoundStartBefore(IReadOnlyList<SessionEvent> events, int index)
    {
        for (int cursor = index - 1; cursor >= 0; cursor--)
        {
            if (events[cursor].Kind == SessionEventKind.RoundStarted)
            {
                return SessionEventPayloadReader.ReadRoundStarted(events[cursor]);
            }
        }

        throw new InvalidOperationException("RoundEnded 之前必须有 RoundStarted。");
    }

    /// <summary>连庄奖励表取值：<c>table[min(streak, 表长) - 1]</c>；空表（变体关闭）为 0。</summary>
    private static int DealerStreakBonus(IReadOnlyList<int> table, byte streak)
    {
        if (table.Count == 0)
        {
            return 0;
        }

        int index = Math.Min(streak, table.Count) - 1;
        return index < 0 ? 0 : table[index];
    }

    /// <summary>
    /// 承包 payer（按优先级取第一个命中的位，与结算单链同序）：抢杠 → 杠上开花承包 → 12 张承包。
    /// 抢杠不产 Meld 事件（补杠被取消），payer 取「自己回合出牌请求被 Kong 应答」的座位；
    /// 杠上开花承包取赢家最后一条明杠的供牌者；12 张承包取赢家第 4 副露的供牌者
    /// （补杠形态的事件不带目标座位，退回它升级的那副同牌种碰的供牌者）。无承包位返回 false。
    /// </summary>
    private static bool TryGetContractPayer(
        RoundEndedPayload round,
        IReadOnlyList<MeldPayload> kongs,
        IReadOnlyList<SessionEvent> events,
        int roundEndedIndex,
        out byte payer)
    {
        payer = Meld.NoTargetSeat;
        if (round.WinnerSeat < 0)
        {
            return false;
        }

        const ScoreActionType RobbingBits =
            ScoreActionType.RobbingTheKong | ScoreActionType.RobbingTheKongPayForEveryone;
        if ((round.ActionType & RobbingBits) != 0)
        {
            for (int index = roundEndedIndex - 1; index >= 0 && events[index].Kind != SessionEventKind.RoundStarted; index--)
            {
                if (events[index].Kind != SessionEventKind.RequestClosed)
                {
                    continue;
                }

                RequestClosedPayload closed = SessionEventPayloadReader.ReadRequestClosed(events[index]);
                if (closed.Outcome != RequestOutcomeKind.Answered || closed.Answer?.Verb != CommandVerb.Kong)
                {
                    continue;
                }

                for (int opened = index - 1; opened >= 0; opened--)
                {
                    if (events[opened].Kind != SessionEventKind.RequestOpened)
                    {
                        continue;
                    }

                    RequestOpenedPayload request = SessionEventPayloadReader.ReadRequestOpened(events[opened]);
                    if (request.RequestId != closed.RequestId || request.Kind != RequestKind.Discard)
                    {
                        continue;
                    }

                    payer = request.Seat;
                    return true;
                }
            }

            return false;
        }

        if ((round.ActionType & ScoreActionType.KongUpFlowerPayForEveryone) != 0)
        {
            for (int index = kongs.Count - 1; index >= 0; index--)
            {
                MeldPayload meld = kongs[index];
                if (meld.Seat == round.WinnerSeat
                    && meld.Kind == MeldKind.KongExposed
                    && meld.TargetSeat < GameState.PlayerCount)
                {
                    payer = meld.TargetSeat;
                    return true;
                }
            }

            return false;
        }

        if ((round.ActionType & ScoreActionType.TwelveCardsConceded) != 0)
        {
            List<MeldPayload> winnerMelds = [];
            for (int index = 0; index < roundEndedIndex; index++)
            {
                if (events[index].Kind != SessionEventKind.Meld)
                {
                    continue;
                }

                MeldPayload meld = SessionEventPayloadReader.ReadMeld(events[index]);
                if (meld.Seat == round.WinnerSeat)
                {
                    winnerMelds.Add(meld);
                }
            }

            if (winnerMelds.Count != GameState.PlayerCount)
            {
                return false;
            }

            MeldPayload fourth = winnerMelds[GameState.PlayerCount - 1];
            if (fourth.TargetSeat < GameState.PlayerCount)
            {
                payer = fourth.TargetSeat;
                return true;
            }

            if (fourth.Kind != MeldKind.KongSupplement || fourth.TileIds.Length == 0)
            {
                return false;
            }

            ushort kongType = (ushort)((fourth.TileIds[0] - 1) / 4);
            for (int index = 0; index < winnerMelds.Count - 1; index++)
            {
                MeldPayload pong = winnerMelds[index];
                if (pong.Kind != MeldKind.Pong
                    || pong.TileIds.Length == 0
                    || (ushort)((pong.TileIds[0] - 1) / 4) != kongType
                    || pong.TargetSeat >= GameState.PlayerCount)
                {
                    continue;
                }

                payer = pong.TargetSeat;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 按契约口径从结算 payload、本局 RoundStarted 与事件重算本局四座 delta：
    /// 杠分 + 自摸/七对 + 承包重定向（抢杠 / 杠上开花承包 / 12 张承包）+ 跟庄（赢家 ≠ 庄且变体开启）
    /// + 连庄奖励（赢家 = 庄且表值大于 0）+ 中马（变体开启、本局有 HorseRevealed 且马座位 = 赢家）。
    /// 变体金额取自内容目录，与结算实现同源；关闭时各项都不加。
    /// 返回候选列表：自摸/七对/12 张/杠上开花承包四支按 <see cref="ScoreActionType.NoJoker"/> 位取有无鬼（记账入口
    /// 按契约置/清该位）只返回一个候选；抢杠胡不置该位，<paramref name="winnerHasJoker"/> 未显式给值时不反推，
    /// 而是返回「无鬼 4」与「有鬼 2」（七对再 ×2）两个合法基分各一个候选，要求恰好一个与实测相等。
    /// </summary>
    /// <param name="round">本局结算 payload。</param>
    /// <param name="started">本局开场的 RoundStarted payload。</param>
    /// <param name="kongs">本局杠事件的 payload 序列。</param>
    /// <param name="variants">内容目录的规则变体。</param>
    /// <param name="events">整场事件流。</param>
    /// <param name="roundEndedIndex"><paramref name="round"/> 在 <paramref name="events"/> 中的下标。</param>
    /// <param name="winnerHasJoker">赢家手牌有无鬼牌；显式给值时优先使用，抢杠支据此锁定唯一基分。</param>
    private static IReadOnlyList<int[]> ExpectedDelta(
        RoundEndedPayload round,
        RoundStartedPayload started,
        IReadOnlyList<MeldPayload> kongs,
        ContentVariants variants,
        IReadOnlyList<SessionEvent> events,
        int roundEndedIndex,
        bool? winnerHasJoker = null)
    {
        int[] kongDelta = new int[GameState.PlayerCount];
        if (round.WinnerSeat < 0)
        {
            return [kongDelta];
        }

        foreach (MeldPayload kong in kongs)
        {
            switch (kong.Kind)
            {
                case MeldKind.KongExposed:
                    kongDelta[kong.Seat] += 3;
                    kongDelta[kong.TargetSeat] -= 3;
                    break;
                case MeldKind.KongConcealed:
                    for (byte seat = 0; seat < GameState.PlayerCount; seat++)
                    {
                        kongDelta[seat] = seat == kong.Seat ? kongDelta[seat] + 6 : kongDelta[seat] - 2;
                    }

                    break;
                case MeldKind.KongSupplement:
                    for (byte seat = 0; seat < GameState.PlayerCount; seat++)
                    {
                        kongDelta[seat] = seat == kong.Seat ? kongDelta[seat] + 3 : kongDelta[seat] - 1;
                    }

                    break;
            }
        }

        bool sevenPairs = (round.ActionType & ScoreActionType.SevenPairs) != 0;
        bool kongUpFlower = (round.ActionType & ScoreActionType.KongUpFlower) != 0;
        bool hasContract = TryGetContractPayer(round, kongs, events, roundEndedIndex, out byte contractPayer);

        var candidates = new List<int[]>();
        foreach (int per in CandidateShares(round, sevenPairs, kongUpFlower, winnerHasJoker))
        {
            int[] delta = (int[])kongDelta.Clone();
            for (byte seat = 0; seat < GameState.PlayerCount; seat++)
            {
                delta[seat] = seat == round.WinnerSeat
                    ? delta[seat] + (3 * per)
                    : delta[seat] - per;
            }

            // 承包重定向（按位判定，一次胡牌只取一个承包者）：抢杠 / 杠上开花承包 / 12 张承包都由一个 payer 付满 3 份。
            // 与普通自摸的「三家各付 per」等价于把另外两家的 per 收回 payer 头上，赢家金额不变。
            if (hasContract)
            {
                for (byte seat = 0; seat < GameState.PlayerCount; seat++)
                {
                    if (seat == round.WinnerSeat || seat == contractPayer)
                    {
                        continue;
                    }

                    delta[seat] += per;
                    delta[contractPayer] -= per;
                }
            }

            // 结算第 2 步跟庄：赢家不是庄家且变体开启；第 3 步连庄奖励：赢家就是庄家且本局表值大于 0。
            if (round.WinnerSeat != started.BankerSeat)
            {
                if (variants.FollowingBanker > 0)
                {
                    delta[round.WinnerSeat] += variants.FollowingBanker;
                    delta[started.BankerSeat] -= variants.FollowingBanker;
                }
            }
            else
            {
                int bonus = DealerStreakBonus(variants.DealerStreakBonus, started.DealerStreak);
                for (byte seat = 0; bonus > 0 && seat < GameState.PlayerCount; seat++)
                {
                    delta[seat] = seat == started.BankerSeat ? delta[seat] + (3 * bonus) : delta[seat] - bonus;
                }
            }

            // 结算第 4 步中马：变体开启且本局产了 HorseRevealed 才可能支付；马座位 = 物理 id % 4。
            if (variants.HorseBonus > 0 && TryGetHorseSeat(events, roundEndedIndex, out byte horseSeat)
                && horseSeat == round.WinnerSeat)
            {
                for (byte seat = 0; seat < GameState.PlayerCount; seat++)
                {
                    delta[seat] = seat == round.WinnerSeat
                        ? delta[seat] + (3 * variants.HorseBonus)
                        : delta[seat] - variants.HorseBonus;
                }
            }

            candidates.Add(delta);
        }

        return candidates;
    }

    /// <summary>
    /// 断言实测 delta 与候选期望恰好一个相等：非抢杠局候选唯一（等价于原「精确相等」断言）；
    /// 抢杠局在未显式给有无鬼时有两个合法基分候选，仍要求恰好命中一个。
    /// </summary>
    private static void AssertDeltaMatches(int[] actual, IReadOnlyList<int[]> candidates)
    {
        Assert.NotEmpty(candidates);
        Assert.Equal(1, candidates.Count(candidate => candidate.SequenceEqual(actual)));
    }

    /// <summary>
    /// 每份金额候选：显式有无鬼时只有一个；抢杠分支（不置 NoJoker，事件流无法分辨有无鬼）缺显式值时
    /// 两个合法基分都给；其余分支按 NoJoker 位取——这四个记账入口按契约置/清该位，位即事实。
    /// </summary>
    private static IReadOnlyList<int> CandidateShares(
        RoundEndedPayload round,
        bool sevenPairs,
        bool kongUpFlower,
        bool? winnerHasJoker)
    {
        const ScoreActionType RobbingBits =
            ScoreActionType.RobbingTheKong | ScoreActionType.RobbingTheKongPayForEveryone;
        int multiplier = kongUpFlower ? 2 : 1;

        if (winnerHasJoker is bool explicitJoker)
        {
            return [IntegrationSupport.PerShare(explicitJoker, sevenPairs, multiplier)];
        }

        if ((round.ActionType & RobbingBits) != 0)
        {
            return
            [
                IntegrationSupport.PerShare(winnerHasJoker: true, sevenPairs, multiplier),
                IntegrationSupport.PerShare(winnerHasJoker: false, sevenPairs, multiplier),
            ];
        }

        return [IntegrationSupport.PerShare((round.ActionType & ScoreActionType.NoJoker) == 0, sevenPairs, multiplier)];
    }

    /// <summary>取本局的 HorseRevealed 马座位：从 RoundEnded 往前扫到本局 RoundStarted 为止。</summary>
    private static bool TryGetHorseSeat(
        IReadOnlyList<SessionEvent> events,
        int roundEndedIndex,
        out byte horseSeat)
    {
        for (int index = roundEndedIndex - 1; index >= 0; index--)
        {
            if (events[index].Kind == SessionEventKind.RoundStarted)
            {
                break;
            }

            if (events[index].Kind == SessionEventKind.HorseRevealed)
            {
                horseSeat = SessionEventPayloadReader.ReadHorseRevealed(events[index]).HorseSeat;
                return true;
            }
        }

        horseSeat = byte.MaxValue;
        return false;
    }
}
