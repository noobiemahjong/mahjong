using Mahjong.Core.Rules;
using Mahjong.Tests.Content.Tables;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 轮次驱动契约（stub 胡牌判定/随机源）：起庄只在首次 RunRound 消耗一次 RNG、每局发牌后庄家摸第 14 张、
/// 命令按座位校验且单步原子、七对优先、命令耗尽按「第一个非鬼牌槽位」确定性补全、墙空流局回滚、
/// 庄家轮换（非庄赢换庄/庄赢连庄/流局连庄）、8 局循环与第 9 次调用抛异常、总分镜像与排名稳定序。
/// 整局与 8 局总分用手算锚点断言（标准自摸 +12/-4×3，8 局 +96/-32×3）。
/// </summary>
public sealed class RoundControllerTests
{
    [Fact]
    public void TotalRounds_IsEight()
    {
        Assert.Equal((byte)8, RoundController.TotalRounds);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new RoundController(null!, new Pcg32(1)));
        Assert.Throws<ArgumentNullException>(() => new RoundController(new StubWinChecker(), null!));
    }

    [Fact]
    public void NewController_StartsAtRoundOneWithEmptyScoresAndNoRngConsumption()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var controller = new RoundController(new StubWinChecker(), rng);

        Assert.Equal((byte)1, controller.RoundNo);
        Assert.False(controller.IsGameOver);
        Assert.Equal(0, rng.Calls);
        Assert.Equal(new short[] { 0, 0, 0, 0 }, controller.Scores.RoundDelta.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, controller.Scores.TotalScores.ToArray());
    }

    [Fact]
    public void FirstRunRound_DeterminesBankerFromFirstNextUInt()
    {
        // 首次返回 6，6 % 4 = 2；其余调用委托给 PCG32(42) 洗牌。
        var rng = new FirstValueRandomSource(firstValue: 6, seed: 42);
        var controller = new RoundController(new StubWinChecker(), rng);
        GameState state = new();

        RoundResult result = controller.RunRound(state, []);

        Assert.Equal((byte)2, result.BankerSeat);
        Assert.Equal((byte)2, controller.BankerSeat);
        Assert.True(result.IsDraw);
        Assert.Equal(0, state.Wall.Count);
        Assert.Equal(84, state.Players.Sum(player => player.Discards.Count));
        Assert.Equal(136, rng.Calls);
        Assert.Equal(135, rng.DelegateCalls);
    }

    [Fact]
    public void RunRound_OpeningState_HasDealtHandsAndBankerDraw()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = true, SevenPairs = false };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        RoundResult result = controller.RunRound(state, [RoundCommand.Win(0)]);

        // 庄家 0 第一手即胡：状态停在开局（发牌 84 张后庄家摸第 14 张，牌墙 83）。
        AssertDealtAndDrawState(state, banker: 0);
        Assert.Equal((byte)0, result.BankerSeat);
        Assert.Equal((byte)0, result.WinnerSeat);
        Assert.False(result.IsDraw);
        Assert.Equal(new short[] { 12, -4, -4, -4 }, result.ScoreDelta.ToArray());
        Assert.Equal(new short[] { 12, -4, -4, -4 }, result.TotalScores.ToArray());
        Assert.Equal(new short[] { 12, -4, -4, -4 }, RoundTestSupport.CopyScores(controller.Scores.TotalScores));
    }

    [Fact]
    public void EmptyCommands_PlayDeterministicDiscardsUntilWallExhaustionDraw()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var controller = new RoundController(new StubWinChecker(), rng);
        GameState state = new();

        RoundResult result = controller.RunRound(state, []);

        AssertDrawResult(result, roundNo: 1, bankerSeat: 0);
        Assert.Equal(0, state.Wall.Count);
        Assert.Equal(136, RoundTestSupport.TotalTiles(state));
        Assert.Equal(84, state.Players.Sum(player => player.Discards.Count));
        Assert.Equal((byte)1, state.RoundNo);
        Assert.Equal((byte)2, controller.RoundNo);
        Assert.Equal((byte)0, controller.BankerSeat);

        // 流局在下家摸牌与座位交接之前返回：最后出牌者是座位 3，CurrentSeat 保持 3；
        // IsBanker 只能来自本局 Deal，仍是庄家 0。
        Assert.Equal((byte)3, state.CurrentSeat);
        Assert.True(state.Players[0].IsBanker);
        Assert.False(state.Players[1].IsBanker);
        Assert.False(state.Players[2].IsBanker);
        Assert.False(state.Players[3].IsBanker);

        Assert.Equal(ScoreActionType.None, controller.Scores.RoundActionType);
        Assert.Equal(new short[] { 0, 0, 0, 0 }, controller.Scores.RoundDelta.ToArray());
        Assert.All(state.Players, player => Assert.Equal(0, (int)player.Score));
    }

    [Fact]
    public void EmptyCommands_SkipJokerTilesWhenChoosingDefaultDiscard()
    {
        // seed 16 + 庄家 0：庄家起手槽位 0 是鬼牌（物理 127，牌种 31），默认出牌必须跳过它。
        GameState probe = new();
        probe.Deal(new Pcg32(16), bankerSeat: 0);
        Assert.Equal(
            RoundTestSupport.JokerTypeId,
            probe.Players[0].Hand[0].Type.Id);

        var rng = new FirstValueRandomSource(firstValue: 0, seed: 16);
        var controller = new RoundController(new StubWinChecker(), rng, RoundTestSupport.Joker);
        GameState state = new();

        RoundResult result = controller.RunRound(state, []);

        AssertDrawResult(result, roundNo: 1, bankerSeat: 0);
        Assert.Equal(0, state.Wall.Count);
        foreach (PlayerState player in state.Players)
        {
            Assert.DoesNotContain(
                player.Discards.Items,
                tile => tile.Type.Id == RoundTestSupport.JokerTypeId);
        }
    }

    [Fact]
    public void EmptyCommands_DefaultDiscardTakesFirstNonJokerHandSlot()
    {
        // seed 16 + 庄家 0：起手槽 0 是鬼牌（物理 127，牌种 31），槽 1 是物理 36；
        // 「取第一个非鬼牌槽位」必须弃 36，取最后一个非鬼牌会弃别家牌并可被该断言判死。
        GameState probe = new();
        probe.Deal(new Pcg32(16), bankerSeat: 0);
        probe.DrawFromWall(0);
        Assert.Equal(RoundTestSupport.JokerTypeId, probe.Players[0].Hand[0].Type.Id);
        Assert.Equal((ushort)127, probe.Players[0].Hand[0].Id);
        Assert.Equal((ushort)36, probe.Players[0].Hand[1].Id);

        var rng = new FirstValueRandomSource(firstValue: 0, seed: 16);
        var controller = new RoundController(new StubWinChecker(), rng, RoundTestSupport.Joker);
        GameState state = new();

        controller.RunRound(state, []);

        Assert.Equal((ushort)36, state.Players[0].Discards[0].Id);

        // seed 42 无鬼：起手槽 0 即物理 75，首弃必须是 75（比对「跳过非鬼牌」之类错误实现）。
        GameState plainProbe = new();
        plainProbe.Deal(new Pcg32(42), bankerSeat: 0);
        plainProbe.DrawFromWall(0);
        Assert.Equal((ushort)75, plainProbe.Players[0].Hand[0].Id);

        var plainRng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var plainController = new RoundController(new StubWinChecker(), plainRng);
        GameState plainState = new();

        plainController.RunRound(plainState, []);

        Assert.Equal((ushort)75, plainState.Players[0].Discards[0].Id);
    }

    [Fact]
    public void ScriptedDiscard_IsAppliedBeforeDefaultCompletion()
    {
        GameState probe = new();
        probe.Deal(new Pcg32(42), bankerSeat: 0);
        Tile firstHandTile = probe.Players[0].Hand[0];

        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var controller = new RoundController(new StubWinChecker(), rng);
        GameState state = new();

        RoundResult result = controller.RunRound(state, [RoundCommand.Discard(0, 0)]);

        AssertDrawResult(result, roundNo: 1, bankerSeat: 0);
        Assert.Equal(firstHandTile.Id, state.Players[0].Discards[0].Id);
        Assert.Equal(84, state.Players.Sum(player => player.Discards.Count));
    }

    [Fact]
    public void SeatMismatch_ThrowsArgumentExceptionWithoutApplyingTheStep()
    {
        // 庄家由首次返回 5 决定为 1；命令却指向座位 0，必须在应用前被拒（不调用胡牌判定）。
        var rng = new FirstValueRandomSource(firstValue: 5, seed: 42);
        var controller = new RoundController(new ThrowingWinChecker(), rng);
        GameState state = new();

        Assert.Throws<ArgumentException>(() => controller.RunRound(state, [RoundCommand.Win(0)]));

        AssertDealtAndDrawState(state, banker: 1);
        AssertUnchangedFromOpening(state, banker: 1, seed: 42);
        Assert.Equal((byte)1, controller.RoundNo);
        Assert.All(state.Players, player => Assert.Equal(0, (int)player.Score));
    }

    [Fact]
    public void DiscardSlotOutOfRange_ThrowsInvalidOperationWithoutApplyingTheStep()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var controller = new RoundController(new StubWinChecker(), rng);
        GameState state = new();

        Assert.Throws<InvalidOperationException>(
            () => controller.RunRound(state, [RoundCommand.Discard(0, 99)]));

        AssertDealtAndDrawState(state, banker: 0);
        AssertUnchangedFromOpening(state, banker: 0, seed: 42);
        Assert.Equal((byte)1, controller.RoundNo);
    }

    [Fact]
    public void DiscardJoker_ThrowsInvalidOperationWithoutApplyingTheStep()
    {
        // seed 16 + 庄家 0：庄家起手槽位 0 是鬼牌；鬼牌不可打出。
        GameState probe = new();
        probe.Deal(new Pcg32(16), bankerSeat: 0);
        Assert.Equal(RoundTestSupport.JokerTypeId, probe.Players[0].Hand[0].Type.Id);

        var rng = new FirstValueRandomSource(firstValue: 0, seed: 16);
        var controller = new RoundController(new StubWinChecker(), rng, RoundTestSupport.Joker);
        GameState state = new();

        Assert.Throws<InvalidOperationException>(
            () => controller.RunRound(state, [RoundCommand.Discard(0, 0)]));

        AssertDealtAndDrawState(state, banker: 0);
        AssertUnchangedFromOpening(state, banker: 0, seed: 16);
        Assert.Empty(state.Players[0].Discards.Items);
        Assert.Equal((byte)1, controller.RoundNo);
    }

    [Fact]
    public void Win_NonWinningHand_ThrowsInvalidOperationWithoutApplyingTheStep()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = false, SevenPairs = false };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        Assert.Throws<InvalidOperationException>(() => controller.RunRound(state, [RoundCommand.Win(0)]));

        Assert.Equal(new[] { "IsSevenPairs", "IsWin" }, wins.Calls);
        AssertDealtAndDrawState(state, banker: 0);
        AssertUnchangedFromOpening(state, banker: 0, seed: 42);
        Assert.Equal((byte)1, controller.RoundNo);
        Assert.All(state.Players, player => Assert.Equal(0, (int)player.Score));
        Assert.Equal(new short[] { 0, 0, 0, 0 }, controller.Scores.TotalScores.ToArray());
    }

    [Fact]
    public void Win_SevenPairsPriority_ScoresSevenPairsNotStandard()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = true, SevenPairs = true };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        RoundResult result = controller.RunRound(state, [RoundCommand.Win(0)]);

        // 七对无鬼自摸：+24 / -8×3；同时保留 SelfDrawn；两种口径都成立时不按普通自摸双算。
        Assert.Equal(new[] { "IsSevenPairs", "IsWin" }, wins.Calls);
        Assert.Equal((byte)0, result.WinnerSeat);
        Assert.Equal(
            ScoreActionType.SelfDrawn | ScoreActionType.SevenPairs | ScoreActionType.NoJoker,
            result.ActionType);
        Assert.Equal(new short[] { 24, -8, -8, -8 }, result.ScoreDelta.ToArray());
        Assert.Equal(new short[] { 24, -8, -8, -8 }, result.TotalScores.ToArray());
        Assert.Equal(new short[] { 24, -8, -8, -8 }, RoundTestSupport.CopyScores(controller.Scores.TotalScores));
        Assert.Equal(0, result.ScoreDelta.Sum(value => (int)value));

        // P1-5：默认（无鬼）驱动的 joker 入参必须是 default（Id 0），hand 14、IsWin 的 melds 0。
        Assert.Equal(2, wins.Arguments.Count);
        Assert.Equal("IsSevenPairs", wins.Arguments[0].Method);
        Assert.Equal(14, wins.Arguments[0].HandCount);
        Assert.Null(wins.Arguments[0].MeldCount);
        Assert.Equal((ushort)0, wins.Arguments[0].JokerId);
        Assert.Equal("IsWin", wins.Arguments[1].Method);
        Assert.Equal(14, wins.Arguments[1].HandCount);
        Assert.Equal(0, wins.Arguments[1].MeldCount);
        Assert.Equal((ushort)0, wins.Arguments[1].JokerId);
    }

    [Fact]
    public void Win_WithJokerInHand_ScoresJokerRate()
    {
        // seed 0 + 庄家 0：起手含鬼牌（物理 126，牌种 31），自摸按有鬼 2 分/家结算。
        GameState probe = new();
        probe.Deal(new Pcg32(0), bankerSeat: 0);
        probe.DrawFromWall(0);
        Assert.Contains(probe.Players[0].Hand.Items, tile => tile.Type.Id == RoundTestSupport.JokerTypeId);

        var rng = new FirstValueRandomSource(firstValue: 0, seed: 0);
        var wins = new StubWinChecker { Win = true, SevenPairs = false };
        var controller = new RoundController(wins, rng, RoundTestSupport.Joker);
        GameState state = new();

        RoundResult result = controller.RunRound(state, [RoundCommand.Win(0)]);

        Assert.Equal(new short[] { 6, -2, -2, -2 }, result.ScoreDelta.ToArray());
        Assert.Equal(ScoreActionType.SelfDrawn, result.ActionType);
        Assert.Equal(0, result.ScoreDelta.Sum(value => (int)value));

        // P1-5：把 joker 传成 default、或把别家手牌传进判定，都会被这组入参断言判死。
        Assert.Equal(2, wins.Arguments.Count);
        WinCheckerCall sevenPairsCall = wins.Arguments[0];
        Assert.Equal("IsSevenPairs", sevenPairsCall.Method);
        Assert.Equal(14, sevenPairsCall.HandCount);
        Assert.Null(sevenPairsCall.MeldCount);
        Assert.Equal((ushort)RoundTestSupport.JokerTypeId, sevenPairsCall.JokerId);
        Assert.Equal(probe.Players[0].Hand[0].Id, sevenPairsCall.FirstTileId);

        WinCheckerCall winCall = wins.Arguments[1];
        Assert.Equal("IsWin", winCall.Method);
        Assert.Equal(14, winCall.HandCount);
        Assert.Equal(0, winCall.MeldCount);
        Assert.Equal((ushort)RoundTestSupport.JokerTypeId, winCall.JokerId);
        Assert.Equal(probe.Players[0].Hand[0].Id, winCall.FirstTileId);
    }

    [Fact]
    public void Win_WithDefaultJokerArgument_TreatsJokerTilesAsOrdinaryTiles()
    {
        // 同一牌序（含牌种 31 的实体牌），但驱动未注入鬼牌：不得把该实体牌当鬼，按无鬼 4 分/家结算。
        GameState probe = new();
        probe.Deal(new Pcg32(0), bankerSeat: 0);
        probe.DrawFromWall(0);
        Assert.Contains(probe.Players[0].Hand.Items, tile => tile.Type.Id == RoundTestSupport.JokerTypeId);

        var rng = new FirstValueRandomSource(firstValue: 0, seed: 0);
        var wins = new StubWinChecker { Win = true, SevenPairs = false };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        RoundResult result = controller.RunRound(state, [RoundCommand.Win(0)]);

        Assert.Equal(new short[] { 12, -4, -4, -4 }, result.ScoreDelta.ToArray());
        Assert.Equal(ScoreActionType.SelfDrawn | ScoreActionType.NoJoker, result.ActionType);
    }

    [Fact]
    public void Win_OnlySevenPairs_IsAcceptedWithoutRegularWin()
    {
        // 真实七对多数不构常规胡：IsSevenPairs=true 且 IsWin=false 必须仍按七对结算；
        // 「七对也要求常规胡」的错误实现会在这里抛 InvalidOperationException。
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = false, SevenPairs = true };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        RoundResult result = controller.RunRound(state, [RoundCommand.Win(0)]);

        Assert.Equal(new short[] { 24, -8, -8, -8 }, result.ScoreDelta.ToArray());
        Assert.Equal(
            ScoreActionType.SelfDrawn | ScoreActionType.SevenPairs | ScoreActionType.NoJoker,
            result.ActionType);
        Assert.Equal(new[] { "IsSevenPairs", "IsWin" }, wins.Calls);

        // 有鬼变体：七对有鬼 = +12 / -4×3，且不带 NoJoker。
        var jokerRng = new FirstValueRandomSource(firstValue: 0, seed: 0);
        var jokerWins = new StubWinChecker { Win = false, SevenPairs = true };
        var jokerController = new RoundController(jokerWins, jokerRng, RoundTestSupport.Joker);

        RoundResult jokerResult = jokerController.RunRound(new GameState(), [RoundCommand.Win(0)]);

        Assert.Equal(new short[] { 12, -4, -4, -4 }, jokerResult.ScoreDelta.ToArray());
        Assert.Equal(
            ScoreActionType.SelfDrawn | ScoreActionType.SevenPairs,
            jokerResult.ActionType);
        Assert.Equal((ushort)RoundTestSupport.JokerTypeId, jokerWins.Arguments[0].JokerId);
    }

    [Fact]
    public void Win_WithDefaultJoker_DoesNotTreatTileTypeZeroAsJoker()
    {
        // seed 27 + 庄家 0：14 张手牌含牌种 0（一万，物理 1），不含牌种 31。
        // default(TileType) 的 Suit 不在 1–5，不得按 Id 0 命中一万；错误实现 IsJokerActive 恒真会给 [6,-2,-2,-2]
        // 且缺 NoJoker 标记。
        GameState probe = new();
        probe.Deal(new Pcg32(27), bankerSeat: 0);
        probe.DrawFromWall(0);
        Assert.Contains(probe.Players[0].Hand.Items, tile => tile.Type.Id == 0);

        var rng = new FirstValueRandomSource(firstValue: 0, seed: 27);
        var wins = new StubWinChecker { Win = true, SevenPairs = false };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        RoundResult result = controller.RunRound(state, [RoundCommand.Win(0)]);

        Assert.Equal(new short[] { 12, -4, -4, -4 }, result.ScoreDelta.ToArray());
        Assert.Equal(ScoreActionType.SelfDrawn | ScoreActionType.NoJoker, result.ActionType);
        Assert.Equal((ushort)0, wins.Arguments[1].JokerId);
    }

    [Fact]
    public void Win_NonBanker_BecomesNextBanker()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = true, SevenPairs = false };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        RoundResult result = controller.RunRound(state, [RoundCommand.Discard(0, 0), RoundCommand.Win(1)]);

        Assert.Equal((byte)0, result.BankerSeat);
        Assert.Equal((byte)1, result.WinnerSeat);
        Assert.Equal((byte)1, controller.BankerSeat);
        Assert.Equal((byte)2, controller.RoundNo);
        Assert.Equal(new short[] { -4, 12, -4, -4 }, result.ScoreDelta.ToArray());
        Assert.Equal(new short[] { -4, 12, -4, -4 }, RoundTestSupport.CopyScores(controller.Scores.TotalScores));
        Assert.Equal(12, (int)state.Players[1].Score);

        // IsBanker 只能来自本局 Deal：本局庄家是 0，轮换后的下一局庄家只在控制器内，未回写局面。
        Assert.True(state.Players[0].IsBanker);
        Assert.False(state.Players[1].IsBanker);
    }

    [Fact]
    public void Win_WinnerNotAdjacentToBanker_BecomesBankerAndDealsNextRound()
    {
        // 第 1 局庄家 0；座位 0、1 各弃一张后座位 2 自摸。赢家 2 不是「庄家+1」（座位 1），
        // 因此能区分「换庄=赢家」与「换庄=庄家下家」。
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = true, SevenPairs = false };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        RoundResult first = controller.RunRound(
            state,
            [RoundCommand.Discard(0, 0), RoundCommand.Discard(1, 0), RoundCommand.Win(2)]);

        Assert.Equal((byte)0, first.BankerSeat);
        Assert.Equal((byte)2, first.WinnerSeat);
        Assert.Equal((byte)2, controller.BankerSeat);
        Assert.Equal(new short[] { -4, -4, 12, -4 }, first.ScoreDelta.ToArray());

        // 第 2 局必须由新庄 2 发牌：首条命令即 Win，状态停在开局快照。
        RoundResult second = controller.RunRound(state, [RoundCommand.Win(2)]);

        Assert.Equal((byte)2, second.BankerSeat);
        Assert.Equal((byte)2, second.WinnerSeat);
        Assert.Equal((byte)2, state.CurrentSeat);
        AssertDealtAndDrawState(state, banker: 2);
        Assert.Equal(new short[] { -4, -4, 12, -4 }, second.ScoreDelta.ToArray());
        Assert.Equal(new short[] { -8, -8, 24, -8 }, second.TotalScores.ToArray());

        // 赢家为庄 → 连庄。
        Assert.Equal((byte)2, controller.BankerSeat);
    }

    [Fact]
    public void Win_Banker_KeepsBanker()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = true, SevenPairs = false };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        RoundResult result = controller.RunRound(state, [RoundCommand.Win(0)]);

        Assert.Equal((byte)0, result.BankerSeat);
        Assert.Equal((byte)0, controller.BankerSeat);
    }

    [Fact]
    public void RunRound_RejectsNullArguments()
    {
        var controller = new RoundController(new StubWinChecker(), new Pcg32(1));

        Assert.Throws<ArgumentNullException>(() => controller.RunRound(null!, []));
        Assert.Throws<ArgumentNullException>(() => controller.RunRound(new GameState(), null!));
    }

    [Fact]
    public void EightRounds_WriteMirroredScoresAfterEachRoundAndStopGame()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = true, SevenPairs = false };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        for (byte round = 1; round <= RoundController.TotalRounds; round++)
        {
            RoundResult result = controller.RunRound(state, [RoundCommand.Win(0)]);

            Assert.Equal(round, result.RoundNo);
            Assert.Equal((byte)0, result.BankerSeat);
            Assert.Equal((byte)0, result.WinnerSeat);
            Assert.Equal(new short[] { 12, -4, -4, -4 }, result.ScoreDelta.ToArray());

            short[] expectedTotals = [checked((short)(12 * round)), checked((short)(-4 * round)), checked((short)(-4 * round)), checked((short)(-4 * round))];
            Assert.Equal(expectedTotals, result.TotalScores.ToArray());
            Assert.Equal(expectedTotals, RoundTestSupport.CopyScores(controller.Scores.TotalScores));
            for (byte seat = 0; seat < GameState.PlayerCount; seat++)
            {
                Assert.Equal(controller.Scores.TotalScores[seat], state.Players[seat].Score);
            }

            Assert.Equal((byte)(round + 1), controller.RoundNo);
        }

        // 8 局标准自摸手算锚点：赢家 +96，其余三家各 -32。
        Assert.Equal(new short[] { 96, -32, -32, -32 }, RoundTestSupport.CopyScores(controller.Scores.TotalScores));
        Assert.True(controller.IsGameOver);
        Assert.Equal((byte)9, controller.RoundNo);
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, controller.Rank());
        Assert.Equal((byte)8, state.RoundNo);
    }

    [Fact]
    public void NinthRound_ThrowsAndLeavesStateUnchanged()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = true, SevenPairs = false };
        var controller = new RoundController(wins, rng);
        GameState state = new();
        for (int round = 0; round < RoundController.TotalRounds; round++)
        {
            controller.RunRound(state, [RoundCommand.Win(0)]);
        }

        string snapshot = RoundTestSupport.Snapshot(state);
        string hash = RoundTestSupport.Hash(state);

        Assert.Throws<InvalidOperationException>(() => controller.RunRound(state, []));

        Assert.Equal(snapshot, RoundTestSupport.Snapshot(state));
        Assert.Equal(hash, RoundTestSupport.Hash(state));
        Assert.Equal((byte)9, controller.RoundNo);
        Assert.True(controller.IsGameOver);
    }

    [Fact]
    public void RoundResult_SnapshotsScoresBeforeReleaseAndKeepsEarlierResultsStable()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = true, SevenPairs = false };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        RoundResult first = controller.RunRound(state, [RoundCommand.Win(0)]);

        // Release 已把账本清零，结果里的 delta/番种必须是 Release 之前的快照。
        Assert.Equal(new short[] { 0, 0, 0, 0 }, controller.Scores.RoundDelta.ToArray());
        Assert.Equal(ScoreActionType.None, controller.Scores.RoundActionType);
        Assert.Equal(new short[] { 12, -4, -4, -4 }, first.ScoreDelta.ToArray());
        Assert.Equal(ScoreActionType.SelfDrawn | ScoreActionType.NoJoker, first.ActionType);

        RoundResult second = controller.RunRound(state, [RoundCommand.Discard(0, 0), RoundCommand.Win(1)]);

        // 第二局后第一局的结果快照不变；第二局按座位 1 获胜累计。
        Assert.Equal(new short[] { 12, -4, -4, -4 }, first.TotalScores.ToArray());
        Assert.Equal(new short[] { -4, 12, -4, -4 }, second.ScoreDelta.ToArray());
        Assert.Equal(new short[] { 8, 8, -8, -8 }, second.TotalScores.ToArray());
        Assert.Equal(new short[] { 8, 8, -8, -8 }, RoundTestSupport.CopyScores(controller.Scores.TotalScores));
    }

    [Fact]
    public void RoundResult_ScoreListsAreReadOnlySnapshots()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = true, SevenPairs = true };
        var controller = new RoundController(wins, rng);

        RoundResult result = controller.RunRound(new GameState(), [RoundCommand.Win(0)]);

        Assert.Equal(4, result.ScoreDelta.Count);
        Assert.Equal(4, result.TotalScores.Count);

        // 契约允许拷贝或只读包装：写入尝试无论成功与否，结果对象里的值都必须保持不变。
        RoundTestSupport.TryWriteScore(result.ScoreDelta, index: 0, value: 99);
        RoundTestSupport.TryWriteScore(result.TotalScores, index: 0, value: 99);

        Assert.Equal(new short[] { 24, -8, -8, -8 }, result.ScoreDelta.ToArray());
        Assert.Equal(new short[] { 24, -8, -8, -8 }, result.TotalScores.ToArray());
    }

    [Fact]
    public void DrawResult_UsesDocumentedNoWinnerSeatAndZeroDelta()
    {
        Assert.Equal((byte)255, RoundResult.NoWinnerSeat);

        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var controller = new RoundController(new StubWinChecker(), rng);

        RoundResult result = controller.RunRound(new GameState(), []);

        Assert.Equal((byte)255, result.WinnerSeat);
        Assert.True(result.IsDraw);
        Assert.Equal(ScoreActionType.None, result.ActionType);
        Assert.Equal(4, result.ScoreDelta.Count);
        Assert.Equal(4, result.TotalScores.Count);
        Assert.Equal(new short[] { 0, 0, 0, 0 }, result.ScoreDelta.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, result.TotalScores.ToArray());
    }

    [Fact]
    public void DrawRound_AfterScoringRound_LeavesTotalsUnchangedAndKeepsBanker()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var wins = new StubWinChecker { Win = true, SevenPairs = false };
        var controller = new RoundController(wins, rng);
        GameState state = new();

        RoundResult win = controller.RunRound(state, [RoundCommand.Win(0)]);
        Assert.Equal(new short[] { 12, -4, -4, -4 }, win.TotalScores.ToArray());

        RoundResult draw = controller.RunRound(state, []);

        // 流局先回滚再 Release：本局净 0，上一局累计与庄家不变。
        Assert.True(draw.IsDraw);
        Assert.Equal(new short[] { 12, -4, -4, -4 }, draw.TotalScores.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, draw.ScoreDelta.ToArray());
        Assert.Equal(new short[] { 12, -4, -4, -4 }, RoundTestSupport.CopyScores(controller.Scores.TotalScores));
        Assert.Equal((byte)0, controller.BankerSeat);
        Assert.Equal(12, (int)state.Players[0].Score);
        Assert.Equal(-4, (int)state.Players[1].Score);
    }

    [Fact]
    public void WinCommand_EndsRoundAndIgnoresTrailingCommands()
    {
        var withTailRng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var withTailWins = new StubWinChecker { Win = true, SevenPairs = false };
        var withTailController = new RoundController(withTailWins, withTailRng);
        GameState withTailState = new();
        RoundResult withTail = withTailController.RunRound(
            withTailState,
            [RoundCommand.Win(0), RoundCommand.Discard(0, 0)]);

        var plainRng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var plainWins = new StubWinChecker { Win = true, SevenPairs = false };
        var plainController = new RoundController(plainWins, plainRng);
        GameState plainState = new();
        RoundResult plain = plainController.RunRound(plainState, [RoundCommand.Win(0)]);

        // 赢家局在 Win 命令处结束：其后剩余命令不消费、不校验（也不抛座位/槽位异常）。
        Assert.Equal(plain.RoundNo, withTail.RoundNo);
        Assert.Equal(plain.BankerSeat, withTail.BankerSeat);
        Assert.Equal(plain.WinnerSeat, withTail.WinnerSeat);
        Assert.Equal(plain.ScoreDelta.ToArray(), withTail.ScoreDelta.ToArray());
        Assert.Equal(plain.TotalScores.ToArray(), withTail.TotalScores.ToArray());
        Assert.Equal(RoundTestSupport.Snapshot(plainState), RoundTestSupport.Snapshot(withTailState));
        Assert.Equal(RoundTestSupport.Hash(plainState), RoundTestSupport.Hash(withTailState));
    }

    [Fact]
    public void Rank_OrdersByScoreDescendingAndTiesBySeatAscending()
    {
        var controller = new RoundController(new StubWinChecker(), new Pcg32(1));
        controller.Scores.Apply(0, 5, ScoreActionType.None);
        controller.Scores.Apply(1, -5, ScoreActionType.None);
        controller.Scores.Apply(2, 0, ScoreActionType.None);
        controller.Scores.Apply(3, 0, ScoreActionType.None);
        controller.Scores.Release();

        IReadOnlyList<byte> rank = controller.Rank();

        // 总分 [5, -5, 0, 0]：0 最高，座位 2、3 同分按座位升序，1 最低。
        Assert.Equal(4, rank.Count);
        Assert.Equal(new byte[] { 0, 2, 3, 1 }, rank);
        Assert.Equal(new short[] { 5, -5, 0, 0 }, RoundTestSupport.CopyScores(controller.Scores.TotalScores));

        // 纯函数：重复调用结果一致且不改变总分。
        Assert.Equal(new byte[] { 0, 2, 3, 1 }, controller.Rank());
        Assert.Equal(new short[] { 5, -5, 0, 0 }, RoundTestSupport.CopyScores(controller.Scores.TotalScores));
    }

    [Fact]
    public void Rank_AllTied_OrdersBySeatAscending()
    {
        var controller = new RoundController(new StubWinChecker(), new Pcg32(1));

        Assert.Equal(new byte[] { 0, 1, 2, 3 }, controller.Rank());
    }

    [Fact]
    public void UndefinedCommandKind_ThrowsArgumentOutOfRangeWithoutApplyingTheStep()
    {
        var rng = new FirstValueRandomSource(firstValue: 0, seed: 42);
        var controller = new RoundController(new StubWinChecker(), rng);
        GameState state = new();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => controller.RunRound(state, [new RoundCommand(0, (RoundCommandKind)3)]));

        AssertDealtAndDrawState(state, banker: 0);
        AssertUnchangedFromOpening(state, banker: 0, seed: 42);
        Assert.Equal((byte)1, controller.RoundNo);
    }

    [Fact]
    public void RoundCommandFactories_CarrySeatKindAndSlot()
    {
        RoundCommand discard = RoundCommand.Discard(2, 7);
        RoundCommand win = RoundCommand.Win(3);

        Assert.Equal((byte)2, discard.Seat);
        Assert.Equal(RoundCommandKind.Discard, discard.Kind);
        Assert.Equal(7, discard.Slot);
        Assert.Equal((byte)3, win.Seat);
        Assert.Equal(RoundCommandKind.Win, win.Kind);
        Assert.Equal(0, win.Slot);
    }

    /// <summary>断言「发牌 + 庄家摸第 14 张」后的开局快照：牌墙 83、庄家 14、其余 13、当前座位=庄家、仅庄家 IsBanker。</summary>
    private static void AssertDealtAndDrawState(GameState state, byte banker)
    {
        Assert.Equal(83, state.Wall.Count);
        Assert.Equal(136, RoundTestSupport.TotalTiles(state));
        Assert.Equal(banker, state.CurrentSeat);
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            PlayerState player = state.Players[seat];
            Assert.Equal(seat == banker ? 14 : 13, player.Hand.Count);
            Assert.Equal(seat == banker, player.IsBanker);
            Assert.Empty(player.Discards.Items);
            Assert.Empty(player.Melds);
        }
    }

    /// <summary>
    /// 用独立重建的开局参照比对「状态没有半改」：同 seed、同庄家重新 Deal + 庄家摸牌，
    /// 逐区域快照与 <c>StateHash</c> 都必须与调用失败后的局面完全一致。
    /// </summary>
    private static void AssertUnchangedFromOpening(GameState state, byte banker, ulong seed)
    {
        GameState reference = new();
        reference.RoundNo = 1;
        reference.Deal(new Pcg32(seed), banker);
        reference.DrawFromWall(banker);

        Assert.Equal(RoundTestSupport.Snapshot(reference), RoundTestSupport.Snapshot(state));
        Assert.Equal(RoundTestSupport.Hash(reference), RoundTestSupport.Hash(state));
    }

    /// <summary>断言流局结果：赢家 255、IsDraw、番种 None、delta 全 0、总分全 0。</summary>
    private static void AssertDrawResult(RoundResult result, byte roundNo, byte bankerSeat)
    {
        Assert.Equal(roundNo, result.RoundNo);
        Assert.Equal(bankerSeat, result.BankerSeat);
        Assert.Equal(RoundResult.NoWinnerSeat, result.WinnerSeat);
        Assert.True(result.IsDraw);
        Assert.Equal(ScoreActionType.None, result.ActionType);
        Assert.Equal(new short[] { 0, 0, 0, 0 }, result.ScoreDelta.ToArray());
        Assert.Equal(new short[] { 0, 0, 0, 0 }, result.TotalScores.ToArray());
    }
}

/// <summary>
/// 真实查表下的轮次契约：注入真实 <see cref="WinChecker"/> 与 <see cref="TableCatalogFixture"/>，
/// 用空命令列表跑满 8 局全流局，钉住发牌/牌墙/局号/总分/排名的端到端事实与「同 seed 同哈希、异 seed 异哈希」的确定性。
/// </summary>
[Collection("table-catalog")]
public sealed class RoundControllerTableTests
{
    private readonly WinChecker _checker;

    public RoundControllerTableTests(TableCatalogFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _checker = new WinChecker(fixture.Catalog);
    }

    [Fact]
    public void RealChecker_EightDrawRounds_KeepScoresZeroAndEndGame()
    {
        RoundController controller = new(_checker, new Pcg32(42), RoundTestSupport.Joker);
        GameState state = new();

        for (byte round = 1; round <= RoundController.TotalRounds; round++)
        {
            RoundResult result = controller.RunRound(state, []);

            Assert.True(result.IsDraw);
            Assert.Equal(round, result.RoundNo);
            Assert.Equal(RoundResult.NoWinnerSeat, result.WinnerSeat);
            Assert.Equal(ScoreActionType.None, result.ActionType);
            Assert.Equal(new short[] { 0, 0, 0, 0 }, result.ScoreDelta.ToArray());
            Assert.Equal(new short[] { 0, 0, 0, 0 }, result.TotalScores.ToArray());
            Assert.Equal(0, state.Wall.Count);
            for (byte seat = 0; seat < GameState.PlayerCount; seat++)
            {
                Assert.Equal(0, (int)state.Players[seat].Score);
                Assert.Equal(controller.Scores.TotalScores[seat], state.Players[seat].Score);
            }

            Assert.Equal((byte)(round + 1), controller.RoundNo);
        }

        Assert.True(controller.IsGameOver);
        Assert.Equal((byte)9, controller.RoundNo);
        Assert.Equal((byte)1, controller.BankerSeat);
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, controller.Rank());
    }

    [Fact]
    public void SameSeedAndScript_ProduceSameFinalStateHashAndScores()
    {
        (GameState stateA, RoundController controllerA) = PlayEightDrawRounds(seed: 42);
        (GameState stateB, RoundController controllerB) = PlayEightDrawRounds(seed: 42);

        Assert.Equal(RoundTestSupport.Hash(stateA), RoundTestSupport.Hash(stateB));
        Assert.Equal(RoundTestSupport.Snapshot(stateA), RoundTestSupport.Snapshot(stateB));
        Assert.Equal(
            RoundTestSupport.CopyScores(controllerA.Scores.TotalScores),
            RoundTestSupport.CopyScores(controllerB.Scores.TotalScores));
        Assert.Equal(controllerA.RoundNo, controllerB.RoundNo);
        Assert.Equal(controllerA.BankerSeat, controllerB.BankerSeat);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentBankerAndFinalStateHash()
    {
        (GameState stateA, RoundController controllerA) = PlayEightDrawRounds(seed: 42);
        (GameState stateB, RoundController controllerB) = PlayEightDrawRounds(seed: 20261029);

        // Pcg32(42).NextUInt() % 4 = 1，Pcg32(20261029).NextUInt() % 4 = 3，起庄不同。
        Assert.Equal((byte)1, controllerA.BankerSeat);
        Assert.Equal((byte)3, controllerB.BankerSeat);
        Assert.NotEqual(RoundTestSupport.Hash(stateA), RoundTestSupport.Hash(stateB));
        Assert.Equal(
            RoundTestSupport.CopyScores(controllerA.Scores.TotalScores),
            RoundTestSupport.CopyScores(controllerB.Scores.TotalScores));
    }

    private (GameState State, RoundController Controller) PlayEightDrawRounds(ulong seed)
    {
        RoundController controller = new(_checker, new Pcg32(seed), RoundTestSupport.Joker);
        GameState state = new();
        for (byte round = 1; round <= RoundController.TotalRounds; round++)
        {
            RoundResult result = controller.RunRound(state, []);
            Assert.Equal(round, result.RoundNo);
            Assert.True(result.IsDraw);
        }

        Assert.True(controller.IsGameOver);
        return (state, controller);
    }
}
