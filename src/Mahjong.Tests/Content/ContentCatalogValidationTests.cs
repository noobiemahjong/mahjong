using System.Security.Cryptography;
using System.Text;
using Mahjong.Core.Content;
using Mahjong.Core.Rules;
using Mahjong.Tests.Support;
using Xunit;

namespace Mahjong.Tests.Content;

/// <summary>
/// 用临时目录里的合法最小内容集反证校验矩阵：每条破坏都必须抛
/// <see cref="ContentLoadException"/>（FlipIndicator 抛 <see cref="NotSupportedException"/>），
/// 且路径与原因可读。
/// </summary>
public sealed class ContentCatalogValidationTests
{
    [Fact]
    public void Load_ValidContent_ExposesCatalogValuesAndContentHash()
    {
        using var content = new TempContentDirectory();
        var catalog = ContentCatalog.Load(content.Root);

        Assert.Equal(34, catalog.TileTypes.Count);
        for (var id = 0; id < 34; id++)
        {
            Assert.Equal((ushort)id, catalog.TileTypes[id].Id);
        }

        Assert.Equal(new TileType(0, SuitType.Character, 1), catalog.TileTypes[0]);
        Assert.Equal(new TileType(8, SuitType.Character, 9), catalog.TileTypes[8]);
        Assert.Equal(new TileType(9, SuitType.Dot, 1), catalog.TileTypes[9]);
        Assert.Equal(new TileType(17, SuitType.Dot, 9), catalog.TileTypes[17]);
        Assert.Equal(new TileType(18, SuitType.Bamboo, 1), catalog.TileTypes[18]);
        Assert.Equal(new TileType(26, SuitType.Bamboo, 9), catalog.TileTypes[26]);
        Assert.Equal(new TileType(27, SuitType.Wind, 1), catalog.TileTypes[27]);
        Assert.Equal(new TileType(30, SuitType.Wind, 4), catalog.TileTypes[30]);
        Assert.Equal(new TileType(31, SuitType.Dragon, 1), catalog.TileTypes[31]);
        Assert.Equal(new TileType(33, SuitType.Dragon, 3), catalog.TileTypes[33]);

        Assert.Equal(5, catalog.Suits.Count);
        Assert.Equal(new Suit(SuitType.Character, 9, 4, true), catalog.Suits[0]);
        Assert.Equal(new Suit(SuitType.Dot, 9, 4, true), catalog.Suits[1]);
        Assert.Equal(new Suit(SuitType.Bamboo, 9, 4, true), catalog.Suits[2]);
        Assert.Equal(new Suit(SuitType.Wind, 4, 4, false), catalog.Suits[3]);
        Assert.Equal(new Suit(SuitType.Dragon, 3, 4, false), catalog.Suits[4]);

        Assert.Equal(4, catalog.Skills.Count);
        Assert.Equal(new Skill(1, "Pong"), catalog.Skills[0]);
        Assert.Equal(new Skill(2, "Kong"), catalog.Skills[1]);
        Assert.Equal(new Skill(3, "SelfDrawn"), catalog.Skills[2]);
        Assert.Equal(new Skill(4, "SelfDrawnWith7Pairs"), catalog.Skills[3]);

        Assert.Equal(JokerMode.FixedType, catalog.Joker.Mode);
        Assert.Equal(new TileType(31, SuitType.Dragon, 1), catalog.Joker.Type);

        Assert.Equal(32, catalog.ContentHash.Length);
        Assert.Equal(catalog.ContentHashHex, Convert.ToHexStringLower(catalog.ContentHash.Span));
        Assert.Equal(64, catalog.ContentHashHex.Length);
        Assert.Matches("^[0-9a-f]{64}$", catalog.ContentHashHex);
        Assert.Equal(TempContentDirectory.ComputeContentHashHex(content.ComputeFileHashes()), catalog.ContentHashHex);

        var second = ContentCatalog.Load(content.Root);
        Assert.Equal(catalog.ContentHash.ToArray(), second.ContentHash.ToArray());
        Assert.Equal(catalog.ContentHashHex, second.ContentHashHex);
    }

    [Fact]
    public void Accessors_ReturnExpectedValuesAndRejectOutOfRange()
    {
        using var content = new TempContentDirectory();
        var catalog = ContentCatalog.Load(content.Root);

        Assert.Equal(new TileType(0, SuitType.Character, 1), catalog.GetTileType(0));
        Assert.Equal(new TileType(33, SuitType.Dragon, 3), catalog.GetTileType(33));
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = catalog.GetTileType(34); });

        Assert.Equal(new Suit(SuitType.Wind, 4, 4, false), catalog.GetSuit(SuitType.Wind));
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = catalog.GetSuit((SuitType)0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = catalog.GetSuit((SuitType)6); });

        Assert.Equal(new Skill(4, "SelfDrawnWith7Pairs"), catalog.GetSkill(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = catalog.GetSkill(0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = catalog.GetSkill(5); });
    }

    [Fact]
    public void Load_MissingDirectory_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "mahjong-content-tests", Guid.NewGuid().ToString("N"));
        var exception = Assert.Throws<ContentLoadException>(() => ContentCatalog.Load(missing));
        Assert.Equal(Path.GetFullPath(missing), exception.Path, ignoreCase: true);
    }

    [Fact]
    public void Load_NullOrWhitespaceDirectory_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => ContentCatalog.Load(null!));
        Assert.Throws<ArgumentException>(() => ContentCatalog.Load("   "));
    }

    [Fact]
    public void Load_MissingJsonFile_ThrowsWithPath()
    {
        using var content = new TempContentDirectory();
        content.DeleteFile("skills.json");

        LoadFails(content, "skills.json", "missing");
    }

    [Fact]
    public void Load_UnparsableJson_ThrowsWithPath()
    {
        using var content = new TempContentDirectory();
        content.WriteText("tiles.json", "{ \"version\": 1, ");
        content.RebuildManifest();

        LoadFails(content, "tiles.json", "JSON is invalid");
    }

    [Fact]
    public void Load_JsonWithMissingField_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText(
            "tiles.json",
            "{ \"id\": 0, \"suit\": \"Character\", \"rank\": 1 }",
            "{ \"id\": 0, \"suit\": \"Character\" }");
        content.RebuildManifest();

        LoadFails(content, "tiles.json", "JSON is invalid");
    }

    [Fact]
    public void Load_JsonWithUnknownField_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText(
            "tiles.json",
            "{ \"id\": 0, \"suit\": \"Character\", \"rank\": 1 }",
            "{ \"id\": 0, \"suit\": \"Character\", \"rank\": 1, \"extra\": 1 }");
        content.RebuildManifest();

        LoadFails(content, "tiles.json", "JSON is invalid");
    }

    [Fact]
    public void Load_JsonWithWrongFieldType_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText(
            "tiles.json",
            "{ \"id\": 0, \"suit\": \"Character\", \"rank\": 1 }",
            "{ \"id\": 0, \"suit\": \"Character\", \"rank\": \"one\" }");
        content.RebuildManifest();

        LoadFails(content, "tiles.json", "JSON is invalid");
    }

    [Fact]
    public void Load_TilesWithWrongCount_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText(
            "tiles.json",
            "    { \"id\": 33, \"suit\": \"Dragon\", \"rank\": 3 }\n",
            "    { \"id\": 33, \"suit\": \"Dragon\", \"rank\": 3 },\n"
            + "    { \"id\": 34, \"suit\": \"Dragon\", \"rank\": 4 }\n");
        content.RebuildManifest();

        LoadFails(content, "tiles.json", "exactly 34 entries");
    }

    [Fact]
    public void Load_TilesWithIdGap_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("tiles.json", "{ \"id\": 5,", "{ \"id\": 34,");
        content.RebuildManifest();

        LoadFails(content, "tiles.json", "expected id 5");
    }

    [Fact]
    public void Load_TilesWithDuplicateId_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("tiles.json", "{ \"id\": 6,", "{ \"id\": 5,");
        content.RebuildManifest();

        LoadFails(content, "tiles.json", "duplicate id 5");
    }

    [Fact]
    public void Load_TilesWithUnknownSuit_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText(
            "tiles.json",
            "{ \"id\": 0, \"suit\": \"Character\", \"rank\": 1 }",
            "{ \"id\": 0, \"suit\": \"Char\", \"rank\": 1 }");
        content.RebuildManifest();

        LoadFails(content, "tiles.json", "unknown suit");
    }

    [Fact]
    public void Load_TilesWithWrongSuitRankMapping_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText(
            "tiles.json",
            "{ \"id\": 0, \"suit\": \"Character\", \"rank\": 1 }",
            "{ \"id\": 0, \"suit\": \"Character\", \"rank\": 2 }");
        content.RebuildManifest();

        LoadFails(content, "tiles.json", "duplicate suit/rank");
    }

    [Fact]
    public void Load_TilesWithRankOutOfSuitRange_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText(
            "tiles.json",
            "{ \"id\": 33, \"suit\": \"Dragon\", \"rank\": 3 }",
            "{ \"id\": 33, \"suit\": \"Dragon\", \"rank\": 4 }");
        content.RebuildManifest();

        LoadFails(content, "tiles.json", "out of range");
    }

    [Fact]
    public void Load_TilesWithVersionNotOne_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("tiles.json", "\"version\": 1", "\"version\": 2");
        content.RebuildManifest();

        LoadFails(content, "tiles.json", "version must be 1");
    }

    [Fact]
    public void Load_SuitsWithWrongValue_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText(
            "suits.json",
            "{ \"suit\": \"Character\", \"rankCount\": 9",
            "{ \"suit\": \"Character\", \"rankCount\": 8");
        content.RebuildManifest();

        LoadFails(content, "suits.json", "must be");
    }

    [Fact]
    public void Load_SuitsWithDuplicateSuit_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("suits.json", "{ \"suit\": \"Dot\"", "{ \"suit\": \"Character\"");
        content.RebuildManifest();

        LoadFails(content, "suits.json", "duplicate suit");
    }

    [Fact]
    public void Load_SuitsWithWrongCount_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText(
            "suits.json",
            "    { \"suit\": \"Dragon\", \"rankCount\": 3, \"amountOfEachRank\": 4, \"allowedSequence\": false }\n",
            "    { \"suit\": \"Dragon\", \"rankCount\": 3, \"amountOfEachRank\": 4, \"allowedSequence\": false },\n"
            + "    { \"suit\": \"Wind\", \"rankCount\": 4, \"amountOfEachRank\": 4, \"allowedSequence\": false }\n");
        content.RebuildManifest();

        LoadFails(content, "suits.json", "exactly 5 entries");
    }

    [Fact]
    public void Load_SkillsWithWrongName_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("skills.json", "{ \"id\": 2, \"name\": \"Kong\" }", "{ \"id\": 2, \"name\": \"Kongg\" }");
        content.RebuildManifest();

        LoadFails(content, "skills.json", "must be named 'Kong'");
    }

    [Fact]
    public void Load_SkillsWithWrongId_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("skills.json", "{ \"id\": 3,", "{ \"id\": 5,");
        content.RebuildManifest();

        LoadFails(content, "skills.json", "unknown skill id 5");
    }

    [Fact]
    public void Load_SkillsInReverseOrder_IsAccepted()
    {
        // 规格只固定 id→name 映射，不固定数组顺序；返回的 Skills 仍按 id 升序。
        using var content = new TempContentDirectory();
        content.WriteText(
            "skills.json",
            """
            {
              "version": 1,
              "skills": [
                { "id": 4, "name": "SelfDrawnWith7Pairs" },
                { "id": 3, "name": "SelfDrawn" },
                { "id": 2, "name": "Kong" },
                { "id": 1, "name": "Pong" }
              ]
            }

            """);
        content.RebuildManifest();

        var catalog = ContentCatalog.Load(content.Root);
        Assert.Equal(new Skill(1, "Pong"), catalog.Skills[0]);
        Assert.Equal(new Skill(2, "Kong"), catalog.Skills[1]);
        Assert.Equal(new Skill(3, "SelfDrawn"), catalog.Skills[2]);
        Assert.Equal(new Skill(4, "SelfDrawnWith7Pairs"), catalog.Skills[3]);
        Assert.Equal(new Skill(4, "SelfDrawnWith7Pairs"), catalog.GetSkill(4));
    }

    [Fact]
    public void Load_UnknownJokerMode_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("rules.json", "\"FixedType\"", "\"Fixed\"");
        content.RebuildManifest();

        LoadFails(content, "rules.json", "joker.mode must be");
    }

    [Fact]
    public void Load_FixedTypeWithoutTypeId_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("rules.json", "{ \"mode\": \"FixedType\", \"typeId\": 31 }", "{ \"mode\": \"FixedType\" }");
        content.RebuildManifest();

        LoadFails(content, "rules.json", "typeId is required");
    }

    [Fact]
    public void Load_FixedTypeWithUnknownTypeId_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("rules.json", "\"typeId\": 31", "\"typeId\": 34");
        content.RebuildManifest();

        LoadFails(content, "rules.json", "does not exist in tiles.json");
    }

    [Fact]
    public void Load_FlipIndicator_ThrowsNotSupportedWithPath()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("rules.json", "\"FixedType\"", "\"FlipIndicator\"");
        content.RebuildManifest();

        var exception = Assert.Throws<NotSupportedException>(() => ContentCatalog.Load(content.Root));
        Assert.Contains(content.PathOf("rules.json"), exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FlipIndicator", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_JokerNone_IgnoresTypeId()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("rules.json", "\"FixedType\", \"typeId\": 31", "\"None\", \"typeId\": 99");
        content.RebuildManifest();

        var catalog = ContentCatalog.Load(content.Root);
        Assert.Equal(JokerMode.None, catalog.Joker.Mode);
        Assert.Null(catalog.Joker.Type);
    }

    [Fact]
    public void Load_JokerNoneWithoutTypeId_IsValid()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("rules.json", "{ \"mode\": \"FixedType\", \"typeId\": 31 }", "{ \"mode\": \"None\" }");
        content.RebuildManifest();

        var catalog = ContentCatalog.Load(content.Root);
        Assert.Equal(JokerMode.None, catalog.Joker.Mode);
        Assert.Null(catalog.Joker.Type);
    }

    [Fact]
    public void Load_JokerFollowsRulesFileInsteadOfHardcodedType()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("rules.json", "\"typeId\": 31", "\"typeId\": 0");
        content.RebuildManifest();

        var catalog = ContentCatalog.Load(content.Root);
        Assert.Equal(JokerMode.FixedType, catalog.Joker.Mode);
        Assert.Equal(new TileType(0, SuitType.Character, 1), catalog.Joker.Type);
    }

    [Fact]
    public void Load_ManifestMissingEntry_Throws()
    {
        using var content = new TempContentDirectory();
        var hashes = content.ComputeFileHashes();
        hashes.Remove("suits.json");
        content.WriteManifest(hashes, TempContentDirectory.ComputeContentHashHex(hashes));

        LoadFails(content, "manifest.json", "entry 'suits.json' is missing");
    }

    [Fact]
    public void Load_ManifestExtraEntry_Throws()
    {
        using var content = new TempContentDirectory();
        var hashes = content.ComputeFileHashes();
        hashes["tables/unlisted.bytes"] = new string('a', 64);
        content.WriteManifest(hashes, TempContentDirectory.ComputeContentHashHex(hashes));

        LoadFails(content, "manifest.json", "not part of the fixed content file list");
    }

    [Fact]
    public void Load_ManifestFileHashMismatch_Throws()
    {
        using var content = new TempContentDirectory();
        // 只改 tiles.json 的内容而不重算 manifest：JSON 仍合法，哈希对不上。
        content.WriteText("tiles.json", "\n" + content.ReadText("tiles.json"));

        LoadFails(content, "tiles.json", "sha256 mismatch");
    }

    [Fact]
    public void Load_ManifestTableFileHashMismatch_ThrowsAtThatFile()
    {
        using var content = new TempContentDirectory();
        var hashes = content.ComputeFileHashes();
        hashes["tables/dictionary_normal.bytes"] = new string('a', 64);
        content.WriteManifest(hashes, TempContentDirectory.ComputeContentHashHex(hashes));

        LoadFails(content, "tables/dictionary_normal.bytes", "sha256 mismatch");
    }

    [Fact]
    public void Load_ContentHashComputedWithReversedPathOrder_Throws()
    {
        using var content = new TempContentDirectory();
        var hashes = content.ComputeFileHashes();
        var sortedOrdinalHash = TempContentDirectory.ComputeContentHashHex(hashes);

        var reversedPaths = hashes.Keys.OrderByDescending(path => path, StringComparer.Ordinal).ToArray();
        var input = new StringBuilder("v1\n");
        foreach (var relativePath in reversedPaths)
        {
            input.Append(relativePath).Append('=').Append(hashes[relativePath]).Append('\n');
        }

        var reversedOrderHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
        // 防止倒序与升序巧合相同而让用例恒真（10 条路径不会，但显式守卫）。
        Assert.NotEqual(sortedOrdinalHash, reversedOrderHash);

        content.WriteManifest(hashes, reversedOrderHash);

        LoadFails(content, "manifest.json", "contentHash mismatch");
    }

    [Fact]
    public void Load_ManifestContentHashMismatch_Throws()
    {
        using var content = new TempContentDirectory();
        var hashes = content.ComputeFileHashes();
        content.WriteManifest(hashes, new string('0', 64));

        LoadFails(content, "manifest.json", "contentHash mismatch");
    }

    [Fact]
    public void Load_ManifestContentHashNotLowercaseHex_Throws()
    {
        using var content = new TempContentDirectory();
        var hashes = content.ComputeFileHashes();
        var uppercase = TempContentDirectory.ComputeContentHashHex(hashes).ToUpperInvariant();
        content.WriteManifest(hashes, uppercase);

        LoadFails(content, "manifest.json", "contentHash must be 64 lowercase hex");
    }

    [Fact]
    public void Load_ManifestWithWrongVersion_Throws()
    {
        using var content = new TempContentDirectory();
        content.ReplaceText("manifest.json", "\"version\": 1", "\"version\": 2");

        LoadFails(content, "manifest.json", "version must be 1");
    }

    [Fact]
    public void Load_MissingTableFile_Throws()
    {
        using var content = new TempContentDirectory();
        content.DeleteFile("tables/dictionary_wind.bytes");

        LoadFails(content, "tables/dictionary_wind.bytes", "missing");
    }

    [Fact]
    public void Load_UnlistedExtraFileOnDisk_IsIgnored()
    {
        using var content = new TempContentDirectory();
        content.WriteBytes("tables/not-in-manifest.bytes", [0x91, 0x90]);

        var catalog = ContentCatalog.Load(content.Root);
        Assert.Equal(32, catalog.ContentHash.Length);
    }

    private static ContentLoadException LoadFails(
        TempContentDirectory content,
        string expectedRelativePath,
        string expectedMessageFragment)
    {
        var exception = Assert.Throws<ContentLoadException>(() => ContentCatalog.Load(content.Root));
        var expectedSuffix = expectedRelativePath.Replace('/', Path.DirectorySeparatorChar);
        Assert.True(
            Path.IsPathRooted(exception.Path),
            $"ContentLoadException.Path must be absolute but was '{exception.Path}'.");
        Assert.True(
            exception.Path.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase),
            $"ContentLoadException.Path '{exception.Path}' should end with '{expectedSuffix}'.");
        Assert.Contains(expectedSuffix, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedMessageFragment, exception.Message, StringComparison.OrdinalIgnoreCase);
        return exception;
    }
}
