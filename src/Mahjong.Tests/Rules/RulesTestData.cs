using System.Text;
using Mahjong.Core.Rules;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 规则模型测试的共享数据构造：按 content/tiles.json 的固定公式独立产生牌型与物理牌
/// （typeId 0–33；物理 id = typeId*4 + copy + 1，copy 0–3），不读 content/ 目录。
/// </summary>
public static class RulesTestData
{
    /// <summary>牌型总数。</summary>
    public const int TileTypeCount = 34;

    /// <summary>物理牌总数（牌型数 × 每种 4 张）。</summary>
    public const int PhysicalTileCount = 136;

    /// <summary>按固定顺序构造牌型：Character/Dot/Bamboo 各 rank 1–9，Wind 1–4，Dragon 1–3。</summary>
    public static TileType TypeOf(int typeId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(typeId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(typeId, TileTypeCount);
        return typeId switch
        {
            <= 8 => new TileType((ushort)typeId, SuitType.Character, (byte)(typeId + 1)),
            <= 17 => new TileType((ushort)typeId, SuitType.Dot, (byte)(typeId - 8)),
            <= 26 => new TileType((ushort)typeId, SuitType.Bamboo, (byte)(typeId - 17)),
            <= 30 => new TileType((ushort)typeId, SuitType.Wind, (byte)(typeId - 26)),
            _ => new TileType((ushort)typeId, SuitType.Dragon, (byte)(typeId - 30)),
        };
    }

    /// <summary>物理牌 id：typeId*4 + copy + 1；copy 取 0–3。</summary>
    public static ushort PhysicalId(int typeId, int copy)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(copy);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(copy, 4);
        return (ushort)((typeId * 4) + copy + 1);
    }

    /// <summary>按牌型与 copy 构造物理牌。</summary>
    public static Tile Physical(int typeId, int copy) => new(PhysicalId(typeId, copy), TypeOf(typeId));

    /// <summary>按物理 id 构造牌；入参须在 1–136。</summary>
    public static Tile FromId(int physicalId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(physicalId, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(physicalId, PhysicalTileCount);
        return new Tile((ushort)physicalId, TypeOf((physicalId - 1) / 4));
    }

    /// <summary>某牌型的 4 张物理牌。</summary>
    public static Tile[] Copies(int typeId) =>
        [Physical(typeId, 0), Physical(typeId, 1), Physical(typeId, 2), Physical(typeId, 3)];

    /// <summary>取牌的物理 id 序列。</summary>
    public static ushort[] Ids(IEnumerable<Tile> tiles) => tiles.Select(tile => tile.Id).ToArray();

    /// <summary>把牌追加到玩家手牌尾部。</summary>
    public static void Fill(PlayerState player, params Tile[] tiles)
    {
        foreach (Tile tile in tiles)
        {
            player.Hand.Append(tile);
        }
    }

    /// <summary>把牌追加到玩家弃牌尾部。</summary>
    public static void Discard(PlayerState player, params Tile[] tiles)
    {
        foreach (Tile tile in tiles)
        {
            player.Discards.Append(tile);
        }
    }

    /// <summary>
    /// 全部区域的完整快照：牌墙、四家手牌/弃牌/副露（种类、明暗、目标座位、牌 id 序列）。
    /// 用于断言同 seed 全槽位一致与失败调用不改状态。
    /// </summary>
    public static string Snapshot(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var builder = new StringBuilder();
        builder.Append("wall=").Append(JoinZone(state.Wall));
        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            PlayerState player = state.Players[seat];
            builder.Append(";seat").Append(seat).Append('=').Append(player.IsBanker ? 'B' : '-')
                .Append(',');
            builder.Append(";hand").Append(seat).Append('=').Append(JoinZone(player.Hand));
            builder.Append(";discards").Append(seat).Append('=').Append(JoinZone(player.Discards));
            builder.Append(";melds").Append(seat).Append('=');
            for (int index = 0; index < player.Melds.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append('|');
                }

                Meld meld = player.Melds[index];
                builder.Append((byte)meld.Kind).Append('/').Append(meld.Opened ? '1' : '0')
                    .Append('/').Append(meld.TargetSeat).Append('/')
                    .Append(string.Join(",", Ids(meld.Tiles)));
            }
        }

        return builder.ToString();
    }

    /// <summary>该局面上所有区域的物理牌总数（牌墙 + 手牌 + 弃牌 + 副露）。</summary>
    public static int TotalTileCount(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int total = state.Wall.Count;
        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            PlayerState player = state.Players[seat];
            total += player.Hand.Count + player.Discards.Count;
            foreach (Meld meld in player.Melds)
            {
                total += meld.Tiles.Count;
            }
        }

        return total;
    }

    private static string JoinZone(TileZone zone) => string.Join(",", Ids(zone.Items));
}
