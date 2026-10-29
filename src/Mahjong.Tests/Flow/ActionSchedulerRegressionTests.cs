using Mahjong.Core.Flow;
using Mahjong.Tests.Flow.Support;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// 敌对审核缺口的回归用例（P2-1 父阻塞饿死待执行子、两层阻塞互不覆盖、越根 abort 后 Complete、
/// abort 后的父指针/父列表、自然结束的 IsRunning、同优先级添加序、已启动祖先 abort 的 finally、
/// DisposeRoutineOnce 幂等）。P2-1 两类用例在「全局阻塞状态」实现下应红，在按动作持有阻塞状态后转绿。
/// </summary>
public sealed class ActionSchedulerRegressionTests
{
    [Fact]
    public void BlockedParent_DoesNotStarvePendingChild()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var parentProbe = new ScriptProbe("parent");
        var childProbe = new ScriptProbe("child");
        GameAction? parent = null;
        GameAction parentAction = scheduler.CreateAction("parent", parentProbe.Build(
            move =>
            {
                if (move == 1)
                {
                    GameAction child = scheduler.CreateAction(
                        "child",
                        childProbe.Build(ActionState.WaitTicks(1), ActionState.Continue));
                    parent!.AddChild(child);
                }
            },
            ActionState.WaitTicks(1000),
            ActionState.Continue));
        parent = parentAction;

        scheduler.Start(parentAction);
        scheduler.Step(1);
        Assert.Equal(1, parentProbe.MoveNextCount);
        Assert.Equal(0, childProbe.MoveNextCount);

        // P2-1 回归：父动作仍在 WaitTicks(1000) 中，其待执行子动作必须被推进，不能被父的阻塞状态饿死。
        scheduler.Step(2);
        Assert.Equal(1, childProbe.MoveNextCount);
        Assert.False(parentAction.Completed);

        scheduler.Step(3);
        Assert.Equal(3, childProbe.MoveNextCount);
        Assert.Equal(1, parentProbe.MoveNextCount);

        scheduler.Step(1001);
        Assert.Equal(3, parentProbe.MoveNextCount);
        Assert.True(parentAction.Completed);
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void ParentWaitingOnChildCompletion_DoesNotDeadlock()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var parentProbe = new ScriptProbe("parent");
        var childProbe = new ScriptProbe("child");
        GameAction? parent = null;
        GameAction parentAction = scheduler.CreateAction("parent", parentProbe.Build(
            move =>
            {
                if (move == 1)
                {
                    GameAction child = scheduler.CreateAction("child", childProbe.Build(ActionState.Continue));
                    parent!.AddChild(child);
                }
            },
            ActionState.WaitUntil(() => parent!.Children.Count > 0 && parent.Children[0].Completed),
            ActionState.Continue));
        parent = parentAction;

        scheduler.Start(parentAction);
        scheduler.Step(1);
        Assert.Equal(1, parentProbe.MoveNextCount);
        Assert.Equal(0, childProbe.MoveNextCount);

        // 合法形态：父 WaitUntil(子完成)；子必须先跑完，父才能解除阻塞，否则死锁。
        scheduler.Step(2);

        Assert.Equal(2, childProbe.MoveNextCount);
        Assert.True(parentAction.Completed);
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void NestedBlocks_DoNotOverwriteEachOther()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var parentProbe = new ScriptProbe("parent");
        var childProbe = new ScriptProbe("child");
        GameAction? parent = null;
        GameAction parentAction = scheduler.CreateAction("parent", parentProbe.Build(
            move =>
            {
                if (move == 1)
                {
                    GameAction child = scheduler.CreateAction(
                        "child",
                        childProbe.Build(ActionState.WaitTicks(5), ActionState.Continue));
                    parent!.AddChild(child);
                }
            },
            ActionState.WaitTicks(9),
            ActionState.Continue));
        parent = parentAction;

        scheduler.Start(parentAction);
        scheduler.Step(0);
        Assert.Equal(1, parentProbe.MoveNextCount);
        Assert.Equal(0, childProbe.MoveNextCount);

        // 子按自己的 wakeTick=6 阻塞；父的 wakeTick=9 更晚，父子各存各的等待。
        scheduler.Step(1);
        Assert.Equal(1, childProbe.MoveNextCount);
        Assert.Equal(1, parentProbe.MoveNextCount);
        scheduler.Step(2);
        Assert.Equal(1, childProbe.MoveNextCount);
        scheduler.Step(5);
        Assert.Equal(1, childProbe.MoveNextCount);
        Assert.Equal(1, parentProbe.MoveNextCount);
        Assert.False(parentAction.Completed);

        // 子在自己的 wakeTick=6 完成；父的 wakeTick=9 不得被子覆盖，父此刻仍必须阻塞。
        scheduler.Step(6);
        Assert.Equal(3, childProbe.MoveNextCount);
        Assert.Equal(1, parentProbe.MoveNextCount);
        Assert.False(parentAction.Completed);
        Assert.True(parentAction.IsBlocked);
        Assert.Equal(ActionStatus.WaitTicks, parentAction.BlockedStatus);

        // tick 8 仍未到父的 wakeTick=9：父保持阻塞，不抢跑。
        scheduler.Step(8);
        Assert.Equal(1, parentProbe.MoveNextCount);
        Assert.False(parentAction.Completed);

        // tick 9 到达父自己的 wakeTick：父解除并完成。
        scheduler.Step(9);
        Assert.Equal(3, parentProbe.MoveNextCount);
        Assert.True(parentAction.Completed);
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void OverRootAbort_ThenComplete_StopsAndDisposesTheMigratedRootSubtree()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var rootProbe = new ScriptProbe("root");
        var ancestorProbe = new ScriptProbe("A");
        var childProbe = new ScriptProbe("P");
        GameAction root = scheduler.CreateAction("root", rootProbe.Build(ActionState.Continue));
        GameAction ancestor = scheduler.CreateAction("A", ancestorProbe.Build(ActionState.Continue));
        GameAction child = scheduler.CreateAction("P", childProbe.Build(
            ActionState.AbortParents(5),
            ActionState.Continue,
            ActionState.Complete("done")));
        root.AddChild(ancestor);
        ancestor.AddChild(child);

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.Equal("done", scheduler.Result);
        Assert.False(scheduler.IsRunning);
        Assert.True(child.Completed);
        Assert.True(root.Completed);
        Assert.True(ancestor.Completed);
        Assert.Null(child.Parent);
        Assert.Equal(1, rootProbe.DisposeCount);
        Assert.Equal(1, ancestorProbe.DisposeCount);
        Assert.Equal(1, childProbe.DisposeCount);
        Assert.Equal(1, childProbe.FinallyCount);

        int movesAfterComplete = childProbe.MoveNextCount;
        scheduler.Step(1);
        Assert.Equal(movesAfterComplete, childProbe.MoveNextCount);
        Assert.Equal("done", scheduler.Result);
    }

    [Fact]
    public void AbortParents_DetachesAbortedAncestorsFromParentPointerAndChildList()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root").Build(ActionState.WaitTicks(10)));
        GameAction a = scheduler.CreateAction("A", new ScriptProbe("A").Build(ActionState.Continue));
        GameAction b = scheduler.CreateAction("B", new ScriptProbe("B").Build(ActionState.Continue));
        GameAction p = scheduler.CreateAction("P", new ScriptProbe("P").Build(
            ActionState.AbortParents(2),
            ActionState.Continue));
        root.AddChild(a);
        a.AddChild(b);
        b.AddChild(p);

        scheduler.Start(root);
        scheduler.Step(0);

        // M25：被 abort 的祖先不再指向父动作，也不在其父的子列表里；current 挂到最后一个被 abort 祖先的父节点。
        Assert.Null(b.Parent);
        Assert.Null(a.Parent);
        Assert.DoesNotContain(b, a.Children);
        Assert.DoesNotContain(a, root.Children);
        Assert.Contains(p, root.Children);
        Assert.Same(root, p.Parent);
    }

    [Fact]
    public void AbortSiblings_RemovesKilledSiblingsFromParentChildList()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction a = scheduler.CreateAction("A", new ScriptProbe("A").Build(ActionState.WaitTicks(1)));
        GameAction p = scheduler.CreateAction("P", new ScriptProbe("P").Build(
            ActionState.AbortSiblings,
            ActionState.Continue));
        GameAction s1 = scheduler.CreateAction("S1", new ScriptProbe("S1").Build());
        GameAction s2 = scheduler.CreateAction("S2", new ScriptProbe("S2").Build());
        a.AddChild(p);
        a.AddChild(s1);
        a.AddChild(s2);
        scheduler.Start(a);
        scheduler.Step(0);

        // M28：被清兄弟从 parent.Children 移除，current 与 parent 保留。
        Assert.DoesNotContain(s1, a.Children);
        Assert.DoesNotContain(s2, a.Children);
        Assert.Contains(p, a.Children);
        Assert.Same(a, p.Parent);
        Assert.False(a.Completed);
    }

    [Fact]
    public void NaturalRootCompletion_SetsIsRunningFalse()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root").Build(ActionState.Continue));

        scheduler.Start(root);
        Assert.True(scheduler.IsRunning);

        scheduler.Step(0);

        Assert.True(root.Completed);
        Assert.False(scheduler.IsRunning);
        Assert.Null(scheduler.Result);

        scheduler.Step(1);
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void SamePriorityChildren_RunInAddOrder()
    {
        var log = new List<string>();
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root", log).Build());
        GameAction first = scheduler.CreateAction("first", new ScriptProbe("first", log).Build());
        GameAction second = scheduler.CreateAction("second", new ScriptProbe("second", log).Build());
        GameAction third = scheduler.CreateAction("third", new ScriptProbe("third", log).Build());
        root.AddChild(first);
        root.AddChild(second);
        root.AddChild(third);

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.Equal(new[] { "first", "second", "third", "root" }, log);

        // 子列表的物理顺序等于添加顺序（AddChild 只能尾部追加）；把 AddChild 改成 Insert(0) 会在此变红。
        Assert.Equal(new[] { first, second, third }, root.Children);
        Assert.Equal(0L, first.CreationOrder);
        Assert.Equal(1L, second.CreationOrder);
        Assert.Equal(2L, third.CreationOrder);
    }

    [Fact]
    public void AbortStartedAncestor_DisposesOnceAndRunsItsFinally()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var ancestorProbe = new ScriptProbe("X");
        var childProbe = new ScriptProbe("Y");
        GameAction? ancestorAction = null;
        ancestorAction = scheduler.CreateAction("X", ancestorProbe.Build(
            move =>
            {
                if (move == 2)
                {
                    GameAction y = scheduler.CreateAction("Y", childProbe.Build(
                        ActionState.AbortParents(1),
                        ActionState.Continue));
                    ancestorAction!.AddChild(y);
                }
            },
            ActionState.Continue,
            ActionState.Continue));

        scheduler.Start(ancestorAction);
        scheduler.Step(0);

        // G3c：祖先已启动（被挂起在 yield 处）后被其子 abort，Dispose 必须执行一次并让 finally 跑一次。
        Assert.True(ancestorAction.Completed);
        Assert.Null(ancestorAction.Parent);
        Assert.Equal(2, ancestorProbe.MoveNextCount);
        Assert.Equal(1, ancestorProbe.DisposeCount);
        Assert.Equal(1, ancestorProbe.FinallyCount);
        Assert.Equal(3, childProbe.MoveNextCount);
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void DisposeRoutineOnce_IsIdempotent()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var routine = new DisposeCountingRoutine();
        GameAction action = scheduler.CreateAction("action", routine);

        action.DisposeRoutineOnce();
        action.DisposeRoutineOnce();

        Assert.Equal(1, routine.DisposeCount);
    }

    private sealed class DisposeCountingRoutine : IEnumerator<ActionState>
    {
        public int DisposeCount { get; private set; }

        public ActionState Current => ActionState.Continue;

        object System.Collections.IEnumerator.Current => ActionState.Continue;

        public bool MoveNext() => true;

        public void Reset() => throw new NotSupportedException();

        public void Dispose() => DisposeCount++;
    }
}
