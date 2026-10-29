namespace Mahjong.Core.Rules;

/// <summary>
/// 规则层使用的确定性随机源：只暴露 32 位无符号随机数，种子由调用方显式持有。
/// 实现必须只依赖实例状态（不读墙钟、不访问全局 RNG）；同一种子与调用序列必须得到同一结果。
/// </summary>
public interface IRandomSource
{
    /// <summary>返回下一个 32 位无符号随机数；实现按调用顺序推进内部状态。</summary>
    /// <returns>下一次随机值。</returns>
    uint NextUInt();
}
