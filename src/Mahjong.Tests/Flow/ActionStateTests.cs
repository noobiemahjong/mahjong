using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// ActionState/ActionStatus 契约：七个状态各自的工厂、未使用字段的默认值、非法参数与异常类型 paramName；
/// ActionStatus 数值与底层类型是协议常量，逐值钉死防漂移。
/// </summary>
public sealed class ActionStateTests
{
    [Fact]
    public void Continue_HasDefaultPayloadFields()
    {
        ActionState state = ActionState.Continue;

        Assert.Equal(ActionStatus.Continue, state.Status);
        Assert.Equal(0, state.Ticks);
        Assert.Null(state.Predicate);
        Assert.Equal(0L, state.RequestId);
        Assert.Equal(0, state.Depth);
        Assert.Null(state.Result);
    }

    [Fact]
    public void WaitTicks_CarriesTickCountAndLeavesOtherFieldsDefault()
    {
        ActionState state = ActionState.WaitTicks(7);

        Assert.Equal(ActionStatus.WaitTicks, state.Status);
        Assert.Equal(7, state.Ticks);
        Assert.Null(state.Predicate);
        Assert.Equal(0L, state.RequestId);
        Assert.Equal(0, state.Depth);
        Assert.Null(state.Result);
    }

    [Fact]
    public void WaitTicks_AcceptsZero()
    {
        ActionState state = ActionState.WaitTicks(0);

        Assert.Equal(ActionStatus.WaitTicks, state.Status);
        Assert.Equal(0, state.Ticks);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void WaitTicks_RejectsNegativeTicks(int ticks)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(() => ActionState.WaitTicks(ticks));

        Assert.Equal("ticks", exception.ParamName);
    }

    [Fact]
    public void WaitUntil_KeepsPredicateReferenceUncached()
    {
        Func<bool> predicate = () => true;

        ActionState state = ActionState.WaitUntil(predicate);

        Assert.Equal(ActionStatus.WaitUntil, state.Status);
        Assert.Same(predicate, state.Predicate);
        Assert.Equal(0, state.Ticks);
        Assert.Equal(0L, state.RequestId);
        Assert.Equal(0, state.Depth);
        Assert.Null(state.Result);
    }

    [Fact]
    public void WaitUntil_RejectsNullPredicate()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => ActionState.WaitUntil(null!));

        Assert.Equal("predicate", exception.ParamName);
    }

    [Fact]
    public void WaitRequest_CarriesRequestId()
    {
        ActionState state = ActionState.WaitRequest(42);

        Assert.Equal(ActionStatus.WaitRequest, state.Status);
        Assert.Equal(42L, state.RequestId);
        Assert.Null(state.Predicate);
        Assert.Equal(0, state.Ticks);
        Assert.Equal(0, state.Depth);
        Assert.Null(state.Result);
    }

    [Fact]
    public void AbortParents_DefaultsToOneAndCarriesDepth()
    {
        ActionState defaultDepth = ActionState.AbortParents();
        ActionState depthThree = ActionState.AbortParents(3);

        Assert.Equal(ActionStatus.AbortParents, defaultDepth.Status);
        Assert.Equal(1, defaultDepth.Depth);
        Assert.Equal(3, depthThree.Depth);
        Assert.Equal(ActionStatus.AbortParents, depthThree.Status);
        Assert.Null(depthThree.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AbortParents_RejectsDepthBelowOne(int depth)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(() => ActionState.AbortParents(depth));

        Assert.Equal("depth", exception.ParamName);
    }

    [Fact]
    public void AbortSiblings_HasExpectedStatusAndDefaultFields()
    {
        ActionState state = ActionState.AbortSiblings;

        Assert.Equal(ActionStatus.AbortSiblings, state.Status);
        Assert.Equal(0, state.Depth);
        Assert.Null(state.Predicate);
        Assert.Equal(0L, state.RequestId);
        Assert.Null(state.Result);
    }

    [Fact]
    public void Complete_CarriesResult()
    {
        ActionState withoutResult = ActionState.Complete();
        object payload = new();
        ActionState withResult = ActionState.Complete(payload);

        Assert.Equal(ActionStatus.Complete, withoutResult.Status);
        Assert.Null(withoutResult.Result);
        Assert.Equal(ActionStatus.Complete, withResult.Status);
        Assert.Same(payload, withResult.Result);
    }

    [Fact]
    public void ActionStatus_LiteralValuesArePinned()
    {
        Assert.Equal(0, (int)ActionStatus.Continue);
        Assert.Equal(1, (int)ActionStatus.WaitTicks);
        Assert.Equal(2, (int)ActionStatus.WaitUntil);
        Assert.Equal(3, (int)ActionStatus.WaitRequest);
        Assert.Equal(4, (int)ActionStatus.AbortParents);
        Assert.Equal(5, (int)ActionStatus.AbortSiblings);
        Assert.Equal(6, (int)ActionStatus.Complete);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(ActionStatus)));
    }

    [Fact]
    public void Trigger_DefaultPriorityIsTenAndIsVirtual()
    {
        Assert.Equal(10, new StubTrigger().Priority);
        Assert.Equal(42, new HighPriorityTrigger().Priority);
    }

    [Fact]
    public void GameEventArgs_RejectsNullState()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new GameEventArgs(null!));

        Assert.Equal("state", exception.ParamName);
    }

    [Fact]
    public void GameEventArgs_CarriesStateAndPayload()
    {
        var state = new GameState();
        object payload = new();
        var args = new GameEventArgs(state, payload);

        Assert.Same(state, args.State);
        Assert.Same(payload, args.Payload);
        Assert.Null(new GameEventArgs(state).Payload);
    }

    private sealed class StubTrigger : Trigger
    {
        public override IEnumerator<ActionState> Run(GameEventArgs args) => throw new NotSupportedException();
    }

    private sealed class HighPriorityTrigger : Trigger
    {
        public override int Priority => 42;

        public override IEnumerator<ActionState> Run(GameEventArgs args) => throw new NotSupportedException();
    }
}
