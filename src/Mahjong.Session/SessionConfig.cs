namespace Mahjong.Session;

/// <summary>
/// 本地会话配置：真人座位、Bot 数、tick 率与两类超时。
/// 只承载配置值，不在本类型内校验；合法组合与数值范围由 <see cref="GameSession"/> 构造时统一校验。
/// </summary>
/// <param name="HumanSeat">真人座位 0–3；<c>null</c> 表示四座全是 Bot。</param>
/// <param name="BotCount">Bot 数；与真人座位组合必须满足 (null, 4) 或 (0–3, 3)。</param>
/// <param name="TickRate">每秒逻辑 tick 数，至少 1；Bot 延迟按它换算。</param>
/// <param name="TurnTimeoutTicks">出牌/自摸等待的回合超时 tick，至少 1。</param>
/// <param name="ResponseTimeoutTicks">弃牌响应窗口超时 tick，至少 1。</param>
public sealed record SessionConfig(
    byte? HumanSeat,
    byte BotCount,
    int TickRate = 10,
    int TurnTimeoutTicks = 600,
    int ResponseTimeoutTicks = 150);
