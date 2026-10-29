using System.Diagnostics;
using Mahjong.Core.Content.Tables;
using Mahjong.Tests.Support;
using Xunit;

namespace Mahjong.Tests.Content.Tables;

/// <summary>
/// 真实查表（仓库 <c>content/tables</c>，约 15 MB）的共享 fixture：整个测试进程只加载一次。
/// 使用 <c>[Collection("table-catalog")]</c> 的测试类串行执行并复用同一实例。
/// </summary>
public sealed class TableCatalogFixture
{
    public TableCatalogFixture()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Catalog = TableCatalog.Load(RepoPaths.TablesDirectory);
        stopwatch.Stop();
        LoadElapsed = stopwatch.Elapsed;
        Console.WriteLine(
            $"[table-catalog] TableCatalog.Load({RepoPaths.TablesDirectory}) = {LoadElapsed.TotalMilliseconds:F1} ms");
    }

    /// <summary>仓库 content/tables 下 6 个真实查表的只读视图。</summary>
    public TableCatalog Catalog { get; }

    /// <summary>fixture 构造时的冷加载耗时。</summary>
    public TimeSpan LoadElapsed { get; }
}

[CollectionDefinition("table-catalog")]
public sealed class TableCatalogCollectionDefinition : ICollectionFixture<TableCatalogFixture>
{
}
