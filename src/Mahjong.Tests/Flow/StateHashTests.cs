using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Tests.Flow.Support;
using Mahjong.Tests.Rules;
using Xunit;

namespace Mahjong.Tests.Flow;

/// <summary>
/// StateHash 契约：SHA-256 小写 hex；布局版本 3（牌墙后追加公共揭示区段）；
/// 默认空局与含牌墙/副露（碰 + 补杠 + opened:false 暗杠）/弃牌/分数的局面各有字面金标；
/// 期望字节由测试内独立按布局规格手工拼出（<see cref="StateHashOracle"/>），不使用生产代码的写字节逻辑。
/// </summary>
public sealed class StateHashTests
{
    /// <summary>默认空 GameState 的 SHA-256 金标（布局 v3：version 3、空牌墙、空揭示区、全零计数、currentSeat 0、roundNo 1），53 字节。</summary>
    private const string EmptyStateHashHex =
        "4f3948a487ab7c2311a2d0d04720b39911f7afdf4b22b098796cc75f655dac05";

    /// <summary>含牌墙/副露（碰、补杠、opened:false 暗杠）/弃牌/分数的局面金标（布局 v3，113 字节）。</summary>
    private const string RichStateHashHex =
        "5d4c1af028100b249f80eb14823346df3ccf4c00c19baf1cd33759a28957ab2a";

    /// <summary>布局 v2 的空局金标，仅作对照：v3 布局下不再产出该值（旧值不删）。</summary>
    private const string LegacyV2EmptyStateHashHex =
        "9233ee2f05f8fb737c69c9d271c7ad741cf3e708eef1cc17931597a31e847b6b";

    /// <summary>布局 v2 的富局面金标，仅作对照：v3 布局下不再产出该值（旧值不删）。</summary>
    private const string LegacyV2RichStateHashHex =
        "fc85d25e85af156153c4afab3da475e1f186a31310078bec7131eaddd623edc2";

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
    public void Layout_UsesVersion3AndIncludesRevealedSegment()
    {
        byte[] empty = StateHashOracle.BuildExpectedBytes(new GameState());

        Assert.Equal(3, StateHashOracle.LayoutVersion);
        Assert.Equal(3, empty[0]);

        // 空局字节数：version 1 + 牌墙 2 + 揭示区 2 + 玩家数 1 + 四家 × 11 + currentSeat 1 + roundNo 2 = 53。
        Assert.Equal(53, empty.Length);

        // 含牌墙 3 张与三副露的富局面：113 字节（揭示区为空仍占 2 字节计数）。
        Assert.Equal(113, StateHashOracle.BuildExpectedBytes(CreateRichState()).Length);
    }

    [Fact]
    public void Layout_RevealedSegmentSitsRightAfterWall()
    {
        var state = new GameState();
        state.Wall.Append(RulesTestData.FromId(31));
        byte[] before = StateHashOracle.BuildExpectedBytes(state);

        state.Revealed.Append(RulesTestData.FromId(71));
        byte[] after = StateHashOracle.BuildExpectedBytes(state);

        // 字节序：version(0) → 牌墙计数 [1..2] → 牌墙 id [3..4] → 揭示区计数 [5..6] → 揭示区 id [7..8]。
        Assert.Equal(31, after[3] | (after[4] << 8));
        Assert.Equal(1, after[5] | (after[6] << 8));
        Assert.Equal(71, after[7] | (after[8] << 8));
        Assert.Equal(before.Length + 2, after.Length);

        // 揭示区为空与非空是不同哈希，且两者都与独立 oracle 一致。
        GameState withRevealed = state;
        var onlyWall = new GameState();
        onlyWall.Wall.Append(RulesTestData.FromId(31));
        Assert.NotEqual(StateHash.Compute(onlyWall), StateHash.Compute(withRevealed));
        Assert.Equal(StateHashOracle.ComputeExpectedHex(withRevealed), StateHash.Compute(withRevealed));
        Assert.Equal(StateHashOracle.ComputeExpectedHex(onlyWall), StateHash.Compute(onlyWall));
    }

    [Fact]
    public void Layout_V3GoldenValuesDifferFromLegacyV2()
    {
        // 旧值保留作对照：布局 v3 下两条 v2 金标都不再产出（断言只增不减）。
        Assert.NotEqual(LegacyV2EmptyStateHashHex, StateHash.Compute(new GameState()));
        Assert.NotEqual(LegacyV2RichStateHashHex, StateHash.Compute(CreateRichState()));
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
    public void Compute_SameDealOnTwoStates_HashesEqual()
    {
        var first = new GameState();
        first.Deal(new Pcg32(42), 0);
        var second = new GameState();
        second.Deal(new Pcg32(42), 0);

        Assert.Equal(StateHash.Compute(first), StateHash.Compute(second));
        Assert.Equal(StateHashOracle.ComputeExpectedHex(first), StateHash.Compute(first));
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
    /// 构造覆盖全部字节段的局面：牌墙 3 张、座位 0 庄家且分数为负、手牌 4 张、
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
