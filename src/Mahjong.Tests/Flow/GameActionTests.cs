using System.Reflection;
using Mahjong.Core.Flow;
using Mahjong.Tests.Flow.Support;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// GameAction 与 ActionScheduler 的构造/挂载契约：只有调度器能创建动作、Id 按实例递增、
/// CreationOrder 由 AddChild 分配、AddChild 与 Start 的失败不改变双方状态。
/// </summary>
public sealed class GameActionTests
{
    [Fact]
    public void GameAction_HasNoPublicConstructor()
    {
        ConstructorInfo[] publicConstructors =
            typeof(GameAction).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void CreateAction_AssignsInstanceIdsAndInitialFields()
    {
        FakeRequestRegistry registry = new();
        ActionScheduler scheduler = FlowTestData.CreateScheduler(registry);
        var first = new ScriptProbe("first");
        var second = new ScriptProbe("second");
        var third = new ScriptProbe("third");
        IEnumerator<ActionState> thirdRoutine = third.Build();

        GameAction action1 = scheduler.CreateAction(first.Name, first.Build(ActionState.Continue));
        GameAction action2 = scheduler.CreateAction(second.Name, second.Build(), priority: 30, interruptible: false);
        GameAction action3 = scheduler.CreateAction(third.Name, thirdRoutine);

        Assert.Equal(1L, action1.Id);
        Assert.Equal(2L, action2.Id);
        Assert.Equal(3L, action3.Id);
        Assert.Equal(0L, action1.CreationOrder);
        Assert.Equal(0L, action2.CreationOrder);
        Assert.Equal(0L, action3.CreationOrder);
        Assert.Null(action1.Parent);
        Assert.Equal(first.Name, action1.Name);
        Assert.Equal(FlowTestData.DefaultPriority, action1.Priority);
        Assert.True(action1.Interruptible);
        Assert.Equal(30, action2.Priority);
        Assert.False(action2.Interruptible);
        Assert.False(action1.Completed);
        Assert.Same(thirdRoutine, action3.Routine);
        Assert.Empty(registry.BeginTicks);
    }

    [Fact]
    public void CreateAction_RejectsNullNameOrRoutine()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        var probe = new ScriptProbe("probe");

        Assert.Throws<ArgumentNullException>(() => scheduler.CreateAction(null!, probe.Build()));
        Assert.Throws<ArgumentNullException>(() => scheduler.CreateAction("action", null!));
    }

    [Fact]
    public void AddChild_AssignsParentAndCreationOrder()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root").Build());
        GameAction first = scheduler.CreateAction("first", new ScriptProbe("first").Build());
        GameAction second = scheduler.CreateAction("second", new ScriptProbe("second").Build());

        root.AddChild(first);
        root.AddChild(second);

        Assert.Same(root, first.Parent);
        Assert.Same(root, second.Parent);
        Assert.Equal(0L, first.CreationOrder);
        Assert.Equal(1L, second.CreationOrder);
    }

    [Fact]
    public void AddChild_RejectsNull()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root").Build());

        Assert.Throws<ArgumentNullException>(() => root.AddChild(null!));
    }

    [Fact]
    public void AddChild_RejectsSelf()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root").Build());

        Assert.Throws<InvalidOperationException>(() => root.AddChild(root));
    }

    [Fact]
    public void AddChild_RejectsDuplicateAndKeepsCreationOrderCounterUnconsumed()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root").Build());
        GameAction first = scheduler.CreateAction("first", new ScriptProbe("first").Build());
        GameAction second = scheduler.CreateAction("second", new ScriptProbe("second").Build());
        root.AddChild(first);

        Assert.Throws<InvalidOperationException>(() => root.AddChild(first));

        Assert.Same(root, first.Parent);
        Assert.Equal(0L, first.CreationOrder);

        root.AddChild(second);
        Assert.Equal(1L, second.CreationOrder);
    }

    [Fact]
    public void AddChild_RejectsChildThatAlreadyHasParent()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root").Build());
        GameAction otherParent = scheduler.CreateAction("other", new ScriptProbe("other").Build());
        GameAction child = scheduler.CreateAction("child", new ScriptProbe("child").Build());
        root.AddChild(child);

        Assert.Throws<InvalidOperationException>(() => otherParent.AddChild(child));

        Assert.Same(root, child.Parent);
        Assert.Equal(0L, child.CreationOrder);
    }

    [Fact]
    public void AddChild_RejectsCompletedChild()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction completedRoot = scheduler.CreateAction("completed", new ScriptProbe("completed").Build());
        scheduler.Start(completedRoot);
        scheduler.Step(0);
        Assert.True(completedRoot.Completed);
        Assert.Null(completedRoot.Parent);

        GameAction parent = scheduler.CreateAction("parent", new ScriptProbe("parent").Build());
        Assert.Throws<InvalidOperationException>(() => parent.AddChild(completedRoot));
    }

    [Fact]
    public void Constructor_RejectsNullRequestsAndNonPositiveMaxSteps()
    {
        Assert.Throws<ArgumentNullException>(() => new ActionScheduler(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionScheduler(new FakeRequestRegistry(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionScheduler(new FakeRequestRegistry(), -1));
    }

    [Fact]
    public void Start_SetsRunningState()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root").Build());

        Assert.False(scheduler.IsRunning);
        scheduler.Start(root);

        Assert.True(scheduler.IsRunning);
        Assert.Equal(0L, scheduler.Tick);
        Assert.Null(scheduler.Result);
    }

    [Fact]
    public void Start_RejectsNullRoot()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();

        Assert.Throws<ArgumentNullException>(() => scheduler.Start(null!));
    }

    [Fact]
    public void Start_RejectsSecondStart()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root").Build());
        scheduler.Start(root);

        Assert.Throws<InvalidOperationException>(() => scheduler.Start(root));
    }

    [Fact]
    public void Start_RejectsRootWithParent()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction parent = scheduler.CreateAction("parent", new ScriptProbe("parent").Build());
        GameAction child = scheduler.CreateAction("child", new ScriptProbe("child").Build());
        parent.AddChild(child);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => scheduler.Start(child));

        Assert.Equal("root", exception.ParamName);
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void Start_RejectsCompletedRoot()
    {
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root").Build());
        scheduler.Start(root);
        scheduler.Step(0);
        Assert.True(root.Completed);

        Assert.Throws<InvalidOperationException>(() => scheduler.Start(root));
    }
}
