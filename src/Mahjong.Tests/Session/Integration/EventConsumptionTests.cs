using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Integration;

/// <summary>
/// 事件消费与可见性：seq 跨 Drain 连续递增且不重复、排空后不再返回；viewer=本人时他人 Deal 无 tileIds、
/// Drew.TileId=-1，viewer=255 全量且本人 tileIds 与全量视图逐元素相等；viewer 只影响本次编码，
/// 不改变事件顺序与其它事件 payload。
/// </summary>
/// <remarks>
/// 固定 seed 的来源：seq 用例用四 Bot 扫描选定的赢家 seed（7），事件种类覆盖赢家局；
/// 可见性用例用脚本化单人 seed 8，仅依赖「本人与他人的 Deal/Drew 都出现」（8 局必然满足），不依赖胜负。
/// </remarks>
[Collection("table-catalog")]
public sealed class EventConsumptionTests
{
    private readonly TableCatalogFixture _fixture;

    public EventConsumptionTests(TableCatalogFixture fixture) => _fixture = fixture;

    [Fact]
    public void SeqIsContiguousAcrossDrainsAndDrainIsDestructive()
    {
        GameSession session = SessionTestSupport.CreateFourBots(_fixture.Catalog, seed: 7);
        session.Start();

        List<SessionEvent> collected = [];
        long expectedSeq = 1;
        long lastTick = -1;

        Consume(session.DrainEvents(IntegrationSupport.FullViewSeat));
        while (!session.IsGameOver)
        {
            session.Step(session.Tick + 1);
            Consume(session.DrainEvents(IntegrationSupport.FullViewSeat));
        }

        // 已排空：任何观察座位再 Drain 都为空，不重复返回。
        Assert.Empty(session.DrainEvents(IntegrationSupport.FullViewSeat));
        Assert.Empty(session.DrainEvents(0));
        Assert.True(collected.Count > 100, "整场事件过少，序号连续性断言可能空转。");
        Assert.Equal(collected.Count, expectedSeq - 1);

        void Consume(IReadOnlyList<SessionEvent> batch)
        {
            foreach (SessionEvent sessionEvent in batch)
            {
                Assert.Equal(expectedSeq, sessionEvent.Seq);
                expectedSeq++;
                Assert.True(sessionEvent.Tick >= lastTick, "事件 Tick 不得倒退。");
                lastTick = sessionEvent.Tick;
                collected.Add(sessionEvent);
            }
        }
    }

    [Fact]
    public void ViewerSeatFiltersOnlyOwnDealAndDrewWhileFullViewIsComplete()
    {
        // seed 8：0 号真人、8 局；只要求本人/他人 Deal、Drew 均出现，不依赖起庄与胜负。
        const ulong seed = 8;
        IntegrationRun ownView = IntegrationSupport.RunScriptedSinglePlayer(
            _fixture.Catalog,
            seed,
            viewerSeat: 0);
        IntegrationRun fullView = IntegrationSupport.RunScriptedSinglePlayer(_fixture.Catalog, seed);

        Assert.True(ownView.GameOver);
        Assert.True(fullView.GameOver);
        Assert.Equal(fullView.Events.Count, ownView.Events.Count);

        int ownDeals = 0;
        int otherDeals = 0;
        int ownDraws = 0;
        int otherDraws = 0;

        for (int index = 0; index < fullView.Events.Count; index++)
        {
            SessionEvent own = ownView.Events[index];
            SessionEvent full = fullView.Events[index];
            Assert.Equal(full.Seq, own.Seq);
            Assert.Equal(full.Tick, own.Tick);
            Assert.Equal(full.Kind, own.Kind);

            switch (full.Kind)
            {
                case SessionEventKind.Deal:
                {
                    DealPayload ownDeal = SessionEventPayloadReader.ReadDeal(own);
                    DealPayload fullDeal = SessionEventPayloadReader.ReadDeal(full);
                    Assert.Equal(fullDeal.Seat, ownDeal.Seat);
                    Assert.Equal((byte)13, fullDeal.Count);
                    Assert.Equal(fullDeal.Count, ownDeal.Count);
                    Assert.Equal(13, fullDeal.TileIds.Length);
                    if (ownDeal.Seat == 0)
                    {
                        // 本人 Deal 的 tileIds 必须与全量视图逐元素相等（长度正确但内容写错/乱序也要判死）。
                        Assert.Equal(fullDeal.TileIds, ownDeal.TileIds);
                        Assert.Equal(13, ownDeal.TileIds.Length);
                        Assert.All(ownDeal.TileIds, tileId => Assert.InRange(tileId, 1, IntegrationSupport.TotalTileCount));
                        ownDeals++;
                    }
                    else
                    {
                        Assert.Empty(ownDeal.TileIds);
                        otherDeals++;
                    }

                    break;
                }

                case SessionEventKind.Drew:
                {
                    DrewPayload ownDrew = SessionEventPayloadReader.ReadDrew(own);
                    DrewPayload fullDrew = SessionEventPayloadReader.ReadDrew(full);
                    Assert.Equal(fullDrew.Seat, ownDrew.Seat);
                    Assert.Equal(fullDrew.WallCount, ownDrew.WallCount);
                    Assert.InRange(fullDrew.TileId, 1, IntegrationSupport.TotalTileCount);
                    if (ownDrew.Seat == 0)
                    {
                        Assert.Equal(fullDrew.TileId, ownDrew.TileId);
                        ownDraws++;
                    }
                    else
                    {
                        Assert.Equal(-1, ownDrew.TileId);
                        otherDraws++;
                    }

                    break;
                }

                case SessionEventKind.HandSync:
                    Assert.Fail("M0.2 不产出 HandSync。");
                    break;

                default:
                    Assert.True(
                        full.Payload.Span.SequenceEqual(own.Payload.Span),
                        $"事件 {index}（{full.Kind}）不应受观察座位影响。");
                    break;
            }
        }

        // 非空断言：本人与他人的 Deal/Drew 都实际出现过。
        Assert.True(ownDeals > 0);
        Assert.True(otherDeals > 0);
        Assert.True(ownDraws > 0);
        Assert.True(otherDraws > 0);
    }
}
