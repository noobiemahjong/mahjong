namespace Mahjong.Core.Rules;

/// <summary>花色的静态属性，来自 <c>content/suits.json</c>。</summary>
/// <param name="Type">花色。</param>
/// <param name="RankCount">该花色的 rank 数量（万/筒/条 9、风 4、箭 3）。</param>
/// <param name="AmountOfEachRank">每个 rank 的牌张数（含鬼牌，v1 均为 4）。</param>
/// <param name="AllowedSequence">该花色能否组成顺子。</param>
public readonly record struct Suit(SuitType Type, int RankCount, int AmountOfEachRank, bool AllowedSequence);
