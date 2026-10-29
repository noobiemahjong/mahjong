namespace Mahjong.Core.Rules;

/// <summary>鬼牌来源。数值是内容与协议的一部分，已发布后不得改动。</summary>
public enum JokerMode : byte
{
    /// <summary>本局没有鬼牌。</summary>
    None = 0,

    /// <summary>鬼牌由 <c>content/rules.json</c> 的 <c>joker.typeId</c> 固定指定。</summary>
    FixedType = 1,

    /// <summary>鬼牌由牌堆翻开的指示牌决定；内容格式 v1 只定义该取值，加载时拒绝。</summary>
    FlipIndicator = 2,
}

/// <summary>当前一局的鬼牌规则；<see cref="Mode"/> 为 <see cref="JokerMode.FixedType"/> 时 <see cref="Type"/> 必非空。</summary>
public readonly record struct JokerRule(JokerMode Mode, TileType? Type);
