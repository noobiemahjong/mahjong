using Mahjong.Core.Flow;
using Mahjong.Session;

namespace Mahjong.Tests.Session.Requests;

/// <summary>按调用顺序记录请求开闭的假 sink；同时保留结构化快照供断言。</summary>
internal sealed class RecordingRequestSink : ISessionRequestSink
{
    /// <summary>开闭调用序列，形如 open:1 / close:1:Answered。</summary>
    public List<string> Log { get; } = [];

    /// <summary>RequestOpened 的请求快照，按调用顺序。</summary>
    public List<PendingRequest> Opened { get; } = [];

    /// <summary>RequestClosed 的快照，按调用顺序。</summary>
    public List<ClosedRecord> Closed { get; } = [];

    public void RequestOpened(PendingRequest request)
    {
        Opened.Add(request);
        Log.Add($"open:{request.RequestId}");
    }

    public void RequestClosed(PendingRequest request, RequestOutcomeKind outcome, GameCommand? answer)
    {
        Closed.Add(new ClosedRecord(request, outcome, answer));
        Log.Add($"close:{request.RequestId}:{outcome}");
    }
}

/// <summary>一次 RequestClosed 的结构化快照。</summary>
internal readonly record struct ClosedRecord(PendingRequest Request, RequestOutcomeKind Outcome, GameCommand? Answer);
