using Mahjong.Core.Flow;
using Mahjong.Core.Rules;

namespace Mahjong.Session;

/// <summary>
/// 会话流程：单根动作协程推进 8 局（发牌 → 摸打 → 弃牌响应 → 结算 → 换庄），
/// 事件顺序与超时托管口径见契约 §6；不读时钟、不等待真实时间。
/// </summary>
public sealed partial class GameSession
{
    /// <summary>
    /// 单根动作协程：8 局线性状态机。每个 tick 由 <see cref="Step"/> 推进到下一个等待点；
    /// 请求一律「打开 → 等结算 → 取结果」三步，结算结果只消费一次。
    /// </summary>
    private IEnumerator<ActionState> RunGameLoop()
    {
        byte bankerSeat = 0;
        for (byte roundNo = 1; roundNo <= RoundController.TotalRounds; roundNo++)
        {
            if (roundNo == 1)
            {
                bankerSeat = (byte)(_rng.NextUInt() % GameState.PlayerCount);
            }

            bool roundFinished = false;
            _state.RoundNo = roundNo;
            _state.Deal(_rng, bankerSeat);

            long roundTick = _scheduler.Tick;
            _events.EmitRoundStarted(roundTick, roundNo, bankerSeat, (ushort)_state.Wall.Count);
            for (byte seatIndex = 0; seatIndex < GameState.PlayerCount; seatIndex++)
            {
                _events.EmitDeal(roundTick, seatIndex, _state.Players[seatIndex].Hand.Items);
            }

            byte seat = bankerSeat;
            EmitTurnStarted(seat);
            Tile drawn = _state.DrawFromWall(seat);
            _lastDrawn = drawn;
            _lastDrawWasKongSupplement = false;
            EmitDrew(seat, drawn);
            bool hasDrawn = true;

            while (!roundFinished)
            {
                PlayerState player = _state.Players[seat];

                // A. 自己回合：可自摸先开 Win 请求，否则开 Discard 请求（自己回合可带杠位）。
                if (hasDrawn)
                {
                    if (CanWin(player))
                    {
                        long winRequestId = _requests.OpenSingle(
                            seat,
                            RequestKind.Win,
                            AllowedVerbs.Win | AllowedVerbs.Pass,
                            _scheduler.Tick,
                            _scheduler.Tick + _config.TurnTimeoutTicks).RequestId;
                        yield return WaitForSettlement(winRequestId);
                        RequestOutcome winOutcome = _requests.TakeOutcome(winRequestId);
                        if (winOutcome.Kind == RequestOutcomeKind.Answered
                            && winOutcome.Payload is GameCommand winCommand
                            && winCommand.Verb == CommandVerb.Win)
                        {
                            bankerSeat = CompleteSelfDrawnWin(roundNo, bankerSeat, seat);
                            roundFinished = true;
                            break;
                        }
                    }

                    AllowedVerbs allowed = AllowedVerbs.Discard;
                    if (CanSelfKong(player))
                    {
                        allowed |= AllowedVerbs.Kong;
                    }

                    long discardRequestId = _requests.OpenSingle(
                        seat,
                        RequestKind.Discard,
                        allowed,
                        _scheduler.Tick,
                        _scheduler.Tick + _config.TurnTimeoutTicks).RequestId;
                    yield return WaitForSettlement(discardRequestId);
                    RequestOutcome discardOutcome = _requests.TakeOutcome(discardRequestId);
                    if (discardOutcome.Kind == RequestOutcomeKind.Answered
                        && discardOutcome.Payload is GameCommand kongCommand
                        && kongCommand.Verb == CommandVerb.Kong)
                    {
                        CommitSelfKong(seat);
                        hasDrawn = true;
                        continue;
                    }

                    DiscardTile(seat, player, ResolveDiscardSlot(player, discardOutcome));
                    hasDrawn = false;
                }
                else
                {
                    // 碰后强制出牌：同一回合内不再摸牌，也不产 TurnStarted。
                    long forcedRequestId = _requests.OpenSingle(
                        seat,
                        RequestKind.Discard,
                        AllowedVerbs.Discard,
                        _scheduler.Tick,
                        _scheduler.Tick + _config.TurnTimeoutTicks).RequestId;
                    yield return WaitForSettlement(forcedRequestId);
                    RequestOutcome forcedOutcome = _requests.TakeOutcome(forcedRequestId);
                    DiscardTile(seat, player, ResolveDiscardSlot(player, forcedOutcome));
                }

                // B. 弃牌响应窗口：只碰/明杠， responders 按座位升序。
                Tile discard = player.Discards[player.Discards.Count - 1];
                PlayerState[] candidates = FindResponseCandidates(seat, discard);
                if (candidates.Length > 0)
                {
                    long groupId = _requests.OpenGroup(
                        discard,
                        candidates,
                        _scheduler.Tick,
                        _scheduler.Tick + _config.ResponseTimeoutTicks)
                        ?? throw new InvalidOperationException("非空候选的响应窗口必须返回 group id。");
                    yield return WaitForGroupClosure(groupId);
                    ResponseResult result = _requests.TakeGroupResult(groupId);
                    if (result.HasWinner)
                    {
                        SkillId winnerSkill = result.WinnerSkill
                            ?? throw new InvalidOperationException("响应窗口胜者必须携带技能。");
                        CommitResponseSkill(result.WinnerSeat, winnerSkill, discard, seat);
                        seat = result.WinnerSeat;
                        hasDrawn = winnerSkill == SkillId.Kong;
                        continue;
                    }
                }

                // C. 无人响应：下家摸牌开始新回合；摸牌前墙空即流局。
                byte nextSeat = (byte)((seat + 1) % GameState.PlayerCount);
                if (_state.Wall.Count == 0)
                {
                    CompleteDrawRound(roundNo, bankerSeat);
                    roundFinished = true;
                    break;
                }

                _state.CurrentSeat = nextSeat;
                seat = nextSeat;
                EmitTurnStarted(seat);
                Tile nextDrawn = _state.DrawFromWall(seat);
                _lastDrawn = nextDrawn;
                _lastDrawWasKongSupplement = false;
                EmitDrew(seat, nextDrawn);
                hasDrawn = true;
            }
        }

        short[] totals = CopyScores(_scores.TotalScores);
        _events.EmitGameEnded(
            _scheduler.Tick,
            totals,
            RoundRanking.Rank(totals),
            string.Empty);
        _gameOver = true;
        yield return ActionState.Complete();
    }

    /// <summary>等待单座请求结算；谓词每 tick 由调度器求值一次。</summary>
    private ActionState WaitForSettlement(long requestId) =>
        ActionState.WaitUntil(() => _requests.IsSettled(requestId));

    /// <summary>等待响应窗口关闭；谓词每 tick 由调度器求值一次。</summary>
    private ActionState WaitForGroupClosure(long groupId) =>
        ActionState.WaitUntil(() => _requests.IsGroupClosed(groupId));

    /// <summary>产出 TurnStarted：截止 tick = 当前 tick + 回合超时。</summary>
    private void EmitTurnStarted(byte seat) =>
        _events.EmitTurnStarted(_scheduler.Tick, seat, _scheduler.Tick + _config.TurnTimeoutTicks);

    /// <summary>产出 Drew：wallCount 取摸牌后的牌墙张数。</summary>
    private void EmitDrew(byte seat, Tile tile) =>
        _events.EmitDrew(_scheduler.Tick, seat, tile.Id, (ushort)_state.Wall.Count);

    /// <summary>当前手牌是否可自摸：七对优先，否则普通自摸。</summary>
    private bool CanWin(PlayerState player) =>
        _verifier.Verify(_state, player, SkillId.SelfDrawnWith7Pairs, card: null, targetCard: null)
        || _verifier.Verify(_state, player, SkillId.SelfDrawn, card: null, targetCard: null);

    /// <summary>自己回合是否可杠：用本回合摸到的牌过一遍真实校验器（含牌墙非空）。</summary>
    private bool CanSelfKong(PlayerState player) =>
        _lastDrawn is Tile drawn
        && _verifier.Verify(_state, player, SkillId.Kong, card: drawn, targetCard: null);

    /// <summary>
    /// 从 Discard 请求结算里取出槽位：应答 Discard 用其槽位，超时（或被取消）走确定性自动出牌。
    /// </summary>
    private int ResolveDiscardSlot(PlayerState player, RequestOutcome outcome)
    {
        if (outcome.Kind == RequestOutcomeKind.Answered
            && outcome.Payload is GameCommand command
            && command.Verb == CommandVerb.Discard)
        {
            return command.Slot;
        }

        return FindAutoDiscardSlot(player);
    }

    /// <summary>超时自动出牌：手牌槽位升序取第一张通过校验器的牌；全不合法视为 bug。</summary>
    private int FindAutoDiscardSlot(PlayerState player)
    {
        IReadOnlyList<Tile> hand = player.Hand.Items;
        for (int slot = 0; slot < hand.Count; slot++)
        {
            if (_verifier.Verify(_state, player, skill: null, card: hand[slot], targetCard: null))
            {
                return slot;
            }
        }

        throw new InvalidOperationException($"座位 {player.Seat} 没有可自动打出的合法牌。");
    }

    /// <summary>出牌并产出 Discarded；slot 为弃牌区槽位（新牌追加后的下标）。</summary>
    private void DiscardTile(byte seat, PlayerState player, int handSlot)
    {
        Tile tile = player.Hand[handSlot];
        _state.Discard(seat, handSlot);
        _events.EmitDiscarded(_scheduler.Tick, seat, (ushort)(player.Discards.Count - 1), tile.Id);
        _lastDrawn = null;
        _lastDrawWasKongSupplement = false;
    }

    /// <summary>弃牌响应候选：其他座位中碰或明杠合法者，按座位号升序。</summary>
    private PlayerState[] FindResponseCandidates(byte discarderSeat, Tile discard)
    {
        PlayerState[] buffer = new PlayerState[GameState.PlayerCount - 1];
        int count = 0;
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            if (seat == discarderSeat)
            {
                continue;
            }

            PlayerState candidate = _state.Players[seat];
            if (_verifier.Verify(_state, candidate, SkillId.Pong, card: null, targetCard: discard)
                || _verifier.Verify(_state, candidate, SkillId.Kong, card: null, targetCard: discard))
            {
                buffer[count++] = candidate;
            }
        }

        if (count == buffer.Length)
        {
            return buffer;
        }

        PlayerState[] result = new PlayerState[count];
        Array.Copy(buffer, result, count);
        return result;
    }

    /// <summary>
    /// 自己回合杠：Commit 三种形态之一，按「Melds 增加 → 暗杠；否则原碰升级 → 补杠」判定；
    /// 产出 Meld、记杠分、从手牌末张读取补牌并产出 Drew。
    /// </summary>
    private void CommitSelfKong(byte seat)
    {
        Tile drawn = _lastDrawn ?? throw new InvalidOperationException("自己回合杠必须有本回合摸到的牌。");
        PlayerState player = _state.Players[seat];
        int meldsBefore = player.Melds.Count;
        List<Meld> pongsBefore = CollectPongs(player);

        var context = new SkillContext(_state, player, targetCard: null, drawnTile: drawn);
        _kongSkill.Commit(context);

        MeldKind kind;
        Meld meld;
        if (player.Melds.Count > meldsBefore)
        {
            kind = MeldKind.KongConcealed;
            meld = player.Melds[meldsBefore];
        }
        else
        {
            kind = MeldKind.KongSupplement;
            meld = FindUpgradedPong(pongsBefore);
        }

        long tick = _scheduler.Tick;
        _events.EmitMeld(tick, seat, kind, meld.Tiles, Meld.NoTargetSeat);
        _scores.ApplyKong(seat, kind);

        Tile supplement = player.Hand[player.Hand.Count - 1];
        _lastDrawn = supplement;
        _lastDrawWasKongSupplement = true;
        _events.EmitDrew(tick, seat, supplement.Id, (ushort)_state.Wall.Count);
    }

    /// <summary>响应窗口胜者提交技能：碰不摸牌，明杠记杠分并取补牌。</summary>
    private void CommitResponseSkill(byte winnerSeat, SkillId skill, Tile discard, byte discarderSeat)
    {
        PlayerState player = _state.Players[winnerSeat];
        int meldsBefore = player.Melds.Count;
        var context = new SkillContext(_state, player, targetCard: discard, targetSeat: discarderSeat);

        MeldKind kind;
        if (skill == SkillId.Pong)
        {
            _pongSkill.Commit(context);
            kind = MeldKind.Pong;
        }
        else if (skill == SkillId.Kong)
        {
            _kongSkill.Commit(context);
            kind = MeldKind.KongExposed;
        }
        else
        {
            throw new InvalidOperationException($"响应窗口胜者技能只能是碰或杠，实际为 {skill}。");
        }

        Meld meld = player.Melds[meldsBefore];
        long tick = _scheduler.Tick;
        _events.EmitMeld(tick, winnerSeat, kind, meld.Tiles, discarderSeat);

        if (kind == MeldKind.KongExposed)
        {
            _scores.ApplyKong(winnerSeat, kind, discarderSeat);
            Tile supplement = player.Hand[player.Hand.Count - 1];
            _lastDrawn = supplement;
            _lastDrawWasKongSupplement = true;
            _events.EmitDrew(tick, winnerSeat, supplement.Id, (ushort)_state.Wall.Count);
        }
        else
        {
            _lastDrawn = null;
            _lastDrawWasKongSupplement = false;
        }
    }

    /// <summary>
    /// 自摸结算：七对走 <see cref="ScoreManager.ApplySevenPairsSelfDrawn"/>（不受其他家杠位影响），
    /// 普通自摸的 kongUpFlower 只取决于赢家本回合最近一次摸牌是否为杠后补牌。
    /// 结算顺序：取样 delta/actionType → Release → 写回镜像 → 产 RoundEnded；返回下一局庄家。
    /// </summary>
    private byte CompleteSelfDrawnWin(byte roundNo, byte bankerSeat, byte winnerSeat)
    {
        PlayerState winner = _state.Players[winnerSeat];
        bool sevenPairs = _wins.IsSevenPairs(winner.Hand.Items, _joker);
        bool standard = _wins.IsWin(winner.Hand.Items, winner.Melds, _joker) && !sevenPairs;
        if (!sevenPairs && !standard)
        {
            throw new InvalidOperationException("Win 请求结算时手牌既非七对也非常规胡。");
        }

        bool hasJoker = _jokerActive && ContainsTileType(winner.Hand.Items, _joker.Id);
        if (sevenPairs)
        {
            _scores.ApplySevenPairsSelfDrawn(winnerSeat, hasJoker);
        }
        else
        {
            _scores.ApplySelfDrawn(
                winnerSeat,
                hasJoker,
                sevenPairs: false,
                kongUpFlower: _lastDrawWasKongSupplement);
        }

        // D10：结算 payload 统一经由公开的 RoundResult 构造，事件只从该快照取字段。
        short[] delta = CopyScores(_scores.RoundDelta);
        ScoreActionType actionType = _scores.RoundActionType;
        _scores.Release();
        WriteBackScores();
        RoundResult result = new(roundNo, bankerSeat, winnerSeat, actionType, delta, CopyScores(_scores.TotalScores));
        _events.EmitRoundEnded(_scheduler.Tick, (sbyte)result.WinnerSeat, result.ScoreDelta, result.ActionType);
        return result.WinnerSeat;
    }

    /// <summary>流局结算：回滚本局暂存再 Release（净 0），写回镜像，产 RoundEnded(-1, 全 0, None)。</summary>
    private void CompleteDrawRound(byte roundNo, byte bankerSeat)
    {
        _scores.RollbackRound();
        _scores.Release();
        WriteBackScores();
        RoundResult result = new(
            roundNo,
            bankerSeat,
            RoundResult.NoWinnerSeat,
            ScoreActionType.None,
            new short[GameState.PlayerCount],
            CopyScores(_scores.TotalScores));
        sbyte emittedWinner = result.IsDraw ? (sbyte)-1 : (sbyte)result.WinnerSeat;
        _events.EmitRoundEnded(_scheduler.Tick, emittedWinner, result.ScoreDelta, result.ActionType);
    }

    /// <summary>收集当前所有碰副露引用，用于补杠后定位被原地升级的那一组。</summary>
    private static List<Meld> CollectPongs(PlayerState player)
    {
        List<Meld> pongs = [];
        foreach (Meld meld in player.Melds)
        {
            if (meld.Kind == MeldKind.Pong)
            {
                pongs.Add(meld);
            }
        }

        return pongs;
    }

    /// <summary>在 Commit 前的碰集合里找已升级为补杠的那一组。</summary>
    private static Meld FindUpgradedPong(List<Meld> pongsBefore)
    {
        foreach (Meld pong in pongsBefore)
        {
            if (pong.Kind != MeldKind.Pong)
            {
                return pong;
            }
        }

        throw new InvalidOperationException("补杠后找不到被升级的碰。");
    }

    /// <summary>把权威总分写回 <see cref="PlayerState.Score"/> 镜像。</summary>
    private void WriteBackScores()
    {
        IReadOnlyList<short> totals = _scores.TotalScores;
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            _state.Players[seat].Score = totals[seat];
        }
    }

    /// <summary>手牌中是否存在指定牌种。</summary>
    private static bool ContainsTileType(IReadOnlyList<Tile> hand, ushort typeId)
    {
        for (int index = 0; index < hand.Count; index++)
        {
            if (hand[index].Type.Id == typeId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>把只读总分序列复制为独立数组，供事件 payload 快照。</summary>
    private static short[] CopyScores(IReadOnlyList<short> scores)
    {
        short[] copy = new short[GameState.PlayerCount];
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            copy[seat] = scores[seat];
        }

        return copy;
    }
}
