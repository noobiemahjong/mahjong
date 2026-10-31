using Mahjong.Core.Content;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;

namespace Mahjong.Session;

/// <summary>
/// 单机/本地对局的组装根：把内容、查表与随机种子装配成 <see cref="GameSession"/>。
/// 会话只接收鬼牌/判定器/随机源，查表加载与 WinChecker 构造留在这里；所有工厂都**不**调用 <see cref="GameSession.Start"/>。
/// </summary>
public static class LocalSessionFactory
{
    /// <summary>按显式配置创建会话；不启动。</summary>
    /// <param name="config">会话配置；合法性由 <see cref="GameSession"/> 构造校验。</param>
    /// <param name="content">已加载的内容目录；只取鬼牌规则。</param>
    /// <param name="tables">已加载的查表；用于构造 <see cref="WinChecker"/>。</param>
    /// <param name="seed">随机种子；同一 seed 与同一输入序列得到同一对局。</param>
    /// <returns>未启动的会话。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="config"/>、<paramref name="content"/> 或 <paramref name="tables"/> 为 null。</exception>
    public static GameSession Create(SessionConfig config, ContentCatalog content, TableCatalog tables, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(tables);

        return new GameSession(config, content, new WinChecker(tables), new Pcg32(seed));
    }

    /// <summary>创建单人对局：0 号座位真人、1–3 号 Bot；不启动。</summary>
    /// <param name="content">已加载的内容目录。</param>
    /// <param name="tables">已加载的查表。</param>
    /// <param name="seed">随机种子。</param>
    /// <param name="tickRate">每秒逻辑 tick 数，至少 1。</param>
    /// <returns>未启动的会话。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> 或 <paramref name="tables"/> 为 null。</exception>
    public static GameSession CreateSinglePlayer(ContentCatalog content, TableCatalog tables, ulong seed, int tickRate = 10) =>
        Create(new SessionConfig(0, 3, tickRate), content, tables, seed);

    /// <summary>创建四 Bot 对局：无真人座位；不启动。</summary>
    /// <param name="content">已加载的内容目录。</param>
    /// <param name="tables">已加载的查表。</param>
    /// <param name="seed">随机种子。</param>
    /// <param name="tickRate">每秒逻辑 tick 数，至少 1。</param>
    /// <returns>未启动的会话。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> 或 <paramref name="tables"/> 为 null。</exception>
    public static GameSession CreateFourBots(ContentCatalog content, TableCatalog tables, ulong seed, int tickRate = 10) =>
        Create(new SessionConfig(null, 4, tickRate), content, tables, seed);
}
