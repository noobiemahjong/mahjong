using Mahjong.Core.Content.Tables;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Integration;

/// <summary>
/// 端到端确定性：同 seed + 同脚本（固定真人策略）两遍，最终 StateHash、四座总分与事件流
/// (Seq, Tick, Kind, Payload 字节) 必须完全一致。
/// </summary>
/// <remarks>
/// seed 由脚本化整场扫描选定（23：1 个非真人赢家局 + 7 个流局）；确定性断言与胜负无关，
/// 选种只为让事件流同时包含赢家与流局两类结算。
/// </remarks>
[Collection("table-catalog")]
public sealed class DeterminismTests
{
    private readonly TableCatalogFixture _fixture;

    public DeterminismTests(TableCatalogFixture fixture) => _fixture = fixture;

    [Fact]
    public void SameSeedAndScriptedHuman_ReproduceStateScoresAndEventStream()
    {
        const ulong seed = 23;
        IntegrationRun first = IntegrationSupport.RunScriptedSinglePlayer(_fixture.Catalog, seed);
        IntegrationRun second = IntegrationSupport.RunScriptedSinglePlayer(_fixture.Catalog, seed);

        Assert.True(first.GameOver);
        Assert.True(second.GameOver);
        Assert.True(first.Events.Count > 100, "整场事件流过短，确定性断言可能空转。");

        // 终局状态与总分（哈希必须真的算出 64 位小写 hex，不是两边都拿空串比出「相等」）。
        Assert.Matches("^[0-9a-f]{64}$", first.FinalStateHash);
        Assert.Matches("^[0-9a-f]{64}$", second.FinalStateHash);
        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(first.MirrorScores, second.MirrorScores);
        Assert.Contains(first.MirrorScores, score => score != 0);
        Assert.Equal(first.SubmitResults.Count, second.SubmitResults.Count);

        // 8 局结构：两遍都必须是 8 条 RoundStarted、8 条 RoundEnded、1 条 GameEnded。
        Assert.Equal(8, first.Events.Count(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted));
        Assert.Equal(8, second.Events.Count(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundStarted));
        Assert.Equal(8, first.Events.Count(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded));
        Assert.Equal(8, second.Events.Count(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded));
        Assert.Single(first.Events, sessionEvent => sessionEvent.Kind == SessionEventKind.GameEnded);
        Assert.Single(second.Events, sessionEvent => sessionEvent.Kind == SessionEventKind.GameEnded);

        // 翻马序列：逐项（tick + payload hex）相等，且序列非空——「两次都为空」不算一致。
        string[] firstHorse = HorseSequence(first.Events);
        string[] secondHorse = HorseSequence(second.Events);
        Assert.NotEmpty(firstHorse);
        Assert.Equal(firstHorse, secondHorse);
        Assert.All(firstHorse, entry => Assert.Matches("^[0-9]+:[0-9A-F]{6}$", entry));

        // 逐局 scoreDelta：8 条、逐条相等，且至少一局非零（全零相等不构成确定性证据）。
        string[] firstDeltas = RoundDeltaTexts(first.Events);
        string[] secondDeltas = RoundDeltaTexts(second.Events);
        Assert.Equal(8, firstDeltas.Length);
        Assert.Equal(firstDeltas, secondDeltas);
        Assert.Contains(firstDeltas, text => text != "0,0,0,0");

        // 事件流逐字节一致：Seq|Tick|Kind|Payload hex 文本相同。
        Assert.Equal(first.SerializedEvents, second.SerializedEvents);
        Assert.Equal(first.Events.Count, second.Events.Count);
        for (int index = 0; index < first.Events.Count; index++)
        {
            SessionEvent left = first.Events[index];
            SessionEvent right = second.Events[index];
            Assert.Equal(left.Seq, right.Seq);
            Assert.Equal(left.Tick, right.Tick);
            Assert.Equal(left.Kind, right.Kind);
            Assert.True(
                left.Payload.Span.SequenceEqual(right.Payload.Span),
                $"事件 {index}（{left.Kind}）payload 不一致。");
        }

        // GameEnded 的总分与名次同样可复现。
        GameEndedPayload firstEnded = SessionEventPayloadReader.ReadGameEnded(
            Assert.Single(first.Events, sessionEvent => sessionEvent.Kind == SessionEventKind.GameEnded));
        GameEndedPayload secondEnded = SessionEventPayloadReader.ReadGameEnded(
            Assert.Single(second.Events, sessionEvent => sessionEvent.Kind == SessionEventKind.GameEnded));
        Assert.Equal(firstEnded.TotalScores, secondEnded.TotalScores);
        Assert.Equal(firstEnded.Ranking, secondEnded.Ranking);
        Assert.Equal(firstEnded.ReplayId, secondEnded.ReplayId);
        Assert.Equal(0, firstEnded.TotalScores.Sum());
        Assert.Equal(firstEnded.TotalScores, first.MirrorScores.Select(score => (int)score).ToArray());
    }

    /// <summary>翻马序列文本：<c>tick:payload-hex</c>；用于逐项比对，不使用「都相等」的空断言。</summary>
    private static string[] HorseSequence(IReadOnlyList<SessionEvent> events) =>
        [.. events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.HorseRevealed)
            .Select(sessionEvent => sessionEvent.Tick + ":" + Convert.ToHexString(sessionEvent.Payload.Span))];

    /// <summary>逐局 scoreDelta 文本：<c>a,b,c,d</c>；四座顺序固定，用于逐局比对。</summary>
    private static string[] RoundDeltaTexts(IReadOnlyList<SessionEvent> events) =>
        [.. events
            .Where(sessionEvent => sessionEvent.Kind == SessionEventKind.RoundEnded)
            .Select(sessionEvent => string.Join(',', SessionEventPayloadReader.ReadRoundEnded(sessionEvent).ScoreDelta))];
}
