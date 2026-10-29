using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mahjong.Core.Content;
using Mahjong.Core.Content.Tables;
using Mahjong.Core.Rules;
using Mahjong.Tests.Content.Tables;
using Mahjong.Tests.Support;
using Xunit;

namespace Mahjong.Tests.Content;

/// <summary>
/// 端到端：仓库真实 <c>content/</c>（ContentCatalog.Load）+ 真实 <c>content/tables</c>（共享 fixture 的 TableCatalog.Load）
/// 一起加载后再交叉查询。期望值按规格 §1/§1.1 在测试里独立重推（哈希是第二实现，不调用生产实现）。
/// </summary>
[Collection("table-catalog")]
public sealed class EndToEndTests
{
    private static readonly string[] ManifestFilePaths =
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

    private readonly TableCatalogFixture _tables;

    public EndToEndTests(TableCatalogFixture tables) => _tables = tables;

    [Fact]
    public void RepoContent_LoadsWithSpecTileMatrixSuitsAndSkills()
    {
        Assert.True(File.Exists(Path.Combine(RepoPaths.ContentDirectory, "manifest.json")));
        Assert.True(Directory.Exists(Path.Combine(RepoPaths.ContentDirectory, "tables")));

        var catalog = ContentCatalog.Load(RepoPaths.ContentDirectory);

        var expectedTiles = new List<TileType>(34);
        ushort id = 0;
        foreach (var (suit, rankCount) in new (SuitType Suit, int RankCount)[]
                 {
                     (SuitType.Character, 9), (SuitType.Dot, 9), (SuitType.Bamboo, 9),
                     (SuitType.Wind, 4), (SuitType.Dragon, 3),
                 })
        {
            for (byte rank = 1; rank <= rankCount; rank++)
            {
                expectedTiles.Add(new TileType(id++, suit, rank));
            }
        }

        Assert.Equal(34, catalog.TileTypes.Count);
        Assert.Equal(expectedTiles, catalog.TileTypes);

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
                new Skill(1, "Pong"),
                new Skill(2, "Kong"),
                new Skill(3, "SelfDrawn"),
                new Skill(4, "SelfDrawnWith7Pairs"),
            ],
            catalog.Skills);

        Assert.Equal(new TileType(31, SuitType.Dragon, 1), catalog.GetTileType(31));
        Assert.Equal(catalog.GetSuit(SuitType.Dragon), new Suit(SuitType.Dragon, 3, 4, false));
        Assert.Equal("SelfDrawnWith7Pairs", catalog.GetSkill(4).Name);
    }

    [Fact]
    public void RepoContent_JokerDefaultsToFixedTypeDragonRank1()
    {
        var catalog = ContentCatalog.Load(RepoPaths.ContentDirectory);

        Assert.Equal(JokerMode.FixedType, catalog.Joker.Mode);
        Assert.True(catalog.Joker.Type.HasValue);
        Assert.Equal(new TileType(31, SuitType.Dragon, 1), catalog.Joker.Type.Value);
    }

    [Fact]
    public void ContentHash_MatchesIndependentRecomputationAndManifest()
    {
        var catalog = ContentCatalog.Load(RepoPaths.ContentDirectory);

        Assert.Equal(32, catalog.ContentHash.Length);
        Assert.Equal(64, catalog.ContentHashHex.Length);
        Assert.Matches("^[0-9a-f]{64}$", catalog.ContentHashHex);
        Assert.Equal(catalog.ContentHashHex, Convert.ToHexStringLower(catalog.ContentHash.Span));

        var recomputed = RecomputeContentHashHex(RepoPaths.ContentDirectory);
        Assert.Equal(recomputed, catalog.ContentHashHex);

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(RepoPaths.ContentDirectory, "manifest.json")));
        Assert.Equal(manifest.RootElement.GetProperty("contentHash").GetString(), catalog.ContentHashHex);
        Assert.Equal("1", manifest.RootElement.GetProperty("version").GetRawText());
    }

    [Fact]
    public void ContentHash_TwoLoadsAreIdentical()
    {
        var first = ContentCatalog.Load(RepoPaths.ContentDirectory);
        var second = ContentCatalog.Load(RepoPaths.ContentDirectory);

        Assert.Equal(first.ContentHash.ToArray(), second.ContentHash.ToArray());
        Assert.Equal(first.ContentHashHex, second.ContentHashHex);
        Assert.Equal(32, second.ContentHash.Length);
    }

    [Fact]
    public void ContentAndTables_AgreeOnSuitsAndAreQueryable()
    {
        var catalog = ContentCatalog.Load(RepoPaths.ContentDirectory);
        var tables = _tables.Catalog;

        // content 声明的每个花色都必须有可用的查表（计数 > 0）。
        foreach (var suit in catalog.Suits)
        {
            Assert.True(tables.WinRowKeyCount(suit.Type) > 0, "no win rows for " + suit.Type);
            Assert.True(tables.AiRowKeyCount(suit.Type) > 0, "no ai rows for " + suit.Type);
        }

        // 鬼牌来自 rules.json：把它的花色接回查表查询（key 由 TableKey.Encode 现场构造，不抄文档）。
        Assert.True(catalog.Joker.Type.HasValue);
        var joker = catalog.Joker.Type.Value;
        Assert.Equal(SuitType.Dragon, joker.Suit);

        var dragonKey = TableKey.Encode([0, 0, 1]);
        Assert.Equal(1UL, dragonKey);
        Assert.True(tables.TryGetWinRows(SuitType.Dragon, dragonKey, out var winRows));
        Assert.NotEmpty(winRows);
        Assert.True(tables.TryGetAiRows(SuitType.Dragon, 0, out var aiRows));
        Assert.NotEmpty(aiRows);
    }

    private static string RecomputeContentHashHex(string contentDirectory)
    {
        var input = new StringBuilder("v1\n");
        foreach (var relativePath in ManifestFilePaths.OrderBy(path => path, StringComparer.Ordinal))
        {
            var fullPath = Path.Combine(contentDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var fileHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fullPath)));
            input.Append(relativePath).Append('=').Append(fileHash).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
    }
}
