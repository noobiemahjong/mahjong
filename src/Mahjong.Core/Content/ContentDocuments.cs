using System.Text.Json.Serialization;

namespace Mahjong.Core.Content;

// content/ 下 JSON 的传输形状。字段名由 camelCase 命名策略映射到规格 §1 的 schema；
// 这些类型只在 ContentCatalog 内部使用，不进入公共 API。

internal sealed class TilesDocument
{
    [JsonRequired]
    public int Version { get; set; }

    [JsonRequired]
    public List<TileDocument>? Tiles { get; set; }
}

internal sealed class TileDocument
{
    [JsonRequired]
    public ushort Id { get; set; }

    [JsonRequired]
    public string? Suit { get; set; }

    [JsonRequired]
    public byte Rank { get; set; }
}

internal sealed class SuitsDocument
{
    [JsonRequired]
    public int Version { get; set; }

    [JsonRequired]
    public List<SuitDocument>? Suits { get; set; }
}

internal sealed class SuitDocument
{
    [JsonRequired]
    public string? Suit { get; set; }

    [JsonRequired]
    public int RankCount { get; set; }

    [JsonRequired]
    public int AmountOfEachRank { get; set; }

    [JsonRequired]
    public bool AllowedSequence { get; set; }
}

internal sealed class SkillsDocument
{
    [JsonRequired]
    public int Version { get; set; }

    [JsonRequired]
    public List<SkillDocument>? Skills { get; set; }
}

internal sealed class SkillDocument
{
    [JsonRequired]
    public int Id { get; set; }

    [JsonRequired]
    public string? Name { get; set; }
}

internal sealed class RulesDocument
{
    [JsonRequired]
    public int Version { get; set; }

    [JsonRequired]
    public JokerDocument? Joker { get; set; }
}

internal sealed class JokerDocument
{
    [JsonRequired]
    public string? Mode { get; set; }

    public int? TypeId { get; set; }
}

internal sealed class ManifestDocument
{
    [JsonRequired]
    public int Version { get; set; }

    [JsonRequired]
    public Dictionary<string, string?>? Files { get; set; }

    [JsonRequired]
    public string? ContentHash { get; set; }
}
