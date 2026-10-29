using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Tests.Flow.Support;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// 确定性：同一脚本在两份独立局面上跑两遍得到同一状态哈希与同一执行轨迹；
/// 两个调度器交错 Step 时各自的动作 Id、请求消费与结果都与单独跑一致（无跨实例/静态状态）。
/// </summary>
public sealed class FlowDeterminismTests
{
    [Fact]
    public void SameScript_RunTwice_ProducesSameStateHashAndTrace()
    {
        string firstHash = RunMutableScript(out string firstTrace);
        string secondHash = RunMutableScript(out string secondTrace);

        Assert.Equal(firstHash, secondHash);
        Assert.Equal(firstTrace, secondTrace);
        Assert.Equal(64, firstHash.Length);
    }

    [Fact]
    public void InterleavedSchedulers_MatchTheirSoloRuns()
    {
        (string TraceA, string TraceB) solo = RunPair(interleaved: false);
        (string TraceA, string TraceB) interleaved = RunPair(interleaved: true);

        Assert.Equal(solo.TraceA, interleaved.TraceA);
        Assert.Equal(solo.TraceB, interleaved.TraceB);
    }

    [Fact]
    public void SeparateSchedulers_AllocateActionIdsAndTicksIndependently()
    {
        ActionScheduler first = FlowTestData.CreateScheduler();
        ActionScheduler second = FlowTestData.CreateScheduler();
        GameAction firstRoot = first.CreateAction("first", new ScriptProbe("first").Build());
        GameAction secondRoot = second.CreateAction("second", new ScriptProbe("second").Build());

        first.Step(1);
        second.Step(9);

        Assert.Equal(1L, firstRoot.Id);
        Assert.Equal(1L, secondRoot.Id);
        Assert.Equal(1L, first.Tick);
        Assert.Equal(9L, second.Tick);
    }

    private static string RunMutableScript(out string trace)
    {
        var log = new List<string>();
        GameState state = FlowTestData.DealtState();
        ActionScheduler scheduler = FlowTestData.CreateScheduler();
        GameAction root = scheduler.CreateAction("root", new ScriptProbe("root").Build());
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            GameAction child = scheduler.CreateAction("seat" + seat, DrawAndDiscard(log, state, seat));
            root.AddChild(child);
        }

        scheduler.Start(root);
        for (long tick = 0; tick < 4; tick++)
        {
            scheduler.Step(tick);
        }

        trace = string.Join("|", log);
        return StateHash.Compute(state);
    }

    private static IEnumerator<ActionState> DrawAndDiscard(List<string> log, GameState state, byte seat)
    {
        log.Add("draw:" + seat);
        state.DrawFromWall(seat);
        yield return ActionState.Continue;
        log.Add("discard:" + seat);
        state.Discard(seat, 0);
        yield return ActionState.Continue;
    }

    private static (string TraceA, string TraceB) RunPair(bool interleaved)
    {
        (FakeRequestRegistry RegistryA, ActionScheduler SchedulerA, ScriptProbe ProbeA, GameAction RootA) a = CreatePeer(7, "A");
        (FakeRequestRegistry RegistryB, ActionScheduler SchedulerB, ScriptProbe ProbeB, GameAction RootB) b = CreatePeer(8, "B");

        if (interleaved)
        {
            for (long tick = 0; tick < 6; tick++)
            {
                RespondAtTick(a.RegistryA, 7, tick);
                RespondAtTick(b.RegistryB, 8, tick);
                a.SchedulerA.Step(tick);
                b.SchedulerB.Step(tick);
            }
        }
        else
        {
            RunSolo(a.RegistryA, a.SchedulerA, 7);
            RunSolo(b.RegistryB, b.SchedulerB, 8);
        }

        string traceA = BuildTrace(a.RegistryA, a.SchedulerA, a.ProbeA, a.RootA);
        string traceB = BuildTrace(b.RegistryB, b.SchedulerB, b.ProbeB, b.RootB);
        return (traceA, traceB);
    }

    private static void RunSolo(FakeRequestRegistry registry, ActionScheduler scheduler, long requestId)
    {
        for (long tick = 0; tick < 6; tick++)
        {
            RespondAtTick(registry, requestId, tick);
            scheduler.Step(tick);
        }
    }

    private static void RespondAtTick(FakeRequestRegistry registry, long requestId, long tick)
    {
        if (tick == 2)
        {
            registry.Respond(requestId, RequestOutcomeKind.Answered, "payload-" + requestId);
        }
    }

    private static (
        FakeRequestRegistry Registry,
        ActionScheduler Scheduler,
        ScriptProbe Probe,
        GameAction Root) CreatePeer(long requestId, string name)
    {
        FakeRequestRegistry registry = new();
        ActionScheduler scheduler = FlowTestData.CreateScheduler(registry);
        var probe = new ScriptProbe(name);
        GameAction root = scheduler.CreateAction(
            name,
            probe.Build(
                ActionState.WaitRequest(requestId),
                ActionState.WaitTicks(1),
                ActionState.Continue,
                ActionState.Complete(name)));
        scheduler.Start(root);
        return (registry, scheduler, probe, root);
    }

    private static string BuildTrace(
        FakeRequestRegistry registry,
        ActionScheduler scheduler,
        ScriptProbe probe,
        GameAction root) =>
        string.Join(
            ";",
            "id=" + root.Id,
            "result=" + scheduler.Result,
            "moves=" + probe.MoveNextCount,
            "dispose=" + probe.DisposeCount,
            "finally=" + probe.FinallyCount,
            "begin=" + string.Join(",", registry.BeginTicks),
            "consume=" + string.Join(",", registry.ConsumeAttempts),
            "tick=" + scheduler.Tick);
}
