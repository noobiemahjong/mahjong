using System.Security.Cryptography;
using Mahjong.Core.Rules;

namespace Mahjong.Tests.Flow.Support;

/// <summary>
/// StateHash 的独立参照实现：严格按 T-5 规格 §3 的字节布局逐字段手工拼字节，
/// u16/i32 小端按位移逐字节写出，不使用生产代码的写字节逻辑，也不引用
/// <c>Mahjong.Core.Flow.StateHash</c>，因此可作为金标的独立第二实现。
/// </summary>
internal static class StateHashOracle
{
    /// <summary>规格 §3 的字节布局版本号。</summary>
    public const byte LayoutVersion = 1;

    /// <summary>玩家数固定为 4（u8）。</summary>
    private const byte ExpectedPlayerCount = 4;

    /// <summary>按规格 §3 手工构造期望字节序列。</summary>
    /// <param name="state">要编码的局面。</param>
    /// <returns>规格 §3 的字节流。</returns>
    public static byte[] BuildExpectedBytes(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var bytes = new List<byte>(256);

        bytes.Add(LayoutVersion);

        AddCountedIds(bytes, state.Wall.Items);
        AddCountedIds(bytes, state.WallTail.Items);

        bytes.Add(ExpectedPlayerCount);
        for (int seat = 0; seat < ExpectedPlayerCount; seat++)
        {
            PlayerState player = state.Players[seat];
            bytes.Add(player.Seat);
            bytes.Add(player.IsBanker ? (byte)1 : (byte)0);
            AddInt32(bytes, player.Score);

            AddCountedIds(bytes, player.Hand.Items);

            if (player.Melds.Count > byte.MaxValue)
            {
                throw new InvalidOperationException("Meld count exceeds the u8 length prefix.");
            }

            bytes.Add((byte)player.Melds.Count);
            foreach (Meld meld in player.Melds)
            {
                bytes.Add((byte)meld.Kind);
                bytes.Add(meld.Opened ? (byte)1 : (byte)0);
                bytes.Add(meld.TargetSeat);
                if (meld.Tiles.Count > byte.MaxValue)
                {
                    throw new InvalidOperationException("Meld tile count exceeds the u8 length prefix.");
                }

                bytes.Add((byte)meld.Tiles.Count);
                foreach (Tile tile in meld.Tiles)
                {
                    AddUInt16(bytes, tile.Id);
                }
            }

            AddCountedIds(bytes, player.Discards.Items);
        }

        bytes.Add(state.CurrentSeat);
        AddUInt16(bytes, state.RoundNo);
        return bytes.ToArray();
    }

    /// <summary>按规格 §3 计算期望的小写十六进制 SHA-256。</summary>
    /// <param name="state">要编码的局面。</param>
    /// <returns>64 字符小写 hex。</returns>
    public static string ComputeExpectedHex(GameState state) =>
        Convert.ToHexString(SHA256.HashData(BuildExpectedBytes(state))).ToLowerInvariant();

    /// <summary>u16 长度前缀 + 按槽位顺序的 tileId 序列。</summary>
    private static void AddCountedIds(List<byte> bytes, IReadOnlyList<Tile> tiles)
    {
        if (tiles.Count > ushort.MaxValue)
        {
            throw new InvalidOperationException("Tile count exceeds the u16 length prefix.");
        }

        AddUInt16(bytes, (ushort)tiles.Count);
        foreach (Tile tile in tiles)
        {
            AddUInt16(bytes, tile.Id);
        }
    }

    /// <summary>u16 小端逐字节写出（不调用 BinaryPrimitives/BitConverter）。</summary>
    private static void AddUInt16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)(value & 0xFF));
        bytes.Add((byte)((value >> 8) & 0xFF));
    }

    /// <summary>i32 小端补码逐字节写出（score 由 short 提升为 int）。</summary>
    private static void AddInt32(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value & 0xFF));
        bytes.Add((byte)((value >> 8) & 0xFF));
        bytes.Add((byte)((value >> 16) & 0xFF));
        bytes.Add((byte)((value >> 24) & 0xFF));
    }
}
