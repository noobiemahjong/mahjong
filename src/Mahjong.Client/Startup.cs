using Godot;
using Mahjong.Core.Content;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;
using Mahjong.Session;

namespace Mahjong.Client;

/// <summary>
/// 客户端入口场景的根节点。普通启动打印就绪标记后延迟切到本地对局场景。
/// 带 <c>--smoke</c> 时加载内容目录与查表、打印 contentHash 与表条目数，固定帧数后以 0 退出。
/// 带 <c>--autoplay</c> 时跳过 UI，用四 Bot 会话同步跑完 8 局，打印终局标记后按结果退出。
/// 内容或查表加载失败时打印原因并以非 0 退出，供 AOT 冒烟脚本判定导出产物可用。
/// </summary>
public partial class Startup : Node
{
    /// <summary>触发冒烟模式的命令行参数。</summary>
    private const string SmokeArgument = "--smoke";

    /// <summary>触发无 UI 自动对局模式的命令行参数；与 <c>--smoke</c> 同时给出时优先。</summary>
    private const string AutoplayArgument = "--autoplay";

    /// <summary>冒烟模式退出前保持运行的帧数，用于覆盖首帧初始化。</summary>
    private const int SmokeFrameLimit = 30;

    /// <summary>普通启动进入的本地对局场景。</summary>
    private const string LocalGameScenePath = "res://LocalGame.tscn";

    /// <summary>自动对局的固定种子；同 seed 同输入序列得到同一场对局。</summary>
    private const ulong AutoplaySeed = 20261031UL;

    /// <summary>自动对局的逻辑 tick 频率；与单机对局一致的 10Hz。</summary>
    private const int AutoplayTickRate = 10;

    /// <summary>自动对局的全局步数上限；超出视为流程未收敛，按失败退出。</summary>
    private const int AutoplayMaxTicks = 1_000_000;

    /// <summary>8 局赛制的总轮数；只用于校验事件计数，不参与输出。</summary>
    private const int AutoplayRoundCount = 8;

    /// <summary>全景观察座位：事件编码不做可见性过滤。</summary>
    private const byte FullViewSeat = 255;

    private bool _smokeMode;
    private bool _autoplayMode;
    private bool _failed;
    private int _elapsedFrames;

    public override void _Ready()
    {
        GD.Print("M0.1 START");

        // 引擎把 -- 之后的自定义参数拆进用户参数，导出版直接传参时则落在普通参数里，两处都要认。
        _smokeMode = HasArgument(OS.GetCmdlineArgs(), SmokeArgument)
            || HasArgument(OS.GetCmdlineUserArgs(), SmokeArgument);
        _autoplayMode = HasArgument(OS.GetCmdlineArgs(), AutoplayArgument)
            || HasArgument(OS.GetCmdlineUserArgs(), AutoplayArgument);
        if (_autoplayMode)
        {
            // --autoplay 优先于 --smoke：两者同时给出时不切 UI，也不跑冒烟模式。
            RunAutoplay();
            return;
        }

        if (!_smokeMode)
        {
            // 延迟到本帧 _Ready 之后切场景：同步 ChangeSceneToFile 会切断当前场景的初始化。
            Callable.From(OpenLocalGame).CallDeferred();
            return;
        }

        try
        {
            string contentDirectory = ContentPaths.Resolve();
            ContentCatalog content = ContentCatalog.Load(contentDirectory);

            // 内容目录在查表之前打印：启动耗时以这一行为终点，查表耗时单独看 TABLES OK。
            GD.Print($"CONTENT OK {content.ContentHashHex}");

            TableCatalog tables = TableCatalog.Load(Path.Combine(contentDirectory, "tables"));
            PrintTableCounts(tables);
            GD.Print("M0.1 SMOKE OK");
        }
        catch (Exception exception)
        {
            GD.PrintErr($"M0.1 SMOKE FAILED: {exception.GetType().Name}: {exception.Message}");
            _failed = true;
            GetTree().Quit(1);
        }
    }

    public override void _Process(double delta)
    {
        if (!_smokeMode || _failed)
        {
            return;
        }

        _elapsedFrames++;
        if (_elapsedFrames >= SmokeFrameLimit)
        {
            // 默认退出码 0；冒烟成功路径的 stderr 只有 Godot 引擎的证书存储错误，由 tools/aot-smoke 的白名单整段匹配。
            GetTree().Quit();
        }
    }

    /// <summary>
    /// 无 UI 自动对局：装配四 Bot 会话并推进到 8 局结束，校验局数、终局事件、总分零和与名次排列后退出。
    /// 成功打印 <c>GAME OK rounds=&lt;事件计数&gt;</c> 并以 0 退出；任一步失败打印 <c>AUTOPLAY FAILED</c> 并以 1 退出，失败路径不打印成功标记。
    /// 全程同步单线程、不写盘：AOT 导出产物只靠这一条进程判定可运行。
    /// </summary>
    private void RunAutoplay()
    {
        try
        {
            string contentDirectory = ContentPaths.Resolve();
            ContentCatalog content = ContentCatalog.Load(contentDirectory);
            TableCatalog tables = TableCatalog.Load(Path.Combine(contentDirectory, "tables"));

            GameSession session = LocalSessionFactory.CreateFourBots(
                content,
                tables,
                AutoplaySeed,
                AutoplayTickRate);
            session.Start();

            // 逻辑就绪终点：会话已装配且已启动，但尚未推进任何 tick。
            GD.Print("AUTOPLAY READY");

            int roundStarts = 0;
            int roundEnds = 0;
            bool gameEnded = false;
            GameEndedPayload gameResult = default;

            int steps = 0;
            while (!session.IsGameOver)
            {
                if (steps >= AutoplayMaxTicks)
                {
                    throw new InvalidOperationException(
                        $"对局未在 {AutoplayMaxTicks} 步内结束（tick={session.Tick}）。");
                }

                session.Step(session.Tick + 1);
                steps++;

                // 每步立即排空事件缓冲，计数器只累计本次推进产出的结算事件。
                foreach (SessionEvent sessionEvent in session.DrainEvents(FullViewSeat))
                {
                    switch (sessionEvent.Kind)
                    {
                        case SessionEventKind.RoundStarted:
                            roundStarts++;
                            break;
                        case SessionEventKind.RoundEnded:
                            roundEnds++;
                            break;
                        case SessionEventKind.GameEnded:
                            gameEnded = true;
                            gameResult = SessionEventPayloadReader.ReadGameEnded(sessionEvent);
                            break;
                    }
                }
            }

            VerifyAutoplayResult(roundStarts, roundEnds, gameEnded, gameResult);
            GD.Print($"GAME OK rounds={roundEnds}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PrintErr($"AUTOPLAY FAILED: {exception.GetType().Name}: {exception.Message}");
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// 自动对局的结束校验：8 局开始/结束事件齐全、收到 GameEnded、四家总分和为 0、名次是 0..3 的排列。
    /// 任一不满足抛 <see cref="InvalidOperationException"/>，由 <see cref="RunAutoplay"/> 的失败路径以 1 退出。
    /// </summary>
    /// <param name="roundStarts">RoundStarted 事件计数。</param>
    /// <param name="roundEnds">RoundEnded 事件计数。</param>
    /// <param name="gameEnded">是否收到 GameEnded 事件。</param>
    /// <param name="gameResult">GameEnded 的 payload；仅在 <paramref name="gameEnded"/> 为 true 时有效。</param>
    /// <exception cref="InvalidOperationException">局数、终局事件、总分或名次不满足契约。</exception>
    private static void VerifyAutoplayResult(int roundStarts, int roundEnds, bool gameEnded, GameEndedPayload gameResult)
    {
        if (roundStarts != AutoplayRoundCount)
        {
            throw new InvalidOperationException($"RoundStarted 事件数={roundStarts}，期望 {AutoplayRoundCount}。");
        }

        if (roundEnds != AutoplayRoundCount)
        {
            throw new InvalidOperationException($"RoundEnded 事件数={roundEnds}，期望 {AutoplayRoundCount}。");
        }

        if (!gameEnded)
        {
            throw new InvalidOperationException("会话已结束但没有收到 GameEnded 事件。");
        }

        if (gameResult.TotalScores.Length != GameState.PlayerCount)
        {
            throw new InvalidOperationException(
                $"TotalScores 长度={gameResult.TotalScores.Length}，期望 {GameState.PlayerCount}。");
        }

        int total = 0;
        foreach (int score in gameResult.TotalScores)
        {
            total += score;
        }

        if (total != 0)
        {
            throw new InvalidOperationException($"TotalScores 四家之和={total}，期望 0。");
        }

        if (gameResult.Ranking.Length != GameState.PlayerCount)
        {
            throw new InvalidOperationException(
                $"Ranking 长度={gameResult.Ranking.Length}，期望 {GameState.PlayerCount}。");
        }

        var rankedSeats = new bool[GameState.PlayerCount];
        foreach (byte seat in gameResult.Ranking)
        {
            if (seat >= GameState.PlayerCount || rankedSeats[seat])
            {
                throw new InvalidOperationException("Ranking 不是 0..3 的排列。");
            }

            rankedSeats[seat] = true;
        }
    }

    /// <summary>打印 6 个查表文件的 key 数量；万/筒/条共用 normal 表，按 Character 取值。</summary>
    private static void PrintTableCounts(TableCatalog tables)
    {
        GD.Print(
            "TABLES OK "
            + $"normalWin={tables.WinRowKeyCount(SuitType.Character)}"
            + $" windWin={tables.WinRowKeyCount(SuitType.Wind)}"
            + $" dragonWin={tables.WinRowKeyCount(SuitType.Dragon)}"
            + $" normalAi={tables.AiRowKeyCount(SuitType.Character)}"
            + $" windAi={tables.AiRowKeyCount(SuitType.Wind)}"
            + $" dragonAi={tables.AiRowKeyCount(SuitType.Dragon)}");
    }

    /// <summary>普通启动：切换到本地对局场景。</summary>
    private void OpenLocalGame() => GetTree().ChangeSceneToFile(LocalGameScenePath);

    private static bool HasArgument(string[] arguments, string expected)
    {
        foreach (string argument in arguments)
        {
            if (string.Equals(argument, expected, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
