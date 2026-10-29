using Mahjong.Core.Flow;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// CommandQueue 契约：DrainForTick 移除并返回 ReceivedTick &lt;= tick 的命令，
/// 按 (ReceivedTick, Seq, 入队序) 升序；返回快照且同一命令只返回一次；空队列返回空列表而非 null。
/// </summary>
public sealed class CommandQueueTests
{
    [Fact]
    public void DrainForTick_ReturnsOnlyCommandsReceivedAtOrBeforeTick()
    {
        var queue = new CommandQueue();
        queue.Enqueue(Command(seq: 1, receivedTick: 1, verb: CommandVerb.Discard));
        queue.Enqueue(Command(seq: 2, receivedTick: 2, verb: CommandVerb.Pong));
        queue.Enqueue(Command(seq: 1, receivedTick: 3, verb: CommandVerb.Win));

        IReadOnlyList<GameCommand> drained = queue.DrainForTick(2);

        Assert.Equal(2, drained.Count);
        Assert.Equal(1L, drained[0].ReceivedTick);
        Assert.Equal(CommandVerb.Discard, drained[0].Verb);
        Assert.Equal(2L, drained[1].ReceivedTick);
        Assert.Equal(CommandVerb.Pong, drained[1].Verb);
    }

    [Fact]
    public void DrainForTick_SortsByReceivedTickThenSeq()
    {
        var queue = new CommandQueue();
        queue.Enqueue(Command(seq: 2, receivedTick: 5, verb: CommandVerb.Pong));
        queue.Enqueue(Command(seq: 9, receivedTick: 3, verb: CommandVerb.Discard));
        queue.Enqueue(Command(seq: 1, receivedTick: 5, verb: CommandVerb.Win));

        IReadOnlyList<GameCommand> drained = queue.DrainForTick(5);

        Assert.Equal(
            new[] { (3L, 9L), (5L, 1L), (5L, 2L) },
            drained.Select(command => (command.ReceivedTick, command.Seq)).ToArray());
    }

    [Fact]
    public void DrainForTick_PreservesEnqueueOrderForEqualTickAndSeq()
    {
        var queue = new CommandQueue();
        queue.Enqueue(Command(seq: 1, receivedTick: 1, verb: CommandVerb.Discard));
        queue.Enqueue(Command(seq: 1, receivedTick: 1, verb: CommandVerb.Pass));

        IReadOnlyList<GameCommand> drained = queue.DrainForTick(1);

        Assert.Equal(
            new[] { CommandVerb.Discard, CommandVerb.Pass },
            drained.Select(command => command.Verb).ToArray());
    }

    [Fact]
    public void DrainForTick_ReturnsSnapshotThatLaterEnqueueDoesNotChange()
    {
        var queue = new CommandQueue();
        queue.Enqueue(Command(seq: 1, receivedTick: 0));
        IReadOnlyList<GameCommand> drained = queue.DrainForTick(1);

        queue.Enqueue(Command(seq: 2, receivedTick: 1));

        Assert.Single(drained);
        Assert.Equal(1L, drained[0].Seq);
    }

    [Fact]
    public void DrainForTick_ReturnsEachCommandOnlyOnce()
    {
        var queue = new CommandQueue();
        queue.Enqueue(Command(seq: 1, receivedTick: 0));
        queue.Enqueue(Command(seq: 2, receivedTick: 1));

        Assert.Single(queue.DrainForTick(0));
        Assert.Single(queue.DrainForTick(1));
        Assert.Empty(queue.DrainForTick(2));
    }

    [Fact]
    public void DrainForTick_OnEmptyQueueReturnsEmptyList()
    {
        var queue = new CommandQueue();

        IReadOnlyList<GameCommand> drained = queue.DrainForTick(0);

        Assert.NotNull(drained);
        Assert.Empty(drained);
    }

    [Fact]
    public void EnqueueAndDrain_PreserveCommandFields()
    {
        var queue = new CommandQueue();
        var command = new GameCommand(5L, 2L, 3, CommandVerb.Kong, 7, 99, 12L);
        queue.Enqueue(command);

        GameCommand drained = Assert.Single(queue.DrainForTick(2));

        Assert.Equal(command.Seq, drained.Seq);
        Assert.Equal(command.ReceivedTick, drained.ReceivedTick);
        Assert.Equal(command.Seat, drained.Seat);
        Assert.Equal(command.Verb, drained.Verb);
        Assert.Equal(command.Slot, drained.Slot);
        Assert.Equal(command.CardId, drained.CardId);
        Assert.Equal(command.RequestId, drained.RequestId);
    }

    private static GameCommand Command(
        long seq,
        long receivedTick,
        CommandVerb verb = CommandVerb.Discard,
        byte seat = 0,
        int slot = 0,
        ushort cardId = 1,
        long requestId = 0) =>
        new(seq, receivedTick, seat, verb, slot, cardId, requestId);
}
