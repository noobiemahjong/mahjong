using Mahjong.Core.Flow;
using Mahjong.Tests.Flow.Support;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// 子动作嵌套等待（P2 覆盖缺口）：父动作挂子 C，C 返回 WaitRequest / WaitUntil 时，
/// 等待状态必须只落在 C 上——父不被牵连（父的推进与阻塞状态不变），
/// C 由自己的结算/谓词解除并完成后父再继续完成。对应变异「子动作的等待挂到父」（N14/N15）必须变红。
/// </summary>
public sealed class ActionSchedulerNestedWaitTests
{
    [Fact]
    public void ChildWaitRequest_BlocksOnlyTheChild()
    {
        FakeRequestRegistry registry = new();
        ActionScheduler scheduler = FlowTestData.CreateScheduler(registry);
        var parentProbe = new ScriptProbe("parent");
        var childProbe = new ScriptProbe("child");
        GameAction? parent = null;
        GameAction? child = null;
        GameAction parentAction = scheduler.CreateAction("parent", parentProbe.Build(
            move =>
            {
                if (move == 1)
                {
                    child = scheduler.CreateAction(
                        "child",
                        childProbe.Build(ActionState.WaitRequest(7), ActionState.Continue));
                    parent!.AddChild(child);
                }
            },
            ActionState.Continue));
        parent = parentAction;

        scheduler.Start(parentAction);
        scheduler.Step(0);

        // 只有子 C 在等待；父没有阻塞状态、也没有被推进。
        Assert.Equal(1, parentProbe.MoveNextCount);
        Assert.Equal(1, childProbe.MoveNextCount);
        Assert.True(child!.IsBlocked);
        Assert.Equal(ActionStatus.WaitRequest, child.BlockedStatus);
        Assert.Equal(7L, child.BlockedRequestId);
        Assert.False(parentAction.IsBlocked);
        Assert.Equal(ActionStatus.Continue, parentAction.BlockedStatus);
        Assert.Empty(registry.ConsumeAttempts);

        scheduler.Step(1);
        Assert.Equal(new long[] { 7L }, registry.ConsumeAttempts);
        Assert.Equal(1, childProbe.MoveNextCount);
        Assert.Equal(1, parentProbe.MoveNextCount);
        Assert.False(parentAction.Completed);
        Assert.False(child.Completed);

        registry.Respond(7, RequestOutcomeKind.Answered, "payload");
        scheduler.Step(2);

        Assert.Equal(3, childProbe.MoveNextCount);
        Assert.Equal(2, parentProbe.MoveNextCount);
        Assert.True(child.Completed);
        Assert.True(parentAction.Completed);
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void ChildWaitUntil_BlocksOnlyTheChild()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var parentProbe = new ScriptProbe("parent");
        var childProbe = new ScriptProbe("child");
        bool ready = false;
        int predicateCalls = 0;
        Func<bool> predicate = () =>
        {
            predicateCalls++;
            return ready;
        };
        GameAction? parent = null;
        GameAction? child = null;
        GameAction parentAction = scheduler.CreateAction("parent", parentProbe.Build(
            move =>
            {
                if (move == 1)
                {
                    child = scheduler.CreateAction(
                        "child",
                        childProbe.Build(ActionState.WaitUntil(predicate), ActionState.Continue));
                    parent!.AddChild(child);
                }
            },
            ActionState.Continue));
        parent = parentAction;

        scheduler.Start(parentAction);
        scheduler.Step(0);

        // 谓词挂在子 C 上而不是父上：C 阻塞、父不阻塞；谓词在存储时不求值。
        Assert.Equal(1, parentProbe.MoveNextCount);
        Assert.Equal(1, childProbe.MoveNextCount);
        Assert.True(child!.IsBlocked);
        Assert.Equal(ActionStatus.WaitUntil, child.BlockedStatus);
        Assert.Same(predicate, child.Predicate);
        Assert.False(parentAction.IsBlocked);
        Assert.Equal(ActionStatus.Continue, parentAction.BlockedStatus);
        Assert.Equal(0, predicateCalls);

        // 只有 C 的解除判断调用谓词；父仍停在 1 次 MoveNext。
        scheduler.Step(1);
        Assert.Equal(1, predicateCalls);
        Assert.Equal(1, childProbe.MoveNextCount);
        Assert.Equal(1, parentProbe.MoveNextCount);
        Assert.False(parentAction.Completed);

        scheduler.Step(2);
        Assert.Equal(2, predicateCalls);
        Assert.Equal(1, childProbe.MoveNextCount);
        Assert.False(parentAction.Completed);

        ready = true;
        scheduler.Step(3);

        Assert.Equal(3, predicateCalls);
        Assert.Equal(3, childProbe.MoveNextCount);
        Assert.Equal(2, parentProbe.MoveNextCount);
        Assert.True(child.Completed);
        Assert.True(parentAction.Completed);
        Assert.False(scheduler.IsRunning);
    }
}
