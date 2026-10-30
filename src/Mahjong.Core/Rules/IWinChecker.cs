namespace Mahjong.Core.Rules;

/// <summary>
/// 胡牌判定接口：常规胡（4 面子 + 1 将）与七对两种口径。
/// 判定只依赖入参与注入的查表快照，不含随机、时钟或跨调用状态，同输入重复调用结果一致。
/// 鬼牌按 <see cref="TileType.Id"/> 识别并只作万能补位；无鬼牌时传 <c>default(TileType)</c>，不得传任意有效牌种来代替“无鬼”。
/// </summary>
public interface IWinChecker
{
    /// <summary>
    /// 判断是否为常规胡：手牌折算张数（<paramref name="hand"/> 张数 + 3 × <paramref name="melds"/> 组数）必须为 14，
    /// 分解为 4 组面子加恰好 1 个将；鬼牌只能作万能补位。不含七对（七对见 <see cref="IsSevenPairs"/>）。
    /// </summary>
    /// <param name="hand">手牌；只读，不会被修改。</param>
    /// <param name="melds">副露；只按每组 3 张折算张数，不解析组内牌面。</param>
    /// <param name="joker">
    /// 当前局的鬼牌牌种，调用方应传 <c>ContentCatalog.Joker.Type</c> 的真实牌种；
    /// 无鬼牌时传 <c>default(TileType)</c>：其 <see cref="TileType.Suit"/> 不在 1–5，牌种 id 0 的一万不会被当作鬼牌。
    /// </param>
    /// <returns>成胡返回 true；张数非法、同牌种超过 4 张或无法分解返回 false。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hand"/> 或 <paramref name="melds"/> 为 null。</exception>
    bool IsWin(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds, TileType joker);

    /// <summary>
    /// 返回向 <paramref name="hand"/> 加入一张后能构成常规胡的牌种，按 <see cref="TileType.Id"/> 升序排列。
    /// 与 <see cref="IsWin"/> 同口径（不含七对）：加入一张后折算张数必须为 14 才会命中。
    /// </summary>
    /// <param name="hand">手牌；只读，不会被修改。</param>
    /// <param name="melds">副露；只按每组 3 张折算张数，组内牌种只用于判断该牌种是否还有第 5 张。</param>
    /// <param name="joker">
    /// 当前局的鬼牌牌种，调用方应传 <c>ContentCatalog.Joker.Type</c> 的真实牌种；
    /// 无鬼牌时传 <c>default(TileType)</c>：其 <see cref="TileType.Suit"/> 不在 1–5，牌种 id 0 的一万不会被当作鬼牌。
    /// </param>
    /// <returns>
    /// 张数不是 13 或输入非法时返回共享只读空集合；张数合法但无命中牌种时返回空列表；
    /// 非空结果每次新建列表，元素按 <see cref="TileType.Id"/> 升序且不重复。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="hand"/> 或 <paramref name="melds"/> 为 null。</exception>
    IReadOnlyList<TileType> GetWinningTypes(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds, TileType joker);

    /// <summary>
    /// 判断是否为七对：<paramref name="hand"/> 恰好 14 张，剔除鬼牌后按牌种统计，奇数张数的牌种数不超过鬼牌数。
    /// 4 张同牌种按两对计；副露不参与七对判定。
    /// </summary>
    /// <param name="hand">手牌；只读，不会被修改。</param>
    /// <param name="joker">
    /// 当前局的鬼牌牌种，调用方应传 <c>ContentCatalog.Joker.Type</c> 的真实牌种；
    /// 无鬼牌时传 <c>default(TileType)</c>：其 <see cref="TileType.Suit"/> 不在 1–5，牌种 id 0 的一万不会被当作鬼牌。
    /// </param>
    /// <returns>成胡返回 true；张数不是 14、同牌种超过 4 张或无法配对返回 false。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hand"/> 为 null。</exception>
    bool IsSevenPairs(IReadOnlyList<Tile> hand, TileType joker);
}
