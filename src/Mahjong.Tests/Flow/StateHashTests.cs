using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Tests.Flow.Support;
using Mahjong.Tests.Rules;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// StateHash 契约：SHA-256 小写 hex；默认空局与含牌墙/牌尾/副露（碰 + 补杠 + opened:false 暗杠）/弃牌/分数的局面各有字面金标；
/// 期望字节由测试内独立按规格 §3 手工拼出（<see cref="StateHashOracle"/>），不使用生产代码的写字节逻辑。
/// </summary>
public sealed class StateHashTests
{
    /// <summary>默认空 GameState 的 SHA-256 金标（规格 §3 字节流：version 1、全零计数、currentSeat 0、roundNo 1）。</summary>
    private const string EmptyStateHashHex =
        "e5e8bfb7d63dc88daa52fc62f0e0a4a9cdc1de3230619660fffd4230bdea36be";

    /// <summary>含牌墙/牌尾/副露（碰、补杠、opened:false 暗杠）/弃牌/分数的局面金标。</summary>
    private const string RichStateHashHex =
        "3778b852dafc7fd4f44ca5673fea7312345f9d00578430c78406b9d60af2beb8";

    [Fact]
    public void Compute_RejectsNullState()
    {
        Assert.Throws<ArgumentNullException>(() => StateHash.Compute(null!));
    }

    [Fact]
    public void Compute_EmptyState_MatchesLiteralGoldenHex()
    {
        GameState state = new();

        string hash = StateHash.Compute(state);

        Assert.Equal(EmptyStateHashHex, hash);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.DoesNotContain("-", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Compute_EmptyState_MatchesIndependentOracle()
    {
        GameState state = new();

        Assert.Equal(EmptyStateHashHex, StateHashOracle.ComputeExpectedHex(state));
        Assert.Equal(StateHashOracle.ComputeExpectedHex(state), StateHash.Compute(state));
    }

    [Fact]
    public void Compute_RichState_MatchesLiteralGoldenHex()
    {
        GameState state = CreateRichState();

        // 覆盖显式断言：第三副露是 opened:false 的暗杠，且 targetSeat=NoTargetSeat（哈希必须编入 opened 位）。
        Meld concealedKong = state.Players[0].Melds[2];
        Assert.Equal(MeldKind.KongConcealed, concealedKong.Kind);
        Assert.False(concealedKong.Opened);
        Assert.Equal(Meld.NoTargetSeat, concealedKong.TargetSeat);
        Assert.Equal(RichStateHashHex, StateHash.Compute(state));
    }

    [Fact]
    public void Compute_RichState_MatchesIndependentOracle()
    {
        GameState state = CreateRichState();

        Assert.Equal(RichStateHashHex, StateHashOracle.ComputeExpectedHex(state));
        Assert.Equal(StateHashOracle.ComputeExpectedHex(state), StateHash.Compute(state));
    }

    [Fact]
    public void Compute_SameStateTwice_ReturnsSameString()
    {
        GameState state = CreateRichState();

        Assert.Equal(StateHash.Compute(state), StateHash.Compute(state));
    }

    [Fact]
    public void Compute_WallSlotOrderIsSignificant()
    {
        var ascending = new GameState();
        ascending.Wall.Append(RulesTestData.FromId(1));
        ascending.Wall.Append(RulesTestData.FromId(2));
        var descending = new GameState();
        descending.Wall.Append(RulesTestData.FromId(2));
        descending.Wall.Append(RulesTestData.FromId(1));

        Assert.NotEqual(StateHash.Compute(ascending), StateHash.Compute(descending));
        Assert.NotEqual(
            StateHashOracle.ComputeExpectedHex(ascending),
            StateHashOracle.ComputeExpectedHex(descending));
        Assert.Equal(StateHashOracle.ComputeExpectedHex(ascending), StateHash.Compute(ascending));
        Assert.Equal(StateHashOracle.ComputeExpectedHex(descending), StateHash.Compute(descending));
    }

    /// <summary>
    /// 构造覆盖全部字节段的局面：牌墙 3 张、牌尾 2 张、座位 0 庄家且分数为负、手牌 4 张、
    /// 三副露（碰 opened/有目标座位、补杠 opened/无目标座位、暗杠 opened:false/无目标座位）、弃牌 2 张；
    /// 其余座位分别在手牌/弃牌/分数/空局面上取值；currentSeat 3、roundNo 5。
    /// </summary>
    private static GameState CreateRichState()
    {
        var state = new GameState();
        foreach (int physicalId in new[] { 101, 102, 103 })
        {
            state.Wall.Append(RulesTestData.FromId(physicalId));
        }

        foreach (int physicalId in new[] { 104, 105 })
        {
            state.WallTail.Append(RulesTestData.FromId(physicalId));
        }

        PlayerState seat0 = state.Players[0];
        seat0.IsBanker = true;
        seat0.Score = -5;
        foreach (int physicalId in new[] { 1, 2, 5, 6 })
        {
            seat0.Hand.Append(RulesTestData.FromId(physicalId));
        }

        seat0.Melds.Add(new Meld(
            MeldKind.Pong,
            new[] { RulesTestData.FromId(9), RulesTestData.FromId(10), RulesTestData.FromId(11) },
            opened: true,
            targetSeat: 2));
        seat0.Melds.Add(new Meld(
            MeldKind.KongSupplement,
            new[]
            {
                RulesTestData.FromId(13),
                RulesTestData.FromId(14),
                RulesTestData.FromId(15),
                RulesTestData.FromId(16),
            },
            opened: true,
            targetSeat: Meld.NoTargetSeat));
        seat0.Melds.Add(new Meld(
            MeldKind.KongConcealed,
            new[]
            {
                RulesTestData.FromId(57),
                RulesTestData.FromId(58),
                RulesTestData.FromId(59),
                RulesTestData.FromId(60),
            },
            opened: false,
            targetSeat: Meld.NoTargetSeat));
        foreach (int physicalId in new[] { 17, 18 })
        {
            seat0.Discards.Append(RulesTestData.FromId(physicalId));
        }

        PlayerState seat1 = state.Players[1];
        seat1.Score = 250;
        seat1.Hand.Append(RulesTestData.FromId(21));
        foreach (int physicalId in new[] { 22, 23 })
        {
            seat1.Discards.Append(RulesTestData.FromId(physicalId));
        }

        PlayerState seat3 = state.Players[3];
        seat3.Score = -100;
        seat3.Hand.Append(RulesTestData.FromId(25));

        state.CurrentSeat = 3;
        state.RoundNo = 5;
        return state;
    }
}
