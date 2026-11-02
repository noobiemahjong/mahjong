namespace Mahjong.Core.Rules;

/// <summary>
/// 一局内已发生的番种与杠位聚合，也用于标记单次计分动作带有哪些番种。
/// 数值是位集（<see cref="System.FlagsAttribute"/>）：一个动作可同时置多个位；
/// 组合是否合法由 <see cref="ScoreManager"/> 按本局已发生状态校验（七对与杠上开花互斥、七对要求 0 副露、杠上开花要求有杠）。
/// 数值是回放与协议编码的一部分，已发布后不得改动。
/// </summary>
[Flags]
public enum ScoreActionType : ushort
{
    /// <summary>无番种：纯金额调整，不计入任何番种或杠位。</summary>
    None = 0,

    /// <summary>自摸；七对自摸与杠上开花自摸同样置位。</summary>
    SelfDrawn = 1,

    /// <summary>七对。</summary>
    SevenPairs = 2,

    /// <summary>暗杠。</summary>
    ConcealedKong = 4,

    /// <summary>明杠。</summary>
    ExposedKong = 8,

    /// <summary>补杠。</summary>
    KongSupplement = 16,

    /// <summary>杠上开花。</summary>
    KongUpFlower = 32,

    /// <summary>无鬼牌自摸。</summary>
    NoJoker = 64,

    /// <summary>
    /// 连庄奖励：庄家胡后按连庄次数收取的奖励。一位规则，庄家收的一笔与三家各付的一笔同置本位置，
    /// 金额为 0 时不置位。
    /// </summary>
    DealerStreak = 128,

    /// <summary>
    /// 跟庄：非庄胡时由庄家付给赢家的一笔。一位规则，赢家收的一笔与庄家付的一笔同置本位置，
    /// 金额为 0 时不置位。
    /// </summary>
    FollowingTheBanker = 256,
}
