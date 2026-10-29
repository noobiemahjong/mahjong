namespace Mahjong.Core.Flow;

/// <summary>
/// 动作树调度器：每个逻辑 tick 推进一次，把 <see cref="ActionState"/> 翻译成继续、阻塞或中断。
/// 不读时钟（<see cref="Tick"/> 只来自 <see cref="Step"/> 参数）、不感知命令队列；命令由会话在调用 <see cref="Step"/> 前排空并应用。
/// 阻塞等待状态由动作自身持有，因此同一时刻可以有多层动作各自等待，只有当前动作的等待参与解除判断。
/// 实例非线程安全，不得跨房间共享。
/// </summary>
public sealed class ActionScheduler
{
    private readonly IRequestRegistry _requests;
    private readonly int _maxStepsPerTick;

    private GameAction? _root;
    private GameAction? _current;
    private long _nextActionId = 1;

    /// <summary>创建调度器。</summary>
    /// <param name="requests">请求注册表：每 tick 结算超时/取消，供 <see cref="ActionStatus.WaitRequest"/> 消费。</param>
    /// <param name="maxStepsPerTick">单 tick 允许的 <c>MoveNext</c> 次数上限，用于把死循环暴露成异常。</param>
    /// <exception cref="ArgumentNullException"><paramref name="requests"/> 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxStepsPerTick"/> 小于 1。</exception>
    public ActionScheduler(IRequestRegistry requests, int maxStepsPerTick = 10_000)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (maxStepsPerTick < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxStepsPerTick),
                maxStepsPerTick,
                "maxStepsPerTick must be at least 1.");
        }

        _requests = requests;
        _maxStepsPerTick = maxStepsPerTick;
    }

    /// <summary>是否有正在推进的动作树：<see cref="Start"/> 之后、树结束之前为 true。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>动作树以 <see cref="ActionStatus.Complete"/> 结束时写入的结果；实例创建后、首次 Complete 之前为 null。</summary>
    public object? Result { get; private set; }

    /// <summary>最近一次 <see cref="Step"/> 传入的 tick；调度器不自己读时钟。</summary>
    public long Tick { get; private set; }

    /// <summary>创建一个未挂父的动作；<see cref="GameAction.Id"/> 由本实例从 1 起递增。</summary>
    /// <param name="name">动作名，用于排障。</param>
    /// <param name="routine">动作协程，不得为 null。</param>
    /// <param name="priority">调度优先级，同父下数值大的先执行。</param>
    /// <param name="interruptible">是否可被中断；调度器只保留该标记，不据此改变推进逻辑。</param>
    /// <returns>未挂父、未完成、创建序为 0 的动作。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 或 <paramref name="routine"/> 为 null。</exception>
    public GameAction CreateAction(
        string name,
        IEnumerator<ActionState> routine,
        int priority = 10,
        bool interruptible = true)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(routine);
        return new GameAction(_nextActionId++, name, routine, priority, interruptible);
    }

    /// <summary>开始推进一棵动作树；之后每次 <see cref="Step"/> 推进它。</summary>
    /// <param name="root">根动作，必须没有父动作且未完成。</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">已有动作树在运行；或 <paramref name="root"/> 已完成。失败时调度器状态不变。</exception>
    /// <exception cref="ArgumentException"><paramref name="root"/> 已有父动作。</exception>
    public void Start(GameAction root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (IsRunning)
        {
            throw new InvalidOperationException("The scheduler is already running an action tree.");
        }

        if (root.Parent is not null)
        {
            throw new ArgumentException("The root action must not have a parent.", nameof(root));
        }

        if (root.Completed)
        {
            throw new InvalidOperationException("A completed action cannot be started.");
        }

        _root = root;
        _current = root;
        IsRunning = true;
    }

    /// <summary>
    /// 推进一个逻辑 tick：先结算请求注册表（超时/取消写成结果），再按固定顺序推进动作树，直到阻塞或整棵树结束。
    /// 未 <see cref="Start"/> 或树已结束时只结算注册表并返回。
    /// </summary>
    /// <param name="tick">逻辑 tick，单调递增由调用方保证；调度器只用于等待结算。</param>
    /// <exception cref="InvalidOperationException">
    /// 单 tick 内 <c>MoveNext</c> 次数超过 <c>maxStepsPerTick</c>，或动作返回未定义状态；均属 bug。
    /// </exception>
    public void Step(long tick)
    {
        Tick = tick;
        _requests.BeginTick(tick);

        if (!IsRunning || _current is null)
        {
            return;
        }

        int steps = 0;
        while (true)
        {
            GameAction current = _current!;

            // 已完成动作绝不再执行：向上收回到父动作；根已完成即整棵树结束。
            if (current.Completed)
            {
                if (current.Parent is GameAction parent)
                {
                    _current = parent;
                    continue;
                }

                FinishRun();
                return;
            }

            // 有未完成的子动作时进入优先级最高者，本动作本 tick 不再执行。
            GameAction? child = SelectNextChild(current);
            if (child is not null)
            {
                _current = child;
                continue;
            }

            // 只有当前动作自己的等待参与解除判断；祖先的等待状态保留，不阻塞已挂载的子动作。
            if (current.IsBlocked && !TryUnblock(current, tick))
            {
                return;
            }

            if (steps >= _maxStepsPerTick)
            {
                throw new InvalidOperationException(
                    $"ActionScheduler exceeded maxStepsPerTick ({_maxStepsPerTick}) at tick {tick}; the action tree is most likely live-locked.");
            }

            steps++;

            if (!current.Routine.MoveNext())
            {
                // 协程体自然结束：标记完成，下一轮循环收回到父动作。
                current.Completed = true;
                continue;
            }

            ActionState state = current.Routine.Current;
            switch (state.Status)
            {
                case ActionStatus.Continue:
                    continue;

                case ActionStatus.WaitTicks:
                    current.BlockUntilTick(tick + state.Ticks);
                    return;

                case ActionStatus.WaitUntil:
                    current.BlockOnPredicate(state.Predicate);
                    return;

                case ActionStatus.WaitRequest:
                    current.BlockOnRequest(state.RequestId);
                    return;

                case ActionStatus.AbortParents:
                    AbortParents(current, state.Depth);
                    continue;

                case ActionStatus.AbortSiblings:
                    AbortSiblings(current);
                    continue;

                case ActionStatus.Complete:
                    CompleteRun(current, state.Result);
                    return;

                default:
                    throw new InvalidOperationException($"Unsupported action status {state.Status}.");
            }
        }
    }

    /// <summary>树以 Complete 结束时收尾：清当前动作的等待、写结果、整棵树标完成并 Dispose 各枚举器一次。</summary>
    private void CompleteRun(GameAction current, object? result)
    {
        current.ClearBlocking();
        Result = result;

        if (_root is not null)
        {
            KillSubtree(_root, null);
        }

        _current = null;
        IsRunning = false;
    }

    /// <summary>根动作协程自然结束时收尾：结果不变（只有 Complete 写结果）。</summary>
    private void FinishRun()
    {
        _current = null;
        IsRunning = false;
    }

    /// <summary>选择下一个要执行的动作：跳过已完成节点，优先级降序、同优先级创建序升序。</summary>
    private static GameAction? SelectNextChild(GameAction action)
    {
        List<GameAction> children = action.Children;
        GameAction? best = null;
        for (int index = 0; index < children.Count; index++)
        {
            GameAction child = children[index];
            if (child.Completed)
            {
                continue;
            }

            if (best is null
                || child.Priority > best.Priority
                || (child.Priority == best.Priority && child.CreationOrder < best.CreationOrder))
            {
                best = child;
            }
        }

        return best;
    }

    /// <summary>解除检查只针对传入动作自己的等待：满足条件时清空它的等待并返回 true；仍在等待返回 false。</summary>
    private bool TryUnblock(GameAction action, long tick)
    {
        switch (action.BlockedStatus)
        {
            case ActionStatus.WaitTicks:
                if (tick < action.WakeTick)
                {
                    return false;
                }

                break;

            case ActionStatus.WaitUntil:
                if (!action.Predicate!.Invoke())
                {
                    return false;
                }

                break;

            case ActionStatus.WaitRequest:
                if (!_requests.TryConsume(action.BlockedRequestId, out _))
                {
                    return false;
                }

                break;

            default:
                return false;
        }

        action.ClearBlocking();
        return true;
    }

    /// <summary>
    /// 中断由近及远的最多 <paramref name="depth"/> 个祖先：标记完成、清空子队列、Dispose 子树（跳过当前动作分支）。
    /// 之后把当前动作挂到最后一个被中断祖先的父节点（无则成为新的根），清空阻塞状态并继续。
    /// </summary>
    private void AbortParents(GameAction current, int depth)
    {
        GameAction? topParent = null;
        GameAction? ancestor = current.Parent;
        int remaining = depth;

        while (ancestor is not null && remaining > 0)
        {
            GameAction? above = ancestor.Parent;
            KillSubtree(ancestor, current);
            ancestor.DetachFromParent();
            above?.Children.Remove(ancestor);
            topParent = above;
            ancestor = above;
            remaining--;
        }

        current.DetachFromParent();
        if (topParent is not null)
        {
            topParent.AddChild(current);
        }
        else
        {
            current.ResetCreationOrderAsRoot();
            _root = current;
        }

        current.ClearBlocking();
    }

    /// <summary>
    /// 中断当前动作的全部兄弟：标记完成、Dispose 子树、从父动作的子队列移除；当前动作与父动作保留。
    /// 当前动作没有父动作时无兄弟可清理。
    /// </summary>
    private void AbortSiblings(GameAction current)
    {
        GameAction? parent = current.Parent;
        if (parent is not null)
        {
            List<GameAction> siblings = parent.Children;
            for (int index = siblings.Count - 1; index >= 0; index--)
            {
                GameAction sibling = siblings[index];
                if (ReferenceEquals(sibling, current))
                {
                    continue;
                }

                KillSubtree(sibling, null);
                sibling.DetachFromParent();
                siblings.RemoveAt(index);
            }
        }

        current.ClearBlocking();
    }

    /// <summary>
    /// 清理整棵子树：递归标记完成、清空各自的等待状态、各枚举器 Dispose 一次、清空子列表。
    /// <paramref name="skip"/> 及其子树不被动（用于 abort 时保留当前动作分支）。
    /// </summary>
    private static void KillSubtree(GameAction action, GameAction? skip)
    {
        if (ReferenceEquals(action, skip))
        {
            return;
        }

        List<GameAction> children = action.Children;
        for (int index = 0; index < children.Count; index++)
        {
            KillSubtree(children[index], skip);
        }

        children.Clear();
        action.Completed = true;
        action.ClearBlocking();
        action.DisposeRoutineOnce();
    }
}
