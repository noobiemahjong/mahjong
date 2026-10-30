using System.Text;
using System.Text.Json;
using Mahjong.Core.Content;
using Mahjong.Core.Rules;
using Mahjong.Tests.Support;
using Xunit;

namespace Mahjong.Tests.Content;

/// <summary>
/// 对仓库真实 <c>content/</c> 的加载：期望值在测试里按规格 §1 独立重推一遍，不复制生产实现。
/// </summary>
public sealed class ContentCatalogTests
{
    [Fact]
    public void RepoContent_LoadsWithExpectedTileTypes()
    {
        var catalog = LoadRepoContent();

        var expected = new List<TileType>(34);
        ushort id = 0;
        foreach (var (suit, rankCount) in new[]
                 {
                     (SuitType.Character, 9),
                     (SuitType.Dot, 9),
                     (SuitType.Bamboo, 9),
                     (SuitType.Wind, 4),
                     (SuitType.Dragon, 3),
                 })
        {
            for (byte rank = 1; rank <= rankCount; rank++)
            {
                expected.Add(new TileType(id++, suit, rank));
            }
        }

        Assert.Equal(34, catalog.TileTypes.Count);
        Assert.Equal(expected, catalog.TileTypes);
        Assert.Equal(new TileType(31, SuitType.Dragon, 1), catalog.GetTileType(31));
    }

    [Fact]
    public void RepoContent_SuitsAndSkillsMatchSpec()
    {
        var catalog = LoadRepoContent();

        Assert.Equal(
            [
                new Suit(SuitType.Character, 9, 4, true),
                new Suit(SuitType.Dot, 9, 4, true),
                new Suit(SuitType.Bamboo, 9, 4, true),
                new Suit(SuitType.Wind, 4, 4, false),
                new Suit(SuitType.Dragon, 3, 4, false),
            ],
            catalog.Suits);

        Assert.Equal(
            [
                new SkillDefinition(1, "Pong"),
                new SkillDefinition(2, "Kong"),
                new SkillDefinition(3, "SelfDrawn"),
                new SkillDefinition(4, "SelfDrawnWith7Pairs"),
            ],
            catalog.Skills);
    }

    [Fact]
    public void RepoContent_JokerDefaultsToDragonRank1()
    {
        var catalog = LoadRepoContent();

        Assert.Equal(JokerMode.FixedType, catalog.Joker.Mode);
        Assert.Equal(new TileType(31, SuitType.Dragon, 1), catalog.Joker.Type);
    }

    [Fact]
    public void RepoContent_ContentHashIsStableAndMatchesManifest()
    {
        var catalog = LoadRepoContent();

        Assert.Equal(32, catalog.ContentHash.Length);
        Assert.Equal(64, catalog.ContentHashHex.Length);
        Assert.Matches("^[0-9a-f]{64}$", catalog.ContentHashHex);
        Assert.Equal(catalog.ContentHashHex, Convert.ToHexStringLower(catalog.ContentHash.Span));

        // manifest.json 里声明的值。
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.ContentDirectory, "manifest.json"))))
        {
            Assert.Equal(manifest.RootElement.GetProperty("contentHash").GetString(), catalog.ContentHashHex);
        }

        // 测试侧独立重算（不调用生产实现的哈希逻辑）。
        var recomputed = TempContentDirectory.ComputeContentHashHex(
            TempContentDirectory.ComputeHashesIn(RepoPaths.ContentDirectory));
        Assert.Equal(recomputed, catalog.ContentHashHex);

        // 两次加载的哈希一致（不依赖静态缓存）。
        var second = ContentCatalog.Load(RepoPaths.ContentDirectory);
        Assert.Equal(catalog.ContentHash.ToArray(), second.ContentHash.ToArray());
        Assert.Equal(catalog.ContentHashHex, second.ContentHashHex);
    }

    [Fact]
    public void RepoContent_JokerFollowsEditedRulesCopy()
    {
        var copy = CopyContentToTemp();
        try
        {
            var baseline = ContentCatalog.Load(copy);
            Assert.Equal(new TileType(31, SuitType.Dragon, 1), baseline.Joker.Type);

            WriteRules(copy, "{ \"mode\": \"None\" }");
            var none = ContentCatalog.Load(copy);
            Assert.Equal(JokerMode.None, none.Joker.Mode);
            Assert.Null(none.Joker.Type);
            Assert.NotEqual(baseline.ContentHashHex, none.ContentHashHex);

            WriteRules(copy, "{ \"mode\": \"FixedType\", \"typeId\": 0 }");
            var fixedType = ContentCatalog.Load(copy);
            Assert.Equal(JokerMode.FixedType, fixedType.Joker.Mode);
            Assert.Equal(new TileType(0, SuitType.Character, 1), fixedType.Joker.Type);
            Assert.NotEqual(baseline.ContentHashHex, fixedType.ContentHashHex);
        }
        finally
        {
            DeleteDirectory(copy);
        }
    }

    [Fact]
    public void RepoContent_SkillsExposeSkillDefinitions_AfterRename()
    {
        // 内容元数据类型由 Skill 改名 SkillDefinition，技能动作类型占用 Skill 名字（协议/规则分家）。
        var catalog = LoadRepoContent();

        SkillDefinition definition = catalog.GetSkill(4);
        Assert.Equal(new SkillDefinition(4, "SelfDrawnWith7Pairs"), definition);
        Assert.IsType<SkillDefinition>(catalog.Skills[0]);
        Assert.Equal(4, catalog.Skills.Count);
    }

    private static ContentCatalog LoadRepoContent() => ContentCatalog.Load(RepoPaths.ContentDirectory);

    private static void WriteRules(string contentRoot, string jokerJson)
    {
        File.WriteAllText(
            Path.Combine(contentRoot, "rules.json"),
            "{\n  \"version\": 1,\n  \"joker\": " + jokerJson + "\n}\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        TempContentDirectory.RebuildManifestIn(contentRoot);
    }

    private static string CopyContentToTemp()
    {
        var target = Path.Combine(Path.GetTempPath(), "mahjong-content-copy-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        foreach (var sourceFile in Directory.EnumerateFiles(RepoPaths.ContentDirectory, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(RepoPaths.ContentDirectory, sourceFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourceFile, destination);
        }

        return target;
    }

    private static void DeleteDirectory(string directory)
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
