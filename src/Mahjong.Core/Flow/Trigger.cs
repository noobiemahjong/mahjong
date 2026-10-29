namespace Mahjong.Core.Flow;

/// <summary>
/// 事件触发器：按 <see cref="Priority"/> 排序产出驱动流程的动作协程。
/// 调度器不派发触发器，也不持有触发器注册表；动作树由调用方用 <see cref="ActionScheduler.CreateAction"/> 直接构造。
/// </summary>
public abstract class Trigger
{
    /// <summary>同一事件内多个触发器的派发顺序，数值大的先。</summary>
    public virtual int Priority => 10;

    /// <summary>产出驱动该触发器流程的动作协程。</summary>
    /// <param name="args">事件参数，含触发时的局面与可选负载。</param>
    /// <returns>产出 <see cref="ActionState"/> 的协程；由调度器推进与 Dispose。</returns>
    public abstract IEnumerator<ActionState> Run(GameEventArgs args);
}
