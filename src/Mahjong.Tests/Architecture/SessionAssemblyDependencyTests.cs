using System.Reflection;
using System.Text;
using Mahjong.Session;
using Mahjong.Tests.Support;
using Xunit;

namespace Mahjong.Tests.Architecture;

/// <summary>
/// Mahjong.Session 的无宿主/无网络/无墙钟守卫：反射断言引用集不含 System.Net*、Godot* 与 Mahjong.Client/Server；
/// 递归扫描会话工程源码（排除 bin/obj，等价于 csproj 默认 Compile 项），去注释后禁止网络、IO、并发/锁、
/// 墙钟、睡眠与随机静态源等 token。两条守卫都做「有牙」自检，避免空集断言恒真。
/// </summary>
public sealed class SessionAssemblyDependencyTests
{
    private const string SessionAssemblyName = "Mahjong.Session";

    private static readonly string[] EngineAssemblyPrefixes = ["Godot"];

    private static readonly string[] ForbiddenHostAssemblies = ["Mahjong.Client", "Mahjong.Server"];

    /// <summary>
    /// 会话源码禁止出现的 token：网络/HTTP、IO、线程与锁、睡眠/延迟、真实时钟与静态随机源。
    /// <c>Thread</c> 是 <c>Thread.Sleep</c> 的超集（同时拦 ThreadPool/new Thread）；<c>Environment.TickCount</c>
    /// 同时拦 TickCount64 前缀；契约 §9.4 点名的六个 token 全部包含在内。
    /// </summary>
    private static readonly string[] ForbiddenSourceTokens =
    [
        "System.Net",
        "HttpClient",
        "Socket",
        "System.IO",
        "Thread",
        "Thread.Sleep",
        "Task.Delay",
        "Task.Run",
        "lock (",
        "lock(",
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTimeOffset",
        "Stopwatch",
        "Environment.TickCount",
        "Random.Shared",
    ];

    [Fact]
    public void ReferenceSetIsReadableAndPointsAtTheSessionAssembly()
    {
        string[] names = ReferenceNames();

        Assert.Equal(SessionAssemblyName, SessionAssembly.GetName().Name);
        Assert.NotEmpty(names);
        Assert.Contains("Mahjong.Core", names);
    }

    [Fact]
    public void SessionAssembly_DoesNotReferenceNetworkEngineOrHostAssemblies()
    {
        // 有牙自检：谓词必须命中合成违规名、放行合法名，否则下面的 Empty 断言可能是恒真。
        Assert.True(IsForbiddenReferenceName("System.Net.Http"));
        Assert.True(IsForbiddenReferenceName("System.Net.Primitives"));
        Assert.True(IsForbiddenReferenceName("GodotSharp"));
        Assert.True(IsForbiddenReferenceName("mahjong.client"));
        Assert.False(IsForbiddenReferenceName("Mahjong.Core"));
        Assert.False(IsForbiddenReferenceName("System.Runtime"));

        string[] offenders = ReferenceNames().Where(IsForbiddenReferenceName).ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void SessionSources_ContainNoNetworkIoConcurrencyClockSleepOrDelay()
    {
        string[] files = SessionSourceFiles();
        Assert.True(files.Length >= 12, $"Session 源码文件数 {files.Length}，少于预期 12，扫描可能没覆盖真实源码。");
        Assert.Contains(files, path => Path.GetFileName(path) == "GameSession.cs");
        Assert.Contains(files, path => Path.GetFileName(path) == "SessionEventBuffer.cs");
        Assert.Contains(files, path => Path.GetFileName(path) == "SessionRequestRegistry.cs");

        List<string> offenders = [];
        foreach (string path in files)
        {
            string source = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(source), $"{Path.GetFileName(path)} 内容为空。");
            foreach (string token in FindForbiddenSourceTokens(source))
            {
                offenders.Add($"{path}: {token}");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void SourceScanner_DetectsForbiddenTokensAndIgnoresComments()
    {
        const string dirty = """
            using System.Net.Sockets;
            using System.IO;
            internal sealed class Dirty
            {
                private readonly object _gate = new();

                internal void UsesForbiddenApis()
                {
                    object socket = new Socket();
                    HttpClient client = new();
                    Thread.Sleep(1);
                    _ = Task.Delay(1);
                    _ = Task.Run(() => 1);
                    _ = DateTime.Now;
                    _ = DateTime.UtcNow;
                    _ = DateTimeOffset.UtcNow;
                    Stopwatch stopwatch = new();
                    _ = Environment.TickCount;
                    _ = Random.Shared;
                    lock (_gate)
                    {
                        _ = socket;
                        _ = client;
                        _ = stopwatch;
                    }

                    lock(_gate)
                    {
                    }
                }
            }
            """;

        // 声明顺序稳定：去注释后按 token 表顺序命中。
        Assert.Equal(ForbiddenSourceTokens, FindForbiddenSourceTokens(dirty));

        // 注释里的同名文字（含 XML doc）不得误报。
        const string clean = """
            /// <summary>不引用 System.Net、Socket、Thread.Sleep、DateTime.Now、Environment.TickCount、Task.Delay。</summary>
            // System.Net Socket Thread.Sleep DateTime.Now Environment.TickCount Task.Delay
            /* System.Net Socket Thread.Sleep DateTime.Now Environment.TickCount Task.Delay lock ( */
            internal sealed class Clean
            {
                internal int Value => 1;
            }
            """;

        Assert.Empty(FindForbiddenSourceTokens(clean));
    }

    private static Assembly SessionAssembly => typeof(GameSession).Assembly;

    private static string[] ReferenceNames() =>
        SessionAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

    private static bool IsForbiddenReferenceName(string name) =>
        EngineAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        || ForbiddenHostAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase)
        || name.StartsWith("System.Net", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 递归枚举会话工程源码，排除编译输出目录：与 csproj 默认 Compile 项（<c>**/*.cs</c>，隐式排除 bin/obj）
    /// 等价，源码迁到子目录后仍被扫描。
    /// </summary>
    private static string[] SessionSourceFiles()
    {
        string separator = Path.DirectorySeparatorChar.ToString();
        string[] buildDirectories = [separator + "bin" + separator, separator + "obj" + separator];
        return Directory
            .GetFiles(
                Path.Combine(RepoPaths.Root, "src", "Mahjong.Session"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !buildDirectories.Any(
                buildDirectory => path.Contains(buildDirectory, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>去掉 C# 注释后按 token 表顺序返回命中项；字符串/字符字面量保留（其中出现的 token 同样判违规）。</summary>
    internal static string[] FindForbiddenSourceTokens(string source)
    {
        string code = StripComments(source);
        return ForbiddenSourceTokens
            .Where(token => code.Contains(token, StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>去掉行注释、块注释（含 XML doc）与字符串/字符转义干扰，保留代码行分隔。</summary>
    internal static string StripComments(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        StringBuilder builder = new(source.Length);
        bool inLineComment = false;
        bool inBlockComment = false;
        bool inString = false;
        bool inVerbatimString = false;
        bool inChar = false;

        for (int index = 0; index < source.Length; index++)
        {
            char current = source[index];
            char next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                    builder.Append('\n');
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index++;
                }
                else if (current == '\n')
                {
                    builder.Append('\n');
                }

                continue;
            }

            if (inVerbatimString)
            {
                builder.Append(current);
                if (current == '"')
                {
                    if (next == '"')
                    {
                        builder.Append(next);
                        index++;
                    }
                    else
                    {
                        inVerbatimString = false;
                    }
                }

                continue;
            }

            if (inString || inChar)
            {
                builder.Append(current);
                if (current == '\\')
                {
                    if (next != '\0')
                    {
                        builder.Append(next);
                        index++;
                    }

                    continue;
                }

                if (inString && current == '"')
                {
                    inString = false;
                }
                else if (inChar && current == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                inLineComment = true;
                index++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index++;
                continue;
            }

            if (current == '@' && next == '"')
            {
                inVerbatimString = true;
                builder.Append(current).Append(next);
                index++;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                builder.Append(current);
                continue;
            }

            if (current == '\'')
            {
                inChar = true;
                builder.Append(current);
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
