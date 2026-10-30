namespace Mahjong.Core.Rules;

/// <summary>
/// 8 局轮次驱动：起庄、每局洗牌发牌、按脚本命令推进回合并结算、庄家轮换与排名。
///
/// 契约要点：
/// <list type="bullet">
/// <item>第 1 局庄家在首次 <see cref="RunRound"/> 开始时用注入 RNG 抽取（<c>NextUInt() % 4</c>），
/// 构造不消耗 RNG；此后由轮换规则决定。</item>
/// <item>每局先 <see cref="GameState.Deal"/>（牌墙 84、四家 13、当前座位=庄家、仅庄家 IsBanker），
/// 再让庄家从墙头摸第 14 张（牌墙 83）。</item>
/// <item>命令按序消费，命令的座位必须等于当前座位；出牌用 <see cref="CardAndSkillVerifier"/> 校验，
/// 自摸按「七对优先」判定并结算。非法命令抛异常且不应用该步。</item>
/// <item>命令耗尽后，本驱动用「当前手牌中第一个非鬼牌槽位」确定性补全出牌；该默认策略只用于无会话的
/// 测试与模拟，不是 T-5 的真实玩家策略。</item>
/// <item>每次出牌后下家从墙头摸牌；摸牌前牌墙为空即流局，本局暂存回滚，总分不变。
/// 流局在下家摸牌与座位交接之前返回，因此局面的 <see cref="GameState.CurrentSeat"/> 仍是最后出牌者，
/// <see cref="PlayerState.IsBanker"/> 仍是本局 <see cref="GameState.Deal"/> 写入的庄家标记。</item>
/// <item>唯一权威总分是 <see cref="Scores"/>；<see cref="PlayerState.Score"/> 只是每局结算后写入的镜像，
/// 保证 <c>StateHash</c> 反映权威总分。</item>
/// <item>赢家非庄则赢家下局坐庄，庄家自摸或流局则庄家不变；第 8 局后 <see cref="IsGameOver"/> 为 true。</item>
/// </list>
///
/// 异常边界：<see cref="ArgumentException"/> 表示调用方脚本非法（座位不匹配等）；
/// <see cref="InvalidOperationException"/> 表示当前局面下该动作非法（槽位越界、鬼牌出牌、非胡手牌等）。
/// 单条命令/单步原子：失败步不应用、不改该步状态；但 <see cref="RunRound"/> 不保证整局事务性，
/// 异常前已经发生的回合不回滚。
///
/// 事件（RoundStarted/TurnStarted/RoundEnded/GameEnded）归 T-5，本类型只产出 <see cref="RoundResult"/>
/// 作为 payload 来源。杠分与杠上开花同样归 T-5：会话在杠 Commit 后调用
/// <see cref="ScoreManager.ApplyKong"/>，自摸时按局内杠位传 <c>kongUpFlower</c>；本驱动不产生杠，
/// 因此自摸结算恒传 <c>kongUpFlower: false</c>。
///
/// 不读时钟、不做 IO；实例非线程安全。
/// </summary>
public sealed class RoundController
{
    /// <summary>一场固定局数。</summary>
    public const byte TotalRounds = 8;

    private readonly IWinChecker _wins;
    private readonly IRandomSource _rng;
    private readonly TileType _joker;
    private readonly CardAndSkillVerifier _verifier;

    private byte _roundNo = 1;
    private byte _bankerSeat;

    /// <summary>创建轮次驱动；不消耗 <paramref name="rng"/>，第 1 局庄家留到首次 <see cref="RunRound"/> 决定。</summary>
    /// <param name="wins">胡牌判定器；判定期只读。</param>
    /// <param name="rng">随机源；用于第 1 局起庄与每局洗牌。</param>
    /// <param name="joker">当前局鬼牌牌种；<c>default(TileType)</c> 表示无鬼。构造时注入后整场不变。</param>
    /// <exception cref="ArgumentNullException"><paramref name="wins"/> 或 <paramref name="rng"/> 为 null。</exception>
    public RoundController(IWinChecker wins, IRandomSource rng, TileType joker = default)
    {
        ArgumentNullException.ThrowIfNull(wins);
        ArgumentNullException.ThrowIfNull(rng);

        _wins = wins;
        _rng = rng;
        _joker = joker;
        _verifier = new CardAndSkillVerifier(wins, joker);
    }

    /// <summary>下一次 <see cref="RunRound"/> 的局号：1–8；9 表示 8 局已打完。</summary>
    public byte RoundNo => _roundNo;

    /// <summary>
    /// 当前/下一局庄家。第 1 局 <see cref="RunRound"/> 之前尚无真值（占位 0），
    /// 首次 <see cref="RunRound"/> 开始时才由 <c>rng.NextUInt() % 4</c> 确定；
    /// 之后为下一局坐庄的座位（赢家非庄则换庄，庄家胡或流局则不变）。
    /// </summary>
    public byte BankerSeat => _bankerSeat;

    /// <summary>8 局是否已打完：<see cref="RoundNo"/> 大于 <see cref="TotalRounds"/>。</summary>
    public bool IsGameOver => _roundNo > TotalRounds;

    /// <summary>本场计分账本；唯一权威总分，由本控制器独占持有。</summary>
    public ScoreManager Scores { get; } = new();

    /// <summary>
    /// 跑一局：起庄（仅第 1 局）、发牌、庄家摸第 14 张、按命令推进回合并结算，最后写回总分与轮换庄家。
    /// 命令按序消费；命令耗尽后按「第一个非鬼牌槽位」确定性出牌，直到有人自摸或牌墙耗尽流局。
    /// 赢家局在 Win 命令处结束，其后剩余命令不再消费、也不再校验。
    /// 本方法假设 <see cref="Scores"/> 没有跨局暂存：控制器每局结束必 <see cref="ScoreManager.Release"/>
    /// 或 <see cref="ScoreManager.RollbackRound"/>，调用方不应预置暂存。
    /// </summary>
    /// <param name="state">权威局面；本局会就地对它发牌与推进。不可为 null。</param>
    /// <param name="commands">本局脚本命令；空列表表示全部走默认出牌。不可为 null。</param>
    /// <returns>本局结果；赢家局含结算后的分数快照，流局局为全 0。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> 或 <paramref name="commands"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">8 局已打完仍调用；或当前局面下命令动作非法（槽位越界、鬼牌出牌、非胡手牌、手牌全是鬼牌无法默认出牌）；失败步不应用。</exception>
    /// <exception cref="ArgumentException">命令座位不等于当前座位；该命令不应用。</exception>
    /// <exception cref="ArgumentOutOfRangeException">命令种类未定义；该命令不应用。</exception>
    public RoundResult RunRound(GameState state, IReadOnlyList<RoundCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(commands);

        if (IsGameOver)
        {
            throw new InvalidOperationException(
                $"The game is over: {TotalRounds} rounds have been played.");
        }

        // 起庄只发生在第 1 局，且在开局时消耗一次 RNG：构造不消耗，保证同 seed 的序列从首局开始可复现。
        if (_roundNo == 1)
        {
            _bankerSeat = (byte)(_rng.NextUInt() % GameState.PlayerCount);
        }

        byte roundNo = _roundNo;
        byte roundBanker = _bankerSeat;

        state.RoundNo = roundNo;
        state.Deal(_rng, roundBanker);
        state.DrawFromWall(roundBanker);

        int nextCommand = 0;
        while (true)
        {
            byte seat = state.CurrentSeat;
            if (nextCommand < commands.Count)
            {
                RoundCommand command = commands[nextCommand];
                nextCommand++;

                if (command.Seat != seat)
                {
                    throw new ArgumentException(
                        $"Command targets seat {command.Seat}, but the current seat is {seat}.",
                        nameof(commands));
                }

                switch (command.Kind)
                {
                    case RoundCommandKind.Win:
                        return CompleteWin(state, roundNo, roundBanker, seat);

                    case RoundCommandKind.Discard:
                        DiscardByCommand(state, seat, command.Slot);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(commands),
                            command.Kind,
                            "Command kind must be Discard or Win.");
                }
            }
            else
            {
                state.Discard(seat, FindDefaultDiscardSlot(state.Players[seat]));
            }

            byte nextSeat = (byte)((seat + 1) % GameState.PlayerCount);
            if (state.Wall.Count == 0)
            {
                return CompleteDraw(state, roundNo, roundBanker);
            }

            state.CurrentSeat = nextSeat;
            state.DrawFromWall(nextSeat);
        }
    }

    /// <summary>
    /// 按总分排名：降序，同分按座位号升序。返回长度 4 的只读列表，是纯函数，随时可调（不要求 8 局已打完）。
    /// </summary>
    /// <returns>4 个座位，按名次从高到低排列。</returns>
    public IReadOnlyList<byte> Rank()
    {
        byte[] seats = new byte[GameState.PlayerCount];
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            seats[seat] = seat;
        }

        IReadOnlyList<short> totals = Scores.TotalScores;
        Array.Sort(seats, (left, right) =>
        {
            int byScore = totals[right].CompareTo(totals[left]);
            return byScore != 0 ? byScore : left.CompareTo(right);
        });

        return Array.AsReadOnly(seats);
    }

    /// <summary>
    /// 自摸结算：七对优先（<see cref="IWinChecker.IsSevenPairs"/> 成立即按七对，不再按常规胡），
    /// 两种口径都不成立抛 <see cref="InvalidOperationException"/> 且不改状态。
    /// 结算前取本局 delta 与番种快照，再 <see cref="ScoreManager.Release"/> 并入总分、写回镜像、轮换庄家、局号加一。
    /// </summary>
    private RoundResult CompleteWin(GameState state, byte roundNo, byte roundBanker, byte winnerSeat)
    {
        PlayerState winner = state.Players[winnerSeat];
        bool sevenPairs = _wins.IsSevenPairs(winner.Hand.Items, _joker);
        bool standard = _wins.IsWin(winner.Hand.Items, winner.Melds, _joker) && !sevenPairs;
        if (!sevenPairs && !standard)
        {
            throw new InvalidOperationException(
                $"Seat {winnerSeat} cannot win: the hand is neither a regular winning hand nor seven pairs.");
        }

        bool hasJoker = IsJokerActive && ContainsTileType(winner.Hand.Items, _joker.Id);
        Scores.ApplySelfDrawn(winnerSeat, hasJoker, sevenPairs, kongUpFlower: false);

        // Release 会清零暂存与番种聚合，所以结果快照必须在 Release 之前取样。
        short[] scoreDelta = CopyScores(Scores.RoundDelta);
        ScoreActionType actionType = Scores.RoundActionType;
        Scores.Release();
        WriteBackScores(state);

        // 赢家非庄则换庄；赢家为庄则仍是同一座位（连庄）。
        _bankerSeat = winnerSeat;
        _roundNo++;

        return new RoundResult(
            roundNo,
            roundBanker,
            winnerSeat,
            actionType,
            scoreDelta,
            CopyScores(Scores.TotalScores));
    }

    /// <summary>
    /// 流局结算：本局暂存先回滚再并入（净 0），总分与庄家不变，局号加一；结果 delta 全 0、番种 None。
    /// </summary>
    private RoundResult CompleteDraw(GameState state, byte roundNo, byte roundBanker)
    {
        Scores.RollbackRound();
        Scores.Release();
        WriteBackScores(state);

        _roundNo++;

        return new RoundResult(
            roundNo,
            roundBanker,
            RoundResult.NoWinnerSeat,
            ScoreActionType.None,
            new short[GameState.PlayerCount],
            CopyScores(Scores.TotalScores));
    }

    /// <summary>
    /// 按脚本出牌：槽位越界或 <see cref="CardAndSkillVerifier"/> 判定不合法（非当前座位、折算张数不是 14、
    /// 牌不在手中、鬼牌）抛 <see cref="InvalidOperationException"/>，不应用该步。
    /// </summary>
    private void DiscardByCommand(GameState state, byte seat, int slot)
    {
        PlayerState player = state.Players[seat];
        if ((uint)slot >= (uint)player.Hand.Count)
        {
            throw new InvalidOperationException(
                $"Hand slot {slot} is out of range 0-{player.Hand.Count - 1} for seat {seat}.");
        }

        Tile tile = player.Hand[slot];
        if (!_verifier.Verify(state, player, skill: null, card: tile, targetCard: null))
        {
            throw new InvalidOperationException(
                $"Seat {seat} cannot discard hand slot {slot}: the discard is not legal in the current state.");
        }

        state.Discard(seat, slot);
    }

    /// <summary>默认出牌槽位：手牌中第一个非鬼牌槽位；手牌全是鬼牌时抛 <see cref="InvalidOperationException"/>。</summary>
    private int FindDefaultDiscardSlot(PlayerState player)
    {
        IReadOnlyList<Tile> hand = player.Hand.Items;
        for (int slot = 0; slot < hand.Count; slot++)
        {
            if (!IsJoker(hand[slot]))
            {
                return slot;
            }
        }

        throw new InvalidOperationException(
            $"Seat {player.Seat} has no non-joker tile to discard by default.");
    }

    /// <summary>把权威总分写回 <see cref="PlayerState.Score"/> 镜像。</summary>
    private void WriteBackScores(GameState state)
    {
        IReadOnlyList<short> totals = Scores.TotalScores;
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            state.Players[seat].Score = totals[seat];
        }
    }

    /// <summary>鬼牌是否生效：与技能/校验器同口径，花色落在 1–5 表示有鬼，<c>default(TileType)</c> 的花色 0 表示无鬼。</summary>
    private bool IsJokerActive =>
        (byte)_joker.Suit >= (byte)SuitType.Character && (byte)_joker.Suit <= (byte)SuitType.Dragon;

    /// <summary>该物理牌是否是当前鬼牌；无鬼时恒为 false，不把真实牌当鬼。</summary>
    private bool IsJoker(Tile tile) => IsJokerActive && tile.Type.Id == _joker.Id;

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

    /// <summary>把账本的只读分数序列复制为独立数组，供 <see cref="RoundResult"/> 做快照。</summary>
    private static short[] CopyScores(IReadOnlyList<short> scores)
    {
        short[] copy = new short[GameState.PlayerCount];
        for (int seat = 0; seat < GameState.PlayerCount; seat++)
        {
            copy[seat] = scores[seat];
        }

        return copy;
    }
}
