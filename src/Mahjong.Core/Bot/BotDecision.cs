using Mahjong.Core.Rules;

namespace Mahjong.Core.Bot;

/// <summary>
/// 一次 Bot 决策：技能动作（<c>Skill</c> 非 null）或出牌（<c>Skill</c> 为 null 且 <c>Card</c> 非 null）。
/// 两者皆 null 表示「过」，只在弃牌响应窗口合法。本类型只承载选择，不校验合法性。
/// </summary>
/// <param name="Skill">技能编号；出牌与「过」为 null。技能动作不携带目标弃牌，目标弃牌由 <see cref="BotContext.TargetCard"/> 提供。</param>
/// <param name="Card">要打出的物理牌；技能动作与「过」为 null。</param>
public readonly record struct BotDecision(SkillId? Skill, Tile? Card);
