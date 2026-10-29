using System.Collections.ObjectModel;

namespace Mahjong.Core.Flow;

/// <summary>命令动词。数值是协议常量，已发布后不得改动。</summary>
public enum CommandVerb : byte
{
    /// <summary>出牌。</summary>
    Discard = 1,

    /// <summary>碰。</summary>
    Pong = 2,

    /// <summary>杠。</summary>
    Kong = 3,

    /// <summary>胡。</summary>
    Win = 4,

    /// <summary>过。</summary>
    Pass = 5,
}

/// <summary>
/// 允许的动词位集。数值是协议常量，已发布后不得改动；命令与请求共用，会话层直接引用，不重复定义。
/// </summary>
[Flags]
public enum AllowedVerbs : ushort
{
    /// <summary>不允许任何动词。</summary>
    None = 0,

    /// <summary>出牌。</summary>
    Discard = 1,

    /// <summary>碰。</summary>
    Pong = 2,

    /// <summary>杠。</summary>
    Kong = 4,

    /// <summary>胡。</summary>
    Win = 8,

    /// <summary>过。</summary>
    Pass = 16,
}

/// <summary>玩家命令：由传输层写入、会话在 tick 边界校验并应用。</summary>
/// <param name="Seq">同一连接内的命令序号，用于同 tick 内排序。</param>
/// <param name="ReceivedTick">到达 tick；命令只在该 tick 及之后被排空。</param>
/// <param name="Seat">发出命令的座位 0–3。</param>
/// <param name="Verb">命令动词。</param>
/// <param name="Slot">手牌或请求负载中的槽位，语义由动词决定。</param>
/// <param name="CardId">物理牌 id，无牌的命令为 0。</param>
/// <param name="RequestId">应答的请求 id，非应答命令为 0。</param>
public readonly record struct GameCommand(
    long Seq,
    long ReceivedTick,
    byte Seat,
    CommandVerb Verb,
    int Slot,
    ushort CardId,
    long RequestId);

/// <summary>
/// 命令队列：会话在 tick 边界排空并应用，调度器不感知命令。
/// 只做缓冲与排序，不做合法性校验；实例非线程安全。
/// </summary>
public sealed class CommandQueue
{
    private readonly List<GameCommand> _pending = [];

    /// <summary>入队一条命令，保留入队顺序（同一 tick 内同一序号的并列排序依据）。</summary>
    /// <param name="command">要入队的命令。</param>
    public void Enqueue(GameCommand command) => _pending.Add(command);

    /// <summary>
    /// 排空本 tick 可用的命令：移除并返回 <see cref="GameCommand.ReceivedTick"/> 不大于 <paramref name="tick"/> 的命令，
    /// 按 (ReceivedTick, Seq, 入队序) 升序。返回快照，后续入队不影响它；同一命令只返回一次。
    /// </summary>
    /// <param name="tick">本次 tick 的逻辑时间。</param>
    /// <returns>快照列表；无命令时返回空列表，不返回 null。</returns>
    public IReadOnlyList<GameCommand> DrainForTick(long tick)
    {
        List<GameCommand> drained = [];
        for (int index = 0; index < _pending.Count;)
        {
            GameCommand command = _pending[index];
            if (command.ReceivedTick <= tick)
            {
                drained.Add(command);
                _pending.RemoveAt(index);
            }
            else
            {
                index++;
            }
        }

        if (drained.Count == 0)
        {
            return Array.Empty<GameCommand>();
        }

        SortByReceivedTickThenSeq(drained);
        return new ReadOnlyCollection<GameCommand>(drained);
    }

    /// <summary>稳定插入排序：并列时保持入队顺序。</summary>
    private static void SortByReceivedTickThenSeq(List<GameCommand> commands)
    {
        for (int index = 1; index < commands.Count; index++)
        {
            GameCommand command = commands[index];
            int position = index - 1;
            while (position >= 0 && Compare(commands[position], command) > 0)
            {
                commands[position + 1] = commands[position];
                position--;
            }

            commands[position + 1] = command;
        }
    }

    private static int Compare(GameCommand left, GameCommand right)
    {
        int byTick = left.ReceivedTick.CompareTo(right.ReceivedTick);
        return byTick != 0 ? byTick : left.Seq.CompareTo(right.Seq);
    }
}
