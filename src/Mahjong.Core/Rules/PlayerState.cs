namespace Mahjong.Core.Rules;

/// <summary>
/// 单个玩家的模型状态：手牌、副露、弃牌、分数与庄家标记。
/// 不提供手牌排序 API；展示排序由 <see cref="HandOrdering"/> 在展示层调用。
/// </summary>
public sealed class PlayerState
{
    /// <summary>创建玩家状态：空手牌、空副露、空弃牌，分数 0，非庄家。</summary>
    /// <param name="seat">座位 0–3。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seat"/> 不在 0–3。</exception>
    public PlayerState(byte seat)
    {
        if (seat >= GameState.PlayerCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seat),
                seat,
                $"Seat must be within 0-{GameState.PlayerCount - 1}.");
        }

        Seat = seat;
        Hand = new TileZone(new ZoneId(seat, ZoneKind.Hand), 14);
        Discards = new TileZone(new ZoneId(seat, ZoneKind.Discard), 24);
        Melds = [];
    }

    /// <summary>座位 0–3，构造后不变。</summary>
    public byte Seat { get; }

    /// <summary>手牌区域；其槽位是手牌域下标。</summary>
    public TileZone Hand { get; }

    /// <summary>副露列表，按声明顺序追加；无排序语义。</summary>
    public List<Meld> Melds { get; }

    /// <summary>弃牌区域；其槽位是弃牌域下标，与手牌槽位不是同一个域。</summary>
    public TileZone Discards { get; }

    /// <summary>分数，可为负。</summary>
    public short Score { get; set; }

    /// <summary>本局是否庄家。</summary>
    public bool IsBanker { get; set; }
}
