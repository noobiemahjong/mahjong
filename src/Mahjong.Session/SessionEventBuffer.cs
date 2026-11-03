using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;

namespace Mahjong.Session;

/// <summary>
/// 会话事件缓冲：按产出顺序保存事件，Drain 时按 viewer 编码 payload 并清空。
/// seq 在 Emit 时从 1 单调分配、Drain 后不重复；Tick 保留产出时的逻辑 tick。
/// 可见性只在 Deal/HandSync/Drew 编码时生效：viewer 为该事件座位或 255 才给出牌 id，其他座位只给数量或 -1。
/// 实例非线程安全；无静态可变状态。
/// </summary>
internal sealed class SessionEventBuffer
{
    /// <summary>全景视图的 viewer 值。</summary>
    private const byte FullViewSeat = 255;

    private readonly List<PendingEvent> _pending = [];
    private long _nextSeq = 1;

    /// <summary>记录一局开始事件。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="roundNo">局号 1–8。</param>
    /// <param name="bankerSeat">庄家座位 0–3。</param>
    /// <param name="wallCount">发牌后牌墙张数。</param>
    /// <param name="dealerStreak">本局连庄次数，载荷第 5 字节。</param>
    public void EmitRoundStarted(long tick, byte roundNo, byte bankerSeat, ushort wallCount, byte dealerStreak) =>
        Add(new PendingEvent
        {
            Kind = SessionEventKind.RoundStarted,
            Tick = tick,
            RoundNo = roundNo,
            BankerSeat = bankerSeat,
            WallCount = wallCount,
            DealerStreak = dealerStreak,
        });

    /// <summary>记录回合开始事件。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="seat">回合座位 0–3。</param>
    /// <param name="deadlineTick">本回合截止 tick。</param>
    public void EmitTurnStarted(long tick, byte seat, long deadlineTick) =>
        Add(new PendingEvent
        {
            Kind = SessionEventKind.TurnStarted,
            Tick = tick,
            Seat = seat,
            DeadlineTick = deadlineTick,
        });

    /// <summary>记录发牌事件；<c>count</c> 取 <paramref name="tiles"/> 的张数。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="seat">座位 0–3。</param>
    /// <param name="tiles">该座位的起手牌。</param>
    /// <exception cref="ArgumentNullException"><paramref name="tiles"/> 为 null。</exception>
    /// <exception cref="ArgumentException">张数超过 255。</exception>
    public void EmitDeal(long tick, byte seat, IReadOnlyList<Tile> tiles) =>
        Add(new PendingEvent
        {
            Kind = SessionEventKind.Deal,
            Tick = tick,
            Seat = seat,
            TileIds = CopyTileIds(tiles),
        });

    /// <summary>记录手牌全量同步事件；<c>count</c> 取 <paramref name="tiles"/> 的张数。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="seat">座位 0–3。</param>
    /// <param name="tiles">该座位的当前手牌。</param>
    /// <exception cref="ArgumentNullException"><paramref name="tiles"/> 为 null。</exception>
    /// <exception cref="ArgumentException">张数超过 255。</exception>
    public void EmitHandSync(long tick, byte seat, IReadOnlyList<Tile> tiles) =>
        Add(new PendingEvent
        {
            Kind = SessionEventKind.HandSync,
            Tick = tick,
            Seat = seat,
            TileIds = CopyTileIds(tiles),
        });

    /// <summary>记录摸牌事件。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="seat">摸牌座位 0–3。</param>
    /// <param name="tileId">物理牌 id。</param>
    /// <param name="wallCount">摸牌后牌墙张数。</param>
    public void EmitDrew(long tick, byte seat, int tileId, ushort wallCount) =>
        Add(new PendingEvent
        {
            Kind = SessionEventKind.Drew,
            Tick = tick,
            Seat = seat,
            TileId = tileId,
            WallCount = wallCount,
        });

    /// <summary>记录出牌事件。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="seat">出牌座位 0–3。</param>
    /// <param name="slot">弃牌区槽位。</param>
    /// <param name="tileId">物理牌 id。</param>
    public void EmitDiscarded(long tick, byte seat, ushort slot, ushort tileId) =>
        Add(new PendingEvent
        {
            Kind = SessionEventKind.Discarded,
            Tick = tick,
            Seat = seat,
            Slot = slot,
            TileId16 = tileId,
        });

    /// <summary>记录副露事件。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="seat">副露座位 0–3。</param>
    /// <param name="kind">副露种类。</param>
    /// <param name="tiles">组成副露的牌。</param>
    /// <param name="targetSeat">被取弃牌的座位 0–3；暗杠/补杠为 255。</param>
    /// <exception cref="ArgumentNullException"><paramref name="tiles"/> 为 null。</exception>
    /// <exception cref="ArgumentException">张数超过 255。</exception>
    public void EmitMeld(long tick, byte seat, MeldKind kind, IReadOnlyList<Tile> tiles, byte targetSeat) =>
        Add(new PendingEvent
        {
            Kind = SessionEventKind.Meld,
            Tick = tick,
            Seat = seat,
            MeldKind = kind,
            TileIds = CopyTileIds(tiles),
            TargetSeat = targetSeat,
        });

    /// <summary>记录请求打开事件。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="request">打开的请求。</param>
    public void EmitRequestOpened(long tick, PendingRequest request) =>
        Add(new PendingEvent
        {
            Kind = SessionEventKind.RequestOpened,
            Tick = tick,
            Request = request,
        });

    /// <summary>
    /// 记录请求结算事件。Answered 时 payload 携带 5 字节实际应答命令，其余种类为 0 字节。
    /// </summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="request">被结算的请求。</param>
    /// <param name="outcome">结算种类。</param>
    /// <param name="answer">Answered 时的实际应答命令；Cancelled/TimedOut 必须为 null。</param>
    /// <exception cref="ArgumentException"><paramref name="outcome"/> 为 Answered 但 <paramref name="answer"/> 为 null：
    /// 拒绝写入，避免产出读取器会拒绝的 payload。</exception>
    public void EmitRequestClosed(long tick, PendingRequest request, RequestOutcomeKind outcome, GameCommand? answer)
    {
        if (outcome == RequestOutcomeKind.Answered && answer is null)
        {
            throw new ArgumentException(
                "Answered 的 RequestClosed 必须携带实际应答命令。",
                nameof(answer));
        }

        Add(new PendingEvent
        {
            Kind = SessionEventKind.RequestClosed,
            Tick = tick,
            Request = request,
            Outcome = outcome,
            Answer = answer,
        });
    }

    /// <summary>记录一局结束事件。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="winnerSeat">赢家座位 0–3；-1 表示流局。</param>
    /// <param name="scoreDelta">4 个座位的本局净变化，下标即座位号。</param>
    /// <param name="actionType">本局计分动作聚合。</param>
    /// <exception cref="ArgumentNullException"><paramref name="scoreDelta"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="scoreDelta"/> 长度不是 4。</exception>
    public void EmitRoundEnded(long tick, sbyte winnerSeat, IReadOnlyList<short> scoreDelta, ScoreActionType actionType) =>
        Add(new PendingEvent
        {
            Kind = SessionEventKind.RoundEnded,
            Tick = tick,
            WinnerSeat = winnerSeat,
            Scores = CopyScores(scoreDelta, nameof(scoreDelta)),
            ActionType = actionType,
        });

    /// <summary>记录整场结束事件。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="totalScores">4 个座位的累计总分，下标即座位号。</param>
    /// <param name="ranking">长度 4 的座位名次，值必须在 0–3。</param>
    /// <param name="replayId">回放 id；M0.2 为空字符串。</param>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    /// <exception cref="ArgumentException">分数或名次长度不是 4、名次座位越界、或 replayId 的 UTF-8 超过 255 字节。</exception>
    public void EmitGameEnded(long tick, IReadOnlyList<short> totalScores, IReadOnlyList<byte> ranking, string replayId)
    {
        ArgumentNullException.ThrowIfNull(replayId);
        byte[] rankingCopy = CopyRanking(ranking);
        byte[] replayBytes = Encoding.UTF8.GetBytes(replayId);
        if (replayBytes.Length > byte.MaxValue)
        {
            throw new ArgumentException("replayId 的 UTF-8 长度不得超过 255 字节。", nameof(replayId));
        }

        Add(new PendingEvent
        {
            Kind = SessionEventKind.GameEnded,
            Tick = tick,
            Scores = CopyScores(totalScores, nameof(totalScores)),
            Ranking = rankingCopy,
            ReplayId = replayId,
        });
    }

    /// <summary>记录座位状态变化事件。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="seat">座位 0–3。</param>
    /// <param name="state">状态编码：0 Online、1 Grace、2 Bot、3 Offline。</param>
    public void EmitSeatStateChanged(long tick, byte seat, byte state) =>
        Add(new PendingEvent
        {
            Kind = SessionEventKind.SeatStateChanged,
            Tick = tick,
            Seat = seat,
            State = state,
        });

    /// <summary>记录翻马事件；公开事件，编码不按 viewer 过滤。</summary>
    /// <param name="tick">产出 tick。</param>
    /// <param name="horseSeat">马牌座位 0–3。</param>
    /// <param name="tileId">马牌的物理牌 id 1–136。</param>
    public void EmitHorseRevealed(long tick, byte horseSeat, ushort tileId) =>
        Add(new PendingEvent
        {
            Kind = SessionEventKind.HorseRevealed,
            Tick = tick,
            Seat = horseSeat,
            TileId16 = tileId,
        });

    /// <summary>
    /// 破坏性读取：把当前全部事件按 viewer 编码成 payload 并清空缓冲；空缓冲返回空列表。
    /// </summary>
    /// <param name="viewerSeat">观察座位 0–3，或 255 表示全景。</param>
    /// <returns>按产出顺序排列的事件快照；不返回 null。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="viewerSeat"/> 不在 0–3 且不等于 255。</exception>
    public IReadOnlyList<SessionEvent> Drain(byte viewerSeat)
    {
        if (viewerSeat is not (0 or 1 or 2 or 3) && viewerSeat != FullViewSeat)
        {
            throw new ArgumentOutOfRangeException(
                nameof(viewerSeat),
                viewerSeat,
                "viewer 必须是 0–3 或 255。");
        }

        if (_pending.Count == 0)
        {
            return Array.Empty<SessionEvent>();
        }

        SessionEvent[] events = new SessionEvent[_pending.Count];
        for (int index = 0; index < events.Length; index++)
        {
            events[index] = Encode(_pending[index], viewerSeat);
        }

        _pending.Clear();
        return Array.AsReadOnly(events);
    }

    private void Add(PendingEvent pending)
    {
        pending.Seq = _nextSeq++;
        _pending.Add(pending);
    }

    private static SessionEvent Encode(PendingEvent pending, byte viewerSeat) =>
        new(pending.Seq, pending.Tick, pending.Kind, EncodePayload(pending, viewerSeat));

    private static byte[] EncodePayload(PendingEvent pending, byte viewerSeat) => pending.Kind switch
    {
        SessionEventKind.RoundStarted => EncodeRoundStarted(pending),
        SessionEventKind.TurnStarted => EncodeTurnStarted(pending),
        SessionEventKind.Deal => EncodeHand(pending, viewerSeat),
        SessionEventKind.HandSync => EncodeHand(pending, viewerSeat),
        SessionEventKind.Drew => EncodeDrew(pending, viewerSeat),
        SessionEventKind.Discarded => EncodeDiscarded(pending),
        SessionEventKind.Meld => EncodeMeld(pending),
        SessionEventKind.RequestOpened => EncodeRequestOpened(pending),
        SessionEventKind.RequestClosed => EncodeRequestClosed(pending),
        SessionEventKind.RoundEnded => EncodeRoundEnded(pending),
        SessionEventKind.GameEnded => EncodeGameEnded(pending),
        SessionEventKind.SeatStateChanged => EncodeSeatStateChanged(pending),
        SessionEventKind.HorseRevealed => EncodeHorseRevealed(pending),
        _ => throw new InvalidOperationException($"未定义的事件种类 {pending.Kind}。"),
    };

    private static byte[] EncodeRoundStarted(PendingEvent pending)
    {
        byte[] payload = new byte[5];
        payload[0] = pending.RoundNo;
        payload[1] = pending.BankerSeat;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), pending.WallCount);
        payload[4] = pending.DealerStreak;
        return payload;
    }

    private static byte[] EncodeTurnStarted(PendingEvent pending)
    {
        byte[] payload = new byte[9];
        payload[0] = pending.Seat;
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(1, 8), pending.DeadlineTick);
        return payload;
    }

    private static byte[] EncodeHand(PendingEvent pending, byte viewerSeat)
    {
        ushort[] tileIds = pending.TileIds ?? [];
        bool visible = viewerSeat == FullViewSeat || viewerSeat == pending.Seat;
        if (!visible)
        {
            return [pending.Seat, (byte)tileIds.Length];
        }

        byte[] payload = new byte[2 + (tileIds.Length * 2)];
        payload[0] = pending.Seat;
        payload[1] = (byte)tileIds.Length;
        for (int index = 0; index < tileIds.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2 + (index * 2), 2), tileIds[index]);
        }

        return payload;
    }

    private static byte[] EncodeDrew(PendingEvent pending, byte viewerSeat)
    {
        byte[] payload = new byte[7];
        payload[0] = pending.Seat;
        bool visible = viewerSeat == FullViewSeat || viewerSeat == pending.Seat;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1, 4), visible ? pending.TileId : -1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(5, 2), pending.WallCount);
        return payload;
    }

    private static byte[] EncodeDiscarded(PendingEvent pending)
    {
        byte[] payload = new byte[5];
        payload[0] = pending.Seat;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1, 2), pending.Slot);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(3, 2), pending.TileId16);
        return payload;
    }

    private static byte[] EncodeMeld(PendingEvent pending)
    {
        ushort[] tileIds = pending.TileIds ?? [];
        byte[] payload = new byte[4 + (tileIds.Length * 2)];
        payload[0] = pending.Seat;
        payload[1] = (byte)pending.MeldKind;
        payload[2] = (byte)tileIds.Length;
        for (int index = 0; index < tileIds.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(3 + (index * 2), 2), tileIds[index]);
        }

        payload[3 + (tileIds.Length * 2)] = pending.TargetSeat;
        return payload;
    }

    private static byte[] EncodeRequestOpened(PendingEvent pending)
    {
        PendingRequest request = pending.Request;
        byte[] payload = new byte[20];
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(0, 8), request.RequestId);
        payload[8] = request.Seat;
        payload[9] = (byte)request.Kind;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), (ushort)request.Allowed);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(12, 8), request.DeadlineTick);
        return payload;
    }

    private static byte[] EncodeRequestClosed(PendingEvent pending)
    {
        PendingRequest request = pending.Request;
        bool hasAnswer = pending.Outcome == RequestOutcomeKind.Answered && pending.Answer.HasValue;
        byte[] payload = new byte[11 + (hasAnswer ? 5 : 0)];
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(0, 8), request.RequestId);
        payload[8] = (byte)pending.Outcome;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(9, 2), hasAnswer ? (ushort)5 : (ushort)0);
        if (hasAnswer)
        {
            GameCommand answer = pending.Answer.GetValueOrDefault();
            payload[11] = (byte)answer.Verb;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12, 2), (ushort)answer.Slot);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14, 2), answer.CardId);
        }

        return payload;
    }

    private static byte[] EncodeRoundEnded(PendingEvent pending)
    {
        short[] scores = pending.Scores ?? [];
        byte[] payload = new byte[19];
        payload[0] = unchecked((byte)pending.WinnerSeat);
        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1 + (seat * 4), 4), scores[seat]);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(17, 2), (ushort)pending.ActionType);
        return payload;
    }

    private static byte[] EncodeGameEnded(PendingEvent pending)
    {
        short[] scores = pending.Scores ?? [];
        byte[] ranking = pending.Ranking ?? [];
        byte[] replayBytes = Encoding.UTF8.GetBytes(pending.ReplayId ?? string.Empty);
        byte[] payload = new byte[21 + replayBytes.Length];
        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(seat * 4, 4), scores[seat]);
        }

        for (int index = 0; index < GameState.PlayerCount; index++)
        {
            payload[16 + index] = ranking[index];
        }

        payload[20] = (byte)replayBytes.Length;
        replayBytes.CopyTo(payload, 21);
        return payload;
    }

    private static byte[] EncodeSeatStateChanged(PendingEvent pending) => [pending.Seat, pending.State];

    /// <summary>翻马载荷 3 字节：horseSeat u8 + tileId u16 小端，字段紧排无对齐。</summary>
    private static byte[] EncodeHorseRevealed(PendingEvent pending)
    {
        byte[] payload = new byte[3];
        payload[0] = pending.Seat;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1, 2), pending.TileId16);
        return payload;
    }

    private static ushort[] CopyTileIds(IReadOnlyList<Tile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (tiles.Count > byte.MaxValue)
        {
            throw new ArgumentException("单个事件的牌张数不得超过 255。", nameof(tiles));
        }

        ushort[] tileIds = new ushort[tiles.Count];
        for (int index = 0; index < tileIds.Length; index++)
        {
            tileIds[index] = tiles[index].Id;
        }

        return tileIds;
    }

    private static short[] CopyScores(IReadOnlyList<short> scores, string paramName)
    {
        ArgumentNullException.ThrowIfNull(scores);
        if (scores.Count != GameState.PlayerCount)
        {
            throw new ArgumentException($"分数序列必须包含 {GameState.PlayerCount} 个座位。", paramName);
        }

        short[] copy = new short[scores.Count];
        for (int index = 0; index < copy.Length; index++)
        {
            copy[index] = scores[index];
        }

        return copy;
    }

    private static byte[] CopyRanking(IReadOnlyList<byte> ranking)
    {
        ArgumentNullException.ThrowIfNull(ranking);
        if (ranking.Count != GameState.PlayerCount)
        {
            throw new ArgumentException($"名次序列必须包含 {GameState.PlayerCount} 个座位。", nameof(ranking));
        }

        byte[] copy = new byte[ranking.Count];
        for (int index = 0; index < copy.Length; index++)
        {
            byte seat = ranking[index];
            if (seat >= GameState.PlayerCount)
            {
                throw new ArgumentException($"名次中的座位 {seat} 越界。", nameof(ranking));
            }

            copy[index] = seat;
        }

        return copy;
    }

    /// <summary>待编码事件：Emit 时填字段并分配 seq，Drain 时按 viewer 编码。</summary>
    private sealed class PendingEvent
    {
        public long Seq;
        public long Tick;
        public SessionEventKind Kind;
        public byte Seat;
        public byte RoundNo;
        public byte BankerSeat;
        public ushort WallCount;
        public byte DealerStreak;
        public long DeadlineTick;
        public int TileId;
        public ushort Slot;
        public ushort TileId16;
        public MeldKind MeldKind;
        public byte TargetSeat;
        public PendingRequest Request;
        public RequestOutcomeKind Outcome;
        public GameCommand? Answer;
        public sbyte WinnerSeat;
        public ScoreActionType ActionType;
        public byte State;
        public ushort[]? TileIds;
        public short[]? Scores;
        public byte[]? Ranking;
        public string? ReplayId;
    }
}
