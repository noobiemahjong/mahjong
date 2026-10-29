using Mahjong.Core.Flow;
using Mahjong.Tests.Flow.Support;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// maxStepsPerTick 边界：只统计 MoveNext 调用次数，正好 max 次允许、第 max + 1 次前抛
/// InvalidOperationException；计数按 Step 复位，下探子节点本身不计步。
/// </summary>
public sealed class ActionSchedulerStepLimitTests
{
    [Fact]
    public void ExactlyMaxSteps_DoesNotThrow()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler(maxStepsPerTick: 3);
        var probe = new ScriptProbe("action");
        GameAction action = scheduler.CreateAction("action", probe.Build(
            ActionState.Continue,
            ActionState.Continue));

        scheduler.Start(action);
        scheduler.Step(0);

        Assert.Equal(3, probe.MoveNextCount);
        Assert.True(action.Completed);
    }

    [Fact]
    public void OneStepOverMax_ThrowsBeforeExecutingIt()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler(maxStepsPerTick: 2);
        var probe = new ScriptProbe("action");
        GameAction action = scheduler.CreateAction("action", probe.Build(
            ActionState.Continue,
            ActionState.Continue,
            ActionState.Continue));

        scheduler.Start(action);

        Assert.Throws<InvalidOperationException>(() => scheduler.Step(0));
        Assert.Equal(2, probe.MoveNextCount);
        Assert.False(action.Completed);
    }

    [Fact]
    public void StepCount_ResetsBetweenSteps()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler(maxStepsPerTick: 2);
        var probe = new ScriptProbe("action");
        GameAction action = scheduler.CreateAction("action", probe.Build(
            ActionState.Continue,
            ActionState.WaitTicks(2),
            ActionState.Continue));

        scheduler.Start(action);
        scheduler.Step(0);
        Assert.Equal(2, probe.MoveNextCount);

        scheduler.Step(2);

        Assert.Equal(4, probe.MoveNextCount);
        Assert.True(action.Completed);
    }

    [Fact]
    public void DescendingIntoChildren_DoesNotCountTowardStepLimit()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler(maxStepsPerTick: 3);
        var rootProbe = new ScriptProbe("root");
        var firstProbe = new ScriptProbe("first");
        var secondProbe = new ScriptProbe("second");
        GameAction root = scheduler.CreateAction("root", rootProbe.Build());
        GameAction first = scheduler.CreateAction("first", firstProbe.Build());
        GameAction second = scheduler.CreateAction("second", secondProbe.Build());
        root.AddChild(first);
        root.AddChild(second);

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.Equal(1, firstProbe.MoveNextCount);
        Assert.Equal(1, secondProbe.MoveNextCount);
        Assert.Equal(1, rootProbe.MoveNextCount);
        Assert.True(root.Completed);
    }
}
