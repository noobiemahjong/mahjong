namespace Mahjong.Server;

/// <summary>无头宿主入口：M0.1 只要求启动成功并正常退出，不写 stderr。</summary>
internal static class Program
{
    private static int Main()
    {
        Console.WriteLine("M0.1 SERVER OK");
        return 0;
    }
}
