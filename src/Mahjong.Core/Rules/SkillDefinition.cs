namespace Mahjong.Core.Rules;

/// <summary>技能定义，来自 <c>content/skills.json</c>；<see cref="Id"/> 是协议里的技能编号，与 <see cref="SkillId"/> 的数值一致。</summary>
/// <param name="Id">技能编号 1–4。</param>
/// <param name="Name">技能名；与内容固定清单一致。</param>
public readonly record struct SkillDefinition(int Id, string Name);
