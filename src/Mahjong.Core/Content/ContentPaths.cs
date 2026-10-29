using System.Diagnostics.CodeAnalysis;

namespace Mahjong.Core.Content;

/// <summary>
/// 定位 <c>content/</c> 根目录。判定顺序：环境变量 <c>MAHJONG_CONTENT_DIR</c>（非空白即权威，失败不回退）→
/// <see cref="AppContext.BaseDirectory"/> 下的 <c>content/</c> → 从当前目录逐级向上找到 <c>content/</c>，
/// 遇到包含 <c>global.json</c> 或 <c>.git</c> 的目录即停止。
/// </summary>
public static class ContentPaths
{
    /// <summary>显式指定内容根目录的环境变量名。</summary>
    public const string EnvironmentVariable = "MAHJONG_CONTENT_DIR";

    /// <summary>内容目录名。</summary>
    public const string ContentDirectoryName = "content";

    /// <summary>
    /// 解析内容根目录的绝对路径。
    /// </summary>
    /// <returns><c>content/</c> 的绝对路径；查表目录由调用方拼 <c>tables/</c>。</returns>
    /// <exception cref="ContentLoadException">环境变量指向的目录不存在，或搜索完仍找不到内容目录。</exception>
    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var configuredPath = GetFullPathOrThrow(configured, $"environment variable {EnvironmentVariable} is not a valid path");
            if (!Directory.Exists(configuredPath))
            {
                throw new ContentLoadException(
                    configuredPath,
                    $"environment variable {EnvironmentVariable} must point to an existing directory; no fallback is attempted.");
            }

            return configuredPath;
        }

        var baseDirectoryCandidate = Path.Combine(AppContext.BaseDirectory, ContentDirectoryName);
        if (Directory.Exists(baseDirectoryCandidate))
        {
            return GetFullPathOrThrow(baseDirectoryCandidate, "base directory content path is not a valid path");
        }

        return ResolveUpwards(Environment.CurrentDirectory);
    }

    /// <summary>
    /// 从 <paramref name="startDirectory"/> 逐级向上找 <c>content/</c>；每个目录先看 <c>content/</c>，
    /// 再看是否包含 <c>global.json</c> 或 <c>.git</c> 作为停界。
    /// </summary>
    /// <exception cref="ContentLoadException">搜索到停界或文件系统根目录仍未找到内容目录。</exception>
    internal static string ResolveUpwards(string startDirectory)
    {
        var start = GetFullPathOrThrow(startDirectory, "search start directory is not a valid path");
        var directory = new DirectoryInfo(start);
        var probed = new List<string>();

        while (directory is not null)
        {
            probed.Add(directory.FullName);
            var candidate = Path.Combine(directory.FullName, ContentDirectoryName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            if (File.Exists(Path.Combine(directory.FullName, "global.json"))
                || Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                break;
            }

            directory = directory.Parent;
        }

        throw new ContentLoadException(
            start,
            $"no '{ContentDirectoryName}' directory was found while searching upwards from '{start}'; "
            + $"probed: {string.Join(", ", probed)}. Set {EnvironmentVariable} to the content root explicitly.");
    }

    [return: NotNullIfNotNull(nameof(path))]
    private static string? GetFullPathOrThrow(string? path, string reason)
    {
        if (path is null)
        {
            throw new ContentLoadException(string.Empty, reason);
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ContentLoadException(path, reason + ": " + exception.Message, exception);
        }
    }
}
