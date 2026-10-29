using System.Buffers.Binary;
using System.Security.Cryptography;
using Mahjong.Core.Rules;

namespace Mahjong.Core.Flow;

/// <summary>
/// 权威局面的状态哈希：把模型字段按固定字节布局编码后取 SHA-256，返回 64 字符小写 hex。
/// 只编码模型字段，不含时间、会话、日志、内容清单与版本；布局口径见
/// <c>.agents/notes/implemented/architecture/2026-10-29-state-hash-byte-layout.md</c>。不读时钟、无静态状态。
/// </summary>
public static class StateHash
{
    /// <summary>字节布局版本，写在流首字节。</summary>
    private const byte LayoutVersion = 1;

    /// <summary>计算局面哈希。</summary>
    /// <param name="state">要哈希的权威局面；只读不改。</param>
    /// <returns>SHA-256 的 64 字符小写十六进制串，无分隔符。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> 为 null。</exception>
    public static string Compute(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        byte[] buffer = new byte[Measure(state)];
        Span<byte> span = buffer;
        int offset = 0;

        WriteU8(span, ref offset, LayoutVersion);
        WriteTileZone(span, ref offset, state.Wall);
        WriteTileZone(span, ref offset, state.WallTail);

        PlayerState[] players = state.Players;
        WriteU8(span, ref offset, (byte)players.Length);
        for (int seat = 0; seat < players.Length; seat++)
        {
            PlayerState player = players[seat];
            WriteU8(span, ref offset, player.Seat);
            WriteU8(span, ref offset, player.IsBanker ? (byte)1 : (byte)0);
            WriteI32(span, ref offset, player.Score);

            WriteTileZone(span, ref offset, player.Hand);

            WriteU8(span, ref offset, (byte)player.Melds.Count);
            for (int meld = 0; meld < player.Melds.Count; meld++)
            {
                Meld current = player.Melds[meld];
                WriteU8(span, ref offset, (byte)current.Kind);
                WriteU8(span, ref offset, current.Opened ? (byte)1 : (byte)0);
                WriteU8(span, ref offset, current.TargetSeat);
                WriteU8(span, ref offset, (byte)current.Tiles.Count);
                for (int tile = 0; tile < current.Tiles.Count; tile++)
                {
                    WriteU16(span, ref offset, current.Tiles[tile].Id);
                }
            }

            WriteTileZone(span, ref offset, player.Discards);
        }

        WriteU8(span, ref offset, state.CurrentSeat);
        WriteU16(span, ref offset, state.RoundNo);

        return Convert.ToHexString(SHA256.HashData(buffer)).ToLowerInvariant();
    }

    /// <summary>字节流长度：先量再写，避免扩容与每 tick 的大对象分配。</summary>
    private static int Measure(GameState state)
    {
        int size = 1 + 2 + (2 * state.Wall.Count) + 2 + (2 * state.WallTail.Count) + 1 + 1 + 2;

        PlayerState[] players = state.Players;
        for (int seat = 0; seat < players.Length; seat++)
        {
            PlayerState player = players[seat];
            size += 1 + 1 + 4;
            size += 2 + (2 * player.Hand.Count);
            size += 1;
            for (int meld = 0; meld < player.Melds.Count; meld++)
            {
                size += 4 + (2 * player.Melds[meld].Tiles.Count);
            }

            size += 2 + (2 * player.Discards.Count);
        }

        return size;
    }

    private static void WriteTileZone(Span<byte> buffer, ref int offset, TileZone zone)
    {
        WriteU16(buffer, ref offset, zone.Count);
        IReadOnlyList<Tile> items = zone.Items;
        for (int index = 0; index < items.Count; index++)
        {
            WriteU16(buffer, ref offset, items[index].Id);
        }
    }

    private static void WriteU8(Span<byte> buffer, ref int offset, byte value)
    {
        buffer[offset] = value;
        offset++;
    }

    private static void WriteU16(Span<byte> buffer, ref int offset, int value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], (ushort)value);
        offset += 2;
    }

    private static void WriteI32(Span<byte> buffer, ref int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer[offset..], value);
        offset += 4;
    }
}
