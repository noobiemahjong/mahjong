using Godot;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;

namespace Mahjong.Client.Input;

/// <summary>
/// 人类命令的唯一出口：从开放请求取 RequestId、按 Allowed 二次校验、构造 GameCommand 并 Submit。
/// 只有视图回调经本类发命令；提交被拒时打印原因、保持局面、不重试。
/// </summary>
public sealed class InputRouter
{
    private readonly GameSession _session;
    private readonly byte _humanSeat;
    private long _sequence;

    /// <summary>绑定会话与人类座位；不读局面、不提交。</summary>
    /// <param name="session">权威会话；只在本进程单线程内使用。</param>
    /// <param name="humanSeat">人类座位 0–3。</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> 为 null。</exception>
    public InputRouter(GameSession session, byte humanSeat)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _humanSeat = humanSeat;
    }

    /// <summary>
    /// 提交一个动词：找不到本座位开放请求、动词不在 Allowed 或出牌槽位越界时不 Submit 并返回 false。
    /// 命令 Seq 由本实例单调分配、ReceivedTick 取会话当前 tick、RequestId 取首个本座位开放请求。
    /// </summary>
    /// <param name="verb">要提交的动词。</param>
    /// <param name="handSlot">出牌用的手牌域槽位；非出牌动词忽略。</param>
    /// <returns>Submit 被接受返回 true；被拒或无有效请求返回 false。</returns>
    public bool SubmitVerb(CommandVerb verb, int handSlot)
    {
        if (!TryFindRequest(out long requestId, out AllowedVerbs allowed) || !IsAllowed(allowed, verb))
        {
            return false;
        }

        ushort cardId = 0;
        int slot = 0;
        if (verb == CommandVerb.Discard)
        {
            TileZone hand = _session.State.Players[_humanSeat].Hand;
            if (handSlot < 0 || handSlot >= hand.Count)
            {
                return false;
            }

            cardId = hand[handSlot].Id;
            slot = handSlot;
        }

        var command = new GameCommand(++_sequence, _session.Tick, _humanSeat, verb, slot, cardId, requestId);
        SubmitResult result = _session.Submit(command);
        if (!result.Accepted)
        {
            GD.PrintErr($"command rejected: {result.Reason}");
            return false;
        }

        return true;
    }

    /// <summary>出牌：等价于 <see cref="SubmitVerb"/> 传 <see cref="CommandVerb.Discard"/>。</summary>
    /// <param name="handSlot">手牌域槽位。</param>
    /// <returns>接受返回 true。</returns>
    public bool SubmitDiscard(int handSlot) => SubmitVerb(CommandVerb.Discard, handSlot);

    private bool TryFindRequest(out long requestId, out AllowedVerbs allowed)
    {
        foreach (PendingRequest request in _session.ActiveRequests)
        {
            if (request.Seat == _humanSeat)
            {
                requestId = request.RequestId;
                allowed = request.Allowed;
                return true;
            }
        }

        requestId = 0;
        allowed = AllowedVerbs.None;
        return false;
    }

    private static bool IsAllowed(AllowedVerbs allowed, CommandVerb verb) => verb switch
    {
        CommandVerb.Discard => (allowed & AllowedVerbs.Discard) != 0,
        CommandVerb.Pong => (allowed & AllowedVerbs.Pong) != 0,
        CommandVerb.Kong => (allowed & AllowedVerbs.Kong) != 0,
        CommandVerb.Win => (allowed & AllowedVerbs.Win) != 0,
        CommandVerb.Pass => (allowed & AllowedVerbs.Pass) != 0,
        _ => false,
    };
}
