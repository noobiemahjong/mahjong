using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Mahjong.Core.Rules;

namespace Mahjong.Core.Content;

/// <summary>
/// 内容目录的只读快照：校验 tiles/suits/skills/rules 四个 JSON 与 manifest.json 之后可查询的内容视图。
/// 查表字节只参与 manifest 的 SHA-256 校验，不在这里解析；查表由 TableCatalog 另行加载。
/// </summary>
public sealed class ContentCatalog
{
    /// <summary>当前内容格式版本；四个 JSON 与 manifest 的 <c>version</c> 都必须是该值。</summary>
    public const int ContentFormatVersion = 1;

    /// <summary>contentHash 的版本前缀，改变清单算法必须同时改它。</summary>
    private const string ContentHashPrefix = "v1\n";

    private const int ContentHashLength = 32;
    private const int Sha256HexLength = 64;

    /// <summary>manifest.files 的固定键集合：4 个 JSON + 6 个查表文件，路径相对 content/ 且用 '/' 分隔。</summary>
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

    /// <summary>tiles.json 的固定映射：id 0–33 依次是万/筒/条 1–9、风 1–4、箭 1–3。</summary>
    private static readonly (SuitType Suit, byte Rank)[] ExpectedTiles = CreateExpectedTiles();

    /// <summary>suits.json 的固定取值。</summary>
    private static readonly Suit[] ExpectedSuits =
    [
        new(SuitType.Character, 9, 4, true),
        new(SuitType.Dot, 9, 4, true),
        new(SuitType.Bamboo, 9, 4, true),
        new(SuitType.Wind, 4, 4, false),
        new(SuitType.Dragon, 3, 4, false),
    ];

    /// <summary>skills.json 的固定取值。</summary>
    private static readonly (int Id, string Name)[] ExpectedSkills =
    [
        (1, "Pong"),
        (2, "Kong"),
        (3, "SelfDrawn"),
        (4, "SelfDrawnWith7Pairs"),
    ];

    private readonly TileType[] _tileTypes;
    private readonly Suit[] _suits;
    private readonly Skill[] _skills;
    private readonly byte[] _contentHash;
    private readonly string _contentHashHex;

    private ContentCatalog(TileType[] tileTypes, Suit[] suits, Skill[] skills, JokerRule joker, byte[] contentHash)
    {
        _tileTypes = tileTypes;
        _suits = suits;
        _skills = skills;
        _contentHash = contentHash;
        _contentHashHex = Convert.ToHexStringLower(contentHash);
        TileTypes = Array.AsReadOnly(tileTypes);
        Suits = Array.AsReadOnly(suits);
        Skills = Array.AsReadOnly(skills);
        Joker = joker;
    }

    /// <summary>34 种牌，按 id 升序。</summary>
    public IReadOnlyList<TileType> TileTypes { get; }

    /// <summary>5 个花色，按 <see cref="SuitType"/> 数值升序。</summary>
    public IReadOnlyList<Suit> Suits { get; }

    /// <summary>4 个技能，按 id 升序。</summary>
    public IReadOnlyList<Skill> Skills { get; }

    /// <summary>内容哈希的原始 32 字节；协议/回放传原始字节，不传 hex 字符串。</summary>
    public ReadOnlyMemory<byte> ContentHash => _contentHash;

    /// <summary>内容哈希的 64 位小写 hex，供日志与显示使用。</summary>
    public string ContentHashHex => _contentHashHex;

    /// <summary>rules.json 决定的当前鬼牌规则；<see cref="JokerRule.Type"/> 是 tiles.json 里的牌种。</summary>
    public JokerRule Joker { get; }

    /// <summary>加载内容目录：校验 4 个 JSON、manifest 与 6 个查表文件的哈希，不解析查表内容。</summary>
    /// <param name="contentDirectory"><c>content/</c> 根目录的路径。</param>
    /// <exception cref="ArgumentException"><paramref name="contentDirectory"/> 为 null 或空白。</exception>
    /// <exception cref="ContentLoadException">目录或文件缺失、JSON 不可解析或取值不符合规格、清单哈希不符。</exception>
    /// <exception cref="NotSupportedException"><c>rules.json</c> 的 <c>joker.mode</c> 为 <c>FlipIndicator</c>。</exception>
    public static ContentCatalog Load(string contentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);

        var root = GetContentRoot(contentDirectory);

        var tilesBytes = ReadRequiredFile(root, "tiles.json");
        var suitsBytes = ReadRequiredFile(root, "suits.json");
        var skillsBytes = ReadRequiredFile(root, "skills.json");
        var rulesBytes = ReadRequiredFile(root, "rules.json");

        var tileTypes = ParseTiles(tilesBytes, Combine(root, "tiles.json"));
        var suits = ParseSuits(suitsBytes, Combine(root, "suits.json"));
        var skills = ParseSkills(skillsBytes, Combine(root, "skills.json"));
        var joker = ParseRules(rulesBytes, Combine(root, "rules.json"), tileTypes);

        var cachedFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["tiles.json"] = tilesBytes,
            ["suits.json"] = suitsBytes,
            ["skills.json"] = skillsBytes,
            ["rules.json"] = rulesBytes,
        };
        var contentHash = VerifyManifest(root, cachedFiles);

        return new ContentCatalog(tileTypes, suits, skills, joker, contentHash);
    }

    /// <summary>按 typeId 取牌种。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="typeId"/> 不在 0–33。</exception>
    public TileType GetTileType(ushort typeId)
    {
        if (typeId >= _tileTypes.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(typeId),
                typeId,
                $"Tile type id must be within 0-{_tileTypes.Length - 1}.");
        }

        return _tileTypes[typeId];
    }

    /// <summary>按花色取属性。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="suit"/> 不是 <see cref="SuitType"/> 的已定义取值。</exception>
    public Suit GetSuit(SuitType suit)
    {
        var index = (int)suit - 1;
        if (index < 0 || index >= _suits.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(suit), suit, "Suit must be a defined SuitType value.");
        }

        return _suits[index];
    }

    /// <summary>按技能 id 取技能。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> 不在 1–4。</exception>
    public Skill GetSkill(int id)
    {
        if (id < 1 || id > _skills.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, $"Skill id must be within 1-{_skills.Length}.");
        }

        return _skills[id - 1];
    }

    private static string GetContentRoot(string contentDirectory)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(contentDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ContentLoadException(contentDirectory, "content directory is not a valid path: " + exception.Message, exception);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new ContentLoadException(fullPath, "content directory does not exist.");
        }

        return fullPath;
    }

    private static byte[] ReadRequiredFile(string root, string relativePath)
    {
        var fullPath = Combine(root, relativePath);
        if (!File.Exists(fullPath))
        {
            throw new ContentLoadException(fullPath, "required content file is missing.");
        }

        try
        {
            return File.ReadAllBytes(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ContentLoadException(fullPath, "content file could not be read: " + exception.Message, exception);
        }
    }

    private static string Combine(string root, string relativePath) =>
        Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static T Deserialize<T>(byte[] utf8Json, string path, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            var document = JsonSerializer.Deserialize(utf8Json, typeInfo);
            return document ?? throw new ContentLoadException(path, "JSON document is null.");
        }
        catch (JsonException exception)
        {
            throw new ContentLoadException(path, "JSON is invalid: " + exception.Message, exception);
        }
    }

    private static TileType[] ParseTiles(byte[] utf8Json, string path)
    {
        var document = Deserialize(utf8Json, path, ContentJsonContext.Default.TilesDocument);
        if (document.Version != ContentFormatVersion)
        {
            throw new ContentLoadException(path, $"version must be {ContentFormatVersion} but was {document.Version}.");
        }

        var entries = document.Tiles ?? throw new ContentLoadException(path, "tiles is required.");
        if (entries.Count != ExpectedTiles.Length)
        {
            throw new ContentLoadException(
                path,
                $"tiles must contain exactly {ExpectedTiles.Length} entries but contained {entries.Count}.");
        }

        var tileTypes = new TileType[entries.Count];
        var seenIds = new HashSet<ushort>();
        var seenSuitRanks = new HashSet<(SuitType Suit, byte Rank)>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null)
            {
                throw new ContentLoadException(path, $"tiles[{index}] is null.");
            }

            var suit = ParseSuit(entry.Suit, path, $"tiles[{index}]");
            if (!seenIds.Add(entry.Id))
            {
                throw new ContentLoadException(path, $"tiles[{index}]: duplicate id {entry.Id}.");
            }

            if (!seenSuitRanks.Add((suit, entry.Rank)))
            {
                throw new ContentLoadException(path, $"tiles[{index}]: duplicate suit/rank ({suit}, {entry.Rank}).");
            }

            var rankCount = ExpectedSuits[(int)suit - 1].RankCount;
            if (entry.Rank < 1 || entry.Rank > rankCount)
            {
                throw new ContentLoadException(
                    path,
                    $"tiles[{index}]: rank {entry.Rank} is out of range for suit {suit} (1-{rankCount}).");
            }

            tileTypes[index] = new TileType(entry.Id, suit, entry.Rank);
        }

        for (var index = 0; index < tileTypes.Length; index++)
        {
            var (expectedSuit, expectedRank) = ExpectedTiles[index];
            var actual = tileTypes[index];
            if (actual.Id != index)
            {
                throw new ContentLoadException(path, $"tiles[{index}]: expected id {index} but was {actual.Id}.");
            }

            if (actual.Suit != expectedSuit || actual.Rank != expectedRank)
            {
                throw new ContentLoadException(
                    path,
                    $"tiles[{index}]: expected ({expectedSuit}, {expectedRank}) but was ({actual.Suit}, {actual.Rank}).");
            }
        }

        return tileTypes;
    }

    private static Suit[] ParseSuits(byte[] utf8Json, string path)
    {
        var document = Deserialize(utf8Json, path, ContentJsonContext.Default.SuitsDocument);
        if (document.Version != ContentFormatVersion)
        {
            throw new ContentLoadException(path, $"version must be {ContentFormatVersion} but was {document.Version}.");
        }

        var entries = document.Suits ?? throw new ContentLoadException(path, "suits is required.");
        if (entries.Count != ExpectedSuits.Length)
        {
            throw new ContentLoadException(
                path,
                $"suits must contain exactly {ExpectedSuits.Length} entries but contained {entries.Count}.");
        }

        var suits = new Suit[ExpectedSuits.Length];
        var seen = new HashSet<SuitType>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null)
            {
                throw new ContentLoadException(path, $"suits[{index}] is null.");
            }

            var suitType = ParseSuit(entry.Suit, path, $"suits[{index}]");
            if (!seen.Add(suitType))
            {
                throw new ContentLoadException(path, $"suits[{index}]: duplicate suit {suitType}.");
            }

            var actual = new Suit(suitType, entry.RankCount, entry.AmountOfEachRank, entry.AllowedSequence);
            var expected = ExpectedSuits[(int)suitType - 1];
            if (actual != expected)
            {
                throw new ContentLoadException(
                    path,
                    $"suits[{index}]: {suitType} must be {expected} but was {actual}.");
            }

            suits[(int)suitType - 1] = actual;
        }

        for (var index = 0; index < suits.Length; index++)
        {
            if (suits[index].Type != ExpectedSuits[index].Type)
            {
                throw new ContentLoadException(path, $"suits is missing {ExpectedSuits[index].Type}.");
            }
        }

        return suits;
    }

    private static Skill[] ParseSkills(byte[] utf8Json, string path)
    {
        var document = Deserialize(utf8Json, path, ContentJsonContext.Default.SkillsDocument);
        if (document.Version != ContentFormatVersion)
        {
            throw new ContentLoadException(path, $"version must be {ContentFormatVersion} but was {document.Version}.");
        }

        var entries = document.Skills ?? throw new ContentLoadException(path, "skills is required.");
        if (entries.Count != ExpectedSkills.Length)
        {
            throw new ContentLoadException(
                path,
                $"skills must contain exactly {ExpectedSkills.Length} entries but contained {entries.Count}.");
        }

        // 规格只固定 id→name 映射，不固定数组顺序：按 id 匹配，返回时再按 id 升序。
        // 先校验 id 集合（恰好 1..4 各一次），再校验名称，最后按 id 归位。
        var counts = new int[ExpectedSkills.Length];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null)
            {
                throw new ContentLoadException(path, $"skills[{index}] is null.");
            }

            if (entry.Id < 1 || entry.Id > ExpectedSkills.Length)
            {
                throw new ContentLoadException(
                    path,
                    $"skills[{index}]: unknown skill id {entry.Id} (valid ids: 1-{ExpectedSkills.Length}).");
            }

            counts[entry.Id - 1]++;
        }

        for (var id = 1; id <= ExpectedSkills.Length; id++)
        {
            if (counts[id - 1] == 0)
            {
                throw new ContentLoadException(path, $"skills: expected id {id} but it is missing.");
            }

            if (counts[id - 1] > 1)
            {
                throw new ContentLoadException(path, $"skills: duplicate id {id}.");
            }
        }

        var skills = new Skill[ExpectedSkills.Length];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index]!;
            var expectedName = ExpectedSkills[entry.Id - 1].Name;
            if (!string.Equals(entry.Name, expectedName, StringComparison.Ordinal))
            {
                throw new ContentLoadException(
                    path,
                    $"skills[{index}]: id {entry.Id} must be named '{expectedName}' but was '{entry.Name ?? "<null>"}'.");
            }

            skills[entry.Id - 1] = new Skill(entry.Id, entry.Name!);
        }

        return skills;
    }

    private static JokerRule ParseRules(byte[] utf8Json, string path, TileType[] tileTypes)
    {
        var document = Deserialize(utf8Json, path, ContentJsonContext.Default.RulesDocument);
        if (document.Version != ContentFormatVersion)
        {
            throw new ContentLoadException(path, $"version must be {ContentFormatVersion} but was {document.Version}.");
        }

        var joker = document.Joker ?? throw new ContentLoadException(path, "joker is required.");
        var mode = ParseJokerMode(joker.Mode, path);
        switch (mode)
        {
            case JokerMode.None:
                return new JokerRule(JokerMode.None, null);

            case JokerMode.FixedType:
                var typeId = joker.TypeId
                    ?? throw new ContentLoadException(path, "joker.typeId is required when joker.mode is 'FixedType'.");
                if (typeId < 0 || typeId >= tileTypes.Length)
                {
                    throw new ContentLoadException(
                        path,
                        $"joker.typeId {typeId} does not exist in tiles.json (valid ids: 0-{tileTypes.Length - 1}).");
                }

                return new JokerRule(JokerMode.FixedType, tileTypes[typeId]);

            case JokerMode.FlipIndicator:
                throw new NotSupportedException(
                    $"Content load failed for '{path}': joker.mode 'FlipIndicator' is not supported in content format v1.");

            default:
                throw new ContentLoadException(path, $"joker.mode '{joker.Mode ?? "<null>"}' is not supported.");
        }
    }

    private static JokerMode ParseJokerMode(string? value, string path) => value switch
    {
        "None" => JokerMode.None,
        "FixedType" => JokerMode.FixedType,
        "FlipIndicator" => JokerMode.FlipIndicator,
        _ => throw new ContentLoadException(
            path,
            $"joker.mode must be 'None', 'FixedType' or 'FlipIndicator' but was '{value ?? "<null>"}'."),
    };

    private static SuitType ParseSuit(string? value, string path, string location) => value switch
    {
        "Character" => SuitType.Character,
        "Dot" => SuitType.Dot,
        "Bamboo" => SuitType.Bamboo,
        "Wind" => SuitType.Wind,
        "Dragon" => SuitType.Dragon,
        _ => throw new ContentLoadException(path, $"{location}: unknown suit '{value ?? "<null>"}'."),
    };

    private static byte[] VerifyManifest(string root, IReadOnlyDictionary<string, byte[]> cachedFiles)
    {
        var manifestPath = Combine(root, "manifest.json");
        var manifestBytes = ReadRequiredFile(root, "manifest.json");
        var manifest = Deserialize(manifestBytes, manifestPath, ContentJsonContext.Default.ManifestDocument);
        if (manifest.Version != ContentFormatVersion)
        {
            throw new ContentLoadException(
                manifestPath,
                $"version must be {ContentFormatVersion} but was {manifest.Version}.");
        }

        var files = manifest.Files ?? throw new ContentLoadException(manifestPath, "files is required.");
        foreach (var expectedPath in ManifestFilePaths)
        {
            if (!files.ContainsKey(expectedPath))
            {
                throw new ContentLoadException(manifestPath, $"files entry '{expectedPath}' is missing.");
            }
        }

        foreach (var declaredPath in files.Keys)
        {
            if (Array.IndexOf(ManifestFilePaths, declaredPath) < 0)
            {
                throw new ContentLoadException(
                    manifestPath,
                    $"files entry '{declaredPath}' is not part of the fixed content file list.");
            }
        }

        var sortedPaths = (string[])ManifestFilePaths.Clone();
        Array.Sort(sortedPaths, StringComparer.Ordinal);
        var contentHashInput = new StringBuilder(ContentHashPrefix);
        foreach (var relativePath in sortedPaths)
        {
            var filePath = Combine(root, relativePath);
            var bytes = cachedFiles.TryGetValue(relativePath, out var cached)
                ? cached
                : ReadRequiredFile(root, relativePath);
            var actualHex = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var declaredHex = files[relativePath];
            if (!string.Equals(declaredHex, actualHex, StringComparison.Ordinal))
            {
                throw new ContentLoadException(
                    filePath,
                    $"manifest sha256 mismatch: declared '{declaredHex ?? "<null>"}', actual '{actualHex}'.");
            }

            contentHashInput.Append(relativePath).Append('=').Append(actualHex).Append('\n');
        }

        var computedHash = SHA256.HashData(Encoding.UTF8.GetBytes(contentHashInput.ToString()));
        var declaredContentHash = manifest.ContentHash;
        if (!IsLowercaseHex(declaredContentHash, Sha256HexLength))
        {
            throw new ContentLoadException(
                manifestPath,
                $"contentHash must be {Sha256HexLength} lowercase hex characters but was '{declaredContentHash ?? "<null>"}'.");
        }

        var computedHex = Convert.ToHexStringLower(computedHash);
        if (!string.Equals(declaredContentHash, computedHex, StringComparison.Ordinal))
        {
            throw new ContentLoadException(
                manifestPath,
                $"contentHash mismatch: declared '{declaredContentHash}', recomputed '{computedHex}'.");
        }

        if (computedHash.Length != ContentHashLength)
        {
            throw new InvalidOperationException("SHA-256 must produce a 32 byte digest.");
        }

        return computedHash;
    }

    private static bool IsLowercaseHex(string? value, int length)
    {
        if (value is null || value.Length != length)
        {
            return false;
        }

        foreach (var character in value)
        {
            var isLowercaseHex = character is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!isLowercaseHex)
            {
                return false;
            }
        }

        return true;
    }

    private static (SuitType Suit, byte Rank)[] CreateExpectedTiles()
    {
        var tiles = new (SuitType Suit, byte Rank)[34];
        var index = 0;
        foreach (var suit in new[] { SuitType.Character, SuitType.Dot, SuitType.Bamboo })
        {
            for (byte rank = 1; rank <= 9; rank++)
            {
                tiles[index++] = (suit, rank);
            }
        }

        for (byte rank = 1; rank <= 4; rank++)
        {
            tiles[index++] = (SuitType.Wind, rank);
        }

        for (byte rank = 1; rank <= 3; rank++)
        {
            tiles[index++] = (SuitType.Dragon, rank);
        }

        return tiles;
    }
}
