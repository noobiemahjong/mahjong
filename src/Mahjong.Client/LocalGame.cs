using System;
using System.Text;
using Godot;
using Mahjong.Client.Input;
using Mahjong.Client.View;
using Mahjong.Core.Content;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Flow;
using Mahjong.Core.Rules;
using Mahjong.Session;

namespace Mahjong.Client;

/// <summary>
/// 单机对局宿主：加载内容与查表、创建本地会话、按 10Hz 累加器推进 tick、消费事件并投影渲染。
/// 所有会话调用只发生在 _Ready/_Process/_UnhandledInput；不跨线程、不加锁、不写盘。
/// </summary>
public partial class LocalGame : Control
{
    /// <summary>固定基准种子；每次重开用 BaseSeed + restartCount，保证可复现且每局不同。</summary>
    private const ulong BaseSeed = 20261031UL;

    /// <summary>逻辑 tick 频率；验收口径 10Hz。</summary>
    private const int SessionTickRate = 10;

    /// <summary>每个 tick 的真实秒数。</summary>
    private const double TickSeconds = 1.0 / SessionTickRate;

    /// <summary>单帧最多推进的 tick 数，防止卡顿后补步形成螺旋。</summary>
    private const int MaxStepsPerFrame = 5;

    /// <summary>M0.2 单机真人座位固定 0；视图仍按 RelativeSeat 公式计算。</summary>
    private const byte HumanSeat = 0;

    private readonly MousePointerSource _mouse = new();
    private readonly TouchPointerSource _touch = new();
    private readonly ClickRouter _clickRouter = new();

    private ContentCatalog? _content;
    private TableCatalog? _tables;
    private GameSession? _session;
    private InputRouter? _inputRouter;
    private ICardAndSkillVerifier? _verifier;
    private ushort _lastDiscardedCardId;
    private Table _table = null!;
    private Control _overlay = null!;
    private Label _title = null!;
    private Label _body = null!;
    private Button _continueButton = null!;
    private Button _restartButton = null!;
    private Button _backButton = null!;
    private TableViewState? _view;

    private int _restartCount;
    private double _accumulator;
    private byte _currentRoundNo = 1;
    private byte _finishedRoundNo = 1;
    private ushort? _selectedTileId;
    private bool _paused;
    private bool _gameOver;
    private bool _localReadyPrinted;
    private string _lastAction = "发牌中";

    /// <summary>取节点、加载内容与查表并启动第一局；内容失败打印原因并以 1 退出。</summary>
    public override void _Ready()
    {
        ApplyUiFont();
        _table = GetNode<Table>("Table");
        _overlay = GetNode<Control>("ResultOverlay");
        _title = GetNode<Label>("ResultOverlay/Title");
        _body = GetNode<Label>("ResultOverlay/Body");
        _continueButton = GetNode<Button>("ResultOverlay/ContinueButton");
        _restartButton = GetNode<Button>("ResultOverlay/RestartButton");
        _backButton = GetNode<Button>("ResultOverlay/BackButton");
        HideOverlay();

        try
        {
            string contentDirectory = ContentPaths.Resolve();
            _content = ContentCatalog.Load(contentDirectory);
            _tables = TableCatalog.Load(Path.Combine(contentDirectory, "tables"));
            // 响应窗口的 Allowed 对全体候选恒置 Kong，按钮启用前要用 Core 的权威校验器收窄。
            _verifier = new CardAndSkillVerifier(new WinChecker(_tables), _content.Joker.Type ?? default);
        }
        catch (Exception exception)
        {
            GD.PrintErr($"local game content load failed: {exception.GetType().Name}: {exception.Message}");
            GetTree().Quit(1);
            return;
        }

        StartSession();
    }

    /// <summary>
    /// 中文 UI 的 CJK 字体回退：运行时创建 SystemFont（不新增 .ttf/.tres 资源），
    /// 挂到根 Control 的 Theme 上向全部子控件级联；AOT 安全（无反射/动态类型）。
    /// </summary>
    private void ApplyUiFont()
    {
        var font = new SystemFont
        {
            FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC" },
            AllowSystemFallback = true,
        };
        Theme = new Theme { DefaultFont = font, DefaultFontSize = 16 };
    }

    /// <summary>把鼠标/触摸事件喂给两条指针源；由 _Process 每帧排空。</summary>
    /// <param name="event">引擎原始输入事件。</param>
    public override void _UnhandledInput(InputEvent @event)
    {
        _mouse.Push(@event);
        _touch.Push(@event);
    }

    /// <summary>
    /// 每帧：投影 → 登记并渲染 → 排空点击 → 10Hz 累加推进 tick。
    /// 一会话内 tick 严格 +1；RoundEnded 暂停、GameEnded 终止；暂停/结束期间不累积时间。
    /// </summary>
    /// <param name="delta">上一帧真实耗时（秒）。</param>
    public override void _Process(double delta)
    {
        if (_session is null)
        {
            return;
        }

        PruneSelection();
        IReadOnlyList<CommandVerb> enabledActions = BuildEnabledActions();
        _view = TableViewProjection.Project(
            _session,
            _content!,
            HumanSeat,
            _selectedTileId,
            BuildStatusLine(enabledActions),
            enabledActions);
        _clickRouter.BeginFrame();
        _table.Render(_view, _clickRouter, OnTileClicked, OnActionClicked);
        RegisterOverlayButtons();
        DrainPointers();
        AdvanceSession(delta);

        // 首帧：投影/渲染/登记/排空/推进都完整走完才视为已到可操作状态；整进程只打印一次。
        if (!_localReadyPrinted)
        {
            _localReadyPrinted = true;
            GD.Print("LOCAL READY");
        }
    }

    /// <summary>选中牌已不在手牌里（超时托管自动出牌、被碰/杠等）时清空选中，避免按钮亮着却提交无效。</summary>
    private void PruneSelection()
    {
        if (_selectedTileId is not { } selected || _session is null)
        {
            return;
        }

        foreach (Tile tile in _session.State.Players[HumanSeat].Hand.Items)
        {
            if (tile.Id == selected)
            {
                return;
            }
        }

        _selectedTileId = null;
    }

    private void AdvanceSession(double delta)
    {
        if (_session is null || _paused || _gameOver || _session.IsGameOver)
        {
            _accumulator = 0;
            return;
        }

        _accumulator += delta;
        int steps = 0;
        while (_accumulator + 1e-9 >= TickSeconds && steps < MaxStepsPerFrame)
        {
            _accumulator -= TickSeconds;
            _session.Step(_session.Tick + 1);
            steps++;
            ConsumeEvents();
            if (_paused || _gameOver)
            {
                _accumulator = 0;
                break;
            }
        }

        if (steps >= MaxStepsPerFrame)
        {
            // 超上限时丢弃本帧剩余累计时间，不补步。
            _accumulator = 0;
        }
    }

    private void DrainPointers()
    {
        while (_mouse.TryRead(out PointerEvent mouseEvent))
        {
            _clickRouter.Feed(mouseEvent);
        }

        while (_touch.TryRead(out PointerEvent touchEvent))
        {
            _clickRouter.Feed(touchEvent);
        }
    }

    private void RegisterOverlayButtons()
    {
        if (!_overlay.Visible)
        {
            return;
        }

        _clickRouter.Register(_restartButton, OnRestartPressed);
        _clickRouter.Register(_backButton, OnBackPressed);
        if (!_gameOver)
        {
            _clickRouter.Register(_continueButton, OnContinuePressed);
        }
    }

    private void OnTileClicked(HandTileView tile)
    {
        if (!CanAct() || !HasEnabled(CommandVerb.Discard))
        {
            return;
        }

        if (_selectedTileId == tile.Id)
        {
            // 二次点击同一张牌 = 出牌。
            if (_inputRouter is not null && _inputRouter.SubmitDiscard(tile.HandSlot))
            {
                _selectedTileId = null;
            }

            return;
        }

        _selectedTileId = tile.Id;
    }

    private void OnActionClicked(CommandVerb verb)
    {
        if (!CanAct() || _inputRouter is null || !HasEnabled(verb))
        {
            return;
        }

        if (verb == CommandVerb.Discard)
        {
            if (_selectedTileId is not { } selected)
            {
                return;
            }

            if (TryFindHandSlot(selected, out int handSlot) && _inputRouter.SubmitDiscard(handSlot))
            {
                _selectedTileId = null;
            }

            return;
        }

        if (_inputRouter.SubmitVerb(verb, 0))
        {
            _selectedTileId = null;
        }
    }

    private void OnContinuePressed()
    {
        if (_gameOver || !_overlay.Visible)
        {
            return;
        }

        HideOverlay();
        _paused = false;
        _accumulator = 0;
    }

    private void OnRestartPressed()
    {
        _restartCount++;
        StartSession();
    }

    private void OnBackPressed() => GetTree().Quit();

    private void StartSession()
    {
        if (_content is null || _tables is null)
        {
            return;
        }

        _session = LocalSessionFactory.CreateSinglePlayer(
            _content,
            _tables,
            BaseSeed + (ulong)_restartCount,
            SessionTickRate);
        _inputRouter = new InputRouter(_session, HumanSeat);
        _session.Start();

        _accumulator = 0;
        _selectedTileId = null;
        _paused = false;
        _gameOver = false;
        _lastDiscardedCardId = 0;
        _currentRoundNo = 1;
        _finishedRoundNo = 1;
        _lastAction = "发牌中";
        HideOverlay();

        // Start 只产座位状态事件；排空以免首步之前缓冲增长。
        ConsumeEvents();
    }

    private void ConsumeEvents()
    {
        if (_session is null || _content is null)
        {
            return;
        }

        IReadOnlyList<SessionEvent> events = _session.DrainEvents(HumanSeat);
        if (events.Count == 0)
        {
            return;
        }

        bool roundEnded = false;
        bool gameEnded = false;
        RoundEndedPayload roundResult = default;
        GameEndedPayload gameResult = default;

        foreach (SessionEvent sessionEvent in events)
        {
            switch (sessionEvent.Kind)
            {
                case SessionEventKind.RoundStarted:
                {
                    RoundStartedPayload payload = SessionEventPayloadReader.ReadRoundStarted(sessionEvent);
                    _currentRoundNo = payload.RoundNo;
                    _lastAction = $"第 {payload.RoundNo} 局开始";
                    break;
                }

                case SessionEventKind.TurnStarted:
                {
                    TurnStartedPayload payload = SessionEventPayloadReader.ReadTurnStarted(sessionEvent);
                    _lastAction = payload.Seat == HumanSeat
                        ? "轮到你"
                        : $"{RelativeName(payload.Seat)}的回合";
                    break;
                }

                case SessionEventKind.Discarded:
                {
                    DiscardedPayload payload = SessionEventPayloadReader.ReadDiscarded(sessionEvent);
                    _lastDiscardedCardId = payload.TileId;
                    _lastAction = $"{RelativeName(payload.Seat)} 打出 {TileLabel(payload.TileId)}";
                    break;
                }

                case SessionEventKind.Meld:
                {
                    MeldPayload payload = SessionEventPayloadReader.ReadMeld(sessionEvent);
                    _lastAction = $"{RelativeName(payload.Seat)} {TableViewProjection.MeldAbbreviation(payload.Kind)}";
                    break;
                }

                case SessionEventKind.Drew:
                {
                    DrewPayload payload = SessionEventPayloadReader.ReadDrew(sessionEvent);
                    _lastAction = payload.Seat == HumanSeat ? "你摸牌" : $"{RelativeName(payload.Seat)} 摸牌";
                    break;
                }

                case SessionEventKind.HorseRevealed:
                {
                    HorseRevealedPayload payload = SessionEventPayloadReader.ReadHorseRevealed(sessionEvent);
                    _lastAction = $"翻马：{RelativeName(payload.HorseSeat)} {TileLabel(payload.TileId)}";
                    break;
                }

                case SessionEventKind.RoundEnded:
                    roundResult = SessionEventPayloadReader.ReadRoundEnded(sessionEvent);
                    _finishedRoundNo = _currentRoundNo;
                    roundEnded = true;
                    break;

                case SessionEventKind.GameEnded:
                    gameResult = SessionEventPayloadReader.ReadGameEnded(sessionEvent);
                    gameEnded = true;
                    break;
            }
        }

        // 同一批里 GameEnded 压过 RoundEnded：终局只显示整局结算层。
        if (gameEnded)
        {
            ShowGameOver(gameResult);
        }
        else if (roundEnded)
        {
            ShowRoundResult(roundResult);
        }
    }

    private void ShowRoundResult(RoundEndedPayload payload)
    {
        if (_session is null)
        {
            return;
        }

        _paused = true;
        _selectedTileId = null;
        _title.Text = $"第 {_finishedRoundNo} 局结束";

        var body = new StringBuilder();
        body.Append(payload.WinnerSeat < 0 ? "流局" : $"赢家 {SeatName((byte)payload.WinnerSeat)}");
        body.Append('\n').Append("动作 ").Append(ActionName(payload.ActionType));
        GameState state = _session.State;
        for (int relativeSeat = 0; relativeSeat < GameState.PlayerCount; relativeSeat++)
        {
            byte seat = SeatOfRelative(relativeSeat);
            body.Append('\n')
                .Append(TableViewProjection.RelativeName(relativeSeat))
                .Append(' ')
                .Append(FormatSigned(payload.ScoreDelta[seat]))
                .Append("  总分 ")
                .Append(FormatSigned(state.Players[seat].Score));
        }

        _body.Text = body.ToString();
        _continueButton.Visible = true;
        ShowOverlay();
    }

    private void ShowGameOver(GameEndedPayload payload)
    {
        if (_session is null)
        {
            return;
        }

        _gameOver = true;
        _paused = false;
        _selectedTileId = null;
        _title.Text = "牌局结束";

        var body = new StringBuilder();
        for (int relativeSeat = 0; relativeSeat < GameState.PlayerCount; relativeSeat++)
        {
            byte seat = SeatOfRelative(relativeSeat);
            body.Append(TableViewProjection.RelativeName(relativeSeat))
                .Append(' ')
                .Append(FormatSigned(payload.TotalScores[seat]))
                .Append('\n');
        }

        for (int rank = 0; rank < payload.Ranking.Length; rank++)
        {
            body.Append("第")
                .Append(rank + 1)
                .Append("名 ")
                .Append(SeatName(payload.Ranking[rank]))
                .Append('\n');
        }

        _body.Text = body.ToString();
        _continueButton.Visible = false;
        ShowOverlay();
    }

    private void ShowOverlay()
    {
        _overlay.Visible = true;
        _restartButton.Visible = true;
        _backButton.Visible = true;
        _table.Modulate = new Color(0.4f, 0.4f, 0.45f);
    }

    private void HideOverlay()
    {
        _overlay.Visible = false;
        _continueButton.Visible = false;
        _restartButton.Visible = true;
        _backButton.Visible = true;
        _table.Modulate = Colors.White;
    }

    /// <summary>
    /// 本座位可用动作：开放请求的 Allowed 位集按固定顺序映射；响应窗口的 Kong 位再用 Core 校验器
    /// 按真实弃牌目标复核——该窗口对全体候选恒置 Kong，直接照搬会把只能碰的局面误亮成可杠。
    /// </summary>
    private IReadOnlyList<CommandVerb> BuildEnabledActions()
    {
        if (_session is null)
        {
            return Array.Empty<CommandVerb>();
        }

        AllowedVerbs allowed = AllowedVerbs.None;
        bool found = false;
        foreach (PendingRequest request in _session.ActiveRequests)
        {
            if (request.Seat != HumanSeat)
            {
                continue;
            }

            AllowedVerbs requestAllowed = request.Allowed;
            if (request.Kind == RequestKind.Pong
                && (requestAllowed & AllowedVerbs.Kong) != 0
                && !IsResponseKongLegal())
            {
                requestAllowed &= ~AllowedVerbs.Kong;
            }

            allowed |= requestAllowed;
            found = true;
        }

        return found ? TableViewProjection.MapAllowedActions(allowed) : Array.Empty<CommandVerb>();
    }

    /// <summary>响应窗口的弃牌目标来自最近一次 Discarded 事件；用 Core 校验器判定人类能否明杠。</summary>
    private bool IsResponseKongLegal()
    {
        if (_session is null || _content is null || _verifier is null || _lastDiscardedCardId == 0)
        {
            return false;
        }

        Tile target = new(
            _lastDiscardedCardId,
            _content.GetTileType((ushort)((_lastDiscardedCardId - 1) / 4)));
        return _verifier.Verify(
            _session.State,
            _session.State.Players[HumanSeat],
            SkillId.Kong,
            card: null,
            targetCard: target);
    }

    private string BuildStatusLine(IReadOnlyList<CommandVerb> enabledActions)
    {
        if (_gameOver)
        {
            return "牌局结束 - 重开或返回";
        }

        if (_paused)
        {
            return $"第 {_finishedRoundNo} 局结束 - 继续或重开";
        }

        if (Contains(enabledActions, CommandVerb.Discard))
        {
            return _selectedTileId is null ? "轮到你 - 请选择一张牌" : "已选牌 - 再点一次或按出牌";
        }

        if (Contains(enabledActions, CommandVerb.Win))
        {
            return "赢牌可用 - 胡或过";
        }

        if (Contains(enabledActions, CommandVerb.Pong) || Contains(enabledActions, CommandVerb.Kong))
        {
            return "响应窗口 - 碰/杠/过";
        }

        return Contains(enabledActions, CommandVerb.Pass) ? "响应窗口 - 过" : _lastAction;
    }

    private bool HasEnabled(CommandVerb verb) =>
        _view is not null && Contains(_view.EnabledActions, verb);

    private static bool Contains(IReadOnlyList<CommandVerb> verbs, CommandVerb verb)
    {
        for (int index = 0; index < verbs.Count; index++)
        {
            if (verbs[index] == verb)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindHandSlot(ushort tileId, out int handSlot)
    {
        if (_view is not null)
        {
            for (int index = 0; index < _view.Hand.Count; index++)
            {
                if (_view.Hand[index].Id == tileId)
                {
                    handSlot = _view.Hand[index].HandSlot;
                    return true;
                }
            }
        }

        handSlot = -1;
        return false;
    }

    private bool CanAct() => _session is not null && !_paused && !_gameOver && !_session.IsGameOver;

    private byte SeatOfRelative(int relativeSeat) => (byte)((HumanSeat + relativeSeat) % GameState.PlayerCount);

    private string RelativeName(byte seat) =>
        TableViewProjection.RelativeName((seat - HumanSeat + GameState.PlayerCount) % GameState.PlayerCount);

    private string SeatName(byte seat) => RelativeName(seat);

    private string TileLabel(ushort cardId) => TableViewProjection.LabelOfCardId(_content!, cardId);

    private static string FormatSigned(int value) => value >= 0 ? $"+{value}" : value.ToString();

    /// <summary>计分动作位集转中文说明；一个动作可同时带多个番种。</summary>
    private static string ActionName(ScoreActionType type)
    {
        if (type == ScoreActionType.None)
        {
            return "无";
        }

        var parts = new List<string>(8);
        if ((type & ScoreActionType.SelfDrawn) != 0) parts.Add("自摸");
        if ((type & ScoreActionType.SevenPairs) != 0) parts.Add("七对");
        if ((type & ScoreActionType.ConcealedKong) != 0) parts.Add("暗杠");
        if ((type & ScoreActionType.ExposedKong) != 0) parts.Add("明杠");
        if ((type & ScoreActionType.KongSupplement) != 0) parts.Add("补杠");
        if ((type & ScoreActionType.KongUpFlower) != 0) parts.Add("杠上开花");
        if ((type & ScoreActionType.NoJoker) != 0) parts.Add("无鬼");
        if ((type & ScoreActionType.DealerStreak) != 0) parts.Add("连庄");
        if ((type & ScoreActionType.FollowingTheBanker) != 0) parts.Add("跟庄");
        if ((type & ScoreActionType.HorseWinner) != 0) parts.Add("中马");
        if ((type & ScoreActionType.RobbingTheKong) != 0) parts.Add("抢杠");
        if ((type & ScoreActionType.RobbingTheKongPayForEveryone) != 0) parts.Add("抢杠包三家");
        if ((type & ScoreActionType.KongUpFlowerPayForEveryone) != 0) parts.Add("杠上开花包三家");
        if ((type & ScoreActionType.TwelveCardsConceded) != 0) parts.Add("12 张包三家");
        return string.Join("、", parts);
    }
}
