namespace Mahjong.Core.Rules;

/// <summary>花色。数值是内容与协议的一部分，已发布后不得改动。</summary>
public enum SuitType : byte
{
    Character = 1,
    Dot = 2,
    Bamboo = 3,
    Wind = 4,
    Dragon = 5,
}

/// <summary>逻辑牌种，<paramref name="Id"/> 对应 content/tiles.json 中的 id（0–33）。</summary>
public readonly record struct TileType(ushort Id, SuitType Suit, byte Rank);

/// <summary>物理牌，<paramref name="Id"/> 为 1–136（typeId*4+copy+1）；0 保留给未知。</summary>
public readonly record struct Tile(ushort Id, TileType Type)
{
    public const ushort UnknownId = 0;
}
