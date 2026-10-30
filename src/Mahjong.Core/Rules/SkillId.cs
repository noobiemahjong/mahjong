namespace Mahjong.Core.Rules;

/// <summary>
/// 技能动作编号。数值是协议与回放编码的一部分，已发布后不得改动。
/// 与内容 <c>content/skills.json</c> 的 <see cref="SkillDefinition.Id"/> 一一对应。
/// </summary>
public enum SkillId : uint
{
    /// <summary>碰。</summary>
    Pong = 1,

    /// <summary>杠；明杠、暗杠、补杠由 <see cref="KongSkill"/> 按上下文自行判定。</summary>
    Kong = 2,

    /// <summary>自摸（常规胡，不含七对）。</summary>
    SelfDrawn = 3,

    /// <summary>七对自摸；优先于普通自摸。</summary>
    SelfDrawnWith7Pairs = 4,
}
