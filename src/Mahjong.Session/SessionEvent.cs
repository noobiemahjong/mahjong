namespace Mahjong.Session;

/// <summary>
/// 会话事件：序号、产出 tick、种类与二进制 payload 的只读快照。
/// <see cref="Seq"/> 从 1 单调递增、Drain 后不重复；<see cref="Tick"/> 是产出该事件的逻辑 tick；
/// payload 布局见 T-5 契约的事件 payload 表，唯一公开读取入口是 <see cref="SessionEventPayloadReader"/>。
/// </summary>
/// <param name="Seq">事件序号，从 1 起按产出顺序单调递增。</param>
/// <param name="Tick">产出该事件的逻辑 tick。</param>
/// <param name="Kind">事件种类，决定 payload 布局。</param>
/// <param name="Payload">小端、按序紧排、无对齐的 payload；只读内存，不随缓冲后续写入变化。</param>
public readonly record struct SessionEvent(long Seq, long Tick, SessionEventKind Kind, ReadOnlyMemory<byte> Payload);
