using Godot;
using Mahjong.Core.Content;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;

namespace Mahjong.Client;

/// <summary>
/// 客户端入口场景的根节点。普通启动只打印就绪标记，不读取内容目录。
/// 带 <c>--smoke</c> 时加载内容目录与查表、打印 contentHash 与表条目数，固定帧数后以 0 退出。
/// 内容或查表加载失败时打印原因并以非 0 退出，供 AOT 冒烟脚本判定导出产物可用。
/// </summary>
public partial class Startup : Node
{
    /// <summary>触发冒烟模式的命令行参数。</summary>
    private const string SmokeArgument = "--smoke";

    /// <summary>冒烟模式退出前保持运行的帧数，用于覆盖首帧初始化。</summary>
    private const int SmokeFrameLimit = 30;

    private bool _smokeMode;
    private bool _failed;
    private int _elapsedFrames;

    public override void _Ready()
    {
        GD.Print("M0.1 START");

        // 引擎把 -- 之后的自定义参数拆进用户参数，导出版直接传参时则落在普通参数里，两处都要认。
        _smokeMode = HasArgument(OS.GetCmdlineArgs(), SmokeArgument)
            || HasArgument(OS.GetCmdlineUserArgs(), SmokeArgument);
        if (!_smokeMode)
        {
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
