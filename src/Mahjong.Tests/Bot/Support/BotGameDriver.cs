using Mahjong.Core.Bot;
using Mahjong.Core.Rules;

namespace Mahjong.Tests.Bot;

/// <summary>单次 Bot 弃牌决策的记录：座位、决策、解析到的手牌槽位与是否经校验合法。</summary>
/// <param name="Seat">决策座位。</param>
/// <param name="Decision">策略返回的决策。</param>
/// <param name="HandSlot">决策牌在手牌中的槽位；非出牌为 -1。</param>
/// <param name="Legal">决策是否经真实校验器判定为合法出牌。</param>
internal readonly record struct BotTurn(byte Seat, BotDecision Decision, int HandSlot, bool Legal);

/// <summary>一次单局驱动的结果：终局局面、逐回合记录与每回合后的物理牌总数。</summary>
internal sealed class BotGameRun
{
    /// <summary>被驱动的局面（终局或首次非法前的状态）。</summary>
    public required GameState State { get; init; }

    /// <summary>按执行顺序记录的回合。</summary>
    public List<BotTurn> Turns { get; } = [];

    /// <summary>每回合出牌后的物理牌总数，用于断言全程 136 张守恒。</summary>
    public List<int> TileCountsAfterTurn { get; } = [];

    /// <summary>首个非法回合；全部合法时为 null。</summary>
    public BotTurn? IllegalTurn { get; set; }

    /// <summary>是否走到牌墙为空（正常终局）。</summary>
    public bool Completed { get; set; }
}

/// <summary>
/// 测试本地单局驱动：发牌 → 庄家摸 1 张 → 每回合让策略只出牌（技能列表为空）并立即弃牌，直到牌墙为空。
/// 不引入会话层；同一 seed 与同一策略实现得到同一弃牌序列与终局哈希。
/// </summary>
internal static class BotGameDriver
{
    /// <summary>驱动一整局，返回逐回合记录与终局局面。</summary>
    /// <param name="seed">洗牌种子；同 seed 同牌序。</param>
    /// <param name="policy">被测策略。</param>
    /// <param name="verifier">真实校验器，用于独立判定每次出牌。</param>
    public static BotGameRun RunDiscardOnlyGame(ulong seed, SimpleBotPolicy policy, ICardAndSkillVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(verifier);

        var run = new BotGameRun { State = new GameState() };
        GameState state = run.State;
        state.Deal(new Pcg32(seed), bankerSeat: 0);
        state.DrawFromWall(0);

        IRandomSource random = new Pcg32(seed);
        SkillId[] noSkills = [];

        while (true)
        {
            byte seat = state.CurrentSeat;
            PlayerState player = state.Players[seat];
            var context = new BotContext(state, player, noSkills, random);
            BotDecision decision = policy.Decide(context);

            int handSlot = -1;
            bool legal = false;
            if (decision.Skill is null && decision.Card is Tile card)
            {
                handSlot = player.Hand.IndexOf(card);
                legal = handSlot >= 0 && verifier.Verify(state, player, null, card, null);
            }

            run.Turns.Add(new BotTurn(seat, decision, handSlot, legal));
            if (!legal)
            {
                run.IllegalTurn = new BotTurn(seat, decision, handSlot, legal);
                return run;
            }

            state.Discard(seat, handSlot);
            run.TileCountsAfterTurn.Add(BotTestSupport.TotalTiles(state));

            if (state.IsWallEmpty)
            {
                run.Completed = true;
                return run;
            }

            byte next = (byte)((seat + 1) % GameState.PlayerCount);
            state.CurrentSeat = next;
            state.DrawFromWall(next);
        }
    }
}
