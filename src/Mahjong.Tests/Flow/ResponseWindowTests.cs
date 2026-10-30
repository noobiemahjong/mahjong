using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Tests.Rules;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// <see cref="ResponseWindow"/> 契约：每候一座分配连续 requestId；应答只收碰/杠，过走 <see cref="ResponseWindow.TryPass"/>；
/// 仲裁键 = 杠 &gt; 碰 → arrivalSeq 升序 → 座位升序；Close 逐座产出 Answered / Cancelled / TimedOut；
/// 边界：迟到、重复应答、requestId 不匹配、非候选、Close 前置条件与幂等、开窗参数校验。
/// 期望值来自 <c>build/t2/tests-derive/arbitration_oracle.py</c> 的独立排序推导（人工复核），不调用实现生成期望。
/// </summary>
public sealed class ResponseWindowTests
{
    /// <summary>被响应的弃牌（物理 id 21 = 5万c0，仅作身份标识）。</summary>
    private static readonly Tile Discard = RulesTestData.FromId(21);

    [Fact]
    public void NewWindow_HasDocumentedDefaults()
    {
        var window = new ResponseWindow();

        Assert.Null(window.Result);
        Assert.Empty(window.Requests);
        Assert.Equal(default, window.Discard);
        Assert.Equal(0L, window.DeadlineTick);
        Assert.False(window.IsOpen);
        Assert.False(window.AllAnswered);
    }

    [Fact]
    public void Open_AllocatesConsecutivePerSeatRequestsInCandidateOrder()
    {
        GameState state = Board();
        var window = new ResponseWindow(7);

        window.Open(Discard, Candidates(state, 3, 0, 2), deadlineTick: 15);

        Assert.True(window.IsOpen);
        Assert.Equal(Discard, window.Discard);
        Assert.Equal(15L, window.DeadlineTick);
        Assert.False(window.AllAnswered);
        Assert.Collection(
            window.Requests,
            request =>
            {
                Assert.Equal(7L, request.RequestId);
                Assert.Equal((byte)3, request.Seat);
                Assert.Equal(RequestKind.Pong, request.Kind);
                Assert.Equal(AllowedVerbs.Pong | AllowedVerbs.Kong | AllowedVerbs.Pass, request.Allowed);
                Assert.Equal(15L, request.DeadlineTick);
            },
            request => Assert.Equal(8L, request.RequestId),
            request => Assert.Equal(9L, request.RequestId));
        Assert.Equal((byte)0, window.Requests[1].Seat);
        Assert.Equal((byte)2, window.Requests[2].Seat);
    }

    [Fact]
    public void Open_ZeroCandidates_IsLegalAndAllAnswered()
    {
        GameState state = Board();
        var window = new ResponseWindow();

        window.Open(Discard, Array.Empty<PlayerState>(), deadlineTick: 20);

        Assert.Empty(window.Requests);
        Assert.True(window.AllAnswered);
        window.Close(5);

        ResponseResult result = Assert.IsType<ResponseResult>(window.Result);
        Assert.False(result.HasWinner);
        Assert.Empty(result.Outcomes);
        Assert.Equal(ResponseResult.NoWinnerSeat, result.WinnerSeat);
    }

    [Fact]
    public void Open_Twice_Throws()
    {
        GameState state = Board();
        var window = new ResponseWindow();
        window.Open(Discard, Candidates(state, 1), deadlineTick: 20);

        Assert.Throws<InvalidOperationException>(() => window.Open(Discard, Candidates(state, 2), deadlineTick: 20));
    }

    [Fact]
    public void Open_AfterClose_Throws()
    {
        GameState state = Board();
        var window = new ResponseWindow();
        window.Open(Discard, Candidates(state, 1), deadlineTick: 20);
        window.Close(20);

        Assert.Throws<InvalidOperationException>(() => window.Open(Discard, Candidates(state, 2), deadlineTick: 20));
    }

    [Fact]
    public void Open_RejectsInvalidArguments_AndStaysUnopened()
    {
        GameState state = Board();
        var window = new ResponseWindow();

        Assert.Throws<ArgumentNullException>(() => window.Open(Discard, null!, 20));
        Assert.Throws<ArgumentException>(
            () => window.Open(Discard, Candidates(state, 0, 1, 2, 3), 20));
        Assert.Throws<ArgumentException>(() => window.Open(Discard, Candidates(state, 1, 1), 20));
        Assert.Throws<ArgumentNullException>(
            () => window.Open(Discard, [state.Players[0], null!], 20));
        Assert.Throws<ArgumentException>(() => window.Open(default, Candidates(state, 1), 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.Open(Discard, Candidates(state, 1), -1));

        Assert.False(window.IsOpen);
        Assert.Empty(window.Requests);
    }

    [Fact]
    public void TrySubmitAndTryPass_BeforeOpen_ReturnFalse()
    {
        GameState state = Board();
        var window = new ResponseWindow();

        Assert.False(window.TrySubmit(state.Players[1], SkillId.Pong, 1, 5));
        Assert.False(window.TryPass(state.Players[1], 1, 5));
    }

    [Fact]
    public void TrySubmitAndTryPass_NullPlayer_Throw()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2);
        PlayerState player = state.Players[1];

        Assert.Throws<ArgumentNullException>(() => window.TrySubmit(null!, SkillId.Pong, 1, 5));
        Assert.Throws<ArgumentNullException>(() => window.TryPass(null!, 1, 5));
        Assert.True(window.TrySubmit(player, SkillId.Pong, 1, 5));
    }

    [Fact]
    public void TrySubmit_NonCandidateSeat_ReturnsFalse()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2);

        Assert.False(window.TrySubmit(state.Players[3], SkillId.Kong, 1, 5));
        Assert.False(window.TryPass(state.Players[0], 2, 5));
    }

    [Fact]
    public void TrySubmit_RequestIdMustMatchTheSeatsOwnId()
    {
        GameState state = Board();
        var window = Open(state, 101, 20, 1, 2);
        PlayerState seat1 = state.Players[1];

        Assert.False(window.TrySubmit(seat1, SkillId.Pong, 102, 5));
        Assert.False(window.TrySubmit(seat1, SkillId.Pong, 201, 5));
        Assert.True(window.TrySubmit(seat1, SkillId.Pong, 101, 6));

        // 座位 2 的 id 是 102：用 101 应答必须失败。
        Assert.False(window.TryPass(state.Players[2], 101, 6));
    }

    [Fact]
    public void TrySubmit_OnlyPongAndKongAreAccepted()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1);
        PlayerState player = state.Players[1];

        Assert.False(window.TrySubmit(player, SkillId.SelfDrawn, 1, 5));
        Assert.False(window.TrySubmit(player, SkillId.SelfDrawnWith7Pairs, 1, 5));
        Assert.False(window.TrySubmit(player, (SkillId)0, 1, 5));
        Assert.False(window.TrySubmit(player, (SkillId)5, 1, 5));

        Assert.True(window.TrySubmit(player, SkillId.Kong, 1, 7));
        window.Close(20);
        Assert.Equal(SkillId.Kong, window.Result!.WinnerSkill);
    }

    [Fact]
    public void TrySubmit_LateArrival_IsRejected()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1);

        Assert.False(window.TrySubmit(state.Players[1], SkillId.Pong, 1, 21));
        Assert.False(window.TryPass(state.Players[1], 1, 21));
        window.Close(20);

        AssertOutcomes(window, (RequestOutcomeKind.TimedOut, null));
        Assert.False(window.Result!.HasWinner);
    }

    [Fact]
    public void TrySubmit_ArrivalEqualToDeadline_IsAccepted()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1);

        Assert.True(window.TrySubmit(state.Players[1], SkillId.Pong, 1, 20));
        window.Close(20);

        Assert.Equal(SkillId.Pong, window.Result!.WinnerSkill);
    }

    [Fact]
    public void TrySubmit_DuplicateAnswer_KeepsTheFirstOne()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1);
        PlayerState player = state.Players[1];

        Assert.True(window.TrySubmit(player, SkillId.Pong, 1, 5));
        Assert.False(window.TrySubmit(player, SkillId.Kong, 1, 6));
        window.Close(20);

        AssertOutcomes(window, (RequestOutcomeKind.Answered, SkillId.Pong));
        Assert.Equal(SkillId.Pong, window.Result!.WinnerSkill);
    }

    [Fact]
    public void TryPass_AfterSubmit_IsRejected()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1);
        PlayerState player = state.Players[1];

        Assert.True(window.TrySubmit(player, SkillId.Kong, 1, 5));
        Assert.False(window.TryPass(player, 1, 6));
        window.Close(20);

        AssertOutcomes(window, (RequestOutcomeKind.Answered, SkillId.Kong));
    }

    [Fact]
    public void TrySubmit_AfterPass_IsRejected_AndPassRemainsTheAnswer()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1);
        PlayerState player = state.Players[1];

        Assert.True(window.TryPass(player, 1, 5));
        Assert.False(window.TrySubmit(player, SkillId.Pong, 1, 6));
        window.Close(20);

        Assert.False(window.Result!.HasWinner);
        AssertOutcomes(window, (RequestOutcomeKind.Answered, null));
    }

    [Fact]
    public void TrySubmitAndTryPass_AfterClose_ReturnFalse()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2);
        window.Close(20);

        Assert.False(window.TrySubmit(state.Players[1], SkillId.Pong, 1, 20));
        Assert.False(window.TryPass(state.Players[1], 1, 20));
        Assert.False(window.IsOpen);
    }

    [Fact]
    public void Arbitration_KongBeatsAnEarlierPong()
    {
        // 独立推导：候选 [1,2,3]、deadline 20；1 号 5 批次碰、2 号 7 批次杠 → 杠优先，2 号胜。
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2, 3);
        Assert.True(window.TrySubmit(state.Players[1], SkillId.Pong, 1, 5));
        Assert.True(window.TrySubmit(state.Players[2], SkillId.Kong, 2, 7));
        window.Close(20);

        ResponseResult result = Assert.IsType<ResponseResult>(window.Result);
        AssertArbitration(result, winnerSeat: 2, winnerSkill: SkillId.Kong);
        AssertOutcomes(
            window,
            (RequestOutcomeKind.Cancelled, null),
            (RequestOutcomeKind.Answered, SkillId.Kong),
            (RequestOutcomeKind.TimedOut, null));
        Assert.Equal(Discard, result.TargetCard);
    }

    [Fact]
    public void Arbitration_SameTick_LowerSeatWins_EvenWhenCandidateOrderIsReversed()
    {
        // 独立推导：候选 [2,1,3]、firstId 10；座位 2 与 1 都在第 5 批次碰 → 座位号升序，1 号胜。
        GameState state = Board();
        var window = Open(state, 10, 20, 2, 1, 3);
        Assert.True(window.TrySubmit(state.Players[2], SkillId.Pong, 10, 5));
        Assert.True(window.TrySubmit(state.Players[1], SkillId.Pong, 11, 5));
        window.Close(20);

        ResponseResult result = Assert.IsType<ResponseResult>(window.Result);
        AssertArbitration(result, winnerSeat: 1, winnerSkill: SkillId.Pong);
        AssertOutcomes(
            window,
            (RequestOutcomeKind.Cancelled, null),
            (RequestOutcomeKind.Answered, SkillId.Pong),
            (RequestOutcomeKind.TimedOut, null));
    }

    [Fact]
    public void Arbitration_SamePriority_EarlierArrivalWins()
    {
        // 独立推导：候选 [1,2]；1 号第 6 批次碰、2 号第 5 批次碰 → 同一优先级先到先得，2 号胜。
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2);
        Assert.True(window.TrySubmit(state.Players[1], SkillId.Pong, 1, 6));
        Assert.True(window.TrySubmit(state.Players[2], SkillId.Pong, 2, 5));
        window.Close(20);

        ResponseResult result = Assert.IsType<ResponseResult>(window.Result);
        AssertArbitration(result, winnerSeat: 2, winnerSkill: SkillId.Pong);
        AssertOutcomes(window, (RequestOutcomeKind.Cancelled, null), (RequestOutcomeKind.Answered, SkillId.Pong));
    }

    [Fact]
    public void Arbitration_KongPriorityBeatsEvenAVeryEarlyPong()
    {
        // 独立推导：候选 [1,2]；1 号第 3 批次碰、2 号第 9 批次杠 → 杠的优先级先于到达批次，2 号胜。
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2);
        Assert.True(window.TrySubmit(state.Players[1], SkillId.Pong, 1, 3));
        Assert.True(window.TrySubmit(state.Players[2], SkillId.Kong, 2, 9));
        window.Close(20);

        AssertArbitration(window.Result!, winnerSeat: 2, winnerSkill: SkillId.Kong);
        AssertOutcomes(window, (RequestOutcomeKind.Cancelled, null), (RequestOutcomeKind.Answered, SkillId.Kong));
    }

    [Fact]
    public void Arbitration_KongVsKong_ArrivalThenSeat()
    {
        // 独立推导：候选 [3,1,2]、firstId 40；3 号 8 批次、1 号 8 批次、2 号 6 批次 → 6 最早，2 号胜。
        GameState state = Board();
        var window = Open(state, 40, 20, 3, 1, 2);
        Assert.True(window.TrySubmit(state.Players[3], SkillId.Kong, 40, 8));
        Assert.True(window.TrySubmit(state.Players[1], SkillId.Kong, 41, 8));
        Assert.True(window.TrySubmit(state.Players[2], SkillId.Kong, 42, 6));
        window.Close(20);

        AssertArbitration(window.Result!, winnerSeat: 2, winnerSkill: SkillId.Kong);
        AssertOutcomes(
            window,
            (RequestOutcomeKind.Cancelled, null),
            (RequestOutcomeKind.Cancelled, null),
            (RequestOutcomeKind.Answered, SkillId.Kong));
    }

    [Fact]
    public void Arbitration_PassIsAnAnswerButNeverCompetes()
    {
        // 独立推导：候选 [1,2,3]；1 号第 5 批次过、2 号第 6 批次碰、3 号未答 → 2 号胜，1 号 Answered(null)。
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2, 3);
        Assert.True(window.TryPass(state.Players[1], 1, 5));
        Assert.True(window.TrySubmit(state.Players[2], SkillId.Pong, 2, 6));
        window.Close(20);

        AssertArbitration(window.Result!, winnerSeat: 2, winnerSkill: SkillId.Pong);
        AssertOutcomes(
            window,
            (RequestOutcomeKind.Answered, null),
            (RequestOutcomeKind.Answered, SkillId.Pong),
            (RequestOutcomeKind.TimedOut, null));
    }

    [Fact]
    public void Arbitration_CandidateOrderDecidesRequestIdsAndOutcomeOrder()
    {
        // 独立推导：候选 [3,0,2]、firstId 7；3 号第 10 批次碰、0 号第 12 批次杠 → 杠胜；结算按候选顺序。
        GameState state = Board();
        var window = Open(state, 7, 15, 3, 0, 2);
        Assert.True(window.TrySubmit(state.Players[3], SkillId.Pong, 7, 10));
        Assert.True(window.TrySubmit(state.Players[0], SkillId.Kong, 8, 12));
        window.Close(15);

        ResponseResult result = Assert.IsType<ResponseResult>(window.Result);
        AssertArbitration(result, winnerSeat: 0, winnerSkill: SkillId.Kong);
        AssertOutcomes(
            window,
            (RequestOutcomeKind.Cancelled, null),
            (RequestOutcomeKind.Answered, SkillId.Kong),
            (RequestOutcomeKind.TimedOut, null));
        Assert.Equal(new long[] { 7, 8, 9 }, result.Outcomes.Select(outcome => outcome.RequestId));
    }

    [Fact]
    public void Arbitration_AllPass_NoWinner_AndClosesBeforeDeadline()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2, 3);
        Assert.True(window.TryPass(state.Players[1], 1, 5));
        Assert.True(window.TryPass(state.Players[2], 2, 5));
        Assert.True(window.TryPass(state.Players[3], 3, 5));
        Assert.True(window.AllAnswered);

        window.Close(5);

        ResponseResult result = Assert.IsType<ResponseResult>(window.Result);
        Assert.False(result.HasWinner);
        Assert.Equal(ResponseResult.NoWinnerSeat, result.WinnerSeat);
        Assert.Null(result.WinnerSkill);
        AssertOutcomes(
            window,
            (RequestOutcomeKind.Answered, null),
            (RequestOutcomeKind.Answered, null),
            (RequestOutcomeKind.Answered, null));
    }

    [Fact]
    public void Close_AtDeadline_SettlesUnansweredAsTimedOut()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2);
        Assert.True(window.TrySubmit(state.Players[1], SkillId.Pong, 1, 5));

        window.Close(20);

        AssertArbitration(window.Result!, winnerSeat: 1, winnerSkill: SkillId.Pong);
        AssertOutcomes(window, (RequestOutcomeKind.Answered, SkillId.Pong), (RequestOutcomeKind.TimedOut, null));
    }

    [Fact]
    public void Close_BeforeOpen_Throws()
    {
        var window = new ResponseWindow();

        Assert.Throws<InvalidOperationException>(() => window.Close(0));
    }

    [Fact]
    public void Close_BeforeDeadlineWithPendingCandidates_Throws_AndWindowStaysOpen()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2);
        Assert.False(window.AllAnswered);
        Assert.True(window.TrySubmit(state.Players[1], SkillId.Pong, 1, 5));
        Assert.False(window.AllAnswered);

        Assert.Throws<InvalidOperationException>(() => window.Close(19));

        Assert.True(window.IsOpen);
        Assert.Null(window.Result);
        Assert.True(window.TryPass(state.Players[2], 2, 19));
        Assert.True(window.AllAnswered);
        window.Close(20);
        AssertArbitration(window.Result!, winnerSeat: 1, winnerSkill: SkillId.Pong);
    }

    [Fact]
    public void Close_IsIdempotent_AndKeepsTheSameResult()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1);
        Assert.True(window.TrySubmit(state.Players[1], SkillId.Kong, 1, 5));
        window.Close(20);

        ResponseResult first = Assert.IsType<ResponseResult>(window.Result);
        string signature = Signature(first);
        window.Close(20);
        window.Close(999);

        Assert.Same(first, window.Result);
        Assert.Equal(signature, Signature(window.Result!));
    }

    [Fact]
    public void Result_NoWinnerConstantsAre255AndNull()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2);
        window.Close(20);

        ResponseResult result = window.Result!;
        Assert.Equal(255, ResponseResult.NoWinnerSeat);
        Assert.Equal((byte)255, result.WinnerSeat);
        Assert.False(result.HasWinner);
        Assert.Null(result.WinnerSkill);
        Assert.Equal(2, result.Outcomes.Count);
    }

    [Fact]
    public void Result_PayloadIsBoxedSkillIdForWinner_AndNullOtherwise()
    {
        GameState state = Board();
        var window = Open(state, 1, 20, 1, 2);
        Assert.True(window.TrySubmit(state.Players[1], SkillId.Pong, 1, 5));
        Assert.True(window.TrySubmit(state.Players[2], SkillId.Kong, 2, 7));
        window.Close(20);

        ResponseResult result = window.Result!;
        Assert.Equal(SkillId.Kong, Assert.IsType<SkillId>(result.Outcomes[1].Payload));
        Assert.Null(result.Outcomes[0].Payload);
        Assert.Equal(SkillId.Kong, result.WinnerSkill);
        Assert.Equal(result.Requests, window.Requests);
    }

    [Fact]
    public void SameInputSequenceTwice_ProducesIdenticalResults()
    {
        (ResponseResult First, ResponseResult Second) = (RunCaseOne(), RunCaseOne());

        Assert.Equal(Signature(First), Signature(Second));
        Assert.Equal(First.WinnerSeat, Second.WinnerSeat);
        Assert.Equal(First.WinnerSkill, Second.WinnerSkill);
    }

    [Fact]
    public void DifferentFirstRequestId_ChangesIdsButNotArbitration()
    {
        ResponseResult first = RunCaseOne();
        ResponseResult second = RunCaseOne(firstRequestId: 100);

        Assert.Equal(first.WinnerSeat, second.WinnerSeat);
        Assert.Equal(first.WinnerSkill, second.WinnerSkill);
        Assert.Equal(new long[] { 100, 101, 102 }, second.Requests.Select(request => request.RequestId));
        Assert.Equal(new long[] { 100, 101, 102 }, second.Outcomes.Select(outcome => outcome.RequestId));
        Assert.Equal(
            first.Outcomes.Select(outcome => outcome.Kind),
            second.Outcomes.Select(outcome => outcome.Kind));
    }

    [Fact]
    public void TwoWindows_Interleaved_DoNotShareState()
    {
        GameState stateA = Board();
        GameState stateB = Board();
        var windowA = Open(stateA, 1, 20, 1, 2);
        var windowB = Open(stateB, 50, 30, 1, 2, 3);

        Assert.True(windowA.TrySubmit(stateA.Players[1], SkillId.Pong, 1, 5));
        Assert.True(windowB.TryPass(stateB.Players[1], 50, 6));
        Assert.True(windowA.TrySubmit(stateA.Players[2], SkillId.Kong, 2, 7));
        Assert.True(windowB.TrySubmit(stateB.Players[3], SkillId.Pong, 52, 4));
        windowB.Close(30);
        windowA.Close(20);

        AssertArbitration(windowA.Result!, winnerSeat: 2, winnerSkill: SkillId.Kong);
        AssertArbitration(windowB.Result!, winnerSeat: 3, winnerSkill: SkillId.Pong);
        Assert.Equal(new long[] { 1, 2 }, windowA.Result!.Requests.Select(request => request.RequestId));
        Assert.Equal(new long[] { 50, 51, 52 }, windowB.Result!.Requests.Select(request => request.RequestId));
    }

    private static ResponseResult RunCaseOne(long firstRequestId = 1)
    {
        GameState state = Board();
        var window = Open(state, firstRequestId, 20, 1, 2, 3);
        Assert.True(window.TrySubmit(state.Players[1], SkillId.Pong, firstRequestId, 5));
        Assert.True(window.TrySubmit(state.Players[2], SkillId.Kong, firstRequestId + 1, 7));
        window.Close(20);
        return window.Result!;
    }

    private static GameState Board() => SkillTestData.NewState();

    private static PlayerState[] Candidates(GameState state, params byte[] seats) =>
        seats.Select(seat => state.Players[seat]).ToArray();

    /// <summary>开窗：参数顺序为 (局面, firstRequestId, deadlineTick, 候选座位…)，弃牌固定用 <see cref="Discard"/>。</summary>
    private static ResponseWindow Open(
        GameState state,
        long firstRequestId,
        long deadlineTick,
        params byte[] seats)
    {
        var window = new ResponseWindow(firstRequestId);
        window.Open(Discard, Candidates(state, seats), deadlineTick);
        return window;
    }

    private static void AssertArbitration(ResponseResult result, byte winnerSeat, SkillId? winnerSkill)
    {
        Assert.True(result.HasWinner);
        Assert.Equal(winnerSeat, result.WinnerSeat);
        Assert.Equal(winnerSkill, result.WinnerSkill);
    }

    private static void AssertOutcomes(
        ResponseWindow window,
        params (RequestOutcomeKind Kind, SkillId? Payload)[] expected)
    {
        ResponseResult result = Assert.IsType<ResponseResult>(window.Result);
        Assert.Equal(expected.Length, result.Outcomes.Count);
        Assert.Equal(expected.Length, result.Requests.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            RequestOutcome outcome = result.Outcomes[index];
            Assert.Equal(result.Requests[index].RequestId, outcome.RequestId);
            Assert.Equal(expected[index].Kind, outcome.Kind);
            if (expected[index].Payload is SkillId skill)
            {
                Assert.Equal(skill, Assert.IsType<SkillId>(outcome.Payload));
            }
            else
            {
                Assert.Null(outcome.Payload);
            }
        }
    }

    private static string Signature(ResponseResult result) =>
        "target=" + result.TargetCard.Id
        + ";winner=" + result.WinnerSeat + "/" + (result.WinnerSkill?.ToString() ?? "none")
        + ";requests=" + string.Join(
            ",",
            result.Requests.Select(request =>
                request.RequestId + ":" + request.Seat + ":" + (byte)request.Kind + ":" + request.Allowed + ":"
                + request.DeadlineTick))
        + ";outcomes=" + string.Join(
            ",",
            result.Outcomes.Select(outcome =>
                outcome.RequestId + ":" + (byte)outcome.Kind + ":"
                + (outcome.Payload is null ? "null" : outcome.Payload.ToString())));
}
