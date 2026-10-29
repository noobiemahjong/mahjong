namespace Mahjong.Core.Content.Tables;

/// <summary>
/// 查表 key 编码：按 rank 顺序把每张数量按十进制拼接（<c>cur = cur * 10 + count</c>），
/// 前导 0 因此被省略（<c>[0,0,1,2,1,0,2,2,0]</c> → <c>1210220</c>）。
/// </summary>
public static class TableKey
{
    /// <summary>normal(9) / wind(4) / dragon(3) 的 rank 数量都被编码成合法 key。</summary>
    /// <param name="rankCounts">每个 rank 的张数，长度必须是 9、4 或 3，每项 0–4。</param>
    /// <exception cref="ArgumentException">长度不是 9/4/3，或某项超过 4（单 rank 只有 4 张）。</exception>
    public static ulong Encode(ReadOnlySpan<byte> rankCounts)
    {
        if (rankCounts.Length is not (3 or 4 or 9))
        {
            throw new ArgumentException(
                $"rank count length must be 3 (dragon), 4 (wind) or 9 (normal), but was {rankCounts.Length}.",
                nameof(rankCounts));
        }

        ulong key = 0;
        for (int index = 0; index < rankCounts.Length; index++)
        {
            byte count = rankCounts[index];
            if (count > 4)
            {
                throw new ArgumentException(
                    $"rank count at index {index} must be 0..4 (a rank has 4 copies), but was {count}.",
                    nameof(rankCounts));
            }

            key = (key * 10) + count;
        }

        return key;
    }
}
