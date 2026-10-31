using Mahjong.Core.Flow;

namespace Mahjong.Session;

/// <summary>
/// 请求登记表向会话上报请求生命周期的接缝：实现者负责把请求开闭写进事件流。
/// 实现必须保持调用顺序（RequestOpened 在开窗/开请求时，RequestClosed 在 BeginTick 结算时）。
/// </summary>
internal interface ISessionRequestSink
{
    /// <summary>请求已打开。</summary>
    /// <param name="request">打开的请求。</param>
    void RequestOpened(PendingRequest request);

    /// <summary>请求已结算。</summary>
    /// <param name="request">被结算的请求。</param>
    /// <param name="outcome">结算种类。</param>
    /// <param name="answer">Answered 时为实际应答命令；Cancelled/TimedOut 为 null。</param>
    void RequestClosed(PendingRequest request, RequestOutcomeKind outcome, GameCommand? answer);
}
