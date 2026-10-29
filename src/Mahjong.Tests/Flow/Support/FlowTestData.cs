using Mahjong.Core.Flow;
using Mahjong.Core.Rules;

namespace Mahjong.Tests.Flow.Support;

/// <summary>
/// 可观测的动作脚本：包装一层计数枚举器，内层是带 try/finally 的迭代器。
/// <see cref="MoveNextCount"/> 统计调度器实际调用的 MoveNext 次数（含返回 false 的那次），
/// <see cref="DisposeCount"/> 由包装层统计，<see cref="FinallyCount"/> 由迭代器 finally 统计；
/// 两者都为 1 才说明 Dispose 让 finally 执行且恰好执行一次。
/// </summary>
internal sealed class ScriptProbe
{
    private readonly List<string>? _log;

    /// <summary>创建脚本探针。</summary>
    /// <param name="name">脚本名；传入日志列表时每次 MoveNext 记一条。</param>
    /// <param name="log">可选的执行日志；为 null 时不记录。</param>
    public ScriptProbe(string name, List<string>? log = null)
    {
        Name = name;
        _log = log;
    }

    /// <summary>脚本名。</summary>
    public string Name { get; }

    /// <summary>调度器调用 MoveNext 的次数（含末尾返回 false 的一次）。</summary>
    public int MoveNextCount { get; private set; }

    /// <summary>调度器调用 Dispose 的次数；大于 1 表示重复 Dispose。</summary>
    public int DisposeCount { get; private set; }

    /// <summary>迭代器 finally 的执行次数；证明 Dispose 触发了 finally。</summary>
    public int FinallyCount { get; private set; }

    /// <summary>Dispose 与 finally 各恰好执行一次。</summary>
    public bool DisposedExactlyOnce => DisposeCount == 1 && FinallyCount == 1;

    /// <summary>按顺序返回给定 ActionState 的脚本；迭代结束后返回 false。</summary>
    /// <param name="steps">脚本逐步产出的状态。</param>
    /// <returns>带计数包装的枚举器。</returns>
    public IEnumerator<ActionState> Build(params ActionState[] steps) =>
        new CountingRoutine(this, ScriptIterator(steps, null));

    /// <summary>按顺序返回给定 ActionState 的脚本，并在每次 MoveNext 产生状态前回调步号（1 起）。</summary>
    /// <param name="onMove">每次 MoveNext 内层执行前的回调；用于在动作体内动态挂子动作等副作用。</param>
    /// <param name="steps">脚本逐步产出的状态。</param>
    /// <returns>带计数包装的枚举器。</returns>
    public IEnumerator<ActionState> Build(Action<int> onMove, params ActionState[] steps) =>
        new CountingRoutine(this, ScriptIterator(steps, onMove));

    /// <summary>包装层调用：记一次 MoveNext，可选记日志。</summary>
    internal void OnMoveNext()
    {
        MoveNextCount++;
        _log?.Add(Name);
    }

    /// <summary>包装层调用：记一次 Dispose。</summary>
    internal void OnDispose() => DisposeCount++;

    private IEnumerator<ActionState> ScriptIterator(ActionState[] steps, Action<int>? onMove)
    {
        try
        {
            int move = 0;
            foreach (ActionState step in steps)
            {
                move++;
                onMove?.Invoke(move);
                yield return step;
            }
        }
        finally
        {
            FinallyCount++;
        }
    }

    private sealed class CountingRoutine : IEnumerator<ActionState>
    {
        private readonly ScriptProbe _probe;
        private readonly IEnumerator<ActionState> _inner;

        public CountingRoutine(ScriptProbe probe, IEnumerator<ActionState> inner)
        {
            _probe = probe;
            _inner = inner;
        }

        public ActionState Current { get; private set; }

        object System.Collections.IEnumerator.Current => Current;

        public bool MoveNext()
        {
            _probe.OnMoveNext();
            if (!_inner.MoveNext())
            {
                Current = default;
                return false;
            }

            Current = _inner.Current;
            return true;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose()
        {
            _probe.OnDispose();
            _inner.Dispose();
        }
    }
}

/// <summary>
/// 假请求注册表：记录每个 tick 的 BeginTick 与每次 TryConsume 的 requestId；
/// 结果按 requestId 预先放入，用掉即移除（与「只消费一次」语义一致），便于验证 false→true 的时序。
/// </summary>
internal sealed class FakeRequestRegistry : IRequestRegistry
{
    private readonly Dictionary<long, RequestOutcome> _outcomes = [];

    /// <summary>Step 每次调用 BeginTick 记录到的 tick 序列。</summary>
    public List<long> BeginTicks { get; } = [];

    /// <summary>每次 TryConsume 尝试的 requestId 序列。</summary>
    public List<long> ConsumeAttempts { get; } = [];

    /// <summary>记录一次 BeginTick。</summary>
    /// <param name="tick">调度器传入的 tick。</param>
    public void BeginTick(long tick) => BeginTicks.Add(tick);

    /// <summary>若该 requestId 已有结果则消费并返回 true，否则返回 false。</summary>
    /// <param name="requestId">请求 id。</param>
    /// <param name="outcome">消费到的结果；未命中时为 default。</param>
    /// <returns>命中并消费返回 true。</returns>
    public bool TryConsume(long requestId, out RequestOutcome outcome)
    {
        ConsumeAttempts.Add(requestId);
        if (_outcomes.Remove(requestId, out outcome))
        {
            return true;
        }

        outcome = default;
        return false;
    }

    /// <summary>给某个 requestId 放一个可见结果；之后第一次 TryConsume 会消费它。</summary>
    /// <param name="requestId">请求 id。</param>
    /// <param name="kind">结果种类。</param>
    /// <param name="payload">可选负载。</param>
    public void Respond(long requestId, RequestOutcomeKind kind, object? payload = null) =>
        _outcomes[requestId] = new RequestOutcome(requestId, kind, payload);
}

/// <summary>Flow 测试的共享构造：调度器工厂与确定性局面。所有等待只由 Step 的 tick 参数驱动。</summary>
internal static class FlowTestData
{
    /// <summary>契约默认动作优先级。</summary>
    public const int DefaultPriority = 10;

    /// <summary>创建带假注册表的调度器；不传注册表时用新的空实例。</summary>
    /// <param name="requests">请求注册表。</param>
    /// <param name="maxStepsPerTick">每 tick 最大 MoveNext 次数。</param>
    public static ActionScheduler CreateScheduler(FakeRequestRegistry? requests = null, int maxStepsPerTick = 10_000) =>
        new(requests ?? new FakeRequestRegistry(), maxStepsPerTick);

    /// <summary>用固定种子发牌，得到可重复的局面。</summary>
    /// <param name="seed">固定种子。</param>
    /// <param name="bankerSeat">庄家座位。</param>
    public static GameState DealtState(ulong seed = 20_261_029UL, byte bankerSeat = 0)
    {
        var state = new GameState();
        state.Deal(new Pcg32(seed), bankerSeat);
        return state;
    }
}
