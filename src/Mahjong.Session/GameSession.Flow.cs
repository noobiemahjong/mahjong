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

        // 连庄次数与 bankerSeat 同寿命同源：协程局部变量，不进局面、不进状态哈希。
        byte dealerStreak = 1;
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
            _events.EmitRoundStarted(roundTick, roundNo, bankerSeat, (ushort)_state.Wall.Count, dealerStreak);
            for (byte seatIndex = 0; seatIndex < GameState.PlayerCount; seatIndex++)
            {
                _events.EmitDeal(roundTick, seatIndex, _state.Players[seatIndex].Hand.Items);
            }

            byte seat = bankerSeat;
            EmitTurnStarted(seat);
            Tile drawn = _state.DrawFromWall(seat);
            _lastDrawn = drawn;
            _lastDrawSource = DrawSource.Normal;
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
                            // 局末转移按「赢家座位 ≠ 本局庄家」判定，不按结算返回值反推（D-07）。
                            byte nextBanker = CompleteSelfDrawnWin(roundNo, bankerSeat, seat, dealerStreak);
                            dealerStreak = seat == bankerSeat ? (byte)(dealerStreak + 1) : (byte)1;
                            bankerSeat = nextBanker;
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
                        SelfKongCommitResult kongResult = new();
                        IEnumerator<ActionState> kong = CommitSelfKong(seat, roundNo, bankerSeat, dealerStreak, kongResult);
                        while (kong.MoveNext())
                        {
                            yield return kong.Current;
                        }

                        if (kongResult.RobbingKongWon)
                        {
                            // 抢杠胡算赢家胡：局末转移与连庄推进与自摸路径同口径（赢家 = 庄家则连庄 +1）。
                            dealerStreak = kongResult.WinnerSeat == bankerSeat ? (byte)(dealerStreak + 1) : (byte)1;
                            bankerSeat = kongResult.WinnerSeat;
                            roundFinished = true;
                            break;
                        }

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
                _lastDrawSource = DrawSource.Normal;
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

    /// <summary>等待抢杠组关闭（全员应答或到 deadline）；谓词每 tick 由调度器求值一次。</summary>
    private ActionState WaitForRobbingKongClosure(long groupId) =>
        ActionState.WaitUntil(() => _requests.IsRobbingKongGroupClosed(groupId));

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
        _lastDrawSource = DrawSource.Normal;
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
    /// 自己回合杠：补杠分支先按「预判 → 抢杠阶段 → 提交或取消」三步走；暗杠没有第 4 张可抢，直接提交。
    /// 抢杠阶段只开窗与收集应答，不动副露、手牌、杠分与补牌；无胜者时才按原顺序提交补杠
    /// （<see cref="KongSkill.Commit"/> → Meld → 杠分 → 补牌 Drew）。
    /// </summary>
    /// <param name="seat">杠家座位 0–3。</param>
    /// <param name="roundNo">本局局号，用于抢杠结算的 RoundResult。</param>
    /// <param name="bankerSeat">本局庄家座位，用于抢杠后的跟庄/连庄。</param>
    /// <param name="dealerStreak">本局连庄次数，用于抢杠后的连庄奖励。</param>
    /// <param name="result">抢杠成立时写入赢家座位；未成立时保持不变。</param>
    private IEnumerator<ActionState> CommitSelfKong(
        byte seat,
        byte roundNo,
        byte bankerSeat,
        byte dealerStreak,
        SelfKongCommitResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Tile drawn = _lastDrawn ?? throw new InvalidOperationException("自己回合杠必须有本回合摸到的牌。");
        PlayerState player = _state.Players[seat];

        if (_robbingKong && _kongSkill.TryCreateSupplementPlan(player, drawn, out KongSkill.KongSupplementPlan plan))
        {
            PlayerState[] candidates = FindRobbingKongCandidates(seat, plan.Tile);
            if (candidates.Length > 0)
            {
                _robbingKongTile = plan.Tile;
                long groupId = _requests.OpenRobbingKongGroup(
                    plan.Tile,
                    candidates,
                    _scheduler.Tick,
                    _scheduler.Tick + _config.ResponseTimeoutTicks)
                    ?? throw new InvalidOperationException("非空候选的抢杠组必须返回 group id。");
                yield return WaitForRobbingKongClosure(groupId);
                RobbingKongResult robbing = _requests.TakeRobbingKongResult(groupId);
                _robbingKongTile = null;

                if (robbing.HasWinner)
                {
                    int handSlot = plan.HandSlots[0];
                    bool sevenPairs = _wins.IsSevenPairs(
                        CombineWithFourthTile(player.Hand.Items, plan.Tile),
                        _joker);
                    result.RobbingKongWon = true;
                    result.WinnerSeat = robbing.WinnerSeat;
                    CompleteRobbingKongWin(
                        roundNo, bankerSeat, robbing.WinnerSeat, seat, plan.Tile, handSlot, sevenPairs, dealerStreak);
                    yield break;
                }
            }
        }

        CommitSelfKongNow(seat);
    }

    /// <summary>
    /// 自己回合杠的提交段：Commit 三种形态之一，按「Melds 增加 → 暗杠；否则原碰升级 → 补杠」判定；
    /// 产出 Meld、记杠分、从手牌末张读取补牌并产出 Drew。
    /// </summary>
    private void CommitSelfKongNow(byte seat)
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
        _lastDrawSource = kind == MeldKind.KongSupplement ? DrawSource.KongSupplement : DrawSource.KongConcealed;
        _events.EmitDrew(tick, seat, supplement.Id, (ushort)_state.Wall.Count);
    }

    /// <summary>
    /// 其余三座中可抢补杠第 4 张者，按座位升序：手牌 + 第 4 张（临时数组，不改手牌区）过
    /// <see cref="IWinChecker.IsWin"/> 或 <see cref="IWinChecker.IsSevenPairs"/> 成立即入选。
    /// </summary>
    private PlayerState[] FindRobbingKongCandidates(byte kongSeat, Tile fourthTile)
    {
        PlayerState[] buffer = new PlayerState[GameState.PlayerCount - 1];
        int count = 0;
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            if (seat == kongSeat)
            {
                continue;
            }

            PlayerState candidate = _state.Players[seat];
            if (CanRobbingKongWin(candidate, fourthTile))
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

    /// <summary>手牌 + 第 4 张是否成胡（七对或常规胡）；只读，用临时数组，不改手牌区。</summary>
    private bool CanRobbingKongWin(PlayerState player, Tile fourthTile)
    {
        Tile[] hand = CombineWithFourthTile(player.Hand.Items, fourthTile);
        return _wins.IsSevenPairs(hand, _joker) || _wins.IsWin(hand, player.Melds, _joker);
    }

    /// <summary>手牌 + 第 4 张的临时数组，顺序为手牌原序再接第 4 张；不改动任何区域。</summary>
    private static Tile[] CombineWithFourthTile(IReadOnlyList<Tile> hand, Tile fourthTile)
    {
        Tile[] combined = new Tile[hand.Count + 1];
        for (int index = 0; index < hand.Count; index++)
        {
            combined[index] = hand[index];
        }

        combined[hand.Count] = fourthTile;
        return combined;
    }

    /// <summary>
    /// 抢杠结算：第 0 步把第 4 张从被抢杠者手牌（补杠预判给出的槽位）移入赢家手牌尾部，物理 id 不变；
    /// 第 1 步记抢杠账，随后与自摸同尾段（跟庄 → 连庄 → 中马 → 取样/Release/写回/RoundEnded）。
    /// 不调 <see cref="KongSkill.Commit"/>、不记杠分、不产 Meld、不产 Drew、不补牌；副露保持碰。
    /// </summary>
    /// <returns>下一局庄家座位（= 赢家）。</returns>
    private byte CompleteRobbingKongWin(
        byte roundNo,
        byte bankerSeat,
        byte winnerSeat,
        byte robbedSeat,
        Tile fourthTile,
        int robbedHandSlot,
        bool sevenPairs,
        byte dealerStreak)
    {
        PlayerState robbed = _state.Players[robbedSeat];
        PlayerState winner = _state.Players[winnerSeat];
        if ((uint)robbedHandSlot >= (uint)robbed.Hand.Count || robbed.Hand[robbedHandSlot] != fourthTile)
        {
            throw new InvalidOperationException("抢杠结算时被抢的第 4 张不在预判槽位上。");
        }

        Tile tile = robbed.Hand.RemoveAt(robbedHandSlot);
        winner.Hand.Append(tile);

        // ① 抢杠账：base 取赢家移牌后的手牌口径（第 4 张已在手），七对 ×2；不置 SelfDrawn/NoJoker。
        bool hasJoker = _jokerActive && ContainsTileType(winner.Hand.Items, _joker.Id);
        int basePer = (hasJoker ? 2 : 4) * (sevenPairs ? 2 : 1);
        _scores.ApplyRobbingKong(winnerSeat, robbedSeat, basePer, sevenPairs);

        return SettleWinTail(roundNo, bankerSeat, winnerSeat, dealerStreak);
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
            _lastDrawSource = DrawSource.KongExposed;
            _events.EmitDrew(tick, winnerSeat, supplement.Id, (ushort)_state.Wall.Count);
        }
        else
        {
            _lastDrawn = null;
            _lastDrawSource = DrawSource.Normal;
        }
    }

    /// <summary>
    /// 自摸结算：第 1 步按单链优先级取一个承包者（杠上开花承包 &gt; 12 张落地承包 &gt; 常规自摸），
    /// 七对走 <see cref="ScoreManager.ApplySevenPairsSelfDrawn"/>（不受其他家杠位影响）。
    /// 承包只重定向 payer，赢家金额不变；非承包的普通自摸仍按「本回合最近一次摸牌是杠后补牌」置
    /// <see cref="ScoreActionType.KongUpFlower"/>（补杠/暗杠补牌也算，但不承包）。
    /// 结算顺序固定五步（T-2 §4）：①胡牌金额 → ②跟庄 → ③连庄奖励 → ④中马（T-4）→
    /// ⑤取样 delta/actionType → Release → 写回镜像 → 产 RoundEnded；返回下一局庄家。
    /// </summary>
    /// <param name="roundNo">局号 1–8。</param>
    /// <param name="bankerSeat">本局庄家座位 0–3，也是跟庄的付款方。</param>
    /// <param name="winnerSeat">胡牌座位 0–3。</param>
    /// <param name="dealerStreak">本局连庄次数，用于查连庄奖励表。</param>
    private byte CompleteSelfDrawnWin(byte roundNo, byte bankerSeat, byte winnerSeat, byte dealerStreak)
    {
        PlayerState winner = _state.Players[winnerSeat];
        bool sevenPairs = _wins.IsSevenPairs(winner.Hand.Items, _joker);
        bool standard = _wins.IsWin(winner.Hand.Items, winner.Melds, _joker) && !sevenPairs;
        if (!sevenPairs && !standard)
        {
            throw new InvalidOperationException("Win 请求结算时手牌既非七对也非常规胡。");
        }

        // ① 胡牌金额：单链优先级，命中即返回，一次胡牌只取一个承包者。
        bool hasJoker = _jokerActive && ContainsTileType(winner.Hand.Items, _joker.Id);
        bool kongUpFlower = _lastDrawSource != DrawSource.Normal;
        if (sevenPairs)
        {
            _scores.ApplySevenPairsSelfDrawn(winnerSeat, hasJoker);
        }
        else if (TryGetKongUpFlowerPayer(winner, out byte kongUpFlowerPayer))
        {
            _scores.ApplyKongUpFlowerContract(
                winnerSeat,
                kongUpFlowerPayer,
                BaseScorePer(hasJoker, sevenPairs: false),
                hasJoker,
                sevenPairs: false);
        }
        else if (TryGetTwelveCardsPayer(winner, out byte twelveCardsPayer))
        {
            _scores.ApplyTwelveCardsContract(
                winnerSeat,
                twelveCardsPayer,
                BaseScorePer(hasJoker, sevenPairs: false),
                hasJoker);
        }
        else
        {
            _scores.ApplySelfDrawn(winnerSeat, hasJoker, sevenPairs: false, kongUpFlower);
        }

        return SettleWinTail(roundNo, bankerSeat, winnerSeat, dealerStreak);
    }

    /// <summary>
    /// 自摸与抢杠共用的结算尾段（T-2 §4 第 2–5 步）：②跟庄 → ③连庄奖励 → ④中马 →
    /// ⑤取样 delta/actionType → Release → 写回镜像 → 产 RoundEnded；返回下一局庄家（= 赢家）。
    /// 中马步：墙头取一张进 <see cref="GameState.Revealed"/>，同 tick 产 <see cref="SessionEventKind.HorseRevealed"/>；
    /// 墙空或无变体时整步跳过；事件恒在 RoundEnded 之前、同 tick。
    /// </summary>
    private byte SettleWinTail(byte roundNo, byte bankerSeat, byte winnerSeat, byte dealerStreak)
    {
        // ② 跟庄：赢家不是庄家且变体开启才付，庄家当场付给赢家，每局最多一次；抢杠胡同样算。
        if (winnerSeat != bankerSeat && _followingBanker > 0)
        {
            _scores.ApplyFollowingTheBanker(winnerSeat, bankerSeat, _followingBanker);
        }

        // ③ 连庄奖励：赢家就是庄家且本局奖励金额大于 0 才付；第 8 局照常；抢杠胡同样算庄家胡。
        if (winnerSeat == bankerSeat)
        {
            int bonus = DealerStreakBonus(dealerStreak);
            if (bonus > 0)
            {
                _scores.ApplyDealerStreak(bankerSeat, bonus);
            }
        }

        // ④ 中马：变体开启才翻；取牌前墙空则整步跳过（无事件、无支付、无区域变化）。
        // 翻出的马牌从墙头取出并入公共揭示区，马座位 = 物理 id % 4；未命中不支付但仍产事件。
        if (_horseBonus > 0 && _state.TryRevealHorse(out Tile horse))
        {
            byte horseSeat = (byte)(horse.Id % GameState.PlayerCount);
            _events.EmitHorseRevealed(_scheduler.Tick, horseSeat, horse.Id);
            if (horseSeat == winnerSeat)
            {
                _scores.ApplyHorseBonus(winnerSeat, _horseBonus);
            }
        }

        // ⑤ 收尾：D10 的结算 payload 统一经由公开的 RoundResult 构造，事件只从该快照取字段。
        short[] delta = CopyScores(_scores.RoundDelta);
        ScoreActionType actionType = _scores.RoundActionType;
        _scores.Release();
        WriteBackScores();
        RoundResult result = new(roundNo, bankerSeat, winnerSeat, actionType, delta, CopyScores(_scores.TotalScores));
        _events.EmitRoundEnded(_scheduler.Tick, (sbyte)result.WinnerSeat, result.ScoreDelta, result.ActionType);
        return result.WinnerSeat;
    }

    /// <summary>每份基分：无鬼 4 / 有鬼 2，七对 ×2；三条承包与常规自摸共用同一口径。</summary>
    private static int BaseScorePer(bool hasJoker, bool sevenPairs) =>
        (hasJoker ? 2 : 4) * (sevenPairs ? 2 : 1);

    /// <summary>
    /// 杠上开花承包的 payer：变体开启、本回合最近一次摸牌来自明杠补牌，且该明杠有目标座位（放杠者）。
    /// 只认明杠（<see cref="MeldKind.KongExposed"/>）：补杠/暗杠补牌自摸只置开花位、不承包。
    /// 命中时返回赢家最后一条明杠的 <see cref="Meld.TargetSeat"/>。
    /// </summary>
    private bool TryGetKongUpFlowerPayer(PlayerState winner, out byte payerSeat)
    {
        payerSeat = Meld.NoTargetSeat;
        if (!_kongFlowerContract || _lastDrawSource != DrawSource.KongExposed)
        {
            return false;
        }

        for (int index = winner.Melds.Count - 1; index >= 0; index--)
        {
            Meld meld = winner.Melds[index];
            if (meld.Kind != MeldKind.KongExposed)
            {
                continue;
            }

            if (meld.TargetSeat >= GameState.PlayerCount)
            {
                return false;
            }

            payerSeat = meld.TargetSeat;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 12 张落地承包的 payer：变体开启、赢家恰 4 副露且全明（暗杠 <see cref="Meld.Opened"/> 为 false，直接排除）、
    /// 第 4 副露（下标 3）有供牌者。补杠保留原碰的 <see cref="Meld.TargetSeat"/>，因此自动取原放牌者。
    /// </summary>
    private bool TryGetTwelveCardsPayer(PlayerState winner, out byte payerSeat)
    {
        payerSeat = Meld.NoTargetSeat;
        if (!_twelveCardsContract || winner.Melds.Count != GameState.PlayerCount)
        {
            return false;
        }

        foreach (Meld meld in winner.Melds)
        {
            if (!meld.Opened)
            {
                return false;
            }
        }

        Meld fourth = winner.Melds[GameState.PlayerCount - 1];
        if (fourth.TargetSeat >= GameState.PlayerCount)
        {
            return false;
        }

        payerSeat = fourth.TargetSeat;
        return true;
    }

    /// <summary>
    /// 本局连庄奖励金额：<c>dealerStreakBonus[min(streak, 表长) - 1]</c>；表为空（变体关闭）时为 0。
    /// </summary>
    private int DealerStreakBonus(byte dealerStreak)
    {
        int count = _dealerStreakBonus.Count;
        if (count == 0)
        {
            return 0;
        }

        int index = Math.Min(dealerStreak, count) - 1;
        return index < 0 ? 0 : _dealerStreakBonus[index];
    }

    /// <summary>流局结算：回滚本局暂存再 Release（净 0），写回镜像，产 RoundEnded(-1, 全 0, None)。庄家与连庄次数都不变。</summary>
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
