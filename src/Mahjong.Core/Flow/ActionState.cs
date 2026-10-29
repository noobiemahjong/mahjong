namespace Mahjong.Core.Flow;

/// <summary>动作在单次 <c>MoveNext</c> 后要求调度器执行的状态。</summary>
public enum ActionStatus : byte
{
    /// <summary>本 tick 继续执行该动作。</summary>
    Continue = 0,

    /// <summary>等待若干个逻辑 tick。</summary>
    WaitTicks = 1,

    /// <summary>每 tick 求值一次谓词，为真后继续。</summary>
    WaitUntil = 2,

    /// <summary>等待请求结算（应答/超时/取消）。</summary>
    WaitRequest = 3,

    /// <summary>中断若干层祖先动作后继续当前动作。</summary>
    AbortParents = 4,

    /// <summary>中断当前动作的全部兄弟后继续当前动作。</summary>
    AbortSiblings = 5,

    /// <summary>以结果终止整棵动作树。</summary>
    Complete = 6,
}

/// <summary>
/// 动作返回值：只表达一个状态，未用到的字段取默认值（0 / null）。
/// 谓词每次被调度器访问时重新调用，不缓存结果；不提供值相等性。
/// </summary>
public readonly struct ActionState
{
    private ActionState(
        ActionStatus status,
        int ticks,
        Func<bool>? predicate,
        long requestId,
        int depth,
        object? result)
    {
        Status = status;
        Ticks = ticks;
        Predicate = predicate;
        RequestId = requestId;
        Depth = depth;
        Result = result;
    }

    /// <summary>状态种类。</summary>
    public ActionStatus Status { get; }

    /// <summary>等待的 tick 数，仅 <see cref="ActionStatus.WaitTicks"/> 有意义。</summary>
    public int Ticks { get; }

    /// <summary>解除条件，仅 <see cref="ActionStatus.WaitUntil"/> 有意义；每次访问重新求值。</summary>
    public Func<bool>? Predicate { get; }

    /// <summary>等待的请求 id，仅 <see cref="ActionStatus.WaitRequest"/> 有意义。</summary>
    public long RequestId { get; }

    /// <summary>向上中断的祖先层数，仅 <see cref="ActionStatus.AbortParents"/> 有意义。</summary>
    public int Depth { get; }

    /// <summary>动作树的结果，仅 <see cref="ActionStatus.Complete"/> 有意义。</summary>
    public object? Result { get; }

    /// <summary>继续执行。</summary>
    public static ActionState Continue { get; } = new(ActionStatus.Continue, 0, null, 0, 0, null);

    /// <summary>等待 <paramref name="ticks"/> 个逻辑 tick；0 表示本次 Step 先返回，下次访问时立即解除。</summary>
    /// <param name="ticks">等待的 tick 数，0 合法。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticks"/> 为负。</exception>
    public static ActionState WaitTicks(int ticks)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Wait tick count must not be negative.");
        }

        return new ActionState(ActionStatus.WaitTicks, ticks, null, 0, 0, null);
    }

    /// <summary>等待谓词返回 true；每 tick 求值一次。</summary>
    /// <param name="predicate">解除条件。</param>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> 为 null。</exception>
    public static ActionState WaitUntil(Func<bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new ActionState(ActionStatus.WaitUntil, 0, predicate, 0, 0, null);
    }

    /// <summary>等待请求结算。</summary>
    /// <param name="requestId">由请求注册表分配的请求 id。</param>
    public static ActionState WaitRequest(long requestId) =>
        new(ActionStatus.WaitRequest, 0, null, requestId, 0, null);

    /// <summary>中断至多 <paramref name="depth"/> 层祖先，然后继续当前动作。</summary>
    /// <param name="depth">向上中断的祖先层数，至少 1。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> 小于 1。</exception>
    public static ActionState AbortParents(int depth = 1)
    {
        if (depth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), depth, "Abort depth must be at least 1.");
        }

        return new ActionState(ActionStatus.AbortParents, 0, null, 0, depth, null);
    }

    /// <summary>中断当前动作的全部兄弟，然后继续当前动作。</summary>
    public static ActionState AbortSiblings { get; } = new(ActionStatus.AbortSiblings, 0, null, 0, 0, null);

    /// <summary>以 <paramref name="result"/> 终止整棵动作树。</summary>
    /// <param name="result">写入 <see cref="ActionScheduler.Result"/> 的结果。</param>
    public static ActionState Complete(object? result = null) =>
        new(ActionStatus.Complete, 0, null, 0, 0, result);
}
