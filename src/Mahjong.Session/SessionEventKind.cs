namespace Mahjong.Session;

/// <summary>会话事件种类。数值是事件流协议常量，已发布后不得改动。</summary>
public enum SessionEventKind : byte
{
    /// <summary>一局开始：局号、庄家与发牌后牌墙张数。</summary>
    RoundStarted = 1,

    /// <summary>某座位从墙头摸牌开始新回合。</summary>
    TurnStarted = 2,

    /// <summary>发牌：某座位的起手牌。</summary>
    Deal = 3,

    /// <summary>手牌全量同步；M0.2 不产出，保留枚举与编解码。</summary>
    HandSync = 4,

    /// <summary>摸牌。</summary>
    Drew = 5,

    /// <summary>出牌。</summary>
    Discarded = 6,

    /// <summary>副露成立（碰/明杠/暗杠/补杠）。</summary>
    Meld = 7,

    /// <summary>请求打开。</summary>
    RequestOpened = 8,

    /// <summary>请求结算。</summary>
    RequestClosed = 9,

    /// <summary>一局结束。</summary>
    RoundEnded = 10,

    /// <summary>整场结束。</summary>
    GameEnded = 11,

    /// <summary>座位状态变化。</summary>
    SeatStateChanged = 12,
}
