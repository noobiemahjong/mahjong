using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Xunit;

namespace Mahjong.Tests.Session.Flow;

/// <summary>
/// 弃牌响应窗口仲裁（会话登记表接缝）：杠 &gt; 碰 → arrivalTick 升序 → 座位升序；
/// 全体显式过时无胜者。直接驱动 <see cref="SessionRequestRegistry"/>，验证会话层对 Core 窗口的接入口径。
/// </summary>
public sealed class GameSessionArbitrationTests
{
    private static readonly Tile Discard = new(5, new TileType(1, SuitType.Character, 2));

    [Fact]
    public void Kong_BeatsPong_EvenWithLaterArrivalAndHigherSeat()
    {
        var state = new GameState();
        var sink = new RecordingSink();
        var registry = new SessionRequestRegistry(state, sink);
        PlayerState[] candidates = [state.Players[1], state.Players[2]];
        long groupId = registry.OpenGroup(Discard, candidates, openedTick: 1, deadlineTick: 10)!.Value;

        PendingRequest seat1 = FindRequest(registry, seat: 1);
        PendingRequest seat2 = FindRequest(registry, seat: 2);

        // 座位 1 先到且座位更小，但只碰；座位 2 后到但明杠 → 杠胜。
        Assert.True(registry.TryRecordAnswer(seat1, Command(seat: 1, CommandVerb.Pong, seat1.RequestId), arrivalTick: 1));
        Assert.True(registry.TryRecordAnswer(seat2, Command(seat: 2, CommandVerb.Kong, seat2.RequestId), arrivalTick: 5));

        ((IRequestRegistry)registry).BeginTick(5);
        Assert.True(registry.IsGroupClosed(groupId));
        ResponseResult result = registry.TakeGroupResult(groupId);

        Assert.True(result.HasWinner);
        Assert.Equal((byte)2, result.WinnerSeat);
        Assert.Equal(SkillId.Kong, result.WinnerSkill);
        Assert.Equal(2, result.Outcomes.Count);
        Assert.Equal(RequestOutcomeKind.Cancelled, result.Outcomes[0].Kind);
        Assert.Equal(RequestOutcomeKind.Answered, result.Outcomes[1].Kind);
    }

    [Fact]
    public void SameSkill_ArrivalBeatsSeat_ThenSeatAscendingOnTie()
    {
        var state = new GameState();
        var sink = new RecordingSink();
        var registry = new SessionRequestRegistry(state, sink);
        PlayerState[] candidates = [state.Players[2], state.Players[3]];
        long groupId = registry.OpenGroup(Discard, candidates, openedTick: 1, deadlineTick: 10)!.Value;

        PendingRequest seat2 = FindRequest(registry, seat: 2);
        PendingRequest seat3 = FindRequest(registry, seat: 3);
        Assert.True(registry.TryRecordAnswer(seat2, Command(seat: 2, CommandVerb.Pong, seat2.RequestId), arrivalTick: 3));
        Assert.True(registry.TryRecordAnswer(seat3, Command(seat: 3, CommandVerb.Pong, seat3.RequestId), arrivalTick: 2));

        ((IRequestRegistry)registry).BeginTick(3);
        ResponseResult result = registry.TakeGroupResult(groupId);

        // 座位 3 到达更早，胜出（不是座位号更小的 2）。
        Assert.Equal((byte)3, result.WinnerSeat);
        Assert.Equal(SkillId.Pong, result.WinnerSkill);

        // 同技能、同 arrivalTick 时按座位升序。
        var secondState = new GameState();
        var secondRegistry = new SessionRequestRegistry(secondState, sink);
        PlayerState[] secondCandidates = [secondState.Players[0], secondState.Players[3]];
        long secondGroup = secondRegistry.OpenGroup(Discard, secondCandidates, openedTick: 1, deadlineTick: 10)!.Value;
        PendingRequest secondSeat0 = FindRequest(secondRegistry, seat: 0);
        PendingRequest secondSeat3 = FindRequest(secondRegistry, seat: 3);
        Assert.True(secondRegistry.TryRecordAnswer(secondSeat3, Command(seat: 3, CommandVerb.Pong, secondSeat3.RequestId), arrivalTick: 4));
        Assert.True(secondRegistry.TryRecordAnswer(secondSeat0, Command(seat: 0, CommandVerb.Pong, secondSeat0.RequestId), arrivalTick: 4));

        ((IRequestRegistry)secondRegistry).BeginTick(4);
        ResponseResult secondResult = secondRegistry.TakeGroupResult(secondGroup);
        Assert.Equal((byte)0, secondResult.WinnerSeat);
    }

    [Fact]
    public void AllPass_ClosesWithoutWinner()
    {
        var state = new GameState();
        var sink = new RecordingSink();
        var registry = new SessionRequestRegistry(state, sink);
        PlayerState[] candidates = [state.Players[0], state.Players[2]];
        long groupId = registry.OpenGroup(Discard, candidates, openedTick: 1, deadlineTick: 10)!.Value;

        PendingRequest seat0 = FindRequest(registry, seat: 0);
        PendingRequest seat2 = FindRequest(registry, seat: 2);
        Assert.True(registry.TryRecordAnswer(seat0, Command(seat: 0, CommandVerb.Pass, seat0.RequestId), arrivalTick: 2));
        Assert.True(registry.TryRecordAnswer(seat2, Command(seat: 2, CommandVerb.Pass, seat2.RequestId), arrivalTick: 2));

        ((IRequestRegistry)registry).BeginTick(2);
        ResponseResult result = registry.TakeGroupResult(groupId);

        Assert.False(result.HasWinner);
        Assert.Equal(ResponseResult.NoWinnerSeat, result.WinnerSeat);
        Assert.Null(result.WinnerSkill);
    }

    private static PendingRequest FindRequest(SessionRequestRegistry registry, byte seat)
    {
        foreach (PendingRequest request in registry.OpenRequests)
        {
            if (request.Seat == seat)
            {
                return request;
            }
        }

        throw new InvalidOperationException($"no open request for seat {seat}");
    }

    private static GameCommand Command(byte seat, CommandVerb verb, long requestId) =>
        new(0, 0, seat, verb, 0, 0, requestId);

    private sealed class RecordingSink : ISessionRequestSink
    {
        public void RequestOpened(PendingRequest request)
        {
        }

        public void RequestClosed(PendingRequest request, RequestOutcomeKind outcome, GameCommand? answer)
        {
        }
    }
}
