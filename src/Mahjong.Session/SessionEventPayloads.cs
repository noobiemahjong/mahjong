using Mahjong.Core.Flow;
using Mahjong.Core.Rules;

namespace Mahjong.Session;

/// <summary>一局开始的 payload：局号、庄家座位、发牌后牌墙张数与本局连庄次数。</summary>
/// <param name="RoundNo">局号 1–8。</param>
/// <param name="BankerSeat">本局庄家座位 0–3。</param>
/// <param name="WallCount">发牌后牌墙张数。</param>
/// <param name="DealerStreak">本局连庄次数：第 1 局恒 1，庄家胡递增、非庄胡归 1、流局不变。</param>
public readonly record struct RoundStartedPayload(byte RoundNo, byte BankerSeat, ushort WallCount, byte DealerStreak);

/// <summary>回合开始的 payload：座位与回合截止 tick。</summary>
/// <param name="Seat">回合座位 0–3。</param>
/// <param name="DeadlineTick">本回合出牌请求的截止 tick。</param>
public readonly record struct TurnStartedPayload(byte Seat, long DeadlineTick);

/// <summary>
/// 发牌的 payload。可见性：viewer 为该座位或 255 时给出牌 id，其他座位只给数量。
/// </summary>
/// <param name="Seat">座位 0–3。</param>
/// <param name="Count">该座位起手牌张数；不可见时仍为真实张数。</param>
/// <param name="TileIds">物理牌 id；不可见时为空数组。</param>
public readonly record struct DealPayload(byte Seat, byte Count, ushort[] TileIds);

/// <summary>
/// 手牌全量同步的 payload。可见性：viewer 为该座位或 255 时给出牌 id，其他座位只给数量。
/// </summary>
/// <param name="Seat">座位 0–3。</param>
/// <param name="Count">手牌张数；不可见时仍为真实张数。</param>
/// <param name="TileIds">物理牌 id；不可见时为空数组。</param>
public readonly record struct HandSyncPayload(byte Seat, byte Count, ushort[] TileIds);

/// <summary>
/// 摸牌的 payload。可见性：viewer 为该座位或 255 时给出真实牌 id，其他座位为 -1。
/// </summary>
/// <param name="Seat">座位 0–3。</param>
/// <param name="TileId">物理牌 id；不可见时为 -1。</param>
/// <param name="WallCount">摸牌后牌墙张数。</param>
public readonly record struct DrewPayload(byte Seat, int TileId, ushort WallCount);

/// <summary>出牌的 payload：座位、弃牌区槽位、物理牌 id。</summary>
/// <param name="Seat">出牌座位 0–3。</param>
/// <param name="Slot">出牌在弃牌区的槽位。</param>
/// <param name="TileId">物理牌 id。</param>
public readonly record struct DiscardedPayload(byte Seat, ushort Slot, ushort TileId);

/// <summary>副露的 payload：座位、种类、牌 id 序列与被取牌座位。</summary>
/// <param name="Seat">副露座位 0–3。</param>
/// <param name="Kind">副露种类。</param>
/// <param name="TileIds">组成副露的物理牌 id，按副露内顺序。</param>
/// <param name="TargetSeat">被取弃牌的座位 0–3；暗杠/补杠为 255。</param>
public readonly record struct MeldPayload(byte Seat, MeldKind Kind, ushort[] TileIds, byte TargetSeat);

/// <summary>请求打开的 payload：请求 id、座位、种类、允许动词位集与截止 tick。</summary>
/// <param name="RequestId">请求 id，由会话从 1 单调分配。</param>
/// <param name="Seat">被征询的座位 0–3。</param>
/// <param name="Kind">请求种类。</param>
/// <param name="Allowed">允许应答的动词位集。</param>
/// <param name="DeadlineTick">截止 tick。</param>
public readonly record struct RequestOpenedPayload(long RequestId, byte Seat, RequestKind Kind, AllowedVerbs Allowed, long DeadlineTick);

/// <summary>请求应答的命令快照：动词、槽位与物理牌 id。</summary>
/// <param name="Verb">应答动词。</param>
/// <param name="Slot">命令槽位；语义由动词决定。</param>
/// <param name="CardId">命令物理牌 id；无牌的命令为 0。</param>
public readonly record struct RequestAnswer(CommandVerb Verb, ushort Slot, ushort CardId);

/// <summary>
/// 请求结算的 payload。<see cref="Answer"/> 仅在 <see cref="Outcome"/> 为 Answered 时非 null，
/// 且是该次实际应答命令（显式“过”也会带 Pass 命令）。
/// </summary>
/// <param name="RequestId">被结算的请求 id。</param>
/// <param name="Outcome">结算种类。</param>
/// <param name="Answer">Answered 时的实际应答命令；Cancelled/TimedOut 为 null。</param>
public readonly record struct RequestClosedPayload(long RequestId, RequestOutcomeKind Outcome, RequestAnswer? Answer);

/// <summary>一局结束的 payload：赢家、四座本局净变化与计分动作聚合。</summary>
/// <param name="WinnerSeat">赢家座位 0–3；-1 表示流局。</param>
/// <param name="ScoreDelta">4 个座位的本局净变化，下标即座位号。</param>
/// <param name="ActionType">本局计分动作聚合；流局为 None。</param>
public readonly record struct RoundEndedPayload(sbyte WinnerSeat, int[] ScoreDelta, ScoreActionType ActionType);

/// <summary>整场结束的 payload：四座总分、名次与回放 id。</summary>
/// <param name="TotalScores">4 个座位的累计总分，下标即座位号。</param>
/// <param name="Ranking">长度 4 的座位名次，下标为名次、值为座位号。</param>
/// <param name="ReplayId">回放 id；M0.2 为空字符串。</param>
public readonly record struct GameEndedPayload(int[] TotalScores, byte[] Ranking, string ReplayId);

/// <summary>座位状态变化的 payload：座位与状态编码。</summary>
/// <param name="Seat">座位 0–3。</param>
/// <param name="State">状态编码：0 Online、1 Grace、2 Bot、3 Offline。</param>
public readonly record struct SeatStateChangedPayload(byte Seat, byte State);
