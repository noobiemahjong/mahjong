namespace Mahjong.Core.Rules;

/// <summary>技能，来自 <c>content/skills.json</c>；<paramref name="Id"/> 是协议里的技能编号。</summary>
public readonly record struct Skill(int Id, string Name);
