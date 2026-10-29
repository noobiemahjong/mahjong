namespace Mahjong.Core.Content.Tables;

/// <summary>牌效（Bot）表的一行：某种牌型的一种分解及其胡牌概率。</summary>
/// <param name="HasPair">该分解是否含将（对子）。</param>
/// <param name="Probability">该分解的胡牌概率，取自旧表数据。</param>
public readonly record struct AiRow(bool HasPair, double Probability);
