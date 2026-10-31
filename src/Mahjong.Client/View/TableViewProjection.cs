using System.Text;
using Mahjong.Core.Content;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;

namespace Mahjong.Client.View;

/// <summary>
/// 从权威 <see cref="GameSession"/> 投影 <see cref="TableViewState"/>：一次读取 State 与开放请求，
/// 不缓存局面、不从事件重放累计。手牌展示序只走 <see cref="HandOrdering"/>，槽位回填原 Hand 下标。
/// </summary>
internal static class TableViewProjection
{
    /// <summary>固定顺序的动作列表；按钮启用与视图输出都按它排列。</summary>
    private const int ActionCount = 5;

    /// <summary>按相对座位取显示名：1 下家、2 对家、3 上家。</summary>
    public static string RelativeName(int relativeSeat) => relativeSeat switch
    {
        0 => "YOU",
        1 => "NEXT",
        2 => "ACROSS",
        3 => "PREV",
        _ => "SEAT",
    };

    /// <summary>请求位集是否允许该动词；命令构造与按钮启用共用。</summary>
    public static bool IsAllowed(AllowedVerbs allowed, CommandVerb verb) => verb switch
    {
        CommandVerb.Discard => (allowed & AllowedVerbs.Discard) != 0,
        CommandVerb.Pong => (allowed & AllowedVerbs.Pong) != 0,
        CommandVerb.Kong => (allowed & AllowedVerbs.Kong) != 0,
        CommandVerb.Win => (allowed & AllowedVerbs.Win) != 0,
        CommandVerb.Pass => (allowed & AllowedVerbs.Pass) != 0,
        _ => false,
    };

    /// <summary>牌种到 ASCII 标签：万 1M-9M、筒 1P-9P、条 1S-9S、风 WE/WS/WW/WN、箭 DR/DG/DW。</summary>
    public static string Label(TileType type) => type.Suit switch
    {
        // SuitType.Character/Dot/Bamboo 依次是万/筒/条（见 TileTypes.cs）。
        SuitType.Character => $"{type.Rank}M",
        SuitType.Dot => $"{type.Rank}P",
        SuitType.Bamboo => $"{type.Rank}S",
        SuitType.Wind => type.Rank switch { 1 => "WE", 2 => "WS", 3 => "WW", _ => "WN" },
        SuitType.Dragon => type.Rank switch { 1 => "DR", 2 => "DG", _ => "DW" },
        _ => "??",
    };

    /// <summary>副露种类缩写，用于副露展示文本与状态行。</summary>
    public static string MeldAbbreviation(MeldKind kind) => kind switch
    {
        MeldKind.Pong => "PONG",
        MeldKind.KongExposed => "KONG",
        MeldKind.KongConcealed => "CONCEALED_KONG",
        MeldKind.KongSupplement => "ADDED_KONG",
        _ => "MELD",
    };

    /// <summary>按牌种 id 取标签；用于事件里的物理牌 id（未知 id 返回 ??）。</summary>
    public static string LabelOfCardId(ContentCatalog content, ushort cardId)
    {
        if (cardId == 0)
        {
            return "??";
        }

        // 物理 id 1-136 由 typeId*4+copy+1 生成，整除 4 反推牌种 id。
        return Label(content.GetTileType((ushort)((cardId - 1) / 4)));
    }

    /// <summary>
    /// 把请求的 Allowed 位集按固定顺序映射成动作列表。
    /// 响应窗口的 Kong 位比候选实际合法性宽（Core 对全体候选恒置 Kong），调用方需用校验器收窄。
    /// </summary>
    public static IReadOnlyList<CommandVerb> MapAllowedActions(AllowedVerbs allowed)
    {
        var verbs = new List<CommandVerb>(ActionCount);
        for (int index = 0; index < ActionCount; index++)
        {
            CommandVerb verb = VerbAt(index);
            if (IsAllowed(allowed, verb))
            {
                verbs.Add(verb);
            }
        }

        return verbs;
    }

    /// <summary>
    /// 投影一帧：四家固定 4 项按下标=相对座位；自己手牌展示序经 HandOrdering，
    /// 他人手牌只有 HandCount；按钮启用由调用方从开放请求 Allowed 映射后传入。
    /// </summary>
    public static TableViewState Project(
        GameSession session,
        ContentCatalog content,
        byte viewerSeat,
        ushort? selectedTileId,
        string statusLine,
        IReadOnlyList<CommandVerb> enabledActions)
    {
        GameState state = session.State;
        var players = new PlayerView[GameState.PlayerCount];
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            PlayerState player = state.Players[seat];
            int relativeSeat = (seat - viewerSeat + GameState.PlayerCount) % GameState.PlayerCount;
            players[relativeSeat] = new PlayerView(
                player.Seat,
                (byte)relativeSeat,
                RelativeName(relativeSeat),
                player.Score,
                player.IsBanker,
                state.CurrentSeat == player.Seat,
                player.Hand.Count,
                ProjectMelds(player, content),
                ProjectDiscards(player, content));
        }

        return new TableViewState(
            state.RoundNo,
            viewerSeat,
            (ushort)state.Wall.Count,
            players,
            ProjectHand(state.Players[viewerSeat], content),
            selectedTileId,
            enabledActions,
            statusLine);
    }

    private static IReadOnlyList<HandTileView> ProjectHand(PlayerState player, ContentCatalog content)
    {
        IReadOnlyList<Tile> sorted = HandOrdering.SortedForDisplay(player.Hand.Items, content.Joker);
        if (sorted.Count == 0)
        {
            return Array.Empty<HandTileView>();
        }

        var hand = new HandTileView[sorted.Count];
        for (int index = 0; index < sorted.Count; index++)
        {
            Tile tile = sorted[index];
            // 展示序下标不是槽位：槽位回填该牌在 PlayerState.Hand 里的真实下标。
            hand[index] = new HandTileView(tile.Id, Label(tile.Type), IsJoker(tile, content), player.Hand.IndexOf(tile));
        }

        return hand;
    }

    private static IReadOnlyList<TileView> ProjectMelds(PlayerState player, ContentCatalog content)
    {
        if (player.Melds.Count == 0)
        {
            return Array.Empty<TileView>();
        }

        // 一项代表一个副露：Label 是「种类缩写 + 每张牌标签」的完整展示文本（契约 §3.3）。
        var melds = new TileView[player.Melds.Count];
        for (int index = 0; index < player.Melds.Count; index++)
        {
            Meld meld = player.Melds[index];
            var text = new StringBuilder(MeldAbbreviation(meld.Kind));
            for (int tileIndex = 0; tileIndex < meld.Tiles.Count; tileIndex++)
            {
                text.Append(' ').Append(Label(meld.Tiles[tileIndex].Type));
            }

            melds[index] = new TileView(meld.Tiles[0].Id, text.ToString(), false);
        }

        return melds;
    }

    private static IReadOnlyList<TileView> ProjectDiscards(PlayerState player, ContentCatalog content)
    {
        if (player.Discards.Count == 0)
        {
            return Array.Empty<TileView>();
        }

        var discards = new TileView[player.Discards.Count];
        for (int index = 0; index < player.Discards.Count; index++)
        {
            Tile tile = player.Discards.Items[index];
            discards[index] = new TileView(tile.Id, Label(tile.Type), IsJoker(tile, content));
        }

        return discards;
    }

    private static CommandVerb VerbAt(int index) => index switch
    {
        0 => CommandVerb.Discard,
        1 => CommandVerb.Pong,
        2 => CommandVerb.Kong,
        3 => CommandVerb.Win,
        _ => CommandVerb.Pass,
    };

    private static bool IsJoker(Tile tile, ContentCatalog content) =>
        content.Joker.Type is { } jokerType && tile.Type.Id == jokerType.Id;
}
