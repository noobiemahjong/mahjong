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

    /// <summary>
    /// 中马：马牌座位与赢家座位相同时的支付位。一位规则，赢家收的一笔与其余三家各付的一笔同置本位置；
    /// 未命中或无支付（<c>horseBonus</c> 为 0）时不置位。
    /// </summary>
    HorseWinner = 512,

    /// <summary>
    /// 抢杠：抢杠胡的赢家位。二位规则的正位（负位为 <see cref="RobbingTheKongPayForEveryone"/>），
    /// 赢家收 3×基分；七对抢杠时与 <see cref="SevenPairs"/> 并置，不置 <see cref="SelfDrawn"/> 与
    /// <see cref="NoJoker"/>（抢杠胡不算自摸）。金额为 0 时不置位。
    /// </summary>
    RobbingTheKong = 1024,

    /// <summary>
    /// 抢杠包三家：被抢杠者（payer）位，付 3×基分。与 <see cref="RobbingTheKong"/> 成对出现，
    /// 只置于 payer 笔，赢家笔不含本位置。金额为 0 时不置位。
    /// </summary>
    RobbingTheKongPayForEveryone = 2048,

    /// <summary>
    /// 杠上开花承包的 payer 位（放明杠者），付 3×杠分（杠分 = 基分 × 2）。
    /// 二位规则：本位置只置于 payer 笔；赢家笔为 <see cref="SelfDrawn"/> 与 <see cref="KongUpFlower"/>。
    /// 金额为 0 时不置位。
    /// </summary>
    KongUpFlowerPayForEveryone = 4096,

    /// <summary>
    /// 12 张落地承包：一位规则，赢家笔与供牌者（payer）笔同置本位置。
    /// 赢家笔另置 <see cref="SelfDrawn"/>（无鬼时再加 <see cref="NoJoker"/>），payer 仅本位置。金额为 0 时不置位。
    /// </summary>
    TwelveCardsConceded = 8192,
}
