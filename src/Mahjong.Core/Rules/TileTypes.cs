namespace Mahjong.Core.Rules;

/// <summary>花色。数值是内容与协议的一部分，已发布后不得改动。</summary>
public enum SuitType : byte
{
    /// <summary>万。</summary>
    Character = 1,

    /// <summary>筒。</summary>
    Dot = 2,

    /// <summary>条。</summary>
    Bamboo = 3,

    /// <summary>风（东/南/西/北）。</summary>
    Wind = 4,

    /// <summary>箭（中/发/白）。</summary>
    Dragon = 5,
}

/// <summary>
/// 逻辑牌种，<see cref="Id"/> 对应 content/tiles.json 中的 id（0–33）。
/// 相等性只比较 <see cref="Id"/>；同一 id 必为同一牌种，花色与 rank 不参与比较。
/// </summary>
public readonly struct TileType : IEquatable<TileType>
{
    /// <summary>构造牌种。</summary>
    /// <param name="id">牌种 id，取值 0–33，对应 content/tiles.json。</param>
    /// <param name="suit">花色。</param>
    /// <param name="rank">花色内的 rank，从 1 起。</param>
    public TileType(ushort id, SuitType suit, byte rank)
    {
        Id = id;
        Suit = suit;
        Rank = rank;
    }

    /// <summary>牌种 id，模型内唯一标识该牌种。</summary>
    public ushort Id { get; }

    /// <summary>花色。</summary>
    public SuitType Suit { get; }

    /// <summary>花色内的 rank，从 1 起。</summary>
    public byte Rank { get; }

    /// <summary>只比较 <see cref="Id"/>。</summary>
    /// <param name="other">要比较的牌种。</param>
    /// <returns>id 相同返回 true。</returns>
    public bool Equals(TileType other) => Id == other.Id;

    /// <summary>只比较 <see cref="Id"/>；<paramref name="obj"/> 不是 <see cref="TileType"/> 或为 null 时返回 false。</summary>
    /// <param name="obj">要比较的对象。</param>
    public override bool Equals(object? obj) => obj is TileType other && Equals(other);

    /// <summary>由 <see cref="Id"/> 计算，保证与 <see cref="Equals(TileType)"/> 一致。</summary>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>只比较 <see cref="Id"/>。</summary>
    public static bool operator ==(TileType left, TileType right) => left.Equals(right);

    /// <summary>只比较 <see cref="Id"/>。</summary>
    public static bool operator !=(TileType left, TileType right) => !left.Equals(right);
}

/// <summary>
/// 物理牌，<see cref="Id"/> 为 1–136（typeId*4+copy+1）；0 保留给未知。
/// 相等性只比较 <see cref="Id"/>；<see cref="Type"/> 不参与比较。
/// </summary>
public readonly struct Tile : IEquatable<Tile>
{
    /// <summary>未知牌的 id；模型内的牌不使用该值。</summary>
    public const ushort UnknownId = 0;

    /// <summary>构造物理牌。</summary>
    /// <param name="id">物理牌 id，1–136；0 仅表示未知。</param>
    /// <param name="type">该牌所属牌种。</param>
    public Tile(ushort id, TileType type)
    {
        Id = id;
        Type = type;
    }

    /// <summary>物理牌 id，1–136；同一 id 在全局至多出现一次。</summary>
    public ushort Id { get; }

    /// <summary>所属牌种。</summary>
    public TileType Type { get; }

    /// <summary>只比较 <see cref="Id"/>。</summary>
    /// <param name="other">要比较的牌。</param>
    /// <returns>id 相同返回 true。</returns>
    public bool Equals(Tile other) => Id == other.Id;

    /// <summary>只比较 <see cref="Id"/>；<paramref name="obj"/> 不是 <see cref="Tile"/> 或为 null 时返回 false。</summary>
    /// <param name="obj">要比较的对象。</param>
    public override bool Equals(object? obj) => obj is Tile other && Equals(other);

    /// <summary>由 <see cref="Id"/> 计算，保证与 <see cref="Equals(Tile)"/> 一致。</summary>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>只比较 <see cref="Id"/>。</summary>
    public static bool operator ==(Tile left, Tile right) => left.Equals(right);

    /// <summary>只比较 <see cref="Id"/>。</summary>
    public static bool operator !=(Tile left, Tile right) => !left.Equals(right);
}
