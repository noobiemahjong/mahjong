using Mahjong.Core.Flow;
using Mahjong.Tests.Flow.Support;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// 中断契约：AbortParents 支持 depth &gt; 1 与越过根、AbortSiblings 只清兄弟；
/// abort/Complete 对受影响动作的枚举器各 Dispose 一次（abort 目标通常是尚未 MoveNext 的祖先/兄弟，
/// 因此以 Dispose 计数验证；Complete 覆盖已启动的当前动作，finally 随 Dispose 执行），
/// 已完成动作不再 MoveNext，Complete 记录结果并停止整棵树。
/// </summary>
public sealed class ActionSchedulerInterruptTests
{
    [Fact]
    public void AbortParents_DepthOne_ClearsAncestorAndDisposesItsPendingChildren()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var rootProbe = new ScriptProbe("root");
        var aProbe = new ScriptProbe("A");
        var pProbe = new ScriptProbe("P");
        var qProbe = new ScriptProbe("Q");
        GameAction root = scheduler.CreateAction("root", rootProbe.Build(ActionState.WaitTicks(10)));
        GameAction a = scheduler.CreateAction("A", aProbe.Build(ActionState.Continue));
        GameAction p = scheduler.CreateAction("P", pProbe.Build(ActionState.AbortParents(), ActionState.Continue));
        GameAction q = scheduler.CreateAction("Q", qProbe.Build(ActionState.Continue));
        root.AddChild(a);
        a.AddChild(p);
        a.AddChild(q);

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.True(a.Completed);
        Assert.True(q.Completed);
        Assert.True(p.Completed);
        Assert.False(root.Completed);
        Assert.Same(root, p.Parent);
        Assert.Equal(0, aProbe.MoveNextCount);
        Assert.Equal(0, qProbe.MoveNextCount);
        Assert.Equal(1, aProbe.DisposeCount);
        Assert.Equal(1, qProbe.DisposeCount);
        Assert.Equal(3, pProbe.MoveNextCount);
        Assert.Equal(1, pProbe.FinallyCount);
        Assert.Equal(1, rootProbe.MoveNextCount);
    }

    [Fact]
    public void AbortParents_DepthTwo_ClearsTwoAncestorsAndKeepsCurrent()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var rootProbe = new ScriptProbe("root");
        var aProbe = new ScriptProbe("A");
        var bProbe = new ScriptProbe("B");
        var pProbe = new ScriptProbe("P");
        var qProbe = new ScriptProbe("Q");
        var rProbe = new ScriptProbe("R");
        GameAction root = scheduler.CreateAction("root", rootProbe.Build(ActionState.WaitTicks(10)));
        GameAction a = scheduler.CreateAction("A", aProbe.Build(ActionState.Continue));
        GameAction b = scheduler.CreateAction("B", bProbe.Build(ActionState.Continue));
        GameAction p = scheduler.CreateAction("P", pProbe.Build(ActionState.AbortParents(2), ActionState.Continue));
        GameAction q = scheduler.CreateAction("Q", qProbe.Build(ActionState.Continue));
        GameAction r = scheduler.CreateAction("R", rProbe.Build(ActionState.Continue));
        root.AddChild(a);
        a.AddChild(b);
        b.AddChild(p);
        b.AddChild(q);
        a.AddChild(r);

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.True(a.Completed);
        Assert.True(b.Completed);
        Assert.True(p.Completed);
        Assert.False(root.Completed);
        Assert.Same(root, p.Parent);
        Assert.Equal(0, aProbe.MoveNextCount);
        Assert.Equal(0, bProbe.MoveNextCount);
        Assert.Equal(0, qProbe.MoveNextCount);
        Assert.Equal(0, rProbe.MoveNextCount);
        Assert.Equal(1, aProbe.DisposeCount);
        Assert.Equal(1, bProbe.DisposeCount);
        Assert.Equal(1, qProbe.DisposeCount);
        Assert.Equal(1, rProbe.DisposeCount);
        Assert.Equal(1, pProbe.FinallyCount);
    }

    [Fact]
    public void AbortParents_DepthBeyondRoot_LeavesCurrentAsNewRoot()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var rootProbe = new ScriptProbe("root");
        var aProbe = new ScriptProbe("A");
        var pProbe = new ScriptProbe("P");
        GameAction root = scheduler.CreateAction("root", rootProbe.Build(ActionState.Continue));
        GameAction a = scheduler.CreateAction("A", aProbe.Build(ActionState.Continue));
        GameAction p = scheduler.CreateAction("P", pProbe.Build(ActionState.AbortParents(5), ActionState.Continue));
        root.AddChild(a);
        a.AddChild(p);

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.True(root.Completed);
        Assert.True(a.Completed);
        Assert.True(p.Completed);
        Assert.Null(p.Parent);
        Assert.Equal(1, rootProbe.DisposeCount);
        Assert.Equal(1, aProbe.DisposeCount);
        Assert.Equal(3, pProbe.MoveNextCount);
    }

    [Fact]
    public void AbortSiblings_ClearsOnlySiblingsAndKeepsParentAndAncestors()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var rootProbe = new ScriptProbe("root");
        var aProbe = new ScriptProbe("A");
        var pProbe = new ScriptProbe("P");
        var s1Probe = new ScriptProbe("S1");
        var s2Probe = new ScriptProbe("S2");
        var bProbe = new ScriptProbe("B");
        GameAction root = scheduler.CreateAction("root", rootProbe.Build());
        GameAction a = scheduler.CreateAction("A", aProbe.Build(ActionState.WaitTicks(1)));
        GameAction p = scheduler.CreateAction("P", pProbe.Build(ActionState.AbortSiblings, ActionState.Continue));
        GameAction s1 = scheduler.CreateAction("S1", s1Probe.Build(), priority: 20);
        GameAction s2 = scheduler.CreateAction("S2", s2Probe.Build(ActionState.Continue));
        GameAction b = scheduler.CreateAction("B", bProbe.Build());
        root.AddChild(a);
        a.AddChild(p);
        a.AddChild(s1);
        a.AddChild(s2);
        root.AddChild(b);

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.True(p.Completed);
        Assert.True(s1.Completed);
        Assert.True(s2.Completed);
        Assert.False(a.Completed);
        Assert.False(root.Completed);
        Assert.False(b.Completed);
        // S1 优先级更高，先在本次 Step 内自然跑完；随后 P 的 AbortSiblings 仍对已启动完的 S1
        // 与未启动的 S2 各 Dispose 一次。
        Assert.Equal(1, s1Probe.MoveNextCount);
        Assert.Equal(1, s1Probe.FinallyCount);
        Assert.Equal(0, s2Probe.MoveNextCount);
        Assert.Equal(0, bProbe.MoveNextCount);
        Assert.Equal(1, s1Probe.DisposeCount);
        Assert.Equal(1, s2Probe.DisposeCount);
        Assert.Equal(1, aProbe.MoveNextCount);

        scheduler.Step(1);

        Assert.True(a.Completed);
        Assert.True(b.Completed);
        Assert.True(root.Completed);
        Assert.Equal(1, bProbe.MoveNextCount);
        Assert.Equal(1, bProbe.FinallyCount);
    }

    [Fact]
    public void AbortSiblings_WithoutParentIsNoOp()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var probe = new ScriptProbe("root");
        GameAction root = scheduler.CreateAction("root", probe.Build(ActionState.AbortSiblings, ActionState.Continue));

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.True(root.Completed);
        Assert.Equal(3, probe.MoveNextCount);
        Assert.Equal(1, probe.FinallyCount);
    }

    [Fact]
    public void Complete_SetsResultStopsWholeTreeAndDisposesEveryRoutineOnce()
    {
        FakeRequestRegistry registry = new();
        ActionScheduler scheduler = FlowTestData.CreateScheduler(registry);
        var rootProbe = new ScriptProbe("root");
        var aProbe = new ScriptProbe("A");
        var bProbe = new ScriptProbe("B");
        var qProbe = new ScriptProbe("Q");
        GameAction root = scheduler.CreateAction("root", rootProbe.Build());
        GameAction a = scheduler.CreateAction("A", aProbe.Build());
        GameAction b = scheduler.CreateAction("B", bProbe.Build(ActionState.Continue, ActionState.Complete("done")));
        GameAction q = scheduler.CreateAction("Q", qProbe.Build(ActionState.Continue));
        root.AddChild(a);
        root.AddChild(b);
        root.AddChild(q);

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.Equal("done", scheduler.Result);
        Assert.False(scheduler.IsRunning);
        Assert.Equal(0L, scheduler.Tick);
        Assert.True(root.Completed);
        Assert.True(a.Completed);
        Assert.True(b.Completed);
        Assert.True(q.Completed);
        Assert.Equal(1, aProbe.MoveNextCount);
        Assert.Equal(2, bProbe.MoveNextCount);
        Assert.Equal(0, qProbe.MoveNextCount);
        Assert.Equal(0, rootProbe.MoveNextCount);
        Assert.Equal(1, rootProbe.DisposeCount);
        Assert.Equal(1, aProbe.DisposeCount);
        Assert.Equal(1, bProbe.DisposeCount);
        Assert.Equal(1, qProbe.DisposeCount);
        Assert.Equal(1, aProbe.FinallyCount);
        Assert.Equal(1, bProbe.FinallyCount);

        scheduler.Step(1);

        Assert.Equal("done", scheduler.Result);
        Assert.Equal(2, bProbe.MoveNextCount);
        Assert.Equal(0, qProbe.MoveNextCount);
        Assert.Equal(0, rootProbe.MoveNextCount);
        Assert.Equal(new long[] { 0L, 1L }, registry.BeginTicks);
    }

    [Fact]
    public void Complete_WithoutResultLeavesResultNull()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var probe = new ScriptProbe("root");
        GameAction root = scheduler.CreateAction("root", probe.Build(ActionState.Complete()));

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.Null(scheduler.Result);
        Assert.False(scheduler.IsRunning);
        Assert.True(root.Completed);
        Assert.Equal(1, probe.DisposeCount);
        Assert.Equal(1, probe.FinallyCount);
    }

    [Fact]
    public void CompletedAction_IsNeverMovedNextAgain()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var probe = new ScriptProbe("action");
        GameAction action = scheduler.CreateAction("action", probe.Build());

        scheduler.Start(action);
        scheduler.Step(0);
        Assert.True(action.Completed);
        Assert.Equal(1, probe.MoveNextCount);
        Assert.Equal(1, probe.FinallyCount);

        scheduler.Step(1);
        scheduler.Step(2);

        Assert.Equal(1, probe.MoveNextCount);
        // 自然结束不要求 Dispose（契约只要求 abort/Complete 路径 Dispose）；重复 Dispose 才是违规。
        Assert.True(probe.DisposeCount <= 1);
    }
}
