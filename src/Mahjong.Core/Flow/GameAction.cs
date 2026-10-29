namespace Mahjong.Core.Flow;

/// <summary>
/// 动作树节点：一个产出 <see cref="ActionState"/> 的协程体加父子关系，以及该动作自己的阻塞等待状态。
/// 阻塞状态按动作持有，同一时刻多层动作可以各自等待，互不覆盖。
/// 只能由 <see cref="ActionScheduler.CreateAction"/> 创建；<see cref="Id"/> 与 <see cref="CreationOrder"/> 由调度器实例分配，没有静态计数。
/// </summary>
public sealed class GameAction
{
    private readonly List<GameAction> _children = [];

    private GameAction? _parent;
    private long _creationOrder;
    private long _nextChildOrder;
    private bool _routineDisposed;

    private ActionStatus _blockedStatus;
    private long _wakeTick;
    private Func<bool>? _predicate;
    private long _blockedRequestId;

    /// <summary>由调度器创建；外部不能构造，避免绕过 Id 与创建序分配。</summary>
    internal GameAction(long id, string name, IEnumerator<ActionState> routine, int priority, bool interruptible)
    {
        Id = id;
        Name = name;
        Routine = routine;
        Priority = priority;
        Interruptible = interruptible;
    }

    /// <summary>调度器实例内唯一的动作 id，从 1 起按创建顺序递增。</summary>
    public long Id { get; }

    /// <summary>同一父节点下的创建序，从 0 起；由 <see cref="AddChild"/> 分配，改挂后重新分配，成为根时为 0。</summary>
    public long CreationOrder => _creationOrder;

    /// <summary>动作名，用于排障与日志标签，不参与调度。</summary>
    public string Name { get; }

    /// <summary>调度优先级：同一父节点下数值大的先执行；相同则按 <see cref="CreationOrder"/> 升序。</summary>
    public int Priority { get; }

    /// <summary>父动作；未挂载时为 null。</summary>
    public GameAction? Parent => _parent;

    /// <summary>动作协程；被中断或完成时由调度器 Dispose 一次。</summary>
    public IEnumerator<ActionState> Routine { get; }

    /// <summary>是否已结束；已完成动作不会再执行，其协程已 Dispose。</summary>
    public bool Completed { get; internal set; }

    /// <summary>是否可被中断；调度器保留该标记，不据此改变推进逻辑。</summary>
    public bool Interruptible { get; }

    /// <summary>当前子动作，按挂载顺序排列；执行顺序由调度器按优先级与创建序选择。</summary>
    internal List<GameAction> Children => _children;

    /// <summary>是否正在等待；未阻塞时为 false。</summary>
    internal bool IsBlocked => _blockedStatus != ActionStatus.Continue;

    /// <summary>等待种类；未阻塞时为 <see cref="ActionStatus.Continue"/>。</summary>
    internal ActionStatus BlockedStatus => _blockedStatus;

    /// <summary>解除等待的 tick；仅 <see cref="ActionStatus.WaitTicks"/> 有意义。</summary>
    internal long WakeTick => _wakeTick;

    /// <summary>解除等待的谓词；仅 <see cref="ActionStatus.WaitUntil"/> 有意义。</summary>
    internal Func<bool>? Predicate => _predicate;

    /// <summary>等待中的请求 id；仅 <see cref="ActionStatus.WaitRequest"/> 有意义。</summary>
    internal long BlockedRequestId => _blockedRequestId;

    /// <summary>进入 <see cref="ActionStatus.WaitTicks"/> 等待，在 <paramref name="wakeTick"/> 及之后的 tick 解除。</summary>
    internal void BlockUntilTick(long wakeTick)
    {
        _blockedStatus = ActionStatus.WaitTicks;
        _wakeTick = wakeTick;
    }

    /// <summary>进入 <see cref="ActionStatus.WaitUntil"/> 等待，谓词为真时解除。</summary>
    internal void BlockOnPredicate(Func<bool>? predicate)
    {
        _blockedStatus = ActionStatus.WaitUntil;
        _predicate = predicate;
    }

    /// <summary>进入 <see cref="ActionStatus.WaitRequest"/> 等待，请求结算后解除。</summary>
    internal void BlockOnRequest(long requestId)
    {
        _blockedStatus = ActionStatus.WaitRequest;
        _blockedRequestId = requestId;
    }

    /// <summary>清空本动作的等待状态；重复调用无副作用。</summary>
    internal void ClearBlocking()
    {
        _blockedStatus = ActionStatus.Continue;
        _wakeTick = 0;
        _predicate = null;
        _blockedRequestId = 0;
    }

    /// <summary>挂载子动作：分配 <see cref="CreationOrder"/> 并追加到子列表尾部。</summary>
    /// <param name="child">要挂载的子动作，必须没有父动作且未完成。</param>
    /// <exception cref="ArgumentNullException"><paramref name="child"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException"><paramref name="child"/> 是自己、已有父动作或已完成；失败时父子两侧都不变。</exception>
    public void AddChild(GameAction child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (ReferenceEquals(child, this))
        {
            throw new InvalidOperationException("An action cannot be its own child.");
        }

        if (child._parent is not null)
        {
            throw new InvalidOperationException(
                ReferenceEquals(child._parent, this)
                    ? $"Action {child.Id} is already a child of action {Id}."
                    : $"Action {child.Id} already has parent action {child._parent.Id}.");
        }

        if (child.Completed)
        {
            throw new InvalidOperationException($"Completed action {child.Id} cannot become a child.");
        }

        child._parent = this;
        child._creationOrder = _nextChildOrder++;
        _children.Add(child);
    }

    /// <summary>解除父关系；只由调度器在改挂或清理子树时调用。</summary>
    internal void DetachFromParent() => _parent = null;

    /// <summary>把 <see cref="CreationOrder"/> 重置为 0（该动作成为新的根时）。</summary>
    internal void ResetCreationOrderAsRoot() => _creationOrder = 0;

    /// <summary>对协程 Dispose 至多一次；重复调用无副作用。</summary>
    internal void DisposeRoutineOnce()
    {
        if (_routineDisposed)
        {
            return;
        }

        _routineDisposed = true;
        Routine.Dispose();
    }
}
