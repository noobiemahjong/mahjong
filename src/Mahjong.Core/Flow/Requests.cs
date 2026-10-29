using System.Collections.ObjectModel;

namespace Mahjong.Core.Flow;

/// <summary>请求结算结果种类。数值是协议常量，已发布后不得改动。</summary>
public enum RequestOutcomeKind : byte
{
    /// <summary>玩家应答。</summary>
    Answered = 0,

    /// <summary>超时。</summary>
    TimedOut = 1,

    /// <summary>取消（如请求组仲裁中落败）。</summary>
    Cancelled = 2,
}

/// <summary>请求结算结果：一部分请求为一次结算，只被消费一次。</summary>
/// <param name="RequestId">被结算的请求 id。</param>
/// <param name="Kind">结算种类。</param>
/// <param name="Payload">应答负载，超时/取消时为 null 或由会话决定。</param>
public readonly record struct RequestOutcome(long RequestId, RequestOutcomeKind Kind, object? Payload);

/// <summary>
/// 请求注册表：调度器在每 tick 推进动作前调用 <see cref="BeginTick"/> 结算超时/取消，
/// 动作用 <see cref="TryConsume"/> 取走结算结果。开窗、分配请求 id、产出结算结果由会话负责。
/// </summary>
public interface IRequestRegistry
{
    /// <summary>推进到指定 tick：把到期未应答的请求写成超时结果，把被取消的请求写成取消结果。</summary>
    /// <param name="tick">当前逻辑 tick。</param>
    void BeginTick(long tick);

    /// <summary>尝试取走一次结算结果；同一结果只成功一次。</summary>
    /// <param name="requestId">请求 id。</param>
    /// <param name="outcome">结算结果；返回 false 时无意义。</param>
    /// <returns>该请求已结算且结果尚未被消费返回 true。</returns>
    bool TryConsume(long requestId, out RequestOutcome outcome);
}

/// <summary>请求种类。数值是协议常量，已发布后不得改动。</summary>
public enum RequestKind : byte
{
    /// <summary>要求出牌。</summary>
    Discard = 0,

    /// <summary>征询碰。</summary>
    Pong = 1,

    /// <summary>征询杠。</summary>
    Kong = 2,

    /// <summary>征询胡。</summary>
    Win = 3,

    /// <summary>征询过。</summary>
    Pass = 4,
}

/// <summary>待结算请求：向一个座位征询一组动词，带截止 tick。</summary>
/// <param name="RequestId">请求 id，由会话分配。</param>
/// <param name="Seat">目标座位 0–3。</param>
/// <param name="Kind">请求种类。</param>
/// <param name="Allowed">允许应答的动词位集。</param>
/// <param name="DeadlineTick">截止 tick，到达后按超时结算。</param>
public readonly record struct PendingRequest(
    long RequestId,
    byte Seat,
    RequestKind Kind,
    AllowedVerbs Allowed,
    long DeadlineTick);

/// <summary>
/// 请求组：同一时刻向多个座位征询的快照（碰/杠竞速），仲裁与结果是会话的职责。
/// </summary>
public sealed class RequestGroup
{
    /// <summary>创建请求组；传入集合在构造时复制，之后修改源集合不影响本组。</summary>
    /// <param name="requestGroupId">请求组 id，任意 long，由会话分配。</param>
    /// <param name="requests">组内请求快照。</param>
    /// <exception cref="ArgumentNullException"><paramref name="requests"/> 为 null。</exception>
    public RequestGroup(long requestGroupId, IEnumerable<PendingRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        RequestGroupId = requestGroupId;
        Requests = new ReadOnlyCollection<PendingRequest>(new List<PendingRequest>(requests));
    }

    /// <summary>组内请求，顺序与构造时传入顺序一致；只读快照。</summary>
    public IReadOnlyList<PendingRequest> Requests { get; }

    /// <summary>请求组 id。</summary>
    public long RequestGroupId { get; }
}
