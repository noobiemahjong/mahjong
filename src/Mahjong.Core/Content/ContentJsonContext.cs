using System.Text.Json.Serialization;

namespace Mahjong.Core.Content;

/// <summary>
/// 内容 JSON 的源生成序列化上下文；生产路径只用它解析，不调用反射式重载（AOT 安全）。
/// 未知字段直接报错，避免 schema 漂移被静默忽略。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(TilesDocument))]
[JsonSerializable(typeof(SuitsDocument))]
[JsonSerializable(typeof(SkillsDocument))]
[JsonSerializable(typeof(RulesDocument))]
[JsonSerializable(typeof(ManifestDocument))]
internal sealed partial class ContentJsonContext : JsonSerializerContext
{
}
