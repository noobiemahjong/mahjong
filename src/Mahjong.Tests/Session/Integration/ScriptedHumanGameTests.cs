using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Integration;

/// <summary>
/// 脚本化真人 8 局端到端：0 号真人每 Step 后 DrainEvents(0) 读事件，按固定策略应答每一次 RequestOpened
/// （Win 请求胡、出牌打第一张合法牌、响应窗口过），1–3 号 Bot 由会话托管；断言 8 条 RoundEnded、1 条 GameEnded、
/// 总分 = 逐局 delta 累加、换庄口径、脚本命令确实生效、真人赢家结算金额非零且无杠位局精确、每步牌数守恒。
/// </summary>
/// <remarks>
/// seed 由一次性 1–100 脚本化整场扫描选定：29 与 60 各出现 1 次真人自摸胡（真人胡极稀少，故必须按场景选种）。
/// 用例内以「humanWins &gt; 0 / humanWinRequests ≥ 1」显式守护该前置：洗牌或起庄语义变更时在此显式失败，
/// 而不是让赢家/金额断言悄悄空转。
/// </remarks>
[Collection("table-catalog")]
public sealed class ScriptedHumanGameTests
{
    private readonly TableCatalogFixture _fixture;

    public ScriptedHumanGameTests(TableCatalogFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(29)]
    [InlineData(60)]
    public void ScriptedHuman_CompletesEightRoundsWithConservedTiles(int seed)
    {
        IntegrationRun run = IntegrationSupport.RunScriptedSinglePlayer(
            _fixture.Catalog,
            (ulong)seed,
            viewerSeat: 0);

        Assert.True(run.GameOver, "脚本化单人会话必须在最大步数内产出 GameEnded。");
        Assert.True(run.Session.IsGameOver);
        Assert.True(run.StepCount < IntegrationSupport.DefaultMaxSteps);

        // 每步牌数守恒：墙 + 四家手牌 + 弃牌 + 副露 = 136。
        Assert.Equal(run.StepCount, run.TileCounts.Count);
        Assert.All(run.TileCounts, count => Assert.Equal(IntegrationSupport.TotalTileCount, count));

        // 8 局结构：RoundStarted 1..8、RoundEnded ×8、GameEnded 恰好一条且是最后一条。
        RoundStartedPayload[] rounds = run.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted)
            .Select(SessionEventPayloadReader.ReadRoundStarted)
            .ToArray();
        Assert.Equal(8, rounds.Length);
        Assert.Equal(Enumerable.Range(1, 8).Select(roundNo => (byte)roundNo), rounds.Select(round => round.RoundNo));
        Assert.All(rounds, round => Assert.Equal((ushort)84, round.WallCount));
        Assert.Equal(8, run.Events.Count(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded));
        SessionEvent gameEndedEvent = Assert.Single(
            run.Events,
            sessionEvent => sessionEvent.Kind == SessionEventKind.GameEnded);
        Assert.Equal(SessionEventKind.GameEnded, run.Events[^1].Kind);
        Assert.DoesNotContain(run.Events, sessionEvent => sessionEvent.Kind == SessionEventKind.HandSync);

        // 每局结算：零和、赢家自摸置位且金额为正、无杠位局精确核算、流局全 0 + None。
        RoundEndedPayload[] settlements = run.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)
            .Select(SessionEventPayloadReader.ReadRoundEnded)
            .ToArray();
        Assert.Equal(8, settlements.Length);
        Assert.All(settlements, IntegrationSupport.AssertSelfDrawnSettlement);

        // 真人赢家路径必须被执行：至少 1 个 WinnerSeat==0 的赢家局、金额非零。
        RoundEndedPayload[] humanWins = settlements.Where(round => round.WinnerSeat == 0).ToArray();
        Assert.NotEmpty(humanWins);
        Assert.All(humanWins, round => Assert.True(
            round.ScoreDelta[0] > 0,
            $"真人赢家局 delta[0] 必须为正，实际 {round.ScoreDelta[0]}。"));

        // 至少一个真人胡局没有杠位，确保「精确金额」分支真的执行（有杠位的局只做零和/符号断言）。
        const ScoreActionType KongFlags =
            ScoreActionType.ConcealedKong | ScoreActionType.ExposedKong | ScoreActionType.KongSupplement;
        Assert.Contains(humanWins, round => (round.ActionType & KongFlags) == 0);

        // 总分三口径一致：逐局 delta 累加 = GameEnded.TotalScores = PlayerState.Score 镜像；且非全 0。
        GameEndedPayload gameEnded = SessionEventPayloadReader.ReadGameEnded(gameEndedEvent);
        int[] accumulated = IntegrationSupport.AccumulateRoundDeltas(run.Events);
        Assert.Equal(accumulated, gameEnded.TotalScores);
        Assert.Equal(accumulated, run.MirrorScores.Select(score => (int)score).ToArray());
        Assert.Equal(0, gameEnded.TotalScores.Sum());
        Assert.Contains(gameEnded.TotalScores, score => score != 0);
        Assert.Equal(GameState.PlayerCount, gameEnded.Ranking.Distinct().Count());
        Assert.Equal(string.Empty, gameEnded.ReplayId);

        // 换庄：首局任意，之后赢家坐庄、流局庄家不变。
        IntegrationSupport.AssertBankerRotation(
            IntegrationSupport.RoundBankers(run.Events),
            IntegrationSupport.RoundWinners(run.Events));

        // 脚本的每次应答都被接受；真人请求全部 Answered 且携带对应命令（不是 Accepted 后石沉大海）。
        Assert.NotEmpty(run.SubmitResults);
        Assert.All(run.SubmitResults, result =>
        {
            Assert.True(result.Accepted, $"脚本应答被拒：{result.Reason}");
            Assert.Null(result.Reason);
            Assert.True(result.RequestId > 0);
        });

        var opened = run.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RequestOpened)
            .Select(SessionEventPayloadReader.ReadRequestOpened)
            .ToDictionary(request => request.RequestId);
        int humanOpenCount = opened.Values.Count(request => request.Seat == 0);
        Assert.True(humanOpenCount > 0, "脚本化真人应至少收到一次请求。");
        Assert.Equal(humanOpenCount, run.SubmitResults.Count);
        int humanWinRequests = opened.Values.Count(
            request => request.Seat == 0 && request.Kind == RequestKind.Win);
        Assert.True(humanWinRequests >= 1, "真人 Win 请求必须至少出现一次，否则脚本的胡分支未被覆盖。");

        var closed = run.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RequestClosed)
            .Select(SessionEventPayloadReader.ReadRequestClosed)
            .ToDictionary(closedRequest => closedRequest.RequestId);

        int humanDiscardAnswers = 0;
        int humanWinAnswers = 0;
        int humanClosedCount = 0;
        foreach (RequestOpenedPayload request in opened.Values.Where(request => request.Seat == 0))
        {
            Assert.True(closed.ContainsKey(request.RequestId), $"真人请求 {request.RequestId} 未被结算。");
            RequestClosedPayload closedRequest = closed[request.RequestId];
            humanClosedCount++;
            Assert.Equal(RequestOutcomeKind.Answered, closedRequest.Outcome);
            Assert.NotNull(closedRequest.Answer);

            CommandVerb expectedVerb = request.Kind switch
            {
                RequestKind.Win => CommandVerb.Win,
                RequestKind.Discard => CommandVerb.Discard,
                _ => CommandVerb.Pass,
            };
            Assert.Equal(expectedVerb, closedRequest.Answer!.Value.Verb);

            if (expectedVerb == CommandVerb.Discard)
            {
                humanDiscardAnswers++;
            }
            else if (expectedVerb == CommandVerb.Win)
            {
                humanWinAnswers++;
            }
        }

        Assert.Equal(humanOpenCount, humanClosedCount);
        Assert.Equal(humanWinRequests, humanWinAnswers);
        Assert.Equal(humanWins.Length, humanWinAnswers);
        Assert.True(humanWinAnswers > 0);

        // 命令确实落到局面：每个被接受的真人出牌对应一条本人 Discarded，每个胡对应一条本人赢的 RoundEnded。
        int humanDiscardEvents = run.Events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.Discarded)
            .Select(SessionEventPayloadReader.ReadDiscarded)
            .Count(discarded => discarded.Seat == 0);
        Assert.Equal(humanDiscardAnswers, humanDiscardEvents);

        Assert.Equal((byte)8, run.Session.State.RoundNo);
        Assert.InRange(run.Session.State.CurrentSeat, (byte)0, (byte)3);
    }

    [Fact]
    public void ScriptedHuman_MirrorScoresAndTileCountHoldAfterEveryStep()
    {
        // seed 29：1 次真人自摸胡；镜像写回在有非零金额的逐步路径上被验证，而不是只在全 0 上成立。
        GameSession session = SessionTestSupport.CreateSinglePlayer(_fixture.Catalog, seed: 29);
        IntegrationDriver driver = new(
            session,
            SessionTestSupport.CreateVerifier(_fixture.Catalog),
            viewerSeat: 0,
            humanSeat: 0,
            policy: IntegrationSupport.ScriptedHumanAnswer);
        driver.Start();

        int[] cumulative = new int[GameState.PlayerCount];
        int processed = 0;
        while (!session.IsGameOver)
        {
            Assert.True(driver.StepCount < IntegrationSupport.DefaultMaxSteps, "脚本化单人会话未在最大步数内结束。");
            driver.StepOnce();

            for (int index = processed; index < driver.Events.Count; index++)
            {
                if (driver.Events[index].Kind != SessionEventKind.RoundEnded)
                {
                    continue;
                }

                RoundEndedPayload round = SessionEventPayloadReader.ReadRoundEnded(driver.Events[index]);
                for (int seat = 0; seat < GameState.PlayerCount; seat++)
                {
                    cumulative[seat] += round.ScoreDelta[seat];
                }
            }

            processed = driver.Events.Count;

            Assert.Equal(IntegrationSupport.TotalTileCount, SessionTestSupport.CountTiles(session.State));
            for (byte seat = 0; seat < GameState.PlayerCount; seat++)
            {
                Assert.Equal((short)cumulative[seat], session.State.Players[seat].Score);
            }
        }

        // 终局：镜像与逐局累加、GameEnded 总分一致，且确实出现过非零金额。
        GameEndedPayload gameEnded = SessionEventPayloadReader.ReadGameEnded(
            Assert.Single(driver.Events, sessionEvent => sessionEvent.Kind == SessionEventKind.GameEnded));
        Assert.Equal(IntegrationSupport.AccumulateRoundDeltas(driver.Events), gameEnded.TotalScores);
        Assert.Equal(cumulative, gameEnded.TotalScores);
        Assert.Equal(gameEnded.TotalScores, IntegrationSupport.MirrorScores(session).Select(score => (int)score).ToArray());
        Assert.Contains(cumulative, value => value != 0);
        Assert.Contains(IntegrationSupport.RoundWinners(driver.Events), winner => winner == 0);
    }
}
