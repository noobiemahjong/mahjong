using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
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
                List<MeldPayload> kongs = KongsInRound(driver.Events, eventCursor);
                int[] expected = ExpectedDelta(round, kongs);
                Assert.Equal(expected, round.ScoreDelta);
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
        Assert.Equal(ExpectedDelta(round, KongsInRound(driver.Events, index)), round.ScoreDelta);

        // 七对金额：per = (有鬼 ? 2 : 4) × 2；赢家 +3per、其余 -per，叠加本局已发生的杠分。
        bool hasJoker = (round.ActionType & ScoreActionType.NoJoker) == 0;
        int per = (hasJoker ? 2 : 4) * 2;
        int kongDeltaWinner = round.ScoreDelta[round.WinnerSeat] - (3 * per);
        Assert.True(kongDeltaWinner >= 0);
    }

    [Fact]
    public void KongUpFlower_IsSetOnlyWhenWinnersLastDrawWasReplacement()
    {
        // seed 7 第 1 局：0 号明杠后补牌自摸 → 置 KongUpFlower，且杠分与开花倍数同时成立。
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
        Assert.Equal(ExpectedDelta(up.Payload, KongsInRound(driver.Events, up.Index)), up.Payload.ScoreDelta);

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

    /// <summary>按契约口径从结算 payload 与事件重算本局四座 delta：杠分 + 自摸/七对。</summary>
    private static int[] ExpectedDelta(RoundEndedPayload round, IReadOnlyList<MeldPayload> kongs)
    {
        int[] delta = new int[GameState.PlayerCount];
        if (round.WinnerSeat < 0)
        {
            return delta;
        }

        foreach (MeldPayload kong in kongs)
        {
            switch (kong.Kind)
            {
                case MeldKind.KongExposed:
                    delta[kong.Seat] += 3;
                    delta[kong.TargetSeat] -= 3;
                    break;
                case MeldKind.KongConcealed:
                    for (byte seat = 0; seat < GameState.PlayerCount; seat++)
                    {
                        delta[seat] = seat == kong.Seat ? delta[seat] + 6 : delta[seat] - 2;
                    }

                    break;
                case MeldKind.KongSupplement:
                    for (byte seat = 0; seat < GameState.PlayerCount; seat++)
                    {
                        delta[seat] = seat == kong.Seat ? delta[seat] + 3 : delta[seat] - 1;
                    }

                    break;
            }
        }

        bool hasJoker = (round.ActionType & ScoreActionType.NoJoker) == 0;
        bool sevenPairs = (round.ActionType & ScoreActionType.SevenPairs) != 0;
        bool kongUpFlower = (round.ActionType & ScoreActionType.KongUpFlower) != 0;
        int per = sevenPairs
            ? (hasJoker ? 2 : 4) * 2
            : (hasJoker ? 2 : 4) * (kongUpFlower ? 2 : 1);
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            delta[seat] = seat == round.WinnerSeat
                ? delta[seat] + (3 * per)
                : delta[seat] - per;
        }

        return delta;
    }
}
