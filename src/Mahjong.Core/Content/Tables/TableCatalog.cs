using Mahjong.Core.Rules;

namespace Mahjong.Core.Content.Tables;

/// <summary>
/// 6 个查表文件的只读内存视图（胡牌判定 dictionary_* + 牌效 ai_dictionary_*）。
/// <see cref="Load"/> 一次读入并校验全部文件，之后查询只做字典查找，不分配。
/// </summary>
public sealed class TableCatalog
{
    private const string NormalWinFileName = "dictionary_normal.bytes";
    private const string WindWinFileName = "dictionary_wind.bytes";
    private const string DragonWinFileName = "dictionary_dragon.bytes";
    private const string NormalAiFileName = "ai_dictionary_normal.bytes";
    private const string WindAiFileName = "ai_dictionary_wind.bytes";
    private const string DragonAiFileName = "ai_dictionary_dragon.bytes";

    private const int NormalRankCount = 9;
    private const int WindRankCount = 4;
    private const int DragonRankCount = 3;

    /// <summary>单个 rank 的物理牌上限，用于校验补张数。</summary>
    private const int MaxCopiesPerRank = 4;

    private readonly Dictionary<ulong, WinRow[]> _normalWinRows;
    private readonly Dictionary<ulong, WinRow[]> _windWinRows;
    private readonly Dictionary<ulong, WinRow[]> _dragonWinRows;
    private readonly Dictionary<ulong, AiRow[]> _normalAiRows;
    private readonly Dictionary<ulong, AiRow[]> _windAiRows;
    private readonly Dictionary<ulong, AiRow[]> _dragonAiRows;

    private TableCatalog(
        Dictionary<ulong, WinRow[]> normalWinRows,
        Dictionary<ulong, WinRow[]> windWinRows,
        Dictionary<ulong, WinRow[]> dragonWinRows,
        Dictionary<ulong, AiRow[]> normalAiRows,
        Dictionary<ulong, AiRow[]> windAiRows,
        Dictionary<ulong, AiRow[]> dragonAiRows)
    {
        _normalWinRows = normalWinRows;
        _windWinRows = windWinRows;
        _dragonWinRows = dragonWinRows;
        _normalAiRows = normalAiRows;
        _windAiRows = windAiRows;
        _dragonAiRows = dragonAiRows;
    }

    /// <summary>读入目录下固定的 6 个查表文件；任何结构问题都抛 <see cref="InvalidDataException"/>。</summary>
    /// <param name="tablesDirectory">查表目录（通常是 content 根下的 <c>tables</c>）。</param>
    /// <exception cref="ArgumentException"><paramref name="tablesDirectory"/> 为 null/空白。</exception>
    /// <exception cref="InvalidDataException">文件缺失、截断、类型码越界、行结构不符、重复 key 或尾随字节。</exception>
    public static TableCatalog Load(string tablesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablesDirectory);
        string directory = Path.GetFullPath(tablesDirectory);
        return new TableCatalog(
            LoadWinRows(Path.Combine(directory, NormalWinFileName), NormalRankCount),
            LoadWinRows(Path.Combine(directory, WindWinFileName), WindRankCount),
            LoadWinRows(Path.Combine(directory, DragonWinFileName), DragonRankCount),
            LoadAiRows(Path.Combine(directory, NormalAiFileName)),
            LoadAiRows(Path.Combine(directory, WindAiFileName)),
            LoadAiRows(Path.Combine(directory, DragonAiFileName)));
    }

    /// <summary>该花色是否有表，以及 <paramref name="key"/> 是否有行；两者都不区分，无候选时返回 false。</summary>
    /// <param name="suit">花色；Character/Dot/Bamboo 共用 normal 表，Wind/Dragon 各有表。</param>
    /// <param name="key">经 <see cref="TableKey.Encode"/> 编码的牌型 key。</param>
    /// <param name="rows">命中时为该 key 的行；未命中时为 <see cref="Array.Empty{T}"/>。</param>
    public bool TryGetWinRows(SuitType suit, ulong key, out IReadOnlyList<WinRow> rows)
    {
        Dictionary<ulong, WinRow[]>? table = suit switch
        {
            SuitType.Character or SuitType.Dot or SuitType.Bamboo => _normalWinRows,
            SuitType.Wind => _windWinRows,
            SuitType.Dragon => _dragonWinRows,
            _ => null,
        };

        if (table is not null && table.TryGetValue(key, out WinRow[]? found))
        {
            rows = found;
            return true;
        }

        rows = Array.Empty<WinRow>();
        return false;
    }

    /// <inheritdoc cref="TryGetWinRows"/>
    public bool TryGetAiRows(SuitType suit, ulong key, out IReadOnlyList<AiRow> rows)
    {
        Dictionary<ulong, AiRow[]>? table = suit switch
        {
            SuitType.Character or SuitType.Dot or SuitType.Bamboo => _normalAiRows,
            SuitType.Wind => _windAiRows,
            SuitType.Dragon => _dragonAiRows,
            _ => null,
        };

        if (table is not null && table.TryGetValue(key, out AiRow[]? found))
        {
            rows = found;
            return true;
        }

        rows = Array.Empty<AiRow>();
        return false;
    }

    /// <summary>该花色胡牌表的 key 数量；无映射花色返回 0。</summary>
    public int WinRowKeyCount(SuitType suit) => suit switch
    {
        SuitType.Character or SuitType.Dot or SuitType.Bamboo => _normalWinRows.Count,
        SuitType.Wind => _windWinRows.Count,
        SuitType.Dragon => _dragonWinRows.Count,
        _ => 0,
    };

    /// <summary>该花色牌效表的 key 数量；无映射花色返回 0。</summary>
    public int AiRowKeyCount(SuitType suit) => suit switch
    {
        SuitType.Character or SuitType.Dot or SuitType.Bamboo => _normalAiRows.Count,
        SuitType.Wind => _windAiRows.Count,
        SuitType.Dragon => _dragonAiRows.Count,
        _ => 0,
    };

    private static Dictionary<ulong, WinRow[]> LoadWinRows(string path, int rankCount)
    {
        MsgPackLite reader = new(ReadTableBytes(path), path);
        int keyCount = ReadRootMapHeader(reader);
        Dictionary<ulong, WinRow[]> rowsByKey = new(keyCount);
        for (int index = 0; index < keyCount; index++)
        {
            ulong key = reader.ReadUInt64("key");
            reader.Label = $"{path} (key {key})";
            int rowCount = reader.ReadArrayHeader("rows");
            if (rowCount == 0)
            {
                throw new InvalidDataException($"{reader.Label}: key has an empty row list");
            }

            WinRow[] rows = new WinRow[rowCount];
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                rows[rowIndex] = ReadWinRow(reader, rankCount);
            }

            if (!rowsByKey.TryAdd(key, rows))
            {
                throw new InvalidDataException($"{reader.Label}: duplicate key at map index {index}");
            }
        }

        reader.Label = path;
        reader.EnsureEnd();
        return rowsByKey;
    }

    private static Dictionary<ulong, AiRow[]> LoadAiRows(string path)
    {
        MsgPackLite reader = new(ReadTableBytes(path), path);
        int keyCount = ReadRootMapHeader(reader);
        Dictionary<ulong, AiRow[]> rowsByKey = new(keyCount);
        for (int index = 0; index < keyCount; index++)
        {
            ulong key = reader.ReadUInt64("key");
            reader.Label = $"{path} (key {key})";
            int rowCount = reader.ReadArrayHeader("rows");
            if (rowCount == 0)
            {
                throw new InvalidDataException($"{reader.Label}: key has an empty row list");
            }

            AiRow[] rows = new AiRow[rowCount];
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                rows[rowIndex] = ReadAiRow(reader);
            }

            if (!rowsByKey.TryAdd(key, rows))
            {
                throw new InvalidDataException($"{reader.Label}: duplicate key at map index {index}");
            }
        }

        reader.Label = path;
        reader.EnsureEnd();
        return rowsByKey;
    }

    /// <summary>顶层固定为 array(1)，其唯一元素是 map&lt;ulong, list&lt;row&gt;&gt;。</summary>
    private static int ReadRootMapHeader(MsgPackLite reader)
    {
        int rootCount = reader.ReadArrayHeader("root");
        if (rootCount != 1)
        {
            throw new InvalidDataException(
                $"{reader.Label}: root must be an array of exactly 1 element, found {rootCount}");
        }

        return reader.ReadMapHeader("root map");
    }

    private static WinRow ReadWinRow(MsgPackLite reader, int rankCount)
    {
        int rowOffset = reader.Offset;
        int fieldCount = reader.ReadArrayHeader("win row");
        if (fieldCount != 4)
        {
            throw new InvalidDataException(
                $"{reader.Label}: win row at offset {rowOffset} must have 4 fields, found {fieldCount}");
        }

        int needJoker = reader.ReadInt32("needJoker");
        bool hasPair = reader.ReadBool("hasPair");
        bool hasWin = reader.ReadBool("hasWin");
        int winningRankCount = reader.ReadArrayHeader("winningRanks");
        if (hasWin && winningRankCount != 0)
        {
            throw new InvalidDataException(
                $"{reader.Label}: win row at offset {rowOffset} has hasWin=true but lists {winningRankCount} winning rank(s)");
        }

        if (!hasWin && winningRankCount != rankCount)
        {
            throw new InvalidDataException(
                $"{reader.Label}: win row at offset {rowOffset} must list {rankCount} winning rank(s) for this suit, found {winningRankCount}");
        }

        int[] winningRanks = new int[winningRankCount];
        for (int index = 0; index < winningRanks.Length; index++)
        {
            int count = reader.ReadInt32("winningRanks");
            if (count > MaxCopiesPerRank)
            {
                throw new InvalidDataException(
                    $"{reader.Label}: winningRanks[{index}] at offset {rowOffset} is {count}, but a rank has at most {MaxCopiesPerRank} copies");
            }

            winningRanks[index] = count;
        }

        return new WinRow(needJoker, hasPair, hasWin, winningRanks);
    }

    private static AiRow ReadAiRow(MsgPackLite reader)
    {
        int rowOffset = reader.Offset;
        int fieldCount = reader.ReadArrayHeader("ai row");
        if (fieldCount != 2)
        {
            throw new InvalidDataException(
                $"{reader.Label}: ai row at offset {rowOffset} must have 2 fields, found {fieldCount}");
        }

        bool hasPair = reader.ReadBool("hasPair");
        double probability = reader.ReadDouble("probability");
        if (!double.IsFinite(probability))
        {
            throw new InvalidDataException(
                $"{reader.Label}: ai row at offset {rowOffset} has non-finite probability {probability}");
        }

        return new AiRow(hasPair, probability);
    }

    private static byte[] ReadTableBytes(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{path}: table file not found");
        }

        return File.ReadAllBytes(path);
    }
}
