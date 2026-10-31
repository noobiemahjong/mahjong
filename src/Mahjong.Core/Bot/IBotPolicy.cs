namespace Mahjong.Core.Bot;

/// <summary>
/// Bot 决策策略契约：只读上下文给出一个合法选择，不改变局面。
/// 实现不得读时钟、不得 sleep、不得做 IO、不得持有跨调用可变状态。
/// </summary>
public interface IBotPolicy
{
    /// <summary>
    /// 依据上下文给出决策。
    /// </summary>
    /// <param name="context">决策上下文；只读。</param>
    /// <returns>技能动作、出牌或「过」；具体语义见 <see cref="BotDecision"/>。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">
    /// 非自己回合且没有目标弃牌（不属于任何合法决策语境），或自己回合没有任何合法出牌；
    /// 弃牌响应窗口没有合法技能时返回「过」，不抛异常。
    /// </exception>
    BotDecision Decide(BotContext context);
}
