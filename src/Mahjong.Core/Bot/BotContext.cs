using Mahjong.Core.Rules;

namespace Mahjong.Core.Bot;

/// <summary>
/// 一次 Bot 决策的只读上下文：权威局面、Bot 自身、可选技能、随机源与可选的弃牌响应目标/刚摸到的牌。
/// 构造即校验，实例创建后不可变；策略只读它、不改局面，M0.2 策略不消费随机（M4 策略可按需使用）。
/// </summary>
public sealed class BotContext
{
    /// <summary>创建 Bot 上下文。</summary>
    /// <param name="state">权威局面；只读。</param>
    /// <param name="self">做决策的玩家；必须是 <paramref name="state"/> 中同座位的同一引用。</param>
    /// <param name="availableSkills">本回合可供选择的技能；每个元素必须是已定义 <see cref="SkillId"/> 且不重复，顺序无语义。</param>
    /// <param name="random">随机源；M0.2 策略不消费它，保留给后续候选 tie-break。</param>
    /// <param name="targetCard">弃牌响应窗口的目标弃牌；无目标为 null。</param>
    /// <param name="lastDrawn">自己回合刚摸到的牌；无提示为 null。</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/>、<paramref name="self"/>、<paramref name="availableSkills"/> 或 <paramref name="random"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="self"/> 不是 <paramref name="state"/> 中同座位玩家的同一引用；<paramref name="availableSkills"/> 含未定义技能或重复技能。</exception>
    public BotContext(
        GameState state,
        PlayerState self,
        IReadOnlyList<SkillId> availableSkills,
        IRandomSource random,
        Tile? targetCard = null,
        Tile? lastDrawn = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(availableSkills);
        ArgumentNullException.ThrowIfNull(random);

        if (!ReferenceEquals(state.Players[self.Seat], self))
        {
            throw new ArgumentException(
                "The self player must be the same instance as state.Players[self.Seat].",
                nameof(self));
        }

        for (int index = 0; index < availableSkills.Count; index++)
        {
            SkillId skill = availableSkills[index];
            if (!IsDefinedSkill(skill))
            {
                throw new ArgumentException(
                    $"The skill list contains an undefined skill id {(uint)skill}.",
                    nameof(availableSkills));
            }

            for (int earlier = 0; earlier < index; earlier++)
            {
                if (availableSkills[earlier] == skill)
                {
                    throw new ArgumentException(
                        $"The skill list contains duplicate skill id {(uint)skill}.",
                        nameof(availableSkills));
                }
            }
        }

        State = state;
        Self = self;
        TargetCard = targetCard;
        LastDrawn = lastDrawn;
        Random = random;

        // 复制成只读快照：调用方之后修改自己的列表不会改变已建上下文的决策输入。
        AvailableSkills = Array.AsReadOnly(availableSkills.ToArray());
    }

    /// <summary>权威局面；策略只读。</summary>
    public GameState State { get; }

    /// <summary>做决策的玩家；与 <see cref="State"/> 中同座位玩家同一引用。</summary>
    public PlayerState Self { get; }

    /// <summary>弃牌响应窗口的目标弃牌；无目标为 null。</summary>
    public Tile? TargetCard { get; }

    /// <summary>自己回合刚摸到的牌；无提示为 null。</summary>
    public Tile? LastDrawn { get; }

    /// <summary><see cref="Self"/> 手牌的实时只读视图；不复制、不排序，后续对局面的手牌变更立即可见。</summary>
    public IReadOnlyList<Tile> Hand => Self.Hand.Items;

    /// <summary>本回合可供选择的技能；构造时的只读快照，顺序无语义。</summary>
    public IReadOnlyList<SkillId> AvailableSkills { get; }

    /// <summary>随机源；M0.2 策略不消费它。</summary>
    public IRandomSource Random { get; }

    /// <summary>技能是否已定义：只有碰、杠、自摸、七对自摸四个编号。</summary>
    private static bool IsDefinedSkill(SkillId skill) => skill switch
    {
        SkillId.Pong or SkillId.Kong or SkillId.SelfDrawn or SkillId.SelfDrawnWith7Pairs => true,
        _ => false,
    };
}
