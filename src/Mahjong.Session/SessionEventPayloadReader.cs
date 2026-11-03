using System.Buffers.Binary;
using System.Text;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;

namespace Mahjong.Session;

/// <summary>
/// 会话事件 payload 的唯一公开读取入口：按事件种类校验并解析 <see cref="SessionEvent.Payload"/>。
/// 种类不匹配抛 <see cref="ArgumentException"/>；payload 截断、计数与长度矛盾、UTF-8 非法或名次越界抛 <see cref="InvalidDataException"/>。
/// 返回的数组是本次解析新建的副本，修改它不影响事件 payload。
/// </summary>
public static class SessionEventPayloadReader
{
    /// <summary>读取 <see cref="SessionEventKind.RoundStarted"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 RoundStarted 的事件。</param>
    /// <returns>解析后的 payload。</returns>
    /// <exception cref="ArgumentException">事件种类不是 RoundStarted。</exception>
    /// <exception cref="InvalidDataException">payload 长度不是 5 字节（含旧 4B 与更短的载荷）。</exception>
    public static RoundStartedPayload ReadRoundStarted(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.RoundStarted);
        RequireLength(payload, 5, SessionEventKind.RoundStarted);
        return new RoundStartedPayload(
            payload[0],
            payload[1],
            BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2, 2)),
            payload[4]);
    }

    /// <summary>读取 <see cref="SessionEventKind.TurnStarted"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 TurnStarted 的事件。</param>
    /// <returns>解析后的 payload。</returns>
    /// <exception cref="ArgumentException">事件种类不是 TurnStarted。</exception>
    /// <exception cref="InvalidDataException">payload 长度非法。</exception>
    public static TurnStartedPayload ReadTurnStarted(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.TurnStarted);
        RequireLength(payload, 9, SessionEventKind.TurnStarted);
        return new TurnStartedPayload(
            payload[0],
            BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(1, 8)));
    }

    /// <summary>读取 <see cref="SessionEventKind.Deal"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 Deal 的事件。</param>
    /// <returns>解析后的 payload；不可见编码的 <see cref="DealPayload.TileIds"/> 为空数组。</returns>
    /// <exception cref="ArgumentException">事件种类不是 Deal。</exception>
    /// <exception cref="InvalidDataException">payload 长度非法或 count 与牌 id 字节数矛盾。</exception>
    public static DealPayload ReadDeal(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.Deal);
        return ReadHandLike(payload, SessionEventKind.Deal);
    }

    /// <summary>读取 <see cref="SessionEventKind.HandSync"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 HandSync 的事件。</param>
    /// <returns>解析后的 payload；不可见编码的 <see cref="HandSyncPayload.TileIds"/> 为空数组。</returns>
    /// <exception cref="ArgumentException">事件种类不是 HandSync。</exception>
    /// <exception cref="InvalidDataException">payload 长度非法或 count 与牌 id 字节数矛盾。</exception>
    public static HandSyncPayload ReadHandSync(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.HandSync);
        DealPayload decoded = ReadHandLike(payload, SessionEventKind.HandSync);
        return new HandSyncPayload(decoded.Seat, decoded.Count, decoded.TileIds);
    }

    /// <summary>读取 <see cref="SessionEventKind.Drew"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 Drew 的事件。</param>
    /// <returns>解析后的 payload；不可见编码的 <see cref="DrewPayload.TileId"/> 为 -1。</returns>
    /// <exception cref="ArgumentException">事件种类不是 Drew。</exception>
    /// <exception cref="InvalidDataException">payload 长度非法。</exception>
    public static DrewPayload ReadDrew(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.Drew);
        RequireLength(payload, 7, SessionEventKind.Drew);
        return new DrewPayload(
            payload[0],
            BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(1, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(5, 2)));
    }

    /// <summary>读取 <see cref="SessionEventKind.Discarded"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 Discarded 的事件。</param>
    /// <returns>解析后的 payload。</returns>
    /// <exception cref="ArgumentException">事件种类不是 Discarded。</exception>
    /// <exception cref="InvalidDataException">payload 长度非法。</exception>
    public static DiscardedPayload ReadDiscarded(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.Discarded);
        RequireLength(payload, 5, SessionEventKind.Discarded);
        return new DiscardedPayload(
            payload[0],
            BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(3, 2)));
    }

    /// <summary>读取 <see cref="SessionEventKind.Meld"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 Meld 的事件。</param>
    /// <returns>解析后的 payload。</returns>
    /// <exception cref="ArgumentException">事件种类不是 Meld。</exception>
    /// <exception cref="InvalidDataException">payload 长度非法或 count 与牌 id 字节数矛盾。</exception>
    public static MeldPayload ReadMeld(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.Meld);
        if (payload.Length < 4)
        {
            throw new InvalidDataException("Meld payload 至少需要 4 字节。");
        }

        byte count = payload[2];
        if (payload.Length != 4 + (count * 2))
        {
            throw new InvalidDataException("Meld payload 长度与 count 矛盾。");
        }

        ushort[] tileIds = new ushort[count];
        for (int index = 0; index < tileIds.Length; index++)
        {
            tileIds[index] = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(3 + (index * 2), 2));
        }

        return new MeldPayload(
            payload[0],
            (MeldKind)payload[1],
            tileIds,
            payload[3 + (count * 2)]);
    }

    /// <summary>读取 <see cref="SessionEventKind.RequestOpened"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 RequestOpened 的事件。</param>
    /// <returns>解析后的 payload。</returns>
    /// <exception cref="ArgumentException">事件种类不是 RequestOpened。</exception>
    /// <exception cref="InvalidDataException">payload 长度非法。</exception>
    public static RequestOpenedPayload ReadRequestOpened(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.RequestOpened);
        RequireLength(payload, 20, SessionEventKind.RequestOpened);
        return new RequestOpenedPayload(
            BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(0, 8)),
            payload[8],
            (RequestKind)payload[9],
            (AllowedVerbs)BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2)),
            BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(12, 8)));
    }

    /// <summary>读取 <see cref="SessionEventKind.RequestClosed"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 RequestClosed 的事件。</param>
    /// <returns>解析后的 payload；Answered 时 <see cref="RequestClosedPayload.Answer"/> 非 null。</returns>
    /// <exception cref="ArgumentException">事件种类不是 RequestClosed。</exception>
    /// <exception cref="InvalidDataException">payload 长度非法，或 answer 长度与 outcome 矛盾。</exception>
    public static RequestClosedPayload ReadRequestClosed(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.RequestClosed);
        if (payload.Length < 11)
        {
            throw new InvalidDataException("RequestClosed payload 至少需要 11 字节。");
        }

        long requestId = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(0, 8));
        RequestOutcomeKind outcome = (RequestOutcomeKind)payload[8];
        int answerLength = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(9, 2));
        if (payload.Length != 11 + answerLength)
        {
            throw new InvalidDataException("RequestClosed payload 长度与 answerLen 矛盾。");
        }

        if (outcome == RequestOutcomeKind.Answered)
        {
            if (answerLength != 5)
            {
                throw new InvalidDataException("Answered 的 RequestClosed 必须带 5 字节应答命令。");
            }

            return new RequestClosedPayload(
                requestId,
                outcome,
                new RequestAnswer(
                    (CommandVerb)payload[11],
                    BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2)),
                    BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(14, 2))));
        }

        if (answerLength != 0)
        {
            throw new InvalidDataException("非 Answered 的 RequestClosed 不得携带应答命令。");
        }

        return new RequestClosedPayload(requestId, outcome, null);
    }

    /// <summary>读取 <see cref="SessionEventKind.RoundEnded"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 RoundEnded 的事件。</param>
    /// <returns>解析后的 payload。</returns>
    /// <exception cref="ArgumentException">事件种类不是 RoundEnded。</exception>
    /// <exception cref="InvalidDataException">payload 长度非法。</exception>
    public static RoundEndedPayload ReadRoundEnded(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.RoundEnded);
        RequireLength(payload, 19, SessionEventKind.RoundEnded);

        int[] scoreDelta = new int[GameState.PlayerCount];
        for (int seat = 0; seat < scoreDelta.Length; seat++)
        {
            scoreDelta[seat] = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(1 + (seat * 4), 4));
        }

        return new RoundEndedPayload(
            (sbyte)payload[0],
            scoreDelta,
            (ScoreActionType)BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(17, 2)));
    }

    /// <summary>读取 <see cref="SessionEventKind.GameEnded"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 GameEnded 的事件。</param>
    /// <returns>解析后的 payload。</returns>
    /// <exception cref="ArgumentException">事件种类不是 GameEnded。</exception>
    /// <exception cref="InvalidDataException">payload 长度非法、UTF-8 非法或名次越界。</exception>
    public static GameEndedPayload ReadGameEnded(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.GameEnded);
        if (payload.Length < 21)
        {
            throw new InvalidDataException("GameEnded payload 至少需要 21 字节。");
        }

        int replayIdLength = payload[20];
        if (payload.Length != 21 + replayIdLength)
        {
            throw new InvalidDataException("GameEnded payload 长度与 replayIdLen 矛盾。");
        }

        int[] totalScores = new int[GameState.PlayerCount];
        for (int seat = 0; seat < totalScores.Length; seat++)
        {
            totalScores[seat] = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(seat * 4, 4));
        }

        byte[] ranking = new byte[GameState.PlayerCount];
        for (int index = 0; index < ranking.Length; index++)
        {
            byte seat = payload[16 + index];
            if (seat >= GameState.PlayerCount)
            {
                throw new InvalidDataException($"名次中的座位 {seat} 越界，必须在 0–{GameState.PlayerCount - 1}。");
            }

            ranking[index] = seat;
        }

        return new GameEndedPayload(totalScores, ranking, DecodeUtf8(payload.Slice(21)));
    }

    /// <summary>读取 <see cref="SessionEventKind.SeatStateChanged"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 SeatStateChanged 的事件。</param>
    /// <returns>解析后的 payload。</returns>
    /// <exception cref="ArgumentException">事件种类不是 SeatStateChanged。</exception>
    /// <exception cref="InvalidDataException">payload 长度非法。</exception>
    public static SeatStateChangedPayload ReadSeatStateChanged(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.SeatStateChanged);
        RequireLength(payload, 2, SessionEventKind.SeatStateChanged);
        return new SeatStateChangedPayload(payload[0], payload[1]);
    }

    /// <summary>读取 <see cref="SessionEventKind.HorseRevealed"/> 的 payload。</summary>
    /// <param name="sessionEvent">种类必须是 HorseRevealed 的事件。</param>
    /// <returns>解析后的 payload；公开事件，四座与观战者同值。</returns>
    /// <exception cref="ArgumentException">事件种类不是 HorseRevealed。</exception>
    /// <exception cref="InvalidDataException">payload 长度不是 3 字节（含 2B 与 4B 载荷）。</exception>
    public static HorseRevealedPayload ReadHorseRevealed(SessionEvent sessionEvent)
    {
        ReadOnlySpan<byte> payload = GetPayload(sessionEvent, SessionEventKind.HorseRevealed);
        RequireLength(payload, 3, SessionEventKind.HorseRevealed);
        return new HorseRevealedPayload(
            payload[0],
            BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2)));
    }

    private static ReadOnlySpan<byte> GetPayload(SessionEvent sessionEvent, SessionEventKind expected)
    {
        if (sessionEvent.Kind != expected)
        {
            throw new ArgumentException(
                $"事件种类是 {sessionEvent.Kind}，读取 {expected} 需要匹配的事件种类。",
                nameof(sessionEvent));
        }

        return sessionEvent.Payload.Span;
    }

    private static void RequireLength(ReadOnlySpan<byte> payload, int expected, SessionEventKind kind)
    {
        if (payload.Length != expected)
        {
            throw new InvalidDataException($"{kind} payload 长度应为 {expected} 字节，实际 {payload.Length} 字节。");
        }
    }

    private static DealPayload ReadHandLike(ReadOnlySpan<byte> payload, SessionEventKind kind)
    {
        if (payload.Length < 2)
        {
            throw new InvalidDataException($"{kind} payload 至少需要 2 字节。");
        }

        byte seat = payload[0];
        byte count = payload[1];
        int tileBytes = payload.Length - 2;
        if (tileBytes == 0)
        {
            return new DealPayload(seat, count, Array.Empty<ushort>());
        }

        if (tileBytes != count * 2)
        {
            throw new InvalidDataException($"{kind} payload 的 count 与牌 id 字节数矛盾。");
        }

        ushort[] tileIds = new ushort[count];
        for (int index = 0; index < tileIds.Length; index++)
        {
            tileIds[index] = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2 + (index * 2), 2));
        }

        return new DealPayload(seat, count, tileIds);
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("replayId 不是合法 UTF-8。", exception);
        }
    }
}
