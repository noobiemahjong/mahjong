using Godot;

namespace Mahjong.Client;

/// <summary>
/// 客户端入口场景的根节点。普通启动打印就绪标记；带 --smoke 时额外打印冒烟标记并在固定帧数后退出。
/// 导出版据此用 <c>Mahjong.exe --smoke</c> 做 AOT 冒烟，无需交互与场景内容。
/// </summary>
public partial class Startup : Node
{
    /// <summary>触发冒烟模式的命令行参数。</summary>
    private const string SmokeArgument = "--smoke";

    /// <summary>冒烟模式退出前保持运行的帧数，用于覆盖首帧初始化。</summary>
    private const int SmokeFrameLimit = 30;

    private bool _smokeMode;
    private int _elapsedFrames;

    public override void _Ready()
    {
        GD.Print("M0.1 START");

        // 引擎把 -- 之后的自定义参数拆进用户参数，导出版直接传参时则落在普通参数里，两处都要认。
        _smokeMode = HasArgument(OS.GetCmdlineArgs(), SmokeArgument)
            || HasArgument(OS.GetCmdlineUserArgs(), SmokeArgument);
        if (_smokeMode)
        {
            GD.Print("M0.1 SMOKE OK");
        }
    }

    public override void _Process(double delta)
    {
        if (!_smokeMode)
        {
            return;
        }

        _elapsedFrames++;
        if (_elapsedFrames >= SmokeFrameLimit)
        {
            // 默认退出码 0；冒烟全程不写 stderr，脚本以 stderr 为空判定通过。
            GetTree().Quit();
        }
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
