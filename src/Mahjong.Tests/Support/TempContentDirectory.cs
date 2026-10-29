using System.Security.Cryptography;
using System.Text;

namespace Mahjong.Tests.Support;

/// <summary>
/// 在系统临时目录里搭一份最小合法内容集（4 个 JSON + 6 个占位查表文件 + manifest.json），
/// 并提供改写单个文件、重算清单的能力；用完 <see cref="Dispose"/> 清理。
/// 内容加载只对查表文件做哈希校验、不解析其内容，所以占位字节足以走通合法路径。
/// </summary>
public sealed class TempContentDirectory : IDisposable
{
    /// <summary>manifest.files 的固定键集合，与 <c>ContentCatalog</c> 的清单一致。</summary>
    public static readonly string[] ManifestFilePaths =
    [
        "rules.json",
        "skills.json",
        "suits.json",
        "tables/ai_dictionary_dragon.bytes",
        "tables/ai_dictionary_normal.bytes",
        "tables/ai_dictionary_wind.bytes",
        "tables/dictionary_dragon.bytes",
        "tables/dictionary_normal.bytes",
        "tables/dictionary_wind.bytes",
        "tiles.json",
    ];

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _root;

    public TempContentDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "mahjong-content-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "tables"));
        foreach (var relativePath in ManifestFilePaths)
        {
            if (relativePath.StartsWith("tables/", StringComparison.Ordinal))
            {
                WriteBytes(relativePath, Encoding.ASCII.GetBytes("placeholder:" + relativePath));
            }
        }

        WriteText("tiles.json", DefaultTilesJson);
        WriteText("suits.json", DefaultSuitsJson);
        WriteText("skills.json", DefaultSkillsJson);
        WriteText("rules.json", DefaultRulesJson);
        RebuildManifest();
    }

    public string Root => _root;

    public string PathOf(string relativePath) =>
        Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public string ReadText(string relativePath) => File.ReadAllText(PathOf(relativePath), Encoding.UTF8);

    public void WriteText(string relativePath, string text)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Utf8NoBom);
    }

    public void WriteBytes(string relativePath, byte[] bytes)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    public void DeleteFile(string relativePath) => File.Delete(PathOf(relativePath));

    public void ReplaceText(string relativePath, string oldText, string newText)
    {
        var text = ReadText(relativePath);
        if (!text.Contains(oldText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{oldText}' was not found in {relativePath}.");
        }

        WriteText(relativePath, text.Replace(oldText, newText, StringComparison.Ordinal));
    }

    /// <summary>按磁盘字节实算清单里每个文件的 sha256（小写 hex）。</summary>
    public Dictionary<string, string> ComputeFileHashes() => ComputeHashesIn(_root);

    /// <summary>按规格 §1.1 的算法独立重算 contentHash（64 位小写 hex）。</summary>
    public static string ComputeContentHashHex(IReadOnlyDictionary<string, string> fileHashes)
    {
        var sortedPaths = fileHashes.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var input = new StringBuilder("v1\n");
        foreach (var relativePath in sortedPaths)
        {
            input.Append(relativePath).Append('=').Append(fileHashes[relativePath]).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
    }

    /// <summary>按给定文件哈希写 manifest.json；<paramref name="contentHashHex"/> 原样写入。</summary>
    public void WriteManifest(IReadOnlyDictionary<string, string> fileHashes, string contentHashHex) =>
        WriteManifestIn(_root, fileHashes, contentHashHex);

    /// <summary>按磁盘实算重写本目录的 manifest.json（全部哈希与 contentHash 都正确）。</summary>
    public void RebuildManifest() => RebuildManifestIn(_root);

    /// <summary>对任意内容目录按磁盘实算重写 manifest.json；用于真实内容的临时副本。</summary>
    public static void RebuildManifestIn(string contentRoot)
    {
        var hashes = ComputeHashesIn(contentRoot);
        WriteManifestIn(contentRoot, hashes, ComputeContentHashHex(hashes));
    }

    /// <summary>对任意内容目录按磁盘字节实算清单里每个文件的 sha256（小写 hex）。</summary>
    public static Dictionary<string, string> ComputeHashesIn(string contentRoot)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in ManifestFilePaths)
        {
            var path = Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            hashes[relativePath] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        }

        return hashes;
    }

    /// <summary>按给定文件哈希写任意内容目录的 manifest.json；<paramref name="contentHashHex"/> 原样写入。</summary>
    public static void WriteManifestIn(
        string contentRoot,
        IReadOnlyDictionary<string, string> fileHashes,
        string contentHashHex)
    {
        var sortedPaths = fileHashes.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var json = new StringBuilder();
        json.Append("{\n  \"version\": 1,\n  \"files\": {\n");
        for (var index = 0; index < sortedPaths.Length; index++)
        {
            var relativePath = sortedPaths[index];
            json.Append("    \"").Append(relativePath).Append("\": \"").Append(fileHashes[relativePath]).Append('"');
            if (index < sortedPaths.Length - 1)
            {
                json.Append(',');
            }

            json.Append('\n');
        }

        json.Append("  },\n  \"contentHash\": \"").Append(contentHashHex).Append("\"\n}\n");
        File.WriteAllText(
            Path.Combine(contentRoot, "manifest.json"),
            json.ToString(),
            Utf8NoBom);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言结果。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public const string DefaultTilesJson = """
        {
          "version": 1,
          "tiles": [
            { "id": 0, "suit": "Character", "rank": 1 },
            { "id": 1, "suit": "Character", "rank": 2 },
            { "id": 2, "suit": "Character", "rank": 3 },
            { "id": 3, "suit": "Character", "rank": 4 },
            { "id": 4, "suit": "Character", "rank": 5 },
            { "id": 5, "suit": "Character", "rank": 6 },
            { "id": 6, "suit": "Character", "rank": 7 },
            { "id": 7, "suit": "Character", "rank": 8 },
            { "id": 8, "suit": "Character", "rank": 9 },
            { "id": 9, "suit": "Dot", "rank": 1 },
            { "id": 10, "suit": "Dot", "rank": 2 },
            { "id": 11, "suit": "Dot", "rank": 3 },
            { "id": 12, "suit": "Dot", "rank": 4 },
            { "id": 13, "suit": "Dot", "rank": 5 },
            { "id": 14, "suit": "Dot", "rank": 6 },
            { "id": 15, "suit": "Dot", "rank": 7 },
            { "id": 16, "suit": "Dot", "rank": 8 },
            { "id": 17, "suit": "Dot", "rank": 9 },
            { "id": 18, "suit": "Bamboo", "rank": 1 },
            { "id": 19, "suit": "Bamboo", "rank": 2 },
            { "id": 20, "suit": "Bamboo", "rank": 3 },
            { "id": 21, "suit": "Bamboo", "rank": 4 },
            { "id": 22, "suit": "Bamboo", "rank": 5 },
            { "id": 23, "suit": "Bamboo", "rank": 6 },
            { "id": 24, "suit": "Bamboo", "rank": 7 },
            { "id": 25, "suit": "Bamboo", "rank": 8 },
            { "id": 26, "suit": "Bamboo", "rank": 9 },
            { "id": 27, "suit": "Wind", "rank": 1 },
            { "id": 28, "suit": "Wind", "rank": 2 },
            { "id": 29, "suit": "Wind", "rank": 3 },
            { "id": 30, "suit": "Wind", "rank": 4 },
            { "id": 31, "suit": "Dragon", "rank": 1 },
            { "id": 32, "suit": "Dragon", "rank": 2 },
            { "id": 33, "suit": "Dragon", "rank": 3 }
          ]
        }

        """;

    public const string DefaultSuitsJson = """
        {
          "version": 1,
          "suits": [
            { "suit": "Character", "rankCount": 9, "amountOfEachRank": 4, "allowedSequence": true },
            { "suit": "Dot", "rankCount": 9, "amountOfEachRank": 4, "allowedSequence": true },
            { "suit": "Bamboo", "rankCount": 9, "amountOfEachRank": 4, "allowedSequence": true },
            { "suit": "Wind", "rankCount": 4, "amountOfEachRank": 4, "allowedSequence": false },
            { "suit": "Dragon", "rankCount": 3, "amountOfEachRank": 4, "allowedSequence": false }
          ]
        }

        """;

    public const string DefaultSkillsJson = """
        {
          "version": 1,
          "skills": [
            { "id": 1, "name": "Pong" },
            { "id": 2, "name": "Kong" },
            { "id": 3, "name": "SelfDrawn" },
            { "id": 4, "name": "SelfDrawnWith7Pairs" }
          ]
        }

        """;

    public const string DefaultRulesJson = """
        {
          "version": 1,
          "joker": { "mode": "FixedType", "typeId": 31 }
        }

        """;
}
