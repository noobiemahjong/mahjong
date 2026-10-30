namespace Mahjong.Core.Rules;

/// <summary>
/// 动作二次校验契约：服务端/本地会话对每个提交动作再校验一次，失败不改变局面。
/// 实现不得读时钟、不得做 IO、不得持有跨调用可变状态。
/// </summary>
public interface ICardAndSkillVerifier
{
    /// <summary>
    /// 校验一次提交动作是否合法：出牌（<paramref name="skill"/> 为 null）或技能动作。
    /// </summary>
    /// <param name="state">权威局面；只读。</param>
    /// <param name="player">提交动作的玩家；必须是 <paramref name="state"/> 中同座位的同一引用。</param>
    /// <param name="skill">技能编号；出牌传 null。</param>
    /// <param name="card">出牌时是打出的牌；技能动作时作为刚摸到的牌（可为 null）。</param>
    /// <param name="targetCard">弃牌响应时的目标弃牌；无目标为 null。</param>
    /// <returns>合法返回 true；其余非法输入返回 false。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> 或 <paramref name="player"/> 为 null。</exception>
    bool Verify(GameState state, PlayerState player, SkillId? skill, Tile? card, Tile? targetCard);
}
