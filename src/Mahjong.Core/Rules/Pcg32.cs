using System.Numerics;

namespace Mahjong.Core.Rules;

/// <summary>
/// PCG32 参考实现：状态保存在实例中，同 seed/stream 与调用序列得到同一序列。
/// 实例非线程安全；规则层不寻址全局 RNG。
/// </summary>
public sealed class Pcg32 : IRandomSource
{
    /// <summary>PCG32 线性同余状态的乘数（参考算法常量）。</summary>
    private const ulong Multiplier = 6364136223846793005UL;

    private readonly ulong _increment;
    private ulong _state;

    /// <summary>按参考算法初始化：inc=(stream&lt;&lt;1)|1，state=0，先 step 一次，state+=seed，再 step 一次。</summary>
    /// <param name="seed">随机种子。</param>
    /// <param name="stream">序列号；不同 stream 得到不同序列。</param>
    public Pcg32(ulong seed, ulong stream = 1)
    {
        _increment = (stream << 1) | 1;
        _state = 0;
        Step();
        _state += seed;
        Step();
    }

    /// <summary>返回下一个 32 位无符号随机数，并推进一步内部状态。</summary>
    /// <returns>下一次随机值。</returns>
    public uint NextUInt()
    {
        ulong old = _state;
        Step();
        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return BitOperations.RotateRight(xorshifted, rotation);
    }

    private void Step() => _state = (_state * Multiplier) + _increment;
}
