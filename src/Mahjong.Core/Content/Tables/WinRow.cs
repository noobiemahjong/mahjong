namespace Mahjong.Core.Content.Tables;

/// <summary>胡牌判定表的一行：某种牌型在 <paramref name="NeedJoker"/> 张鬼牌下的分解结果。</summary>
/// <param name="NeedJoker">该分解需要的鬼牌张数。</param>
/// <param name="HasPair">该分解是否已含将（对子）。</param>
/// <param name="HasWin"><see langword="true"/> 表示该分解在 <paramref name="NeedJoker"/> 张鬼牌下直接成胡。</param>
/// <param name="WinningRanks">
/// 未成胡时各 rank 的补张数，长度等于花色 rank 数（normal 9 / wind 4 / dragon 3），全 1 表示该花色任意一张可补；
/// <paramref name="HasWin"/> 为 <see langword="true"/> 时为空数组。
/// 该数组是 <see cref="TableCatalog"/> 缓存的实例，调用方不得修改（改动会污染后续查询）；需要可变副本时自行拷贝。
/// </param>
public readonly record struct WinRow(int NeedJoker, bool HasPair, bool HasWin, int[] WinningRanks);