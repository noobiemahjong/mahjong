using Mahjong.Core.Flow;
using Mahjong.Tests.Flow.Support;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// ActionStatus 的阻塞语义：Continue 继续推进；WaitTicks 在 tick == wakeTick 恢复、tick == wakeTick - 1 阻塞；
/// WaitUntil 每 tick 重算谓词并在恢复后换掉旧谓词；WaitRequest 只在 TryConsume 命中时恢复。
/// 未 Start 的 Step 不抛错，但 BeginTick 仍被无条件调用。
/// </summary>
public sealed class ActionSchedulerStatusTests
{
    [Fact]
    public void Continue_RunsToCompletionWithinOneStep()
    {
        FakeRequestRegistry registry = new();
        ActionScheduler scheduler = FlowTestData.CreateScheduler(registry);
        var probe = new ScriptProbe("action");
        GameAction action = scheduler.CreateAction("action", probe.Build(
            ActionState.Continue,
            ActionState.Continue,
            ActionState.Continue));

        scheduler.Start(action);
        scheduler.Step(0);

        Assert.Equal(4, probe.MoveNextCount);
        Assert.True(action.Completed);
        Assert.Equal(0L, scheduler.Tick);
        Assert.Equal(new long[] { 0L }, registry.BeginTicks);

        scheduler.Step(1);
        Assert.Equal(4, probe.MoveNextCount);
        Assert.Equal(new long[] { 0L, 1L }, registry.BeginTicks);
    }

    [Fact]
    public void WaitTicks_ResumesAtWakeTickAndStaysBlockedOneTickBefore()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var probe = new ScriptProbe("action");
        GameAction action = scheduler.CreateAction("action", probe.Build(
            ActionState.WaitTicks(2),
            ActionState.Continue));

        scheduler.Start(action);
        scheduler.Step(0);
        Assert.Equal(1, probe.MoveNextCount);
        Assert.False(action.Completed);

        scheduler.Step(1);
        Assert.Equal(1, probe.MoveNextCount);
        Assert.False(action.Completed);

        scheduler.Step(2);
        Assert.Equal(3, probe.MoveNextCount);
        Assert.True(action.Completed);
    }

    [Fact]
    public void WaitTicks_ZeroResumesOnTheNextVisitToTheSameTick()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var probe = new ScriptProbe("action");
        GameAction action = scheduler.CreateAction("action", probe.Build(
            ActionState.WaitTicks(0),
            ActionState.Continue));

        scheduler.Start(action);
        scheduler.Step(0);
        Assert.Equal(1, probe.MoveNextCount);
        Assert.False(action.Completed);

        scheduler.Step(0);
        Assert.Equal(3, probe.MoveNextCount);
        Assert.True(action.Completed);
    }

    [Fact]
    public void WaitTicks_ResumesWhenTickJumpsPastWakeTick()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var probe = new ScriptProbe("action");
        GameAction action = scheduler.CreateAction("action", probe.Build(
            ActionState.WaitTicks(2),
            ActionState.Continue));

        scheduler.Start(action);
        scheduler.Step(0);
        Assert.Equal(1, probe.MoveNextCount);

        scheduler.Step(5);
        Assert.Equal(3, probe.MoveNextCount);
        Assert.True(action.Completed);
    }

    [Fact]
    public void WaitUntil_RecomputesPredicateEachTickAndReplacesOldPredicate()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        int firstPredicateCalls = 0;
        int secondPredicateCalls = 0;
        bool firstReady = false;
        bool secondReady = false;
        var probe = new ScriptProbe("action");
        GameAction action = scheduler.CreateAction("action", probe.Build(
            ActionState.WaitUntil(() =>
            {
                firstPredicateCalls++;
                return firstReady;
            }),
            ActionState.WaitUntil(() =>
            {
                secondPredicateCalls++;
                return secondReady;
            }),
            ActionState.Continue));

        scheduler.Start(action);
        scheduler.Step(0);
        Assert.Equal(0, firstPredicateCalls);
        Assert.Equal(1, probe.MoveNextCount);

        scheduler.Step(1);
        Assert.Equal(1, firstPredicateCalls);
        Assert.Equal(1, probe.MoveNextCount);

        firstReady = true;
        scheduler.Step(2);
        Assert.Equal(2, firstPredicateCalls);
        Assert.Equal(0, secondPredicateCalls);
        Assert.Equal(2, probe.MoveNextCount);

        scheduler.Step(3);
        Assert.Equal(2, firstPredicateCalls);
        Assert.Equal(1, secondPredicateCalls);
        Assert.Equal(2, probe.MoveNextCount);

        secondReady = true;
        scheduler.Step(4);
        Assert.Equal(2, secondPredicateCalls);
        Assert.Equal(4, probe.MoveNextCount);
        Assert.True(action.Completed);
    }

    [Fact]
    public void WaitRequest_ResumesOnlyAfterRegistryConsumes()
    {
        FakeRequestRegistry registry = new();
        ActionScheduler scheduler = FlowTestData.CreateScheduler(registry);
        var probe = new ScriptProbe("action");
        GameAction action = scheduler.CreateAction("action", probe.Build(
            ActionState.WaitRequest(7),
            ActionState.Continue));

        scheduler.Start(action);
        scheduler.Step(0);
        Assert.Equal(1, probe.MoveNextCount);
        Assert.Empty(registry.ConsumeAttempts);

        scheduler.Step(1);
        Assert.Equal(1, probe.MoveNextCount);
        Assert.Equal(new long[] { 7L }, registry.ConsumeAttempts);
        Assert.False(action.Completed);

        registry.Respond(7, RequestOutcomeKind.Answered, "payload");
        scheduler.Step(2);
        Assert.Equal(3, probe.MoveNextCount);
        Assert.True(action.Completed);
        Assert.Equal(new long[] { 0L, 1L, 2L }, registry.BeginTicks);
        Assert.Equal(new long[] { 7L, 7L }, registry.ConsumeAttempts);
    }

    [Fact]
    public void WaitRequest_TimedOutOutcomeAlsoResumes()
    {
        FakeRequestRegistry registry = new();
        ActionScheduler scheduler = FlowTestData.CreateScheduler(registry);
        var probe = new ScriptProbe("action");
        GameAction action = scheduler.CreateAction("action", probe.Build(
            ActionState.WaitRequest(11),
            ActionState.Continue));

        scheduler.Start(action);
        scheduler.Step(0);
        registry.Respond(11, RequestOutcomeKind.TimedOut);
        scheduler.Step(1);

        Assert.True(action.Completed);
    }

    [Fact]
    public void Step_WithoutStartDoesNotThrowAndStillCallsBeginTick()
    {
        FakeRequestRegistry registry = new();
        ActionScheduler scheduler = FlowTestData.CreateScheduler(registry);

        scheduler.Step(5);

        Assert.False(scheduler.IsRunning);
        Assert.Equal(5L, scheduler.Tick);
        Assert.Equal(new long[] { 5L }, registry.BeginTicks);
        Assert.Empty(registry.ConsumeAttempts);

        scheduler.Step(6);
        Assert.Equal(new long[] { 5L, 6L }, registry.BeginTicks);
    }
}
