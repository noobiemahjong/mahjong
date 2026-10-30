using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 技能协议常量：<see cref="SkillId"/> 是无符号底层类型，数值固定为 1–4（发布后不得改动）；
/// 该数值同时用于响应窗口应答负载与事件流编码，因此单列测试钉住字面量。
/// </summary>
public sealed class SkillIdTests
{
    [Fact]
    public void Values_AreFrozenProtocolConstants()
    {
        Assert.Equal(1u, (uint)SkillId.Pong);
        Assert.Equal(2u, (uint)SkillId.Kong);
        Assert.Equal(3u, (uint)SkillId.SelfDrawn);
        Assert.Equal(4u, (uint)SkillId.SelfDrawnWith7Pairs);
    }

    [Fact]
    public void UnderlyingType_IsUInt32()
    {
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(SkillId)));
    }
}
