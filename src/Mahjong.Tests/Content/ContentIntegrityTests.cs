using Mahjong.Core.Content;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;
using Mahjong.Tests.Support;
using Xunit;

namespace Mahjong.Tests.Content;

/// <summary>
/// 负向集成测试：每一处破坏都在系统临时目录的副本上做（真实 <c>content/</c> 只读），测完清理。
/// 内容错误断言 <see cref="ContentLoadException"/>（公开 <see cref="ContentLoadException.Path"/> 且消息含路径），
/// 查表错误断言 <see cref="InvalidDataException"/> 且消息含文件路径；
/// 查表用例用独立的 <see cref="TestMsgPackWriter"/> 合成小表，不复制 15 MB 真实表。
/// </summary>
public sealed class ContentIntegrityTests
{
    private static readonly string[] WinTableFiles =
    [
        "dictionary_normal.bytes",
        "dictionary_wind.bytes",
        "dictionary_dragon.bytes",
    ];

    private static readonly string[] AiTableFiles =
    [
        "ai_dictionary_normal.bytes",
        "ai_dictionary_wind.bytes",
        "ai_dictionary_dragon.bytes",
    ];

    // ---------------- 内容清单 / manifest ----------------

    [Fact]
    public void MissingContentFile_ThrowsWithFilePath()
    {
        using var content = new TempContentDirectory();
        content.DeleteFile("skills.json");

        AssertContentLoadError(content, "skills.json", "required content file is missing");
    }

    [Fact]
    public void UnparsableContentJson_ThrowsWithFilePath()
    {
        using var content = new TempContentDirectory();
        content.WriteText("suits.json", "{ \"version\": 1, \"suits\": [ }");
        content.RebuildManifest();

        AssertContentLoadError(content, "suits.json", "JSON is invalid");
    }

    [Fact]
    public void ManifestMissingEntry_Throws()
    {
        using var content = new TempContentDirectory();
        var hashes = content.ComputeFileHashes();
        hashes.Remove("rules.json");
        content.WriteManifest(hashes, TempContentDirectory.ComputeContentHashHex(hashes));

        AssertContentLoadError(content, "manifest.json", "rules.json");
    }

    [Fact]
    public void ManifestExtraEntry_Throws()
    {
        using var content = new TempContentDirectory();
        var hashes = content.ComputeFileHashes();
        hashes["extra.json"] = new string('0', 64);
        content.WriteManifest(hashes, TempContentDirectory.ComputeContentHashHex(hashes));

        AssertContentLoadError(content, "manifest.json", "extra.json");
    }

    [Fact]
    public void ManifestFileHashMismatch_ThrowsAtThatFile()
    {
        using var content = new TempContentDirectory();
        var hashes = content.ComputeFileHashes();
        hashes["tiles.json"] = new string('a', 64);
        content.WriteManifest(hashes, TempContentDirectory.ComputeContentHashHex(hashes));

        AssertContentLoadError(content, "tiles.json", "sha256 mismatch");
    }

    [Fact]
    public void ManifestContentHashMismatch_Throws()
    {
        using var content = new TempContentDirectory();
        content.WriteManifest(content.ComputeFileHashes(), new string('0', 64));

        AssertContentLoadError(content, "manifest.json", "contentHash mismatch");
    }

    [Fact]
    public void JsonChangedWithoutManifestRebuild_ThrowsHashMismatch()
    {
        // 与「手工改了 content 里的 JSON、忘记重算 manifest」一致：只改字节，不调用 RebuildManifest。
        using var content = new TempContentDirectory();
        content.WriteText("tiles.json", TempContentDirectory.DefaultTilesJson + " ");

        AssertContentLoadError(content, "tiles.json", "sha256 mismatch");
    }

    [Fact]
    public void ManifestUppercaseContentHash_Throws()
    {
        using var content = new TempContentDirectory();
        var hashes = content.ComputeFileHashes();
        var upper = TempContentDirectory.ComputeContentHashHex(hashes).ToUpperInvariant();
        content.WriteManifest(hashes, upper);

        AssertContentLoadError(content, "manifest.json", "lowercase");
    }

    [Fact]
    public void ManifestWrongVersion_Throws()
    {
        using var content = new TempContentDirectory();
        // 先写一份哈希自洽的清单，再把 version 改成 2：版本校验先于哈希校验，错误必须来自版本而不是哈希。
        content.RebuildManifest();
        var manifestPath = Path.Combine(content.Root, "manifest.json");
        File.WriteAllText(
            manifestPath,
            File.ReadAllText(manifestPath).Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal));

        AssertContentLoadError(content, "manifest.json", "version must be 1");
    }

    // ---------------- 内容语义 ----------------

    [Fact]
    public void TilesIdGap_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("tiles.json", "\"id\": 5,", "\"id\": 99,");
        content.RebuildManifest();

        AssertContentLoadError(content, "tiles.json", "expected id 5");
    }

    [Fact]
    public void TilesDuplicateId_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("tiles.json", "\"id\": 6,", "\"id\": 5,");
        content.RebuildManifest();

        AssertContentLoadError(content, "tiles.json", "duplicate id 5");
    }

    [Fact]
    public void TilesUnknownSuit_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("tiles.json", "\"suit\": \"Character\"", "\"suit\": \"Circle\"");
        content.RebuildManifest();

        AssertContentLoadError(content, "tiles.json", "unknown suit");
    }

    [Fact]
    public void SuitsWrongValue_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("suits.json", "\"rankCount\": 9", "\"rankCount\": 8");
        content.RebuildManifest();

        AssertContentLoadError(content, "suits.json", "must be");
    }

    [Fact]
    public void SkillsWrongId_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("skills.json", "\"id\": 2,", "\"id\": 3,");
        content.RebuildManifest();

        AssertContentLoadError(content, "skills.json", "expected id 2");
    }

    [Fact]
    public void RulesUnknownMode_Throws()
    {
        using var content = new TempContentDirectory();
        content.WriteText("rules.json", "{\n  \"version\": 1,\n  \"joker\": { \"mode\": \"Random\" }\n}\n");
        content.RebuildManifest();

        AssertContentLoadError(content, "rules.json", "joker.mode");
    }

    [Fact]
    public void RulesFixedTypeWithUnknownTypeId_Throws()
    {
        using var content = new TempContentDirectory();
        content.WriteText("rules.json", "{\n  \"version\": 1,\n  \"joker\": { \"mode\": \"FixedType\", \"typeId\": 99 }\n}\n");
        content.RebuildManifest();

        AssertContentLoadError(content, "rules.json", "does not exist in tiles.json");
    }

    [Fact]
    public void RulesFixedTypeWithoutTypeId_Throws()
    {
        using var content = new TempContentDirectory();
        content.WriteText("rules.json", "{\n  \"version\": 1,\n  \"joker\": { \"mode\": \"FixedType\" }\n}\n");
        content.RebuildManifest();

        AssertContentLoadError(content, "rules.json", "joker.typeId is required");
    }

    [Fact]
    public void RulesFlipIndicator_ThrowsNotSupported()
    {
        using var content = new TempContentDirectory();
        content.WriteText("rules.json", "{\n  \"version\": 1,\n  \"joker\": { \"mode\": \"FlipIndicator\" }\n}\n");
        content.RebuildManifest();

        var exception = Assert.Throws<NotSupportedException>(() => ContentCatalog.Load(content.Root));
        Assert.Contains("rules.json", exception.Message);
    }

    [Fact]
    public void RulesNoneWithTypeId_IsAcceptedAndJokerIsNull()
    {
        using var content = new TempContentDirectory();
        content.WriteText("rules.json", "{\n  \"version\": 1,\n  \"joker\": { \"mode\": \"None\", \"typeId\": 99 }\n}\n");
        content.RebuildManifest();

        var catalog = ContentCatalog.Load(content.Root);
        Assert.Equal(JokerMode.None, catalog.Joker.Mode);
        Assert.False(catalog.Joker.Type.HasValue);
    }

    // ---------------- 查表读取（合成小表） ----------------

    [Fact]
    public void TableTruncated_ThrowsWithFilePath()
    {
        var truncated = ValidWinTableBytes();
        WithSyntheticTables("dictionary_normal.bytes", truncated[..^1], root =>
            AssertTableLoadError(root, "dictionary_normal.bytes"));
    }

    [Fact]
    public void TableTrailingBytes_ThrowsWithFilePath()
    {
        var withTrailing = ValidWinTableBytes().Concat(new byte[] { 0x00 }).ToArray();
        WithSyntheticTables("dictionary_normal.bytes", withTrailing, root =>
            AssertTableLoadError(root, "dictionary_normal.bytes"));
    }

    [Fact]
    public void TableDuplicateKey_ThrowsWithFilePath()
    {
        var duplicate = new TestMsgPackWriter()
            .ArrayHeader(1)
            .FixMap(2)
            .UInt(0).ArrayHeader(1).FixArray(4).Int(0).Bool(true).Bool(true).ArrayHeader(0)
            .UInt(0).ArrayHeader(1).FixArray(4).Int(1).Bool(true).Bool(true).ArrayHeader(0)
            .ToArray();
        WithSyntheticTables("dictionary_normal.bytes", duplicate, root =>
            AssertTableLoadError(root, "dictionary_normal.bytes"));
    }

    [Fact]
    public void TableRowLengthMismatch_ThrowsWithFilePath()
    {
        var shortRow = new TestMsgPackWriter()
            .ArrayHeader(1)
            .FixMap(1)
            .UInt(0).ArrayHeader(1).FixArray(3).Int(0).Bool(true).Bool(true)
            .ToArray();
        WithSyntheticTables("dictionary_normal.bytes", shortRow, root =>
            AssertTableLoadError(root, "dictionary_normal.bytes"));
    }

    [Fact]
    public void TableRowElementTypeMismatch_ThrowsWithFilePath()
    {
        var wrongType = new TestMsgPackWriter()
            .ArrayHeader(1)
            .FixMap(1)
            .UInt(0).ArrayHeader(1).FixArray(4).Bool(true).Bool(true).Bool(true).ArrayHeader(0)
            .ToArray();
        WithSyntheticTables("dictionary_normal.bytes", wrongType, root =>
            AssertTableLoadError(root, "dictionary_normal.bytes"));
    }

    [Fact]
    public void TableDeclaredHeaderOverflow_ThrowsWithFilePath()
    {
        // array32 声明 0xFFFFFFFF 个元素但没有任何数据：必须抛 InvalidDataException，不能按声明预分配/越界。
        var overflow = new TestMsgPackWriter().Array32(0xFFFF_FFFFu).ToArray();
        WithSyntheticTables("dictionary_normal.bytes", overflow, root =>
            AssertTableLoadError(root, "dictionary_normal.bytes"));
    }

    [Fact]
    public void TableUnsupportedTypeCode_ThrowsWithFilePath()
    {
        // fixstr 作为 map key：应被 MsgPackLite 拒绝（本子集只接受整数 key）。
        var unsupported = new TestMsgPackWriter()
            .ArrayHeader(1)
            .FixMap(1)
            .String("key")
            .ArrayHeader(1).FixArray(4).Int(0).Bool(true).Bool(true).ArrayHeader(0)
            .ToArray();
        WithSyntheticTables("dictionary_normal.bytes", unsupported, root =>
            AssertTableLoadError(root, "dictionary_normal.bytes"));
    }

    [Fact]
    public void TableMissingFile_ThrowsWithFilePath()
    {
        WithSyntheticTables("ai_dictionary_wind.bytes", null, root =>
            AssertTableLoadError(root, "ai_dictionary_wind.bytes"));
    }

    [Fact]
    public void SyntheticTables_AreValidBaseline()
    {
        // 对照组：同一套合成字节未破坏时必须能加载，证明上面的红是真红而不是基线本身就坏。
        WithSyntheticTables(null, null, root =>
        {
            var catalog = TableCatalog.Load(root);
            Assert.Equal(1, catalog.WinRowKeyCount(SuitType.Character));
            Assert.Equal(1, catalog.AiRowKeyCount(SuitType.Character));
            Assert.True(catalog.TryGetWinRows(SuitType.Dragon, 0, out var winRows));
            Assert.Single(winRows);
            Assert.True(catalog.TryGetAiRows(SuitType.Wind, 0, out var aiRows));
            Assert.Single(aiRows);
        });
    }

    // ---------------- helpers ----------------

    private static void AssertContentLoadError(TempContentDirectory content, string expectedPathFragment, string expectedMessageFragment)
    {
        var exception = Assert.Throws<ContentLoadException>(() => ContentCatalog.Load(content.Root));
        Assert.Contains(expectedPathFragment, exception.Path);
        Assert.Contains(expectedPathFragment, exception.Message);
        Assert.Contains(expectedMessageFragment, exception.Message);
    }

    private static void AssertTableLoadError(string tablesDirectory, string fileName)
    {
        var expectedPath = Path.Combine(tablesDirectory, fileName);
        var exception = Assert.Throws<InvalidDataException>(() => TableCatalog.Load(tablesDirectory));
        Assert.Contains(expectedPath, exception.Message);
    }

    private static byte[] ValidWinTableBytes() => new TestMsgPackWriter()
        .ArrayHeader(1)
        .FixMap(1)
        .UInt(0).ArrayHeader(1).FixArray(4).Int(0).Bool(true).Bool(true).ArrayHeader(0)
        .ToArray();

    private static byte[] ValidAiTableBytes() => new TestMsgPackWriter()
        .ArrayHeader(1)
        .FixMap(1)
        .UInt(0).ArrayHeader(1).FixArray(2).Bool(true).Float64(0.5)
        .ToArray();

    private static void WithSyntheticTables(string? targetFile, byte[]? replacementBytes, Action<string> assertion)
    {
        var root = Path.Combine(Path.GetTempPath(), "mahjong-table-integrity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var fileName in WinTableFiles)
            {
                File.WriteAllBytes(Path.Combine(root, fileName), ValidWinTableBytes());
            }

            foreach (var fileName in AiTableFiles)
            {
                File.WriteAllBytes(Path.Combine(root, fileName), ValidAiTableBytes());
            }

            if (targetFile is not null)
            {
                var targetPath = Path.Combine(root, targetFile);
                if (replacementBytes is null)
                {
                    File.Delete(targetPath);
                }
                else
                {
                    File.WriteAllBytes(targetPath, replacementBytes);
                }
            }

            assertion(root);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响断言结果。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
