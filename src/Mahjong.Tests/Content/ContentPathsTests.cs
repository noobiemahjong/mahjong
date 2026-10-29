using Mahjong.Core.Content;
using Mahjong.Tests.Support;
using Xunit;

namespace Mahjong.Tests.Content;

/// <summary>
/// <see cref="ContentPaths.Resolve"/> 读进程级环境变量，且 BaseDirectory 分支会创建目录，
/// 这些测试放在禁用并行的集合里，并在 finally 中恢复环境变量与临时目录，避免污染其它测试类。
/// </summary>
[Collection("content-paths")]
public sealed class ContentPathsTests
{
    [Fact]
    public void Resolve_EnvironmentVariableIsAuthoritative()
    {
        var directory = CreateTempDirectory();
        try
        {
            WithEnvironmentVariable(directory, () =>
                Assert.Equal(Path.GetFullPath(directory), ContentPaths.Resolve(), ignoreCase: true));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void Resolve_EnvironmentVariablePointingToMissingDirectory_ThrowsWithoutFallback()
    {
        var missing = Path.Combine(CreateTempDirectory(), "does-not-exist");
        try
        {
            WithEnvironmentVariable(missing, () =>
            {
                var exception = Assert.Throws<ContentLoadException>(() => ContentPaths.Resolve());
                Assert.Equal(Path.GetFullPath(missing), exception.Path, ignoreCase: true);
                Assert.Contains(ContentPaths.EnvironmentVariable, exception.Message, StringComparison.Ordinal);
            });
        }
        finally
        {
            DeleteTempDirectory(Path.GetDirectoryName(missing)!);
        }
    }

    [Fact]
    public void Resolve_EnvironmentVariablePointingToFile_Throws()
    {
        var directory = CreateTempDirectory();
        var file = Path.Combine(directory, "content-dir-is-a-file.txt");
        File.WriteAllText(file, "not a directory");
        try
        {
            WithEnvironmentVariable(file, () =>
            {
                var exception = Assert.Throws<ContentLoadException>(() => ContentPaths.Resolve());
                Assert.Equal(Path.GetFullPath(file), exception.Path, ignoreCase: true);
            });
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void Resolve_WhitespaceEnvironmentVariableIsIgnoredAndFallsThrough()
    {
        // 空白视为未设置：若被当成权威值会因"目录不存在"抛错；这里断言它回落到 BaseDirectory 分支。
        var baseDirectoryContent = Path.Combine(AppContext.BaseDirectory, ContentPaths.ContentDirectoryName);
        WithEnvironmentVariable("   ", () =>
        {
            Directory.CreateDirectory(baseDirectoryContent);
            try
            {
                Assert.Equal(Path.GetFullPath(baseDirectoryContent), ContentPaths.Resolve(), ignoreCase: true);
            }
            finally
            {
                Directory.Delete(baseDirectoryContent, recursive: true);
            }
        });
    }

    [Fact]
    public void Resolve_UsesBaseDirectoryContentWhenPresent()
    {
        var baseDirectoryContent = Path.Combine(AppContext.BaseDirectory, ContentPaths.ContentDirectoryName);
        WithEnvironmentVariable(null, () =>
        {
            Directory.CreateDirectory(baseDirectoryContent);
            try
            {
                Assert.Equal(Path.GetFullPath(baseDirectoryContent), ContentPaths.Resolve(), ignoreCase: true);
            }
            finally
            {
                Directory.Delete(baseDirectoryContent, recursive: true);
            }
        });
    }

    [Fact]
    public void ResolveUpwards_FindsContentInAncestorBeforeBoundary()
    {
        var repository = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(repository, "global.json"), "{}");
            var content = Path.Combine(repository, ContentPaths.ContentDirectoryName);
            Directory.CreateDirectory(content);
            var start = Path.Combine(repository, "deep", "nested");
            Directory.CreateDirectory(start);

            Assert.Equal(Path.GetFullPath(content), ContentPaths.ResolveUpwards(start), ignoreCase: true);
        }
        finally
        {
            DeleteTempDirectory(repository);
        }
    }

    [Fact]
    public void ResolveUpwards_ContentWinsOverBoundaryMarkerInSameDirectory()
    {
        var repository = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(repository, "global.json"), "{}");
            var content = Path.Combine(repository, ContentPaths.ContentDirectoryName);
            Directory.CreateDirectory(content);

            Assert.Equal(Path.GetFullPath(content), ContentPaths.ResolveUpwards(repository), ignoreCase: true);
        }
        finally
        {
            DeleteTempDirectory(repository);
        }
    }

    [Fact]
    public void ResolveUpwards_StopsAtGlobalJsonBoundaryAndReportsSearchStart()
    {
        var repository = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(repository, "global.json"), "{}");
            var start = Path.Combine(repository, "deep");
            Directory.CreateDirectory(start);

            var exception = Assert.Throws<ContentLoadException>(() => ContentPaths.ResolveUpwards(start));
            Assert.Equal(Path.GetFullPath(start), exception.Path, ignoreCase: true);
            Assert.Contains(start, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempDirectory(repository);
        }
    }

    [Fact]
    public void ResolveUpwards_StopsAtGitDirectoryBoundary()
    {
        var repository = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(repository, ".git"));
            var start = Path.Combine(repository, "deep");
            Directory.CreateDirectory(start);

            var exception = Assert.Throws<ContentLoadException>(() => ContentPaths.ResolveUpwards(start));
            Assert.Contains(start, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempDirectory(repository);
        }
    }

    private static void WithEnvironmentVariable(string? value, Action action)
    {
        var original = Environment.GetEnvironmentVariable(ContentPaths.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ContentPaths.EnvironmentVariable, value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ContentPaths.EnvironmentVariable, original);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mahjong-content-paths-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>与 <see cref="ContentPathsTests"/> 配套的禁并行集合定义。</summary>
[CollectionDefinition("content-paths", DisableParallelization = true)]
public sealed class ContentPathsCollection
{
}
