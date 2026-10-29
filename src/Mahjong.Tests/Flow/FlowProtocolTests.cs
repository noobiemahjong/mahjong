using Mahjong.Core.Flow;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// 命令/请求协议常量与值类型：枚举字面值与底层类型钉死防漂移；GameCommand/PendingRequest/RequestOutcome
/// 字段顺序与取值；RequestGroup 防御性拷贝与空集合。
/// </summary>
public sealed class FlowProtocolTests
{
    [Fact]
    public void CommandVerb_LiteralValuesArePinned()
    {
        Assert.Equal(1, (int)CommandVerb.Discard);
        Assert.Equal(2, (int)CommandVerb.Pong);
        Assert.Equal(3, (int)CommandVerb.Kong);
        Assert.Equal(4, (int)CommandVerb.Win);
        Assert.Equal(5, (int)CommandVerb.Pass);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(CommandVerb)));
    }

    [Fact]
    public void AllowedVerbs_LiteralValuesAndFlagCombinationsArePinned()
    {
        Assert.Equal(0, (int)AllowedVerbs.None);
        Assert.Equal(1, (int)AllowedVerbs.Discard);
        Assert.Equal(2, (int)AllowedVerbs.Pong);
        Assert.Equal(4, (int)AllowedVerbs.Kong);
        Assert.Equal(8, (int)AllowedVerbs.Win);
        Assert.Equal(16, (int)AllowedVerbs.Pass);
        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(AllowedVerbs)));
        Assert.True(typeof(AllowedVerbs).IsDefined(typeof(FlagsAttribute), inherit: false));

        AllowedVerbs drawAndPong = AllowedVerbs.Discard | AllowedVerbs.Pong;
        Assert.Equal(3, (int)drawAndPong);
        Assert.Equal(AllowedVerbs.Pong, drawAndPong & AllowedVerbs.Pong);
        Assert.Equal(31, (int)(AllowedVerbs.Discard | AllowedVerbs.Pong | AllowedVerbs.Kong | AllowedVerbs.Win | AllowedVerbs.Pass));
    }

    [Fact]
    public void RequestKind_LiteralValuesArePinned()
    {
        Assert.Equal(0, (int)RequestKind.Discard);
        Assert.Equal(1, (int)RequestKind.Pong);
        Assert.Equal(2, (int)RequestKind.Kong);
        Assert.Equal(3, (int)RequestKind.Win);
        Assert.Equal(4, (int)RequestKind.Pass);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(RequestKind)));
    }

    [Fact]
    public void RequestOutcomeKind_LiteralValuesArePinned()
    {
        Assert.Equal(0, (int)RequestOutcomeKind.Answered);
        Assert.Equal(1, (int)RequestOutcomeKind.TimedOut);
        Assert.Equal(2, (int)RequestOutcomeKind.Cancelled);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(RequestOutcomeKind)));
    }

    [Fact]
    public void GameCommand_CarriesFieldsInDeclaredOrder()
    {
        var command = new GameCommand(
            Seq: 9L,
            ReceivedTick: 4L,
            Seat: 2,
            Verb: CommandVerb.Kong,
            Slot: 13,
            CardId: 136,
            RequestId: 77L);

        Assert.Equal(9L, command.Seq);
        Assert.Equal(4L, command.ReceivedTick);
        Assert.Equal(2, (int)command.Seat);
        Assert.Equal(CommandVerb.Kong, command.Verb);
        Assert.Equal(13, command.Slot);
        Assert.Equal(136, (int)command.CardId);
        Assert.Equal(77L, command.RequestId);
        Assert.Equal(command, command with { Slot = 13 });
    }

    [Fact]
    public void PendingRequestAndRequestOutcome_CarryFields()
    {
        var pending = new PendingRequest(
            RequestId: 5L,
            Seat: 1,
            Kind: RequestKind.Pong,
            Allowed: AllowedVerbs.Pong | AllowedVerbs.Pass,
            DeadlineTick: 30L);
        object payload = new();
        var outcome = new RequestOutcome(5L, RequestOutcomeKind.Answered, payload);

        Assert.Equal(5L, pending.RequestId);
        Assert.Equal(1, (int)pending.Seat);
        Assert.Equal(RequestKind.Pong, pending.Kind);
        Assert.Equal(AllowedVerbs.Pong | AllowedVerbs.Pass, pending.Allowed);
        Assert.Equal(30L, pending.DeadlineTick);

        Assert.Equal(5L, outcome.RequestId);
        Assert.Equal(RequestOutcomeKind.Answered, outcome.Kind);
        Assert.Same(payload, outcome.Payload);
        Assert.Null(new RequestOutcome(5L, RequestOutcomeKind.TimedOut, null).Payload);
    }

    [Fact]
    public void RequestGroup_RejectsNullRequests()
    {
        Assert.Throws<ArgumentNullException>(() => new RequestGroup(1L, null!));
    }

    [Fact]
    public void RequestGroup_MakesDefensiveCopyOfRequests()
    {
        var first = new PendingRequest(1L, 0, RequestKind.Discard, AllowedVerbs.Discard, 10L);
        var second = new PendingRequest(2L, 1, RequestKind.Pass, AllowedVerbs.Pass, 10L);
        var source = new List<PendingRequest> { first, second };
        var group = new RequestGroup(42L, source);

        source.Clear();

        Assert.Equal(42L, group.RequestGroupId);
        Assert.Equal(2, group.Requests.Count);
        Assert.Equal(first, group.Requests[0]);
        Assert.Equal(second, group.Requests[1]);
        Assert.Equal(new[] { 1L, 2L }, group.Requests.Select(request => request.RequestId).ToArray());
    }

    [Fact]
    public void RequestGroup_AcceptsEmptyRequestList()
    {
        var group = new RequestGroup(7L, Array.Empty<PendingRequest>());

        Assert.Equal(7L, group.RequestGroupId);
        Assert.Empty(group.Requests);
    }
}
