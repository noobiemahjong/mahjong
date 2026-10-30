using Mahjong.Core.Rules;
using Mahjong.Tests.Content.Tables;
using Xunit;

namespace Mahjong.Tests.Rules;

/// <summary>
/// 自摸两技能契约：<see cref="SelfDrawnSkill.Validate"/> = <c>IsWin(hand, melds, joker) &amp;&amp; !IsSevenPairs(hand, joker)</c>；
/// <see cref="SelfDrawnWith7PairsSkill.Validate"/> = <c>IsSevenPairs(hand, joker)</c>（七对优先，防与普通自摸双算）；
/// Commit 只复校验、不改状态（结算不在 T-2）；七对交叉手牌用 11223344556677 同时满足两种胡型以钉住反向排除分支。
/// 真实查表由 <see cref="TableCatalogFixture"/> 共享，整个进程只加载一次 15 MB 数据。
/// </summary>
[Collection("table-catalog")]
public sealed class SelfDrawnSkillTests
{
    private readonly WinChecker _checker;

    public SelfDrawnSkillTests(TableCatalogFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _checker = new WinChecker(fixture.Catalog);
    }

    /// <summary>普通胡 14 张：123 + 456 + 789 + 999 + 1筒将（type ids 0,1,2,3,4,5,6,7,8,9,9,9,17,17）。</summary>
    private static List<Tile> StandardWinHand() =>
        WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 17, 17);

    /// <summary>七对 14 张（非普通胡）。</summary>
    private static List<Tile> SevenPairsHand() =>
        WinCheckerTestData.Hand(0, 0, 1, 1, 2, 2, 9, 9, 10, 10, 18, 18, 27, 27);

    /// <summary>交叉手牌 11223344556677：既是七对（7 个对子），又是普通胡（123+123+456+456+77）。</summary>
    private static List<Tile> BothWinAndSevenPairsHand() =>
        WinCheckerTestData.Hand(0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6);

    [Fact]
    public void Constructors_RejectNullChecker()
    {
        Assert.Throws<ArgumentNullException>(() => new SelfDrawnSkill(null!));
        Assert.Throws<ArgumentNullException>(() => new SelfDrawnWith7PairsSkill(null!));
    }

    [Fact]
    public void Ids_AreSelfDrawnAndSelfDrawnWith7Pairs()
    {
        Assert.Equal(SkillId.SelfDrawn, new SelfDrawnSkill(_checker).Id);
        Assert.Equal(SkillId.SelfDrawnWith7Pairs, new SelfDrawnWith7PairsSkill(_checker).Id);
    }

    [Fact]
    public void SelfDrawn_Validate_True_OnStandardWinningHand()
    {
        GameState state = StateWithHand(StandardWinHand());

        Assert.True(new SelfDrawnSkill(_checker, WinCheckerTestData.Joker).Validate(SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void SelfDrawn_Validate_False_OnNonWinningHand()
    {
        GameState state = StateWithHand(WinCheckerTestData.Hand(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13));

        Assert.False(new SelfDrawnSkill(_checker, WinCheckerTestData.Joker).Validate(SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void SelfDrawn_Validate_False_OnPureSevenPairsHand()
    {
        GameState state = StateWithHand(SevenPairsHand());

        Assert.True(_checker.IsSevenPairs(SevenPairsHand(), WinCheckerTestData.Joker));
        Assert.False(_checker.IsWin(SevenPairsHand(), Array.Empty<Meld>(), WinCheckerTestData.Joker));
        Assert.False(new SelfDrawnSkill(_checker, WinCheckerTestData.Joker).Validate(SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void SelfDrawn_Validate_False_OnHandThatIsBothWinAndSevenPairs()
    {
        List<Tile> hand = BothWinAndSevenPairsHand();
        GameState state = StateWithHand(hand);

        // 先证明该手牌两种口径都成立，再钉住 SelfDrawnSkill 的反向排除分支（普通七对牌 IsWin=false，无法区分）。
        Assert.True(_checker.IsWin(hand, Array.Empty<Meld>(), WinCheckerTestData.Joker));
        Assert.True(_checker.IsSevenPairs(hand, WinCheckerTestData.Joker));

        Assert.False(new SelfDrawnSkill(_checker, WinCheckerTestData.Joker).Validate(SkillTestData.Context(state, 0)));
        Assert.True(
            new SelfDrawnWith7PairsSkill(_checker, WinCheckerTestData.Joker).Validate(SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void SelfDrawnWith7Pairs_Validate_True_OnSevenPairsHand()
    {
        GameState state = StateWithHand(SevenPairsHand());

        Assert.True(
            new SelfDrawnWith7PairsSkill(_checker, WinCheckerTestData.Joker).Validate(SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void SelfDrawnWith7Pairs_Validate_False_OnStandardWinningHand()
    {
        GameState state = StateWithHand(StandardWinHand());

        Assert.False(
            new SelfDrawnWith7PairsSkill(_checker, WinCheckerTestData.Joker).Validate(SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void SelfDrawn_Validate_AccountsForMeldsAndIgnoresDrawnTile()
    {
        // 手牌 11 张（111 + 123 + 456 + 88）+ 1 组碰 = 折合 14 张；DrawnTile 不参与 Validate（可空）。
        GameState state = StateWithHand(WinCheckerTestData.Hand(0, 0, 0, 1, 2, 3, 4, 5, 6, 8, 8));
        state.Players[0].Melds.Add(WinCheckerTestData.Pong(9));

        Assert.True(new SelfDrawnSkill(_checker, WinCheckerTestData.Joker).Validate(
            SkillTestData.Context(state, 0, drawnTile: null)));
        Assert.False(new SelfDrawnWith7PairsSkill(_checker, WinCheckerTestData.Joker).Validate(
            SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void Commit_DoesNotChangeState_OnValidWin()
    {
        GameState state = StateWithHand(StandardWinHand());
        string snapshotBefore = SkillTestData.Snapshot(state);
        string hashBefore = SkillTestData.Hash(state);
        var standard = new SelfDrawnSkill(_checker, WinCheckerTestData.Joker);
        var sevenPairs = new SelfDrawnWith7PairsSkill(_checker, WinCheckerTestData.Joker);

        standard.Commit(SkillTestData.Context(state, 0));
        Assert.Equal(snapshotBefore, SkillTestData.Snapshot(state));
        Assert.Equal(hashBefore, SkillTestData.Hash(state));

        GameState sevenPairsState = StateWithHand(SevenPairsHand());
        string sevenPairsSnapshot = SkillTestData.Snapshot(sevenPairsState);
        string sevenPairsHash = SkillTestData.Hash(sevenPairsState);
        sevenPairs.Commit(SkillTestData.Context(sevenPairsState, 0));
        Assert.Equal(sevenPairsSnapshot, SkillTestData.Snapshot(sevenPairsState));
        Assert.Equal(sevenPairsHash, SkillTestData.Hash(sevenPairsState));
    }

    [Fact]
    public void Commit_WhenValidateFails_ThrowsAndLeavesStateUnchanged()
    {
        GameState state = StateWithHand(BothWinAndSevenPairsHand());
        string snapshotBefore = SkillTestData.Snapshot(state);
        string hashBefore = SkillTestData.Hash(state);

        // 交叉手牌对普通自摸技能 Validate=false（它同时是七对），Commit 必须抛且不改状态。
        Assert.Throws<InvalidOperationException>(
            () => new SelfDrawnSkill(_checker, WinCheckerTestData.Joker).Commit(
                SkillTestData.Context(state, 0)));
        Assert.Equal(snapshotBefore, SkillTestData.Snapshot(state));
        Assert.Equal(hashBefore, SkillTestData.Hash(state));

        // 非胡手牌对七对技能同样失败且不改状态。
        GameState nonWin = StateWithHand(StandardWinHand());
        string nonWinSnapshot = SkillTestData.Snapshot(nonWin);
        string nonWinHash = SkillTestData.Hash(nonWin);
        Assert.Throws<InvalidOperationException>(
            () => new SelfDrawnWith7PairsSkill(_checker, WinCheckerTestData.Joker).Commit(
                SkillTestData.Context(nonWin, 0)));
        Assert.Equal(nonWinSnapshot, SkillTestData.Snapshot(nonWin));
        Assert.Equal(nonWinHash, SkillTestData.Hash(nonWin));
    }

    [Fact]
    public void SelfDrawn_Validate_PassesTheInjectedJokerTypeToTheChecker()
    {
        // 111 + 234 + 567 + 999 + 17/31：无鬼时 17 与红中都是单张不成胡；红中为鬼时补成将。
        List<Tile> hand = WinCheckerTestData.Hand(0, 0, 0, 1, 2, 3, 4, 5, 6, 9, 9, 9, 17, 31);
        GameState state = StateWithHand(hand);
        Assert.True(_checker.IsWin(hand, Array.Empty<Meld>(), WinCheckerTestData.Joker));
        Assert.False(_checker.IsWin(hand, Array.Empty<Meld>(), WinCheckerTestData.NoJoker));

        Assert.True(new SelfDrawnSkill(_checker, WinCheckerTestData.Joker).Validate(SkillTestData.Context(state, 0)));
        Assert.False(new SelfDrawnSkill(_checker).Validate(SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void SelfDrawnWith7Pairs_Validate_PassesTheInjectedJokerTypeToTheChecker()
    {
        // 6 个对子 + 南风单张 + 红中：无鬼时南风/红中各为奇数张牌种（2 > 0）不成七对；红中为鬼时配平。
        List<Tile> hand = WinCheckerTestData.Hand(0, 0, 1, 1, 2, 2, 9, 9, 10, 10, 18, 18, 27, 31);
        GameState state = StateWithHand(hand);
        Assert.True(_checker.IsSevenPairs(hand, WinCheckerTestData.Joker));
        Assert.False(_checker.IsSevenPairs(hand, WinCheckerTestData.NoJoker));

        Assert.True(
            new SelfDrawnWith7PairsSkill(_checker, WinCheckerTestData.Joker).Validate(SkillTestData.Context(state, 0)));
        Assert.False(new SelfDrawnWith7PairsSkill(_checker).Validate(SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void UseCase_SevenPairsSkill_DelegatesOnlyToIsSevenPairs()
    {
        var fake = new RecordingWinChecker { Win = false, SevenPairs = true };
        GameState state = StateWithHand(StandardWinHand());

        Assert.True(new SelfDrawnWith7PairsSkill(fake).Validate(SkillTestData.Context(state, 0)));
        Assert.Equal(0, fake.WinCalls);
        Assert.Equal(1, fake.SevenPairsCalls);

        fake.SevenPairs = false;
        Assert.False(new SelfDrawnWith7PairsSkill(fake).Validate(SkillTestData.Context(state, 0)));
    }

    [Fact]
    public void UseCase_StandardSkill_RequiresWinAndRejectsSevenPairs()
    {
        var fake = new RecordingWinChecker { Win = true, SevenPairs = false };
        GameState state = StateWithHand(StandardWinHand());

        Assert.True(new SelfDrawnSkill(fake).Validate(SkillTestData.Context(state, 0)));
        Assert.Equal(1, fake.WinCalls);
        Assert.Equal(1, fake.SevenPairsCalls);

        // IsWin 与 IsSevenPairs 同时成立时必须拒绝（七对优先）。
        fake.SevenPairs = true;
        Assert.False(new SelfDrawnSkill(fake).Validate(SkillTestData.Context(state, 0)));

        // IsWin 不成立时直接拒绝。
        fake.Win = false;
        fake.SevenPairs = false;
        Assert.False(new SelfDrawnSkill(fake).Validate(SkillTestData.Context(state, 0)));
    }

    private static GameState StateWithHand(List<Tile> hand)
    {
        GameState state = SkillTestData.NewState();
        SkillTestData.FillHand(state, 0, [.. hand]);
        return state;
    }

    /// <summary>可编造的胡牌判定：记录 IsWin/IsSevenPairs 调用次数，用于验证技能的分派契约。</summary>
    private sealed class RecordingWinChecker : IWinChecker
    {
        public bool Win { get; set; }

        public bool SevenPairs { get; set; }

        public int WinCalls { get; private set; }

        public int SevenPairsCalls { get; private set; }

        public bool IsWin(IReadOnlyList<Tile> hand, IReadOnlyList<Meld> melds, TileType joker)
        {
            WinCalls++;
            return Win;
        }

        public IReadOnlyList<TileType> GetWinningTypes(
            IReadOnlyList<Tile> hand,
            IReadOnlyList<Meld> melds,
            TileType joker) => throw new NotSupportedException("技能只调用 IsWin/IsSevenPairs。");

        public bool IsSevenPairs(IReadOnlyList<Tile> hand, TileType joker)
        {
            SevenPairsCalls++;
            return SevenPairs;
        }
    }
}
