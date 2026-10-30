using System.Collections.ObjectModel;

namespace Mahjong.Core.Rules;

/// <summary>区域种类。数值是协议与回放编码的一部分，已发布后不得改动。</summary>
public enum ZoneKind : byte
{
    /// <summary>牌墙：正常摸牌从头部取，杠补牌从末尾取。</summary>
    Wall = 0,

    /// <summary>手牌。</summary>
    Hand = 1,

    /// <summary>副露。</summary>
    Meld = 2,

    /// <summary>弃牌区。</summary>
    Discard = 3,

    /// <summary>规则性揭示的牌。</summary>
    Revealed = 4,
}

/// <summary>区域标识：座位 + 区域种类；<see cref="PublicSeat"/> 表示不属于任何座位的公共区。</summary>
/// <param name="Seat">座位 0–3；公共区为 <see cref="PublicSeat"/>。</param>
/// <param name="Kind">区域种类。</param>
public readonly record struct ZoneId(byte Seat, ZoneKind Kind)
{
    /// <summary>公共区座位值（牌墙不属于任何座位）。</summary>
    public const byte PublicSeat = 255;
}

/// <summary>
/// 牌的有序容器：只允许尾部追加，移除按槽位进行；槽位顺序即流程顺序。
/// 不提供插入与排序 API（展示排序由 ViewModel 负责）。实例非线程安全。
/// </summary>
public sealed class TileZone
{
    private readonly List<Tile> _items;
    private readonly HashSet<Tile> _ids;
    private readonly ReadOnlyCollection<Tile> _itemsView;

    /// <summary>创建空区域。</summary>
    /// <param name="id">区域标识。</param>
    /// <param name="capacity">初始容量提示，必须非负；不是数量上限，可继续追加。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> 为负。</exception>
    public TileZone(ZoneId id, int capacity = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Id = id;
        _items = new List<Tile>(capacity);
        _ids = new HashSet<Tile>(capacity);
        _itemsView = _items.AsReadOnly();
    }

    /// <summary>区域标识。</summary>
    public ZoneId Id { get; }

    /// <summary>当前牌数。</summary>
    public int Count => _items.Count;

    /// <summary>按槽位取牌；槽位即 <see cref="Items"/> 的下标。</summary>
    /// <param name="slot">0 起的槽位。</param>
    /// <exception cref="InvalidOperationException"><paramref name="slot"/> 越界（负数或 ≥ <see cref="Count"/>）。</exception>
    public Tile this[int slot]
    {
        get
        {
            ThrowIfSlotOutOfRange(slot);
            return _items[slot];
        }
    }

    /// <summary>当前牌序列的实时只读视图；不复制、不排序，后续追加与移除对其可见。</summary>
    public IReadOnlyList<Tile> Items => _itemsView;

    /// <summary>在尾部追加一张牌。</summary>
    /// <param name="tile">要追加的牌。</param>
    /// <exception cref="InvalidOperationException">区域内已存在同 <see cref="Tile.Id"/> 的牌；失败时区域不变。</exception>
    public void Append(Tile tile)
    {
        if (!_ids.Add(tile))
        {
            throw new InvalidOperationException($"Zone {Id} already contains a tile with id {tile.Id}.");
        }

        _items.Add(tile);
    }

    /// <summary>移除指定槽位的牌，其后槽位前移一位。</summary>
    /// <param name="slot">0 起的槽位。</param>
    /// <returns>被移除的牌。</returns>
    /// <exception cref="InvalidOperationException"><paramref name="slot"/> 越界；失败时区域不变。</exception>
    public Tile RemoveAt(int slot)
    {
        ThrowIfSlotOutOfRange(slot);
        Tile tile = _items[slot];
        _ids.Remove(tile);
        _items.RemoveAt(slot);
        return tile;
    }

    /// <summary>按 <see cref="Tile.Id"/> 移除第一张匹配的牌。</summary>
    /// <param name="tile">要移除的牌；只按 id 匹配。</param>
    /// <returns>找到并移除返回 true；不存在返回 false。</returns>
    public bool Remove(Tile tile)
    {
        if (!_ids.Remove(tile))
        {
            return false;
        }

        _items.Remove(tile);
        return true;
    }

    /// <summary>按 <see cref="Tile.Id"/> 查找第一张匹配的牌。</summary>
    /// <param name="tile">要查找的牌；只按 id 匹配。</param>
    /// <returns>0 起的槽位；不存在返回 -1。</returns>
    public int IndexOf(Tile tile) => _items.IndexOf(tile);

    /// <summary>清空区域。</summary>
    public void Clear()
    {
        _items.Clear();
        _ids.Clear();
    }

    private void ThrowIfSlotOutOfRange(int slot)
    {
        if ((uint)slot >= (uint)_items.Count)
        {
            throw new InvalidOperationException(
                $"Slot {slot} is out of range for zone {Id} with {_items.Count} item(s).");
        }
    }
}
