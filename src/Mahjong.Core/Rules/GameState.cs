namespace Mahjong.Core.Rules;

/// <summary>
/// 一局对局的权威模型：牌墙、牌尾、四名玩家、当前座位与局号。
/// 只承载规则层的状态转换（发牌、摸打、副露），不读时钟、不做 IO；实例非线程安全。
/// </summary>
public sealed class GameState
{
    /// <summary>玩家座位数，固定 4。</summary>
    public const byte PlayerCount = 4;

    /// <summary>物理牌总数（34 种 × 4 张）。</summary>
    private const int TotalTiles = 136;

    /// <summary>洗牌后牌墙的张数；洗牌结果的末 14 张留给牌尾。</summary>
    private const int WallCapacity = 122;

    /// <summary>牌尾张数，只用于杠补。</summary>
    private const int WallTailCapacity = 14;

    /// <summary>每局起手张数；庄家另多摸 1 张。</summary>
    private const int InitialHandSize = 13;

    private const byte MinRoundNo = 1;
    private const byte MaxRoundNo = 8;

    private byte _currentSeat;
    private byte _roundNo = MinRoundNo;

    /// <summary>创建空局：4 名玩家，牌墙与牌尾为空，当前座位 0，局号 1。</summary>
    public GameState()
    {
        Wall = new TileZone(new ZoneId(ZoneId.PublicSeat, ZoneKind.Wall), WallCapacity);
        WallTail = new TileZone(new ZoneId(ZoneId.PublicSeat, ZoneKind.WallTail), WallTailCapacity);
        Players = new PlayerState[PlayerCount];
        for (byte seat = 0; seat < PlayerCount; seat++)
        {
            Players[seat] = new PlayerState(seat);
        }

        CurrentSeat = 0;
        RoundNo = MinRoundNo;
    }

    /// <summary>牌墙：正常摸牌从头部取。</summary>
    public TileZone Wall { get; }

    /// <summary>牌尾：杠补牌从尾部取。</summary>
    public TileZone WallTail { get; }

    /// <summary>四名玩家，下标即座位号。</summary>
    public PlayerState[] Players { get; }

    /// <summary>当前座位 0–3。</summary>
    /// <exception cref="ArgumentOutOfRangeException">赋值不在 0–3；失败时原值不变。</exception>
    public byte CurrentSeat
    {
        get => _currentSeat;
        set
        {
            if (value >= PlayerCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"Seat must be within 0-{PlayerCount - 1}.");
            }

            _currentSeat = value;
        }
    }

    /// <summary>局号 1–8。</summary>
    /// <exception cref="ArgumentOutOfRangeException">赋值不在 1–8；失败时原值不变。</exception>
    public byte RoundNo
    {
        get => _roundNo;
        set
        {
            if (value < MinRoundNo || value > MaxRoundNo)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"Round number must be within {MinRoundNo}-{MaxRoundNo}.");
            }

            _roundNo = value;
        }
    }

    /// <summary>牌墙是否为空。流局判据只看牌墙，牌尾允许有余额。</summary>
    public bool IsWallEmpty => Wall.Count == 0;

    /// <summary>
    /// 按规格 §3.1 的确定性顺序发牌：重置牌墙、牌尾、四家手牌/副露/弃牌与庄家标记（不动分数与局号），
    /// 用 Fisher-Yates 洗出 1–136 的排列，末 14 张进牌尾、其余进牌墙，再按 (庄家,+1,+2,+3) mod 4 发牌。
    /// 可重复调用；同 seed 与同 bankerSeat 必得同一全槽位。
    /// </summary>
    /// <param name="rng">随机源；同一种子的同一实现得到同一排列。</param>
    /// <param name="bankerSeat">庄家座位 0–3。</param>
    /// <exception cref="ArgumentNullException"><paramref name="rng"/> 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bankerSeat"/> 不在 0–3。</exception>
    public void Deal(IRandomSource rng, byte bankerSeat)
    {
        ArgumentNullException.ThrowIfNull(rng);
        if (bankerSeat >= PlayerCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bankerSeat),
                bankerSeat,
                $"Seat must be within 0-{PlayerCount - 1}.");
        }

        Wall.Clear();
        WallTail.Clear();
        foreach (PlayerState player in Players)
        {
            player.Hand.Clear();
            player.Melds.Clear();
            player.Discards.Clear();
            player.IsBanker = false;
        }

        ushort[] order = new ushort[TotalTiles];
        for (int index = 0; index < order.Length; index++)
        {
            order[index] = (ushort)(index + 1);
        }

        for (int index = TotalTiles - 1; index >= 1; index--)
        {
            int swapIndex = (int)(rng.NextUInt() % (uint)(index + 1));
            (order[index], order[swapIndex]) = (order[swapIndex], order[index]);
        }

        for (int index = WallCapacity; index < TotalTiles; index++)
        {
            WallTail.Append(CreateTile(order[index]));
        }

        for (int index = 0; index < WallCapacity; index++)
        {
            Wall.Append(CreateTile(order[index]));
        }

        for (int offset = 0; offset < PlayerCount; offset++)
        {
            byte seat = (byte)((bankerSeat + offset) % PlayerCount);
            for (int count = 0; count < InitialHandSize; count++)
            {
                // DrawFromWall 自身已把牌追加到该座位手牌，这里不能再次 Append。
                DrawFromWall(seat);
            }
        }

        DrawFromWall(bankerSeat);

        Players[bankerSeat].IsBanker = true;
        CurrentSeat = bankerSeat;
    }

    /// <summary>从牌墙头部摸一张牌并追加到该座位手牌。</summary>
    /// <param name="seat">座位 0–3。</param>
    /// <returns>摸到的牌。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seat"/> 不在 0–3。</exception>
    /// <exception cref="InvalidOperationException">牌墙为空；失败时状态不变。</exception>
    public Tile DrawFromWall(byte seat)
    {
        PlayerState player = GetPlayer(seat);
        if (Wall.Count == 0)
        {
            throw new InvalidOperationException("The wall is empty; no tile can be drawn.");
        }

        Tile tile = Wall.RemoveAt(0);
        player.Hand.Append(tile);
        return tile;
    }

    /// <summary>从牌尾末端摸一张牌（杠补）并追加到该座位手牌。</summary>
    /// <param name="seat">座位 0–3。</param>
    /// <returns>摸到的牌。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seat"/> 不在 0–3。</exception>
    /// <exception cref="InvalidOperationException">牌尾为空；失败时状态不变。</exception>
    public Tile DrawFromTail(byte seat)
    {
        PlayerState player = GetPlayer(seat);
        if (WallTail.Count == 0)
        {
            throw new InvalidOperationException("The wall tail is empty; no tile can be drawn.");
        }

        Tile tile = WallTail.RemoveAt(WallTail.Count - 1);
        player.Hand.Append(tile);
        return tile;
    }

    /// <summary>出牌：从手牌指定槽位移除并追加到弃牌区。</summary>
    /// <param name="seat">座位 0–3。</param>
    /// <param name="handSlot">手牌槽位（<see cref="PlayerState.Hand"/> 的下标），不是弃牌槽位。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seat"/> 不在 0–3。</exception>
    /// <exception cref="InvalidOperationException"><paramref name="handSlot"/> 越界；失败时状态不变。</exception>
    public void Discard(byte seat, int handSlot)
    {
        PlayerState player = GetPlayer(seat);
        Tile tile = player.Hand.RemoveAt(handSlot);
        player.Discards.Append(tile);
    }

    /// <summary>
    /// 声明副露：把手牌槽位的牌（碰/明杠另加目标座位的弃牌）移入副露。
    /// 先完成全部校验与预取，再修改状态；任何校验失败都不改变状态。
    /// 副露本身不摸牌：杠后的补牌由流程层调用 <see cref="DrawFromTail"/>。
    /// </summary>
    /// <param name="seat">声明者座位 0–3。</param>
    /// <param name="kind">副露种类，必须是已定义的 <see cref="MeldKind"/>。</param>
    /// <param name="handSlots">参与副露的手牌槽位：碰 2、明杠 3、暗杠 4、补杠 1，且互不重复。</param>
    /// <param name="targetSeat">碰/明杠为被取弃牌的座位 0–3 且不等于 <paramref name="seat"/>；暗杠/补杠必须为 <see cref="Meld.NoTargetSeat"/>。</param>
    /// <param name="targetDiscardSlot">碰/明杠为目标座位弃牌区槽位；暗杠/补杠必须为 null。</param>
    /// <returns>新建的副露；补杠时返回被原地升级的既有副露。</returns>
    /// <exception cref="ArgumentOutOfRangeException">座位越界；或 <paramref name="kind"/> 未定义。</exception>
    /// <exception cref="ArgumentException">手牌槽数与 kind 不符、槽位重复、或目标座位/目标弃牌槽位组合与 kind 不匹配。</exception>
    /// <exception cref="InvalidOperationException">手牌槽位或目标弃牌槽位越界、副露牌种不一致、找不到可补杠的碰；失败时状态不变。</exception>
    public Meld DeclareMeld(
        byte seat,
        MeldKind kind,
        ReadOnlySpan<int> handSlots,
        byte targetSeat,
        int? targetDiscardSlot = null)
    {
        if (seat >= PlayerCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seat),
                seat,
                $"Seat must be within 0-{PlayerCount - 1}.");
        }

        if (targetSeat >= PlayerCount && targetSeat != Meld.NoTargetSeat)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetSeat),
                targetSeat,
                $"Target seat must be within 0-{PlayerCount - 1} or {Meld.NoTargetSeat}.");
        }

        int requiredHandTiles = kind switch
        {
            MeldKind.Pong => 2,
            MeldKind.KongExposed => 3,
            MeldKind.KongConcealed => 4,
            MeldKind.KongSupplement => 1,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Meld kind must be a defined MeldKind value."),
        };

        if (handSlots.Length != requiredHandTiles)
        {
            throw new ArgumentException(
                $"A {kind} meld requires {requiredHandTiles} hand tile slot(s), but {handSlots.Length} were provided.",
                nameof(handSlots));
        }

        bool takesDiscard = kind is MeldKind.Pong or MeldKind.KongExposed;
        if (takesDiscard)
        {
            if (targetSeat == Meld.NoTargetSeat || targetSeat == seat)
            {
                throw new ArgumentException(
                    $"A {kind} meld requires a target seat in 0-{PlayerCount - 1} that differs from the declaring seat.",
                    nameof(targetSeat));
            }

            if (targetDiscardSlot is null)
            {
                throw new ArgumentException(
                    $"A {kind} meld requires a target discard slot.",
                    nameof(targetDiscardSlot));
            }
        }
        else
        {
            if (targetSeat != Meld.NoTargetSeat)
            {
                throw new ArgumentException(
                    $"A {kind} meld must use {Meld.NoTargetSeat} as target seat.",
                    nameof(targetSeat));
            }

            if (targetDiscardSlot is not null)
            {
                throw new ArgumentException(
                    $"A {kind} meld must not specify a target discard slot.",
                    nameof(targetDiscardSlot));
            }
        }

        for (int left = 0; left < handSlots.Length; left++)
        {
            for (int right = left + 1; right < handSlots.Length; right++)
            {
                if (handSlots[left] == handSlots[right])
                {
                    throw new ArgumentException("Hand tile slots must be distinct.", nameof(handSlots));
                }
            }
        }

        PlayerState player = Players[seat];
        int meldTileCount = requiredHandTiles + (takesDiscard ? 1 : 0);
        Tile[] meldTiles = new Tile[meldTileCount];
        for (int index = 0; index < requiredHandTiles; index++)
        {
            int slot = handSlots[index];
            if ((uint)slot >= (uint)player.Hand.Count)
            {
                throw new InvalidOperationException(
                    $"Hand slot {slot} is out of range 0-{player.Hand.Count - 1}.");
            }

            meldTiles[index] = player.Hand[slot];
        }

        if (takesDiscard)
        {
            PlayerState source = Players[targetSeat];
            int discardSlot = targetDiscardSlot!.Value;
            if ((uint)discardSlot >= (uint)source.Discards.Count)
            {
                throw new InvalidOperationException(
                    $"Discard slot {discardSlot} is out of range 0-{source.Discards.Count - 1} for seat {targetSeat}.");
            }

            meldTiles[requiredHandTiles] = source.Discards[discardSlot];
        }

        ushort typeId = meldTiles[0].Type.Id;
        for (int index = 1; index < meldTiles.Length; index++)
        {
            if (meldTiles[index].Type.Id != typeId)
            {
                throw new InvalidOperationException("All tiles moved into a meld must share the same tile type id.");
            }
        }

        for (int left = 0; left < meldTiles.Length; left++)
        {
            for (int right = left + 1; right < meldTiles.Length; right++)
            {
                if (meldTiles[left].Id == meldTiles[right].Id)
                {
                    throw new InvalidOperationException(
                        $"Tile id {meldTiles[left].Id} cannot appear twice in a meld.");
                }
            }
        }

        if (kind == MeldKind.KongSupplement)
        {
            Meld? pong = FindPong(player, typeId);
            if (pong is null)
            {
                throw new InvalidOperationException(
                    $"Seat {seat} has no Pong of tile type id {typeId} to upgrade to KongSupplement.");
            }

            // 原子性：所有可能失败的校验（座位/槽数/槽位/牌种/物理牌重复/可升级的碰）都已完成；
            // UpgradeToKongSupplement 的前置条件已由上面校验满足，RemoveAt 的手牌槽位也已校验，
            // 这两步在当前状态下都不会失败，因此不存在“改到一半”的路径。先升级后移牌，
            // 使升级的校验失败（仅可能来自被外部破坏的不变量）也不会先丢掉手牌。
            pong.UpgradeToKongSupplement(meldTiles[0]);
            player.Hand.RemoveAt(handSlots[0]);
            return pong;
        }

        bool opened = kind != MeldKind.KongConcealed;
        byte meldTargetSeat = takesDiscard ? targetSeat : Meld.NoTargetSeat;

        // 先构造并校验 Meld，确保后续修改状态下不再有可失败点。
        Meld meld = new Meld(kind, meldTiles, opened, meldTargetSeat);
        RemoveHandTiles(player, handSlots);
        if (takesDiscard)
        {
            Players[targetSeat].Discards.RemoveAt(targetDiscardSlot!.Value);
        }

        player.Melds.Add(meld);
        return meld;
    }

    private PlayerState GetPlayer(byte seat)
    {
        if (seat >= PlayerCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seat),
                seat,
                $"Seat must be within 0-{PlayerCount - 1}.");
        }

        return Players[seat];
    }

    /// <summary>按索引降序移除手牌槽位，避免移除过程中槽位左移影响后续索引。</summary>
    private static void RemoveHandTiles(PlayerState player, ReadOnlySpan<int> handSlots)
    {
        Span<int> slots = stackalloc int[handSlots.Length];
        handSlots.CopyTo(slots);
        slots.Sort();
        for (int index = slots.Length - 1; index >= 0; index--)
        {
            player.Hand.RemoveAt(slots[index]);
        }
    }

    private static Meld? FindPong(PlayerState player, ushort typeId)
    {
        for (int index = 0; index < player.Melds.Count; index++)
        {
            Meld meld = player.Melds[index];
            if (meld.Kind == MeldKind.Pong && meld.Tiles.Count > 0 && meld.Tiles[0].Type.Id == typeId)
            {
                return meld;
            }
        }

        return null;
    }

    /// <summary>
    /// 由物理牌 id 还原牌：id = typeId*4+copy+1；牌种到花色/rank 的映射即 content/tiles.json v1 的固定布局
    /// （T-3 §1：万/筒/条 1–9、风 1–4、箭 1–3）。GameState 在 Rules 层不依赖 Content 程序集，故按该固定布局还原；
    /// 内容布局变更须按基线变更流程处理，不在本层做兼容分支。
    /// </summary>
    private static Tile CreateTile(ushort physicalId) =>
        new(physicalId, CreateTileType((ushort)((physicalId - 1) / 4)));

    private static TileType CreateTileType(ushort typeId) => typeId switch
    {
        <= 8 => new TileType(typeId, SuitType.Character, (byte)(typeId + 1)),
        <= 17 => new TileType(typeId, SuitType.Dot, (byte)(typeId - 8)),
        <= 26 => new TileType(typeId, SuitType.Bamboo, (byte)(typeId - 17)),
        <= 30 => new TileType(typeId, SuitType.Wind, (byte)(typeId - 26)),
        _ => new TileType(typeId, SuitType.Dragon, (byte)(typeId - 30)),
    };
}
