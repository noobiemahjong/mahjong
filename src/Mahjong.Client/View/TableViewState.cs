using Mahjong.Core.Flow;

namespace Mahjong.Client.View;

/// <summary>牌的可展示快照：物理牌 id、ASCII 标签与鬼牌标记；不含规则层其它字段。</summary>
public readonly record struct TileView(ushort Id, string Label, bool IsJoker);

/// <summary>
/// 一个座位的展示快照。相对座位 0 是自己、1 下家、2 对家、3 上家；
/// 他人手牌只暴露张数，不携带牌 id 或牌种。
/// </summary>
public sealed record PlayerView(
    byte Seat,
    byte RelativeSeat,
    string Name,
    short Score,
    bool IsBanker,
    bool IsCurrent,
    int HandCount,
    IReadOnlyList<TileView> Melds,
    IReadOnlyList<TileView> Discards);

/// <summary>自己手牌的展示项：展示序号与手牌域真实槽位分离，提交命令必须用 <see cref="HandSlot"/>。</summary>
public readonly record struct HandTileView(ushort Id, string Label, bool IsJoker, int HandSlot);

/// <summary>一帧的牌桌快照：由权威 GameState 与开放请求投影得到，视图只读它、不读会话。</summary>
public sealed record TableViewState(
    byte RoundNo,
    byte ViewerSeat,
    ushort WallCount,
    IReadOnlyList<PlayerView> Players,
    IReadOnlyList<HandTileView> Hand,
    ushort? SelectedTileId,
    IReadOnlyList<CommandVerb> EnabledActions,
    string StatusLine);
