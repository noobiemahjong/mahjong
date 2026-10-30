namespace Mahjong.Core.Rules;

/// <summary>轮次驱动命令的种类。数值是脚本与回放编码的一部分，已发布后不得改动。</summary>
public enum RoundCommandKind : byte
{
    /// <summary>出牌：<see cref="RoundCommand.Slot"/> 是手牌槽位。</summary>
    Discard = 1,

    /// <summary>自摸胡：结算本局并结束本局。</summary>
    Win = 2,
}

/// <summary>
/// 轮次驱动的单条脚本命令：哪一席做什么。只承载数据，不做合法性校验；
/// 座位、槽位与动作合法性由 <see cref="RoundController.RunRound"/> 按当前局面逐条校验，
/// 非法命令抛异常且不应用该步。
/// </summary>
/// <param name="Seat">执行命令的座位 0–3。</param>
/// <param name="Kind">命令种类。</param>
/// <param name="Slot">出牌时的手牌槽位；<see cref="RoundCommandKind.Win"/> 不使用该字段。</param>
public readonly record struct RoundCommand(byte Seat, RoundCommandKind Kind, int Slot = 0)
{
    /// <summary>构造出牌命令。</summary>
    /// <param name="seat">出牌座位 0–3。</param>
    /// <param name="slot">手牌槽位。</param>
    /// <returns>出牌命令。</returns>
    public static RoundCommand Discard(byte seat, int slot) => new(seat, RoundCommandKind.Discard, slot);

    /// <summary>构造自摸胡命令。</summary>
    /// <param name="seat">赢家座位 0–3。</param>
    /// <returns>自摸胡命令。</returns>
    public static RoundCommand Win(byte seat) => new(seat, RoundCommandKind.Win);
}
