using System.Collections.ObjectModel;

namespace Mahjong.Core.Rules;

/// <summary>副露种类。数值是协议与回放编码的一部分，已发布后不得改动。</summary>
public enum MeldKind : byte
{
    /// <summary>碰：3 张，其中 1 张来自他家的弃牌。</summary>
    Pong = 1,

    /// <summary>明杠：4 张，其中 1 张来自他家的弃牌。</summary>
    KongExposed = 2,

    /// <summary>暗杠：4 张，全部来自手牌。</summary>
    KongConcealed = 3,

    /// <summary>补杠：既有碰加上手牌第 4 张，原地升级。</summary>
    KongSupplement = 4,
}

/// <summary>
/// 副露：固定张数的同牌种群（碰 3 张、各种杠 4 张）。
/// 杠按 3 张折算计入手牌张数，第 4 张视为补张；牌数守恒由 <see cref="GameState"/> 负责。
/// </summary>
public sealed class Meld
{
    /// <summary>无目标座位（暗杠与补杠）。</summary>
    public const byte NoTargetSeat = 255;

    private readonly List<Tile> _tiles;
    private readonly ReadOnlyCollection<Tile> _tilesView;

    /// <summary>创建副露。</summary>
    /// <param name="kind">副露种类，必须是已定义的 <see cref="MeldKind"/>。</param>
    /// <param name="tiles">组成副露的牌：碰 3 张，各种杠 4 张。</param>
    /// <param name="opened">是否已对其他玩家亮明；与 <paramref name="kind"/> 不做交叉校验。</param>
    /// <param name="targetSeat">被取牌的目标座位 0–3，或 <see cref="NoTargetSeat"/>。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> 未定义；或 <paramref name="targetSeat"/> 既不在 0–3 也不等于 <see cref="NoTargetSeat"/>。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tiles"/> 为 null。</exception>
    /// <exception cref="ArgumentException">张数与 kind 不符；牌种（<see cref="TileType.Id"/>）不一致；物理牌 id 重复。</exception>
    public Meld(MeldKind kind, IReadOnlyList<Tile> tiles, bool opened, byte targetSeat)
    {
        int expectedTileCount = kind switch
        {
            MeldKind.Pong => 3,
            MeldKind.KongExposed or MeldKind.KongConcealed or MeldKind.KongSupplement => 4,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Meld kind must be a defined MeldKind value."),
        };

        ArgumentNullException.ThrowIfNull(tiles);

        if (tiles.Count != expectedTileCount)
        {
            throw new ArgumentException(
                $"A {kind} meld must contain {expectedTileCount} tiles, but {tiles.Count} were provided.",
                nameof(tiles));
        }

        ushort typeId = tiles[0].Type.Id;
        for (int index = 1; index < tiles.Count; index++)
        {
            if (tiles[index].Type.Id != typeId)
            {
                throw new ArgumentException(
                    "All tiles in a meld must share the same tile type id.",
                    nameof(tiles));
            }
        }

        for (int left = 0; left < tiles.Count; left++)
        {
            for (int right = left + 1; right < tiles.Count; right++)
            {
                if (tiles[left].Id == tiles[right].Id)
                {
                    throw new ArgumentException(
                        $"A meld cannot contain the same physical tile twice (id {tiles[left].Id}).",
                        nameof(tiles));
                }
            }
        }

        if (targetSeat >= GameState.PlayerCount && targetSeat != NoTargetSeat)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetSeat),
                targetSeat,
                $"Target seat must be within 0-{GameState.PlayerCount - 1} or {NoTargetSeat}.");
        }

        Kind = kind;
        Opened = opened;
        TargetSeat = targetSeat;

        // 构造时复制传入集合：副露与调用方集合解耦，之后修改源集合不影响 Tiles/张数/槽位。
        // ReadOnlyCollection 只在构造期创建一次，补杠只通过 internal UpgradeToKongSupplement 追加。
        _tiles = new List<Tile>(tiles);
        _tilesView = new ReadOnlyCollection<Tile>(_tiles);
    }

    /// <summary>副露种类；补杠升级后由 <see cref="MeldKind.Pong"/> 变为 <see cref="MeldKind.KongSupplement"/>。</summary>
    public MeldKind Kind { get; private set; }

    /// <summary>组成副露的牌，顺序与构造时传入顺序一致；构造时已复制传入集合，之后修改调用方集合不影响本副露。</summary>
    /// <remarks>实时只读视图：补杠原地升级后可见第 4 张；不可强转 List 修改。</remarks>
    public IReadOnlyList<Tile> Tiles => _tilesView;

    /// <summary>是否已亮明。</summary>
    public bool Opened { get; }

    /// <summary>被取牌的目标座位；暗杠与补杠为 <see cref="NoTargetSeat"/>。</summary>
    public byte TargetSeat { get; }

    /// <summary>
    /// 把既有碰原地升级为补杠：追加第 4 张并把种类改为 <see cref="MeldKind.KongSupplement"/>，保留对象身份。
    /// 只由 <see cref="GameState.DeclareMeld"/> 在校验通过后调用。
    /// </summary>
    /// <param name="tile">补杠的第 4 张，牌种必须与既有 3 张一致。</param>
    /// <exception cref="InvalidOperationException">当前不是 3 张的碰；牌种不一致；或该物理牌已在副露中。失败时副露不变。</exception>
    internal void UpgradeToKongSupplement(Tile tile)
    {
        if (Kind != MeldKind.Pong || _tiles.Count != 3)
        {
            throw new InvalidOperationException("Only a Pong with exactly 3 tiles can be upgraded to KongSupplement.");
        }

        if (tile.Type.Id != _tiles[0].Type.Id)
        {
            throw new InvalidOperationException("The supplement tile must share the Pong's tile type id.");
        }

        for (int index = 0; index < _tiles.Count; index++)
        {
            if (_tiles[index].Id == tile.Id)
            {
                throw new InvalidOperationException($"Tile id {tile.Id} is already part of the meld.");
            }
        }

        _tiles.Add(tile);
        Kind = MeldKind.KongSupplement;
    }
}
