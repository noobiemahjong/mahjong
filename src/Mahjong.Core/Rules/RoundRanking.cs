namespace Mahjong.Core.Rules;

/// <summary>
/// 名次排序的单一实现：总分降序、同分座位号升序。纯函数，不读时钟、不持有状态。
/// </summary>
public static class RoundRanking
{
    /// <summary>名次：总分降序、同分座位号升序；返回长度 4 的只读座位列表。</summary>
    /// <param name="totalScores">4 个座位的总分，下标即座位号。</param>
    /// <returns>4 个座位，按名次从高到低排列；只读快照，调用方不能修改。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="totalScores"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="totalScores"/> 长度不是 4。</exception>
    public static IReadOnlyList<byte> Rank(IReadOnlyList<short> totalScores)
    {
        ArgumentNullException.ThrowIfNull(totalScores);
        if (totalScores.Count != GameState.PlayerCount)
        {
            throw new ArgumentException(
                $"Total scores must contain {GameState.PlayerCount} seats.",
                nameof(totalScores));
        }

        byte[] seats = new byte[GameState.PlayerCount];
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            seats[seat] = seat;
        }

        Array.Sort(seats, (left, right) =>
        {
            int byScore = totalScores[right].CompareTo(totalScores[left]);
            return byScore != 0 ? byScore : left.CompareTo(right);
        });

        return Array.AsReadOnly(seats);
    }
}
