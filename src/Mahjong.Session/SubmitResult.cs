namespace Mahjong.Session;

/// <summary>
/// 命令提交结果。校验失败不改变局面、不产事件、不推进 tick，只返回原因。
/// </summary>
/// <param name="Accepted">命令是否被接受并记录为对应请求的应答。</param>
/// <param name="Reason">拒绝原因，人类可读短句，不属于协议；接受时为 <c>null</c>。</param>
/// <param name="RequestId">被应答的请求 id；拒绝时为 0。</param>
public readonly record struct SubmitResult(bool Accepted, string? Reason, long RequestId);
