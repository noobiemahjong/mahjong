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

        // 终局状态与总分。
        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(first.MirrorScores, second.MirrorScores);
        Assert.Equal(first.SubmitResults.Count, second.SubmitResults.Count);

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
    }
}
