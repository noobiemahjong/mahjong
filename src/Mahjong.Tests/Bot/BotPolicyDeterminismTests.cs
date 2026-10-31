using Mahjong.Core.Bot;
using Mahjong.Core.Rules;
using Xunit;

namespace Mahjong.Tests.Bot;

/// <summary>
/// 单局确定性：测试本地驱动（不引入 T-5 会话），同 seed 的终局状态哈希与弃牌物理 id 序列一致、
/// 不同 seed 不同；单局 84 次弃牌、牌墙为空、终局四家各 13 张、全程 136 张守恒。
/// </summary>
public sealed class BotPolicyDeterminismTests
{
    private const int ExpectedDiscards = 84;
    private const int ExpectedHandSize = 13;

    [Fact]
    public void SameSeed_ProducesSameFinalStateHash()
    {
        BotGameRun first = Run(42);
        BotGameRun second = Run(42);

        string firstHash = BotTestSupport.Hash(first.State);
        Assert.Equal(firstHash, BotTestSupport.Hash(second.State));
        Assert.Matches("^[0-9a-f]{64}$", firstHash);
        Assert.True(first.Completed);
        Assert.True(second.Completed);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentFinalStateHash()
    {
        BotGameRun first = Run(42);
        BotGameRun second = Run(20261029);

        Assert.NotEqual(BotTestSupport.Hash(first.State), BotTestSupport.Hash(second.State));
    }

    [Fact]
    public void FullGame_Has84DiscardsAndExpectedFinalShape()
    {
        BotGameRun run = Run(42);

        Assert.True(run.Completed);
        Assert.Null(run.IllegalTurn);
        Assert.Equal(ExpectedDiscards, run.Turns.Count);
        Assert.True(run.State.IsWallEmpty);
        Assert.Equal(0, run.State.Wall.Count);

        int totalDiscards = 0;
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            PlayerState player = run.State.Players[seat];
            Assert.Equal(ExpectedHandSize, player.Hand.Count);
            totalDiscards += player.Discards.Count;
        }

        Assert.Equal(ExpectedDiscards, totalDiscards);
        Assert.Equal(136, BotTestSupport.TotalTiles(run.State));
        Assert.All(run.TileCountsAfterTurn, count => Assert.Equal(136, count));
    }

    [Fact]
    public void SameSeed_ProducesSameDiscardPhysicalIdSequence()
    {
        ushort[] first = DiscardIdSequence(Run(42));
        ushort[] second = DiscardIdSequence(Run(42));

        Assert.Equal(ExpectedDiscards, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void InterleavedRuns_DoNotShareMutableState()
    {
        string firstHash = BotTestSupport.Hash(Run(42).State);
        _ = Run(20261029);
        string repeatedHash = BotTestSupport.Hash(Run(42).State);

        Assert.Equal(firstHash, repeatedHash);
    }

    private static BotGameRun Run(ulong seed)
    {
        var wins = new ProgrammableWinChecker();
        var verifier = new CardAndSkillVerifier(wins, BotTestSupport.Joker);
        var policy = new SimpleBotPolicy(wins, verifier);
        BotGameRun run = BotGameDriver.RunDiscardOnlyGame(seed, policy, verifier);

        Assert.True(run.Completed, $"seed {seed} did not reach an empty wall");
        Assert.Null(run.IllegalTurn);
        return run;
    }

    private static ushort[] DiscardIdSequence(BotGameRun run) =>
        [.. run.Turns.Select(turn => turn.Decision.Card!.Value.Id)];
}
