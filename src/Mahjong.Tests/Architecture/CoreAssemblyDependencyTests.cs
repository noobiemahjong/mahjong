using Mahjong.Core;
using Xunit;

namespace Mahjong.Tests.Architecture;

/// <summary>
/// 依赖方向守卫：Core 必须保持引擎无关，且不得反向依赖任何宿主程序集。
/// 读 referenced assemblies 而非只看 ProjectReference，因为传递引用同样会破坏分层。
/// </summary>
public sealed class CoreAssemblyDependencyTests
{
    private const string EngineAssemblyPrefix = "Godot";

    private static readonly string[] HostAssemblies = ["Mahjong.Session", "Mahjong.Client", "Mahjong.Server"];

    [Fact]
    public void ReferenceSetIsReadable()
    {
        // 枚举为空时另两条守卫会空断言通过。
        Assert.NotEmpty(ReferencedAssemblyNames());
    }

    [Fact]
    public void CoreDoesNotReferenceEngineAssemblies()
    {
        var offenders = ReferencedAssemblyNames()
            .Where(name => name.StartsWith(EngineAssemblyPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CoreDoesNotReferenceHostAssemblies()
    {
        var offenders = ReferencedAssemblyNames()
            .Where(name => HostAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string[] ReferencedAssemblyNames() =>
        typeof(CoreAssembly).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
}
