using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// <see cref="SkillContext"/> 构造契约：state/source 非空；source 必须与 state.Players[source.Seat] 同一引用；
/// TargetSeat 只允许 0–3 或 <see cref="Meld.NoTargetSeat"/>；其余属性按构造入参原样暴露（TargetSeat 是独立入参）。
/// </summary>
public sealed class SkillContextTests
{
    [Fact]
    public void Constructor_RejectsNullState()
    {
        GameState state = SkillTestData.NewState();

        Assert.Throws<ArgumentNullException>(
            () => new SkillContext(null!, state.Players[0]));
    }

    [Fact]
    public void Constructor_RejectsNullSource()
    {
        GameState state = SkillTestData.NewState();

        Assert.Throws<ArgumentNullException>(() => new SkillContext(state, null!));
    }

    [Fact]
    public void Constructor_RejectsSourceThatIsNotTheSameReferenceInStatePlayers()
    {
        GameState state = SkillTestData.NewState();
        var lookalike = new PlayerState(0);

        // 座位号相同但不是同一引用：避免把「同座位号」当作「同一玩家」。
        Assert.Throws<ArgumentException>(() => new SkillContext(state, lookalike));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(200)]
    [InlineData(254)]
    public void Constructor_RejectsTargetSeatOutOfRange(byte targetSeat)
    {
        GameState state = SkillTestData.NewState();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SkillTestData.Context(state, 0, targetSeat: targetSeat));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(Meld.NoTargetSeat)]
    public void Constructor_AcceptsSeatsZeroToThreeAndNoTargetSeat(byte targetSeat)
    {
        GameState state = SkillTestData.NewState();

        SkillContext context = SkillTestData.Context(state, 0, targetSeat: targetSeat);

        Assert.Equal(targetSeat, context.TargetSeat);
    }

    [Fact]
    public void Properties_ExposeConstructorArguments()
    {
        GameState state = SkillTestData.NewState();
        Tile targetCard = SkillTestData.T(5, 2);
        Tile drawnTile = SkillTestData.T(7, 1);

        SkillContext context = new(
            state,
            state.Players[1],
            targetCard,
            drawnTile,
            targetSeat: 2,
            requestId: 42);

        Assert.Same(state, context.State);
        Assert.Same(state.Players[1], context.Source);
        Assert.Equal(targetCard, context.TargetCard!.Value);
        Assert.Equal(drawnTile, context.DrawnTile!.Value);
        Assert.Equal((byte)2, context.TargetSeat);
        Assert.Equal(42L, context.RequestId);
    }

    [Fact]
    public void Constructor_Defaults_AreNoTargetCardNoDrawnTileNoTargetSeatAndZeroRequestId()
    {
        GameState state = SkillTestData.NewState();

        SkillContext context = new(state, state.Players[3]);

        Assert.Null(context.TargetCard);
        Assert.Null(context.DrawnTile);
        Assert.Equal(Meld.NoTargetSeat, context.TargetSeat);
        Assert.Equal(255, context.TargetSeat);
        Assert.Equal(0L, context.RequestId);
    }
}
