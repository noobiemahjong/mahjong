using Mahjong.Core.Rules;

namespace Mahjong.Core.Flow;

/// <summary>
/// 事件参数：承载触发时的权威局面与可选负载。
/// </summary>
public sealed class GameEventArgs
{
    /// <summary>创建事件参数。</summary>
    /// <param name="state">触发时的权威局面，触发器只读不改。</param>
    /// <param name="payload">可选负载，语义由事件种类决定。</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> 为 null。</exception>
    public GameEventArgs(GameState state, object? payload = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
        Payload = payload;
    }

    /// <summary>触发时的权威局面。</summary>
    public GameState State { get; }

    /// <summary>可选负载；语义由具体事件决定，无负载时为 null。</summary>
    public object? Payload { get; }
}
