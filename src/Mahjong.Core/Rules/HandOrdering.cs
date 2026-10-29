namespace Mahjong.Core.Rules;

/// <summary>
/// 手牌展示排序：只返回新序列，不修改输入 zone 或集合；显示序不参与规则与协议。
/// 排序稳定：相同展示键的牌保持输入顺序。
/// </summary>
public static class HandOrdering
{
    /// <summary>按牌种 <see cref="TileType.Id"/> 升序排列，不做鬼牌特殊处理。</summary>
    /// <param name="tiles">输入序列。</param>
    /// <returns>新序列；<paramref name="tiles"/> 中同牌种的牌保持输入顺序。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tiles"/> 为 null。</exception>
    public static IReadOnlyList<Tile> SortedForDisplay(IEnumerable<Tile> tiles) =>
        SortedForDisplay(tiles, new JokerRule(JokerMode.None, null));

    /// <summary>按牌种 <see cref="TileType.Id"/> 升序排列，<paramref name="joker"/> 指定的鬼牌排最后。</summary>
    /// <param name="tiles">输入序列。</param>
    /// <param name="joker">当前局的鬼牌规则；<see cref="JokerRule.Type"/> 非空时该牌种的牌一律排最后。</param>
    /// <returns>新序列；<paramref name="tiles"/> 中同展示键的牌保持输入顺序。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tiles"/> 为 null。</exception>
    public static IReadOnlyList<Tile> SortedForDisplay(IEnumerable<Tile> tiles, JokerRule joker)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        TileType? jokerType = joker.Type;
        Tile[] sorted = tiles.ToArray();
        for (int index = 1; index < sorted.Length; index++)
        {
            Tile current = sorted[index];
            int currentKey = DisplayKey(current, jokerType);
            int position = index - 1;
            while (position >= 0 && DisplayKey(sorted[position], jokerType) > currentKey)
            {
                sorted[position + 1] = sorted[position];
                position--;
            }

            sorted[position + 1] = current;
        }

        return Array.AsReadOnly(sorted);
    }

    private static int DisplayKey(Tile tile, TileType? jokerType) =>
        jokerType is not null && tile.Type.Id == jokerType.Value.Id ? int.MaxValue : tile.Type.Id;
}
