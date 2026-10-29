namespace Mahjong.Tests.Support;

/// <summary>
/// 测试用的仓库路径定位：从 <see cref="AppContext.BaseDirectory"/> 向上找同时含
/// <c>global.json</c> 与 <c>content/tables/dictionary_normal.bytes</c> 的仓库根目录。
/// </summary>
public static class RepoPaths
{
    private const string RootMarker = "global.json";

    private static readonly string TableMarker = Path.Combine("content", "tables", "dictionary_normal.bytes");

    private static readonly Lazy<string> RepositoryRoot = new(FindRepositoryRoot);

    public static string Root => RepositoryRoot.Value;

    /// <summary>仓库 <c>content/</c> 的绝对路径。</summary>
    public static string ContentDirectory => Path.Combine(Root, "content");

    /// <summary>仓库 <c>content/tables/</c> 的绝对路径。</summary>
    public static string TablesDirectory => Path.Combine(ContentDirectory, "tables");

    private static string FindRepositoryRoot()
    {
        var probed = new List<string>();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            probed.Add(directory.FullName);
            if (File.Exists(Path.Combine(directory.FullName, RootMarker))
                && File.Exists(Path.Combine(directory.FullName, TableMarker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root: no ancestor of '{AppContext.BaseDirectory}' contains both "
            + $"'{RootMarker}' and '{TableMarker}'. Probed: {string.Join(", ", probed)}.");
    }
}
