using Mahjong.Core.Flow;
using Mahjong.Tests.Flow.Support;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// 调度顺序：子动作按 Priority 降序、同 Priority 按 CreationOrder 升序；
/// 已完成子节点被跳过，不会再次 MoveNext。
/// </summary>
public sealed class ActionSchedulerOrderTests
{
    [Fact]
    public void Children_RunByPriorityDescendingThenCreationOrderAscending()
    {
        var log = new List<string>();
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root", log).Build());
        GameAction p10a = scheduler.CreateAction("p10a", new ScriptProbe("p10a", log).Build(), priority: 10);
        GameAction p20a = scheduler.CreateAction("p20a", new ScriptProbe("p20a", log).Build(), priority: 20);
        GameAction p10b = scheduler.CreateAction("p10b", new ScriptProbe("p10b", log).Build(), priority: 10);
        GameAction p20b = scheduler.CreateAction("p20b", new ScriptProbe("p20b", log).Build(), priority: 20);
        root.AddChild(p10a);
        root.AddChild(p20a);
        root.AddChild(p10b);
        root.AddChild(p20b);

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.Equal(new[] { "p20a", "p20b", "p10a", "p10b", "root" }, log);
    }

    [Fact]
    public void Children_WithSamePriority_RunInCreationOrder()
    {
        var log = new List<string>();
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root", log).Build());
        GameAction a = scheduler.CreateAction("a", new ScriptProbe("a", log).Build());
        GameAction b = scheduler.CreateAction("b", new ScriptProbe("b", log).Build());
        GameAction c = scheduler.CreateAction("c", new ScriptProbe("c", log).Build());
        root.AddChild(a);
        root.AddChild(b);
        root.AddChild(c);

        scheduler.Start(root);
        scheduler.Step(0);

        Assert.Equal(new[] { "a", "b", "c", "root" }, log);
    }

    [Fact]
    public void CompletedChildren_AreSkippedOnTheNextSelection()
    {
        var log = new List<string>();
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var aProbe = new ScriptProbe("a", log);
        var bProbe = new ScriptProbe("b", log);
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root", log).Build());
        GameAction a = scheduler.CreateAction("a", aProbe.Build(ActionState.WaitTicks(1)));
        GameAction b = scheduler.CreateAction("b", bProbe.Build());
        root.AddChild(a);
        root.AddChild(b);

        scheduler.Start(root);
        scheduler.Step(0);
        Assert.Equal(new[] { "a" }, log);
        Assert.Equal(1, aProbe.MoveNextCount);
        Assert.Equal(0, bProbe.MoveNextCount);

        scheduler.Step(1);
        Assert.Equal(new[] { "a", "a", "b", "root" }, log);
        Assert.Equal(2, aProbe.MoveNextCount);
        Assert.Equal(1, bProbe.MoveNextCount);
        Assert.True(a.Completed);
        Assert.True(b.Completed);
        Assert.True(root.Completed);
    }
}
