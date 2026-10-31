using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Session.Support;
using Xunit;

namespace Mahjong.Tests.Session.Flow;

/// <summary>
/// 会话生命周期与首回合事件顺序：构造校验、Start 幂等/Step 顺序约束、座位状态事件、
/// 发牌 → 摸牌 → 首个请求打开的顺序与 payload 字段。
/// </summary>
[Collection("table-catalog")]
public sealed class GameSessionLifecycleTests
{
    private readonly TableCatalogFixture _fixture;

    public GameSessionLifecycleTests(TableCatalogFixture fixture) => _fixture = fixture;

    private GameSession Create(SessionConfig config) =>
        SessionTestSupport.Create(config, _fixture.Catalog, seed: 42);

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        var content = SessionTestSupport.Content;
        var wins = new WinChecker(_fixture.Catalog);
        var rng = new Pcg32(1);
        var config = new SessionConfig(null, 4);

        Assert.Throws<ArgumentNullException>(() => new GameSession(null!, content, wins, rng));
        Assert.Throws<ArgumentNullException>(() => new GameSession(config, null!, wins, rng));
        Assert.Throws<ArgumentNullException>(() => new GameSession(config, content, null!, rng));
        Assert.Throws<ArgumentNullException>(() => new GameSession(config, content, wins, null!));
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(0, 4)]
    [InlineData(null, 3)]
    [InlineData(2, 2)]
    public void Constructor_RejectsInvalidSeatAndBotCombinations(int? humanSeat, byte botCount)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => Create(new SessionConfig((byte?)humanSeat, botCount)));

        Assert.Equal("config", exception.ParamName);
    }

    [Theory]
    [InlineData(0, 600, 150)]
    [InlineData(-1, 600, 150)]
    [InlineData(10, 0, 150)]
    [InlineData(10, -3, 150)]
    [InlineData(10, 600, 0)]
    [InlineData(10, 600, -1)]
    public void Constructor_RejectsNonPositiveTickRateAndTimeouts(int tickRate, int turnTimeout, int responseTimeout)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => Create(new SessionConfig(null, 4, tickRate, turnTimeout, responseTimeout)));

        Assert.Equal("config", exception.ParamName);
    }

    [Fact]
    public void Constructor_DoesNotStartSession()
    {
        GameSession session = Create(new SessionConfig(null, 4));

        Assert.False(session.IsStarted);
        Assert.False(session.IsGameOver);
        Assert.Equal(0, session.Tick);
        Assert.Empty(session.ActiveRequests);
        Assert.Empty(session.DrainEvents(255));
        Assert.Empty(session.DrainEvents(0));
    }

    [Fact]
    public void Start_EmitsSeatStatesForAllFourSeats()
    {
        GameSession session = Create(new SessionConfig(0, 3));

        session.Start();

        Assert.True(session.IsStarted);
        IReadOnlyList<SessionEvent> events = session.DrainEvents(255);
        Assert.Equal(4, events.Count);
        for (byte seat = 0; seat < GameState.PlayerCount; seat++)
        {
            SessionEvent sessionEvent = events[seat];
            Assert.Equal(SessionEventKind.SeatStateChanged, sessionEvent.Kind);
            Assert.Equal(0, sessionEvent.Tick);
            SeatStateChangedPayload payload = SessionEventPayloadReader.ReadSeatStateChanged(sessionEvent);
            Assert.Equal(seat, payload.Seat);
            Assert.Equal(seat == 0 ? (byte)0 : (byte)2, payload.State);
        }
    }

    [Fact]
    public void Start_Twice_Throws()
    {
        GameSession session = Create(new SessionConfig(null, 4));
        session.Start();

        Assert.Throws<InvalidOperationException>(session.Start);
    }

    [Fact]
    public void Step_BeforeStart_Throws()
    {
        GameSession session = Create(new SessionConfig(null, 4));

        Assert.Throws<InvalidOperationException>(() => session.Step(1));
    }

    [Fact]
    public void Step_TickMustStrictlyIncrease()
    {
        GameSession session = Create(new SessionConfig(null, 4));
        session.Start();
        session.Step(5);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Step(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Step(4));
        session.Step(6);
        Assert.Equal(6, session.Tick);
    }

    [Fact]
    public void FirstStep_EmitsRoundStartedDealsTurnStartedDrewAndFirstRequest()
    {
        GameSession session = Create(new SessionConfig(null, 4));
        session.Start();
        Assert.Equal(4, session.DrainEvents(255).Count);

        session.Step(1);
        IReadOnlyList<SessionEvent> events = session.DrainEvents(255);

        Assert.Equal(
            new[]
            {
                SessionEventKind.RoundStarted,
                SessionEventKind.Deal,
                SessionEventKind.Deal,
                SessionEventKind.Deal,
                SessionEventKind.Deal,
                SessionEventKind.TurnStarted,
                SessionEventKind.Drew,
                SessionEventKind.RequestOpened,
            },
            events.Select(sessionEvent => sessionEvent.Kind).ToArray());

        RoundStartedPayload roundStarted = SessionEventPayloadReader.ReadRoundStarted(events[0]);
        Assert.Equal((byte)1, roundStarted.RoundNo);
        Assert.Equal((ushort)84, roundStarted.WallCount);
        byte banker = roundStarted.BankerSeat;
        Assert.Equal(banker, session.State.CurrentSeat);

        for (int index = 0; index < GameState.PlayerCount; index++)
        {
            DealPayload deal = SessionEventPayloadReader.ReadDeal(events[1 + index]);
            Assert.Equal((byte)index, deal.Seat);
            Assert.Equal((byte)13, deal.Count);
            Assert.Equal(13, deal.TileIds.Length);
            Assert.Equal(index == banker ? 14 : 13, session.State.Players[index].Hand.Count);
        }

        TurnStartedPayload turnStarted = SessionEventPayloadReader.ReadTurnStarted(events[5]);
        Assert.Equal(banker, turnStarted.Seat);
        Assert.Equal(1L + 600, turnStarted.DeadlineTick);

        DrewPayload drew = SessionEventPayloadReader.ReadDrew(events[6]);
        Assert.Equal(banker, drew.Seat);
        Assert.Equal(83, drew.WallCount);
        Assert.Equal(14, session.State.Players[banker].Hand.Count);
        Assert.Equal(83, session.State.Wall.Count);

        RequestOpenedPayload opened = SessionEventPayloadReader.ReadRequestOpened(events[7]);
        Assert.Equal(1L, opened.RequestId);
        Assert.Equal(banker, opened.Seat);
        if (opened.Kind == RequestKind.Win)
        {
            Assert.Equal(AllowedVerbs.Win | AllowedVerbs.Pass, opened.Allowed);
        }
        else
        {
            Assert.Equal(RequestKind.Discard, opened.Kind);
            Assert.True(opened.Allowed.HasFlag(AllowedVerbs.Discard));
        }
        Assert.Single(session.ActiveRequests);
        Assert.Equal(opened.RequestId, session.ActiveRequests[0].RequestId);
        Assert.Equal(601L, opened.DeadlineTick);
        Assert.Equal(1L, session.Tick);
        Assert.Equal(136, SessionTestSupport.CountTiles(session.State));
    }

    [Fact]
    public void DrainEvents_RejectsInvalidViewer()
    {
        GameSession session = Create(new SessionConfig(null, 4));

        Assert.Throws<ArgumentOutOfRangeException>(() => session.DrainEvents(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.DrainEvents(254));
    }

    [Fact]
    public void DrainEvents_IsDestructive()
    {
        GameSession session = Create(new SessionConfig(null, 4));
        session.Start();
        Assert.NotEmpty(session.DrainEvents(255));
        Assert.Empty(session.DrainEvents(255));
    }
}
